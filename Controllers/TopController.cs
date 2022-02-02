using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers
{
	[ApiController, Route("leaderboard"), RequireAuth, UseMongoTransaction]
	public class TopController : PlatformController
	{
		private Services.LeaderboardService _leaderboardService;
		private ResetService _resetService;

		[HttpPatch, Route("score")]
		public ActionResult AddScore()
		{
			int score = Require<int>("score");
			string type = Require<string>("type");
			
			Leaderboard leaderboard = _leaderboardService.AddScore(Token.AccountId, type, score);

			return Ok();
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
			string type = Require<string>("type");
			Leaderboard board = _leaderboardService.Find(Token.AccountId, type);
			
			

			return Ok( new
			{
				Response = board.GenerateScoreResponse(Token.AccountId)
			});
		}

		[HttpGet, Route("test"), NoAuth]
		public ActionResult Test()
		{
			Leaderboard output = null;
			SortedList<string, long> list = new SortedList<string, long>();
			for (int i = 0; i < 1_000; i++)
			{
				string accountId = "rando-" + Guid.NewGuid().ToString();
				int score = new Random().Next(0, 500);
				list.Add(accountId, score);
				output = _leaderboardService.AddScore(accountId, "pvp_daily", score);
			}
			
			// int ts = 
			// list.Select(l => new Entry()
			// {
			// 	Rank = 
			// })
			
			return Ok(new
			{
				Leaderboard = output
			});
		}
		
		[HttpGet, Route("health"), NoAuth]
		public override ActionResult HealthCheck()
		{
			return Ok(_leaderboardService.HealthCheckResponseObject,
				_resetService.HealthCheckResponseObject
			);
		}
	}
}