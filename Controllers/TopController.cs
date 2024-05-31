using System;
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

	[Flags]
	public enum ScoreMode
	{
		IndividualOnly = 0b0001,
		GuildOnly = 0b0010,
		IndividualAndGuild = 0b0011,
	}
	
	
	[HttpPatch, Route("score"), HealthMonitor(weight: 1)]
	public ActionResult AddScore()
	{
		int score = Require<int>("score");
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		ScoreMode mode = Require<ScoreMode>("mode");
		mode = (ScoreMode)Math.Min((int) ScoreMode.IndividualAndGuild, Math.Max((int)ScoreMode.IndividualOnly, (int)mode));

		return Ok();

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
		string[] types = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE).Split(',');
		string guildId = Optional<string>("guildId");
		
		Enrollment[] enrollments = _enrollmentService.FindMultiple(Token.AccountId, types, guildId);
		List<Leaderboard> shards = _leaderboardService
			.GetShards(enrollments)
			.Where(shard => string.IsNullOrWhiteSpace(shard.GuildId) || enrollments.Select(enrollment => enrollment.GuildId).Contains(shard.GuildId))
			.ToList();

		foreach (Enrollment enrollment in enrollments)
		{
			// Make sure that the base shard definition has a score of 0 for the player.
			if (!shards.Any(shard => shard.Type == enrollment.LeaderboardType && string.IsNullOrWhiteSpace(shard.GuildId)))
				shards.Add(_leaderboardService.AddScore(enrollment, score: 0, false));
			
			// Make sure that the guild shard definition has a score of 0 for the player, but only if the enrollment has a guildId attached.
			if (!string.IsNullOrWhiteSpace(enrollment.GuildId) && !shards.Any(shard => shard.Type == enrollment.LeaderboardType && shard.GuildId == enrollment.GuildId))
				shards.Add(_leaderboardService.AddScore(enrollment, score: 0, true));
		}

		RumbleJson output = new()
		{
			{ "enrollments", enrollments },
			{ "leaderboards", shards
				.Select(shard => shard.GenerateScoreResponse(Token.AccountId))
				.Where(json => json != null)
			}
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