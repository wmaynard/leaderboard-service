using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MongoDB.Bson;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[ApiController, Route("leaderboard/admin"), RequireAuth(AuthType.ADMIN_TOKEN)]
public class AdminController : PlatformController
{
#pragma warning disable CS0649
	private readonly EnrollmentService _enrollmentService;
	private readonly Services.LeaderboardService _leaderboardService;
	private readonly RewardsService _rewardsService;
	private readonly RolloverService _rolloverService;
	private readonly ArchiveService _archiveService;
	private readonly LadderService _ladderService;
	private readonly SeasonDefinitionService _seasons;
	// private readonly ResetService _resetService;
#pragma warning restore CS0649

	[HttpPost, Route("update")]
	public ActionResult CreateOrUpdate()
	{
		Leaderboard[] leaderboards = Require<Leaderboard[]>("leaderboards");
		string[] deletions = Optional<string[]>("idsToDelete");

		int deleted = deletions != null && deletions.Any()
			? _leaderboardService.DeleteType(deletions)
			: 0;

		foreach (Leaderboard leaderboard in leaderboards)
		{
			leaderboard.Validate();

			if (_leaderboardService.Count(leaderboard.Type) > 0)
			{
				// TODO: If the Max tier changes, delete abandoned tiers or create new tiers.
				long affected = _leaderboardService.UpdateLeaderboardType(leaderboard);
				continue;
			}

			List<string> ids = new ();
			int currentTier = 0;
			do
			{
				leaderboard.Tier = currentTier;
				_leaderboardService.Create(leaderboard);
				ids.Add(leaderboard.Id);
				leaderboard.NullifyId();
			} while (currentTier++ < leaderboard.MaxTier);
		}

		return Ok(new RumbleJson
		{
			{ "deleted", deleted }
		});
	}

	[HttpGet, Route("shardStats")]
	public ActionResult GetShardStats()
	{
		ShardStat[] stats = _leaderboardService.ProjectShardStats();

		// DM from Ryan on 6/10/24: After getting some feedback last rollover, I think we want to change the calculation
		// to no longer include scores of 0 in the population stats calculations.
		// This comment specifically refers to ladder and not leaderboards, but it's not a bad idea to include this
		// in admin information for Portal's use for leaderboards.
		foreach (ShardStat stat in stats.Where(stat => stat.PlayerCounts.Any(count => count > 0)))
		{
			string key = $"stat_{stat.LeaderboardId}";
			
			if (_cacheService.HasValue(key, out long activePlayers))
				stat.ActivePlayers = activePlayers;
			else
			{
				stat.ActivePlayers = _enrollmentService.CountActivePlayers(stat.LeaderboardId);
				_cacheService.Store(key, stat.ActivePlayers, expirationMS: IntervalMs.TenMinutes);
			}
		}
		
		return Ok(new RumbleJson
		{
			{ "stats", stats }
		});
	}

	[HttpPatch, Route("scores")]
	public ActionResult SetScoresManually()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		Entry[] entries = Require<Entry[]>("scores");

		List<string> failed = new();
		
		foreach (Entry entry in entries)
		{
			Enrollment enrollment = _enrollmentService.Find(entry.AccountID, type).FirstOrDefault();
			Leaderboard leaderboard = _leaderboardService.SetScore(enrollment, entry.Score);
			
			if (leaderboard == null)
				failed.Add(entry.AccountID);
		}
		
		if (failed.Any())
			Log.Warn(Owner.Default, "Unable to update leaderboard '{type}'", data: new
			{
				FailedAccounts = failed
			});

