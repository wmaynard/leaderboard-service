using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
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
			if (!leaderboard.Validate(out string[] errors))
				return Problem(data: new { Errors = errors });

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
				leaderboard.ResetID();
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
				title: $"{PlatformEnvironment.Deployment}-{RolloverType.Weekly.ToString()} rollover manually triggered",
				message: $"{Token.ScreenName} manually triggered the leaderboards rollover.")
			.Attach(name: "Token information", content: Token.JSON)
			.Send()
			.Wait();
#endif
		_leaderboardService.Rollover(RolloverType.Weekly);
		return Ok();
	}
	
	
	[HttpGet, Route("health"), NoAuth]
	public override ActionResult HealthCheck()
	{
		return Ok(_leaderboardService.HealthCheckResponseObject);
	}
}