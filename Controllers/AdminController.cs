using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
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

			List<string> ids = new List<string>();
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

	[HttpPatch, Route("scores")]
	public ActionResult SetScoresManually()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		Entry[] entries = Require<Entry[]>("scores");

		List<string> failed = new List<string>();
		
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

	[HttpPost, Route("sendRewards")]
	public ActionResult SendRewards()
	{
		_rewardsService.SendRewards();

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
			.Attach(name: "Token information", content: Token.JSON)
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
		
		Random rando = new Random();

		int successes = 0;
		int failures = 0;

		List<string> ids = new List<string>();
		
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

		List<Leaderboard> leaderboards = new List<Leaderboard>();
		
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
		enrollment.Status = Enrollment.PromotionStatus.Acknowledged;

		_enrollmentService.Update(enrollment);
		_leaderboardService.RemovePlayer(accountId, type);
		_leaderboardService.AddScore(enrollment, score);
		

		return Ok();
	}
}