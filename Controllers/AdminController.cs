using System;
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

			if (_leaderboardService.Count(leaderboard.Type) > 0)
				throw new Exception($"Leaderboard type '{leaderboard.Type}' already exists.");

			_leaderboardService.Create(leaderboard);
			return Ok(new
			{
				Leaderboard = leaderboard
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