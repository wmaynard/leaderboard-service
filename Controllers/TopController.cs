using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;
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
		bool returnShards = Optional<bool>("returnShards");
		ScoreMode mode = Optional<ScoreMode>("mode");
		mode = (ScoreMode)Math.Min((int) ScoreMode.IndividualAndGuild, Math.Max((int)ScoreMode.IndividualOnly, (int)mode));
		
		if (score == 0)
			return Ok();
		
		string guildId = null;
		Enrollment enrollment = null;
		Leaderboard shard = null;
		HashSet<string> idsUpdated = new();
		List<Leaderboard> output = new();
		
		if (mode.HasFlag(ScoreMode.GuildOnly))
		{
			_apiService
				.Request("/guild")
				.AddAuthorization(Token.Authorization)
				.OnSuccess(response => guildId = response.Optional<Guild>("guild")?.Id)
				.OnFailure(_ => {  })
				.Get();
			enrollment ??= _enrollmentService.FindOrCreate(Token.AccountId, type, guildId);
			if (!string.IsNullOrWhiteSpace(guildId))
			{
				shard = _leaderboardService.AddScore(enrollment, score, useGuild: true);
				idsUpdated.Add(shard.Id);
				if (enrollment.CurrentLeaderboardID != shard.Id)
					_enrollmentService.FlagAsActive(enrollment, shard);
				
				if (shard.Scores.Count(entry => entry.AccountID == Token.AccountId) >= 2)
					Log.Error(Owner.Will, "Shard has more than one entry for an account; something is wrong.", data: new
					{
						Shard = shard,
						IncomingScore = score
					});
				
				if (shard.Scores.Count == 1 && shard.Scores.First().Score == score)
					Log.Info(Owner.Will, "New guild shard spawned.", data: new
					{
						Help = "If this happens multiple times for a single account ID / leaderboard type, there may be a problem in Mongo.",
						Shard = shard,
						IncomingSocre = score
					});
				if (returnShards)
					output.Add(shard.Copy());
			}
		}
		
		if (mode.HasFlag(ScoreMode.IndividualOnly))
		{
			enrollment = _enrollmentService.FindOrCreate(Token.AccountId, type, guildId);
			shard = _leaderboardService.AddScore(enrollment, score, useGuild: false);
			idsUpdated.Add(shard.Id);
			if (enrollment.CurrentLeaderboardID != shard.Id)
				_enrollmentService.FlagAsActive(enrollment, shard);
			if (returnShards)
				output.Add(shard.Copy());
		}

		return !returnShards
			? Ok()
			: Ok(new RumbleJson
			{
				{
					"leaderboards", output
						.Select(board => board.GenerateScoreResponse(Token.AccountId))
				}
			});
	}
	
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
			{ "leaderboards", shards   // TODO: Remove the OrderBy / ThenBy here.  This is a kluge to fix a hard-coded index read on the game server to differentiate the public vs guild shard.
				.OrderBy(shard => shard.Type)
				.ThenBy(shard => shard.GuildId)
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