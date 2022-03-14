using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers
{
	[ApiController, Route("leaderboard/admin"), RequireAuth(TokenType.ADMIN)]
	public class AdminController : PlatformController
	{
#pragma warning disable CS0649
		private readonly Services.LeaderboardService _leaderboardService;
		// private readonly ResetService _resetService;
#pragma warning restore CS0649
		
		
		[HttpPost, Route("update")]
		public ActionResult CreateOrUpdate()
		{
			Leaderboard leaderboard = Require<Leaderboard>("leaderboard");
			if (!leaderboard.Validate(out string[] errors))
				return Problem(new
				{
					Errors = errors
				});

			
			if (_leaderboardService.Count(leaderboard.Type) > 0)
			{
				// TODO: If the Max
				long affected = _leaderboardService.UpdateLeaderboardType(leaderboard);
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
			return Ok(new
			{
				Leaderboard = leaderboard,
				TierIDs = ids
			});
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
		
		[HttpPost, Route("rollover"), IgnorePerformance]
		public ActionResult ManualRollover()
		{
			string id = Require<string>("leaderboardId");
			
			_leaderboardService.Rollover(id).Wait();

			return Ok();
		}
		
		
		[HttpGet, Route("health"), NoAuth]
		public override ActionResult HealthCheck()
		{
			return Ok(_leaderboardService.HealthCheckResponseObject);
		}
	}
}