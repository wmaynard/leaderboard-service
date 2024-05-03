using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Interop;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[ApiController, Route("leaderboard"), RequireAuth]
public class TopController : PlatformController
{
#pragma warning disable
	private Services.LeaderboardService _leaderboardService;
	private EnrollmentService _enrollmentService;
	private RewardsService _rewardsService;
#pragma warning restore

	[HttpPatch, Route("score"), HealthMonitor(weight: 1)]
	public ActionResult AddScore()
	{
		int score = Require<int>("score");
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);

		if (score == 0)
			return Ok();

		string guildId = Optional<string>("guildId");
		bool useGuild = !string.IsNullOrWhiteSpace(guildId) && guildId.CanBeMongoId();
		
		if (useGuild && !IsInGuild(Token.Authorization, guildId))
			throw new PlatformException("Attempted to score on guild leaderboard shard but not in specified guild.");

		Enrollment enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type, guildId);
		Leaderboard leaderboard = _leaderboardService.AddScore(enrollment, score, useGuild);

		if (enrollment.CurrentLeaderboardID != leaderboard.Id)
			_enrollmentService.FlagAsActive(enrollment, leaderboard);

		return Ok();
	}

	private bool IsInGuild(string token, string guildId)
	{
		bool output = true;
		_apiService
			.Request("http://localhost:5101/guild")
			.AddAuthorization(token)
			.OnSuccess(response =>
			{
				output = guildId == response.Optional<Guild>("guild")?.Id;
			})
			.OnFailure(_ => output = false)
			.Get();

		return output;
	}

	// TODO: Move to admin controller
	
	
	[HttpGet, Route("rankings")]
	public ActionResult GetRankings()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		string guildId = Optional<string>("guildId");
		
		Enrollment enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type);
		Leaderboard[] shards = _leaderboardService.GetShards(enrollment);
		if (!shards.Any())
			shards = new[] { _leaderboardService.AddScore(enrollment, score: 0, false) };

		if (!string.IsNullOrWhiteSpace(guildId))
			shards = shards
				.Where(shard => string.IsNullOrWhiteSpace(shard.GuildId) || shard.GuildId == guildId)
				.ToArray();

		RumbleJson output = new()
		{
			{ "enrollment", enrollment },
			{ "leaderboards", shards.Select(shard => shard.GenerateScoreResponse(Token.AccountId)).Where(json => json != null) }
		};
		
		return Ok(output);
	}

	[HttpGet, Route("enrollments"), HealthMonitor(weight: 5)]
	public ActionResult GetEnrollments()
	{
		string typeString = Optional<string>("leaderboardIds");
		
		List<Enrollment> enrollments = _enrollmentService.Find(Token.AccountId, typeString);

		return Ok(new RumbleJson
		{
			{ "enrollments", enrollments } 
		});
	}

	[HttpDelete, Route("notification")]
	public ActionResult AcknowledgeRollover()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);

		_enrollmentService.AcknowledgeRollover(Token.AccountId, type);
		
		return Ok();
	}

	[HttpGet, Route("")]
	public ActionResult LeaderboardById()
	{
		Leaderboard output = _leaderboardService.FindById(Require<string>("id"));
		
		return Ok(new RumbleJson
		{
			{ "leaderboard", output },
			{ "entries", output?.CalculateRanks() }
		});
	}
}