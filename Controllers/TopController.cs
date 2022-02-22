using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers
{
	[ApiController, Route("leaderboard"), RequireAuth, UseMongoTransaction]
	public class TopController : PlatformController
	{
#pragma warning disable CS0649
		private Services.LeaderboardService _leaderboardService;
		private EnrollmentService _enrollmentService;
		private ResetService _resetService;
#pragma warning restore CS0649

		[HttpPatch, Route("score")]
		public ActionResult AddScore()
		{
			int score = Require<int>("score");
			string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);

			if (score == 0)
				return Ok();

			Enrollment enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type);
			Leaderboard leaderboard = _leaderboardService.AddScore(enrollment, score);
			if (enrollment.CurrentLeaderboardID == leaderboard.Id)
				return Ok(new { Leaderboard = leaderboard });
			
			enrollment.CurrentLeaderboardID = leaderboard.Id;
			enrollment.IsActive = true;
			_enrollmentService.Update(enrollment);

			return Ok(new { Leaderboard = leaderboard });
		}

		// TODO: Move to admin controller
		
		
		[HttpGet, Route("rankings")]
		public ActionResult GetRankings()
		{
			string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
			
			// _enrollmentService.DemotePlayers(new string[] { Token.AccountId }, new Leaderboard(){MaxTier = 6, Type = type });
			Enrollment enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type);
			Leaderboard board = _leaderboardService.AddScore(enrollment, 0);
			// Leaderboard board = _leaderboardService.Find(Token.AccountId, type);
			
			return Ok( new
			{
				LeaderboardId = board.Id,
				Response = board.GenerateScoreResponse(Token.AccountId)
			});
		}

		// [HttpGet, Route("test"), NoAuth]
		// public ActionResult Test()
		// {
		// 	Leaderboard output = null;
		// 	SortedList<string, long> list = new SortedList<string, long>();
		// 	for (int i = 0; i < 1_000; i++)
		// 	{
		// 		string accountId = "rando-" + Guid.NewGuid().ToString();
		// 		int score = new Random().Next(0, 500);
		// 		list.Add(accountId, score);
		// 		output = _leaderboardService.AddScore(accountId, "pvp_daily", score);
		// 	}
		// 	
		// 	return Ok(new
		// 	{
		// 		Leaderboard = output
		// 	});
		// }

		[HttpGet, Route("triggerDailyRollover"), NoAuth]
		public ActionResult Test2()
		{
			_leaderboardService.Rollover(RolloverType.Daily);
			return Ok();
		}
		
		#region LOAD_BALANCER
		[HttpGet, Route("health"), NoAuth]
		public override ActionResult HealthCheck()
		{
			return Ok(_leaderboardService.HealthCheckResponseObject, _resetService.HealthCheckResponseObject);
		}
		#endregion LOAD_BALANCER
	}
}