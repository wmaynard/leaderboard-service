using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers
{
	[ApiController, Route("leaderboard"), RequireAuth, UseMongoTransaction]
	public class TopController : PlatformController
	{
#pragma warning disable
		private Services.LeaderboardService _leaderboardService;
		private EnrollmentService _enrollmentService;
		private ResetService _resetService;
		private RewardsService _rewardsService;
		private DynamicConfigService _config;
#pragma warning restore

		[HttpPatch, Route("score")]
		public ActionResult AddScore()
		{
			int score = Require<int>("score");
			string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);

			if (score == 0)
				return Ok();

			Enrollment enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type);
			Leaderboard leaderboard = _leaderboardService.AddScore(enrollment, score);
			_rewardsService.Validate(Token.AccountId);

			if (leaderboard == null)
				throw new UnknownLeaderboardException(type);

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
			
			Enrollment enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type);
			Leaderboard leaderboard = _leaderboardService.AddScore(enrollment, 0);
			// Leaderboard board = _leaderboardService.Find(Token.AccountId, type);
			if (leaderboard == null)
				throw new UnknownLeaderboardException(type);
			
			return Ok( new
			{
				LeaderboardId = leaderboard.Type,
				Tier = leaderboard.Tier,
				SeasonalMaxTier = enrollment.SeasonalMaxTier,
				Response = leaderboard.GenerateScoreResponse(Token.AccountId),
			});
		}
}
}