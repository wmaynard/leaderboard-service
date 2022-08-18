using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using RCL.Logging;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[ApiController, Route("leaderboard/admin"), RequireAuth(AuthType.ADMIN_TOKEN), UseMongoTransaction]
public class AdminController : PlatformController
{
#pragma warning disable CS0649
	private readonly EnrollmentService _enrollmentService;
	private readonly Services.LeaderboardService _leaderboardService;
	private readonly RewardsService _rewardsService;
	private readonly ApiService _apiService;
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
				return Ok(new
				{
					AffectedBoards = affected,
					Leaderboard = leaderboard
				});
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

		return Ok(new
		{
			deleted = deleted
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
		return Ok(new
		{
			LeaderboardIds = types
		});
	}

	[HttpPost, Route("sendRewards")]
	public ActionResult SendRewards()
	{
		_rewardsService.SendRewards();

		return Ok();
	}
	
	[HttpPost, Route("rollover"), IgnorePerformance]
	public ActionResult ManualRollover()
	{
		try
		{
			int deployment = int.Parse(PlatformEnvironment.Deployment);
			if (deployment > 300)
				throw new PlatformException("This action is not allowed on prod.");
		}
		catch { }

#if RELEASE
		SlackDiagnostics
			.Log(
				title: $"{PlatformEnvironment.Deployment}-{RolloverType.Daily.ToString()} rollover manually triggered",
				message: $"{Token.ScreenName} manually triggered the leaderboards rollover.")
			.Attach(name: "Token information", content: Token.JSON)
			.Send()
			.Wait();
#endif
		_leaderboardService.Rollover(RolloverType.Daily);
		
#if RELEASE
		SlackDiagnostics
			.Log(
				title: $"{PlatformEnvironment.Deployment}-{RolloverType.Weekly.ToString()} rollover manually triggered",
				message: $"{Token.ScreenName} manually triggered the leaderboards rollover.")
			.Attach(name: "Token information", content: Token.JSON)
			.Send()
			.Wait();
#endif
		_leaderboardService.Rollover(RolloverType.Weekly);
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
				_apiService
					.Request(PlatformEnvironment.Url("/player/v2/launch"))
					.SetPayload(new GenericData
					{
						{ "installId", $"locust-leaderboard-{count}" }
					})
					.OnSuccess((_, _) =>
					{
						successes++;
					})
					.OnFailure((_, _) =>
					{
						failures++;
					}).Post(out GenericData launchResponse, out int launchCode);

				if (!launchCode.Between(200, 299))
					continue;

				string token = launchResponse.Require<string>("accessToken");

				_apiService
					.Request(PlatformEnvironment.Url("/leaderboard/score"))
					.AddAuthorization(token)
					.SetPayload(new GenericData
					{
						{ "score", rando.Next(min, max) },
						{ "leaderboardId", type }
					})
					.OnSuccess((_, _) =>
					{
						successes++;
					})
					.OnFailure((_, _) =>
					{
						failures++;
					})
					.Patch(out GenericData scoreResponse, out int scoreCode);

				string id = scoreResponse.Optional<Leaderboard>("leaderboard")?.Id;
				if (id != null)
					ids.Add(id);
			}
			catch (Exception e)
			{
				Console.WriteLine(e);
				throw;
			}

		List<Leaderboard> leaderboards = new List<Leaderboard>();
		
		foreach (string id in ids.Distinct())
			leaderboards.Add(_leaderboardService.Find(id));

		GenericData output = new GenericData
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
}