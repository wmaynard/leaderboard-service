using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MongoDB.Bson;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
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

			leaderboard.RolloversRemaining = leaderboard.RolloversInSeason;

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
	public ActionResult GetShardStats() => Ok(new RumbleJson
	{
		{ "stats", _leaderboardService.ProjectShardStats() }
	});

	[HttpPatch, Route("scores")]
	public ActionResult SetScoresManually()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		Entry[] entries = Require<Entry[]>("scores");

		List<string> failed = new();
		
		foreach (Entry entry in entries)
		{
			Enrollment enrollment = _enrollmentService.FindOne(enrollment => enrollment.AccountID == entry.AccountID && enrollment.LeaderboardType == type);
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
			throw new PlatformException("Not allowed on prod.", code: ErrorCode.Unauthorized); // TODO: Create error code
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
		
		while (count-- > 0)
			try
			{
				if (count % 10 == 0)
					Log.Local(Owner.Will, $"{count} scores remaining.");
				
				_apiService
					.Request(PlatformEnvironment.Url("/player/v2/launch"))
					.SetPayload(new RumbleJson
					{
						{ "installId", $"locust-leaderboard-{count}" }
					})
					.OnSuccess(response =>
					{
						string token = response.AsRumbleJson.Require<string>("accessToken");
						
						_apiService
#if DEBUG
							.Request(PlatformEnvironment.Url("http://localhost:5091/leaderboard/score"))
#else
							.Request(PlatformEnvironment.Url("/leaderboard/score"))
#endif
							.AddAuthorization(token)
							.SetPayload(new RumbleJson
							{
								{ "score", rando.Next(min, max) },
								{ "leaderboardId", type }
							})
							.OnSuccess(secondResponse =>
							{
								string id = secondResponse.AsRumbleJson.Optional<Leaderboard>("leaderboard")?.Id;
								if (id != null)
									ids.Add(id);
								successes++;
							})
							.OnFailure(secondResponse => failures++)
							.Patch();
						
						successes++;
					})
					.OnFailure(response => failures++)
					.Post();
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}

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

	[HttpPatch, Route("season")]
	public ActionResult UpdateSeason()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		int rolloversRemaining = Optional<int>(Leaderboard.FRIENDLY_KEY_SEASON_COUNTDOWN);
		int rolloversInSeason = Optional<int>(Leaderboard.FRIENDLY_KEY_SEASON_ROLLOVERS);
		
		Log.Info(Owner.Will, "Updated leaderboard season information.", new RumbleJson
		{
			{ Leaderboard.FRIENDLY_KEY_TYPE, type },
			{ Leaderboard.FRIENDLY_KEY_SEASON_ROLLOVERS, rolloversInSeason },
			{ Leaderboard.FRIENDLY_KEY_SEASON_COUNTDOWN, rolloversRemaining }
		});

		return Ok(new RumbleJson
		{
			{ "modified", _leaderboardService.UpdateSeason(type, rolloversInSeason, rolloversRemaining) }
		});
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
		enrollment.SeasonalMaxTier = Math.Max(enrollment.Tier, newTier);
		enrollment.IsActive = score > 0;
		enrollment.IsActiveInSeason = score > 0;
		enrollment.Status = Enrollment.PromotionStatus.Acknowledged;

		_enrollmentService.Update(enrollment);
		_leaderboardService.RemovePlayer(accountId, type);
		_leaderboardService.AddScore(enrollment, score);

		return Ok();
	}

	[HttpPatch, Route("active")]
	public ActionResult UpdateEnrollmentActivity()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		bool? active = Optional<bool?>(Enrollment.FRIENDLY_KEY_IS_ACTIVE);
		bool? seasonActive = Optional<bool?>(Enrollment.FRIENDLY_KEY_IS_ACTIVE_SEASON);
		string[] accountIds = Require<string[]>("accountIds");

		if (active == null && seasonActive == null)
			throw new PlatformException($"You must specify at least one non-null value for: {Enrollment.FRIENDLY_KEY_IS_ACTIVE} or {Enrollment.FRIENDLY_KEY_IS_ACTIVE_SEASON}");

		RumbleJson output = new RumbleJson();
		if (active != null)
			output["setAsActive"] = _enrollmentService.SetCurrentlyActive(accountIds, type, (bool)active);

		if (seasonActive != null)
			output["setAsActiveInSeason"] = _enrollmentService.SetActiveInSeason(accountIds, type, (bool)seasonActive);

		return Ok(output);
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

	[HttpGet, Route("rolloversRemaining")]
	public ActionResult GetLeaderboardDetails()
	{
		RumbleJson[] output = _leaderboardService
			.GetRolloversRemaining()
			.DistinctBy(leaderboard => leaderboard.Type)
			.Select(leaderboard => new RumbleJson
			{
				{ Leaderboard.FRIENDLY_KEY_TYPE, leaderboard.Type },
				{ Leaderboard.FRIENDLY_KEY_SEASON_ROLLOVERS, leaderboard.RolloversInSeason },
				{ Leaderboard.FRIENDLY_KEY_SEASON_COUNTDOWN, leaderboard.RolloversRemaining }
			})
			.ToArray();
		return Ok(new RumbleJson
		{
			{ "leaderboards", output }
		});
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
		string accounts = Require<string>("accountIds");

		if (string.IsNullOrWhiteSpace(accounts))
			throw new PlatformException("No accountIds provided.");

		string[] accountIds = accounts.Split(',');

		return Ok(new RumbleJson
		{
			{ "players", _ladderService.GetPlayerScores(accountIds) },
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