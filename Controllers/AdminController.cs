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
		
		
		[HttpPost, Route("create")]
		public ActionResult Create()
		{
			Leaderboard leaderboard = Require<Leaderboard>("leaderboard");

			// TODO: /leaderboard/admin/update
			if (_leaderboardService.Count(leaderboard.Type) > 0)
				throw new Exception($"Leaderboard type '{leaderboard.Type}' already exists.");

			List<string> ids = new List<string>();
			int currentTier = 1;
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
		
		[HttpPost, Route("rollover"), IgnorePerformance]
		public ActionResult ManualRollover()
		{
			string id = Require<string>("leaderboardId");
			
			_leaderboardService.Rollover(id);

			return Ok();
		}
		
		
		[HttpGet, Route("health"), NoAuth]
		public override ActionResult HealthCheck()
		{
			return Ok(_leaderboardService.HealthCheckResponseObject);
		}
	}
}