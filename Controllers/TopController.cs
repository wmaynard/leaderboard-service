using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[ApiController, Route("leaderboard"), RequireAuth, UseMongoTransaction]
public class TopController : PlatformController
{
#pragma warning disable
	private Services.LeaderboardService _leaderboardService;
	private EnrollmentService _enrollmentService;
	private RewardsService _rewardsService;
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

		if (enrollment.CurrentLeaderboardID == leaderboard.Id)
			return Ok(new { Leaderboard = leaderboard });
		
		enrollment.CurrentLeaderboardID = leaderboard.Id;
		enrollment.IsActive = true;
		enrollment.IsActiveInSeason = leaderboard.SeasonsEnabled;

		if (enrollment.Status == Enrollment.PromotionStatus.Acknowledged)
			enrollment.ActiveTier = enrollment.Tier;
		_enrollmentService.Update(enrollment);

		return Ok(new { Leaderboard = leaderboard });
	}

	// TODO: Move to admin controller
	
	
	[HttpGet, Route("rankings")]
	public ActionResult GetRankings()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		
		Enrollment enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type);
		Leaderboard leaderboard = _leaderboardService.AddScore(enrollment, score: 0);
		// Leaderboard board = _leaderboardService.Find(Token.AccountId, type);

		RumbleJson output = new RumbleJson
		{
			{ "enrollment", enrollment },
			{ "leaderboard", leaderboard.GenerateScoreResponse(Token.AccountId) }
		};
		
		return Ok(output);
	}

	[HttpDelete, Route("notification")]
	public ActionResult AcknowledgeRollover()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);

		_enrollmentService.AcknowledgeRollover(Token.AccountId, type);
		
		return Ok();
	}
}