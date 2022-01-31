using System;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Controllers
{
	[ApiController, Route("leaderboard"), RequireAuth, UseMongoTransaction]
	public class TopController : PlatformController
	{
		public Services.LeaderboardService _leaderboardService;

		[HttpPatch, Route("score")]
		public ActionResult AddScore()
		{
			int score = Require<int>("score");
			string type = Require<string>("type");
			
			Leaderboard leaderboard = _leaderboardService.SetScore(Token.AccountId, type, score);

			if (leaderboard == null)
				throw new UnknownLeaderboardException(type);

			return Ok(leaderboard);
		}

		// TODO: Move to admin controller
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
		
		[HttpGet, Route("rankings")]
		public ActionResult GetRankings()
		{
			return Ok();
		}
		
		public override ActionResult HealthCheck()
		{
			return Ok(_leaderboardService.HealthCheckResponseObject);
		}
	}
}