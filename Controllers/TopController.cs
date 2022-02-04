using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
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
		private Services.LeaderboardService _leaderboardService;
		private RegistryService _registryService;

		private ResetService _resetService;
		// private ResetService _resetService;

		[HttpPatch, Route("score")]
		public ActionResult AddScore()
		{
			int score = Require<int>("score");
			string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);

			Registration registration = _registryService.Find(Token.AccountId);
			Leaderboard leaderboard = _leaderboardService.AddScore(Token.AccountId, type, score);
			if (registration.TryEnroll(leaderboard))
				_registryService.Update(registration);
			
			return Ok();
		}

		// TODO: Move to admin controller
		
		
		[HttpGet, Route("rankings")]
		public ActionResult GetRankings()
		{
			string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
			Leaderboard board = _leaderboardService.Find(Token.AccountId, type);
			
			

			return Ok( new
			{
				LeaderboardId = board.Id,
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
			
			return Ok(new
			{
				Leaderboard = output
			});
		}
		
		#region LOAD_BALANCER
		[HttpGet, Route("health"), NoAuth]
		public override ActionResult HealthCheck()
		{
			return Ok(_leaderboardService.HealthCheckResponseObject);
		}
		#endregion LOAD_BALANCER
	}
}