		return Ok();
	}

	[HttpGet, Route("list")]
	public ActionResult ListLeaderboards()
	{
		string[] types = _leaderboardService.ListLeaderboardTypes();
		return Ok(new RumbleJson
		{
			{ "leaderboardIds", types }
		});
	}

	[HttpPatch, Route("confiscate"), IgnorePerformance]
	public ActionResult Confiscate()
	{
		// Intended for use with local development; with the way rollover service works, only a primary node can
		// create rollover tasks.  This endpoint forces the responding container to become the primary node.
		_rolloverService.Confiscate();

		return Ok();
	}
	
	// TODO: Remove NoAuth on 12/17
	[HttpPost, Route("rollover"), IgnorePerformance, NoAuth]
	public ActionResult ManualRollover()
	{
		if (PlatformEnvironment.IsProd && !(Token?.IsAdmin ?? true))
			throw new InvalidTokenException(Token?.Authorization, "/admin/rollover");
		if (Token == null)
			throw new InvalidTokenException(null, "/admin/rollover");
		
		_rolloverService.ManualRollover();

		int waitTime = 0;
		bool tasksRemain = true;
		do
		{
			const int ms = 5_000;
			Thread.Sleep(ms);
			waitTime += ms;
			tasksRemain = _rolloverService.TasksRemaining() > 0 || _rolloverService.WaitingOnTaskCompletion();
			Log.Local(Owner.Will, $"Tasks remaining: {_rolloverService.TasksRemaining()}");
			
			if (waitTime > 120_000)
				throw new PlatformException("Manual rollover timed out while waiting for tasks to complete.");
		} while (tasksRemain);

		SlackDiagnostics
			.Log(
				title: $"{PlatformEnvironment.Deployment} rollover manually triggered",
				message: $"{Token.ScreenName} manually triggered the leaderboards rollover.")
			.Attach(name: "Token information", content: Token.ToJson())
			.Send()
			.Wait();
		return Ok();
	}

	/// <summary>
	/// This allows QA to create tons of scores to test with.
	/// </summary>
	/// <returns></returns>
	/// <exception cref="PlatformException"></exception>
	[HttpPost, Route("mockScores"), IgnorePerformance]
	public ActionResult AddFakeUserScores()
	{
		if (PlatformEnvironment.IsProd)
			throw new PlatformException("Not allowed on prod.", code: ErrorCode.Unauthorized);
		const int MAX_USERS = 1_000;
		
		int count = Require<int>("userCount");
		string type = Require<string>("leaderboardId");

		if (!count.Between(1, MAX_USERS))
			throw new PlatformException($"User count must be a positive integer and {MAX_USERS} or less.", code: ErrorCode.InvalidRequestData);

		int min = Optional<int?>("minScore") ?? 0;
		int max = Optional<int?>("maxScore") ?? 100;

		if (min >= max)
			throw new PlatformException("Minimum score must be less than the maximum score.", code: ErrorCode.InvalidRequestData);
		
		Random rando = new();

		int successes = 0;
		int failures = 0;

		List<string> ids = new();

		Task[] tasks = new Task[count];
		while (count-- > 0)
			tasks[count] = Task.Run(() =>
			{
				try
				{
					Log.Local(Owner.Will, "Getting token");
					// TD-19907: Previously this was incorrectly pointed at /launch, which has was deprecated and
					// subsequently removed.
					string token = _apiService.GenerateToken(
						accountId: ObjectId.GenerateNewId().ToString(),
						screenname: $"MockLeader {rando.Next(0, 10_000)}",
						email: "",
						discriminator: 0,
						audiences: Audience.LeaderboardService
					);
					Log.Local(Owner.Will, "Adding score");
					_apiService
						.Request(PlatformEnvironment.IsLocal
							? "http://localhost:5091/leaderboard/score"  
							: "/leaderboard/score"
						)
						.AddAuthorization(token)
						.SetPayload(new RumbleJson
						{
							{"score", rando.Next(min, max)},
							{"leaderboardId", type}
						})
						.OnSuccess(response =>
						{
							string id = response.Optional<Leaderboard>("leaderboard")?.Id;
							if (!string.IsNullOrWhiteSpace(id))
								ids.Add(id);
							successes++;
						})
						.OnFailure(response => failures++)
						.Patch();
					
					Log.Local(Owner.Will, "Done");
				}
				catch (Exception e)
				{
					Log.Local(Owner.Will, "Could not run mock score task.", emphasis: Log.LogType.ERROR);
				}
			});

		Task.WaitAll(tasks);
			

		List<Leaderboard> leaderboards = new();
		
		foreach (string id in ids.Distinct())
			leaderboards.Add(_leaderboardService.Find(id));

		RumbleJson output = new RumbleJson
		{
			{ "successfulRequests", successes },
			{ "failedRequests", failures }
		};
		if (leaderboards.Count > 1)
			output["leaderboards"] = leaderboards;
		else if (leaderboards.Any())
			output["leaderboard"] = leaderboards.First();

		return Ok(output);
	}

	[HttpGet, Route("enrollments")]
	public ActionResult GetUserEnrollments() => Ok(new RumbleJson
	{
		{ "enrollments", _enrollmentService.Find(Require<string>("accountId")) }
	});

	[HttpGet, Route("archive")]
	public ActionResult ArchiveById()
	{
		Leaderboard output = _archiveService.FindById(Require<string>("id"));
		
		return Ok(new RumbleJson
		{
			{ "leaderboard", output },
			{ "entries", output?.CalculateRanks() }
		});
	}

	[HttpPatch, Route("enrollment")]
	public ActionResult UpdateEnrollmentInformation()
	{
		string accountId = Require<string>("accountId");
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		int newTier = Require<int>("tier");
		int score = Optional<int>("score");

		if (!accountId.CanBeMongoId())
			throw new PlatformException("Not a valid mongo ID!");

		Enrollment enrollment = _enrollmentService.FindOrCreate(accountId, type);
		enrollment.Tier = newTier;
		enrollment.IsActive = score > 0;
		enrollment.Status = Enrollment.PromotionStatus.Acknowledged;

		_enrollmentService.Update(enrollment);
		_leaderboardService.RemovePlayer(accountId, type);
		_leaderboardService.AddScore(enrollment, score, false);

		return Ok();
	}

	[HttpPatch, Route("active")]
	public ActionResult UpdateEnrollmentActivity()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		bool? active = Optional<bool?>(Enrollment.FRIENDLY_KEY_IS_ACTIVE);
		string[] accountIds = Require<string[]>("accountIds");

		if (active == null)
			throw new PlatformException($"You must specify a non-null value for: {Enrollment.FRIENDLY_KEY_IS_ACTIVE}");

		return Ok(new RumbleJson
		{
			{ "setAsActive", _enrollmentService.SetCurrentlyActive(accountIds, (bool)active)}
		});
	}

	[HttpPost, Route("debugRollover")]
	public ActionResult DebugArchives()
	{
		string id = Require<string>("archiveId");
		
		Leaderboard regen = _leaderboardService.Unarchive(id);

		_leaderboardService.Rollover(regen.Id).Wait();

		return Ok();
	}

	/// <summary>
	/// Used to expose top shard data, originally intended for a community / Discord embedded page.
	/// </summary>
	/// <returns>The top shard for a given leaderboard type.</returns>
	[HttpGet, Route("topShard")]
	public ActionResult GetTopShardScores()
	{
		// TODO: Global leaderboards need restructuring.  This will need to be updated later when this is addressed for scalability!
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		int limit = Require<int>("limit");

		Leaderboard leaderboard = _leaderboardService.FindBaseLeaderboard(type, limit);
		leaderboard.Scores = leaderboard.CalculateRanks()
			.OrderBy(entry => entry.Rank)
			.Take(limit)
			.ToList();

		return Ok(leaderboard);
	}

	[HttpPatch, Route("startTime")]
	public ActionResult UpdateLeaderboardStartTime()
	{
		if (PlatformEnvironment.IsProd)
			throw new EnvironmentPermissionsException();
		
		long startTime = Require<long>(Leaderboard.FRIENDLY_KEY_START_TIME);
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);

		long affected = _leaderboardService.UpdateStartTime(type, startTime);

		return Ok(new RumbleJson
		{
			{ "affectedShards", affected }
		});
	}

	[HttpPatch, Route("ladder/score")]
	public ActionResult AddLadderScore()
	{
		long score = Require<long>("score");
		string accountId = Require<string>("accountId");
		
		return Ok(new RumbleJson
		{
			{ "player", _ladderService.AddScore(accountId, score) }
		});
	}

	[HttpGet, Route("ladder/scores")]
	public ActionResult GetLadderScoresForPlayers()
	{
		string accounts = Optional<string>("accountIds");

		LadderInfo[] output;
		if (accounts != null)
		{
			if (string.IsNullOrWhiteSpace(accounts))
				throw new PlatformException("No accountIds provided.");

			string[] accountIds = accounts.Split(',');
			output = _ladderService.GetPlayerScores(accountIds);
		}
		else
			output = _ladderService.GetRankings().ToArray();

		return Ok(new RumbleJson
		{
			{ "players", output },
			{ "populationStats", _ladderService.GetPopulationStats() }
		});
	}

	[HttpPost, Route("ladder/seasonRollover")]
	public ActionResult EndCurrentSeason()
	{
		if (PlatformEnvironment.IsProd)
			throw new PlatformException("Someone tried to use a QA endpoint to end the ladder season in prod.  This is not allowed.");
		
		LadderSeasonDefinition upcoming = _seasons.GetCurrentSeason();
		if (upcoming == null)
			throw new PlatformException("Tried to test the end of a season, but there are no other seasons to copy details from.");

		if (Optional<bool>("addNewMockScores"))
		{
			Random rando = new();
			List<LadderInfo> newLadderEntries = new();

			int toInsert = rando.Next(50_000, 75_000);
			for (int i = 0; i < toInsert; i++)
			{
				int score = rando.Next(1000, 1400);
				newLadderEntries.Add(new LadderInfo
				{
					AccountId = ObjectId.GenerateNewId().ToString(),
					CreatedOn = Timestamp.OneDayAgo,
					IsActive = true,
					MaxScore = score,
					PreviousScoreChange = score,
					Score = score,
					Timestamp = Timestamp.Now
				});
			}
			_ladderService.Insert(newLadderEntries.ToArray());
		}
		if (Optional<bool>("mockScores"))
			_ladderService.UpdateLadderScoresAtRandom(25);
		
		LadderSeasonDefinition toEnd = upcoming.Copy();
		toEnd.ChangeId();
		toEnd.SeasonId = $"QA_{Timestamp.Now}_{upcoming.SeasonId}";
		toEnd.EndTime = Timestamp.Now;
		_seasons.Insert(toEnd);

		return Ok(toEnd);
	}
	

	[HttpPost, Route("ladder/seasons")]
	public ActionResult DefineSeasons()
	{
		LadderSeasonDefinition[] seasons = Require<LadderSeasonDefinition[]>("seasons");
		
		return Ok(new RumbleJson
		{
			{ "seasons", _seasons.Define(seasons) }
		});
	}
}