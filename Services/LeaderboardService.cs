using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders.Physical;
using MongoDB.Bson;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class LeaderboardService : PlatformMongoService<Leaderboard>
{
	private readonly ArchiveService _archiveService;
	private readonly EnrollmentService _enrollmentService;
	private readonly RewardsService _rewardService;
	private readonly BroadcastService _broadcastService;
	public static LeaderboardService Instance { get; private set; }
	// public LeaderboardService(ArchiveService service) : base("leaderboards") => _archiveService = service;
	public LeaderboardService(ArchiveService archives, BroadcastService broadcast, EnrollmentService enrollments, RewardsService reward) : base("leaderboards")
	{
		_archiveService = archives;
		_broadcastService = broadcast;
		_enrollmentService = enrollments;
		_rewardService = reward;
		Instance = this;
	}

	public ShardStat[] ProjectShardStats() => _collection
		.Find(_ => true)
		.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => new
		{
			Type = leaderboard.Type,
			PlayerCount = leaderboard.Scores.Count,
			Tier = leaderboard.Tier
		}))
		.ToList()
		.GroupBy(obj => new
		{
			obj.Type,
			obj.Tier
		})
		.Select(group => new ShardStat
		{
			LeaderboardId = group.First().Type,
			Tier = group.First().Tier,
			PlayerCounts = group
				.Select(g => (long)g.PlayerCount)
				.OrderByDescending(_ => _)
				.ToArray()
		})
		.OrderBy(stat => stat.LeaderboardId)
		.ThenBy(stat => stat.Tier)
		.ToArray();

	public Leaderboard Find(string id) => _collection.Find(filter: leaderboard => leaderboard.Id == id).FirstOrDefault();

	internal long Count(string type) => _collection.CountDocuments(filter: leaderboard => leaderboard.Type == type);

	public long UpdateLeaderboardType(Leaderboard template)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);
		
		return session != null 
			? _collection.UpdateMany(
				filter: leaderboard => leaderboard.Type == template.Type,
				session: session,
				update: Builders<Leaderboard>.Update
					.Set(leaderboard => leaderboard.Description, template.Description)
					.Set(leaderboard => leaderboard.RolloverType, template.RolloverType)
					.Set(leaderboard => leaderboard.TierRules, template.TierRules)
					.Set(leaderboard => leaderboard.TierCount, template.TierCount)
					.Set(leaderboard => leaderboard.Title, template.Title)
					.Set(leaderboard => leaderboard.StartTime, template.StartTime)
					.Set(leaderboard => leaderboard.EndTime, template.EndTime)
			).ModifiedCount
			: _collection.UpdateMany(
				filter: leaderboard => leaderboard.Type == template.Type,
				update: Builders<Leaderboard>.Update
					.Set(leaderboard => leaderboard.Description, template.Description)
					.Set(leaderboard => leaderboard.RolloverType, template.RolloverType)
					.Set(leaderboard => leaderboard.TierRules, template.TierRules)
					.Set(leaderboard => leaderboard.TierCount, template.TierCount)
					.Set(leaderboard => leaderboard.Title, template.Title)
					.Set(leaderboard => leaderboard.StartTime, template.StartTime)
					.Set(leaderboard => leaderboard.EndTime, template.EndTime)
			).ModifiedCount;
	}

	private FilterDefinition<Leaderboard> CreateFilter(Enrollment enrollment, bool useGuild = false, bool allowLocked = false)
	{
		allowLocked = allowLocked || PlatformEnvironment.IsDev;
		if (!allowLocked)
			EnsureUnlocked(enrollment);
		
		FilterDefinitionBuilder<Leaderboard> filter = Builders<Leaderboard>.Filter;

		List<FilterDefinition<Leaderboard>> filters = new()
		{
			filter.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
			filter.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
			filter.ElemMatch(
				field: leaderboard => leaderboard.Scores,
				filter: entry => entry.AccountID == enrollment.AccountID
			)
		};
		
		if (useGuild && !string.IsNullOrWhiteSpace(enrollment.GuildId))
			filters.Add(filter.Eq(leaderboard => leaderboard.GuildId, enrollment.GuildId));
		else
			filters.Add(filter.Eq(leaderboard => leaderboard.GuildId, null));
		
		return filter.And(filters); 
	}
	
	

	private FilterDefinition<Leaderboard> CreateShardFilter(Enrollment enrollment, int playersPerShard)
	{
		FilterDefinitionBuilder<Leaderboard> filter = Builders<Leaderboard>.Filter;
		return filter.And(
			filter.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
			filter.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
			// filter.Eq(leaderboard => leaderboard.IsFull, false),
			filter.SizeLt(leaderboard => leaderboard.Scores, playersPerShard) // This condition ignores negative values and returns true
		);
	}
	
	/// <summary>
	/// Attempts to append a user's score to an existing score in a leaderboard.
	/// </summary>
	/// <param name="enrollment">The user's enrollment information.</param>
	/// <param name="score">Points that the user scored for the current leaderboard.</param>
	/// <param name="session">A Mongo session to complete all transactions in.</param>
	/// <returns>The leaderboard the user is in, if updated.  Otherwise returns null.</returns>
	private Leaderboard AddToExistingScore(Enrollment enrollment, long score, bool useGuild, IClientSessionHandle session = null)
	{
		FilterDefinition<Leaderboard> filter = CreateFilter(enrollment, useGuild, allowLocked: false);
		UpdateDefinition<Leaderboard> update = Builders<Leaderboard>.Update.Inc($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}", score);
		
		// This adds a timestamp to nonzero scores; it doesn't overwrite the above incrementation.
		if (score != 0)
			update = update.Set($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_LAST_UPDATED}", TimestampMs.Now);
		
		FindOneAndUpdateOptions<Leaderboard> options = new()
		{
			ReturnDocument = ReturnDocument.After,
			IsUpsert = false
		};

		Leaderboard output = session == null
			? _collection.FindOneAndUpdate<Leaderboard>(
				filter: filter,
				update: update,
				options: options
			)
			: _collection.FindOneAndUpdate<Leaderboard>(
				filter: filter,
				session: session,
				update: update,
				options: options
			);

		// TD-20905 Fix for Guild leaderboards creating separate shards for every guild member rather than adding them to the same shard.
		if (output == null && useGuild)
		{
			Leaderboard guildShard = _collection.Find(
					Builders<Leaderboard>.Filter.And(
						Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
						Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
						Builders<Leaderboard>.Filter.Or(
							Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.GuildId, enrollment.GuildId),
							Builders<Leaderboard>.Filter.Exists(leaderboard => leaderboard.GuildId, false),
							Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.GuildId, null)
						)
					)
				)
				.SortByDescending(leaderboard => leaderboard.GuildId)  // The sort here makes sure that the template shard is last
				.ThenBy(leaderboard => leaderboard.CreatedOn)          // If somehow we have more than one guild shard created, use the oldest one
				.FirstOrDefault();

			// If the Guild ID is null, no guild shard exists for this leaderboard yet; create one.
			if (string.IsNullOrWhiteSpace(guildShard.GuildId))
			{
				guildShard.ChangeId();
				guildShard.Scores = new()
				{
					new Entry
					{
						AccountID = enrollment.AccountID,
						Score = 0
					}
				};
				guildShard.GuildId = enrollment.GuildId;
				guildShard.ShardID = ObjectId.GenerateNewId().ToString();
				_collection.InsertOne(guildShard);

				return AddToExistingScore(enrollment, score, useGuild, session);
			}
			// If the guild shard doesn't have the current player, add them to the set and return the leaderboard.
			if (guildShard.Scores.All(entry => entry.AccountID != enrollment.AccountID))
				return _collection.FindOneAndUpdate(
					filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Id, guildShard.Id),
					update: Builders<Leaderboard>.Update.AddToSet(leaderboard => leaderboard.Scores, new Entry
					{
						AccountID = enrollment.AccountID,
						LastUpdated = Timestamp.Now,
						Score = score
					}),
					options: new FindOneAndUpdateOptions<Leaderboard>
					{
						ReturnDocument = ReturnDocument.After
					}
				);
		}

		return output;
	}
	
	/// <summary>
	/// Attempts to add a new score entry to a leaderboard.  Returns null if there are no available shards to enter.
	/// </summary>
	/// <param name="enrollment">The user's enrollment information.</param>
	/// <param name="score">Points that the user scored for the current leaderboard.</param>
	/// <param name="session">A Mongo session to complete all transactions in.</param>
	/// <returns>The leaderboard the user is in, if updated.  Otherwise returns null.</returns>
	private Leaderboard AppendNewEntry(Enrollment enrollment, long score, IClientSessionHandle session = null)
	{
		TierRules rules = _collection
			.Find(leaderboard => leaderboard.Type == enrollment.LeaderboardType)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.TierRules))
			.FirstOrDefault()
			?.FirstOrDefault(rules => rules.Tier == enrollment.Tier);

		if (rules == null)
			return null;
		
		FilterDefinition<Leaderboard> filter = CreateShardFilter(enrollment, rules.PlayersPerShard);
		UpdateDefinition<Leaderboard> update = Builders<Leaderboard>.Update.AddToSet(leaderboard => leaderboard.Scores, new Entry
		{
			AccountID = enrollment.AccountID,
			Score = Math.Max(score, 0), // Ensure the user's score is at least 0.
			LastUpdated = TimestampMs.Now
		});
		FindOneAndUpdateOptions<Leaderboard> options = new FindOneAndUpdateOptions<Leaderboard>
		{
			ReturnDocument = ReturnDocument.After,
			IsUpsert = false
		};
		
		return session == null
			? _collection.FindOneAndUpdate<Leaderboard>(
				filter: filter,
				update: update,
				options: options
			)
			: _collection.FindOneAndUpdate<Leaderboard>(
				filter: filter,
				session: session,
				update: update,
				options: options
			);
	}
	
	/// <summary>
	/// Creates a shard of a leaderboard with the user's current score in it.  Once all other updates fail, this gets called to score the user.
	/// </summary>
	/// <param name="enrollment">The user's enrollment information.</param>
	/// <param name="score">Points that the user scored for the current leaderboard.</param>
	/// <param name="session">A Mongo session to complete all transactions in.</param>
	/// <returns>The new leaderboard shard for the current user.  Should never be null.</returns>
	private Leaderboard SpawnShard(Enrollment enrollment, long score, IClientSessionHandle session = null)
	{
		Leaderboard template = _collection
			.Find(leaderboard => leaderboard.Type == enrollment.LeaderboardType && leaderboard.Tier == enrollment.Tier)
			.FirstOrDefault();

		if (template == null)
			return null;
			// throw new PlatformException("Leaderboard not found.  This is a problem with a Mongo query.", code: ErrorCode.MongoRecordNotFound);
		
		template.ChangeId();
		template.Scores = new List<Entry> { new Entry
		{
			AccountID = enrollment.AccountID,
			LastUpdated = Timestamp.Now,
			Score = score
		}};
		template.ShardID = ObjectId.GenerateNewId().ToString();
		
		if (session != null)
			_collection.InsertOne(session, template);
		else
			_collection.InsertOne(template);
		return template;
	}

	private void FloorScores(Leaderboard leaderboard,  IClientSessionHandle session)
	{
		Log.Warn(Owner.Will, "Scores were found to be below zero.  Updating all scores to a minimum of 0.", data: new
		{
			LeaderboardId = leaderboard.Id,
			Type = leaderboard.Type
		});

		// If the score is negative, there's a chance the user was pushed below 0.  This *should* be handled first in the client / server, which shouldn't send us values that
		// push someone below 0.
		// Negative values in the first place should be exceedingly rare, bordering on disallowed.  The one exception is the ability to use the leaderboards-service to track
		// trophy count for PvP, which does require fluctuating values.  All other scoring criteria should be additive only.
		// Still, we need to account for a scenario in which someone has been pushed below 0.  No one should be allowed to do that, so if that's the case, this update
		// will set all negative scores to 0.
		FilterDefinition<Leaderboard> filter = Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Id, leaderboard.Id);
		UpdateDefinition<Leaderboard> update = Builders<Leaderboard>.Update.Max($"{Leaderboard.DB_KEY_SCORES}.$[].{Entry.DB_KEY_SCORE}", value: 0);
		FindOneAndUpdateOptions<Leaderboard> options = new FindOneAndUpdateOptions<Leaderboard>
		{
			ReturnDocument = ReturnDocument.After,
			IsUpsert = false
		};
		
		leaderboard = session == null
			? _collection.FindOneAndUpdate<Leaderboard>(
				filter: filter,
				update: update,
				options: options
			)
			: _collection.FindOneAndUpdate<Leaderboard>(
				filter: filter,
				session: session,
				update: update,
				options: options
			);
	}

	private void EnsureUnlocked(Enrollment enrollment)
	{
		FilterDefinitionBuilder<Leaderboard> builder = Builders<Leaderboard>.Filter;
		FilterDefinition<Leaderboard> filter = builder.And(
			builder.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
			builder.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
			builder.ElemMatch(leaderboard => leaderboard.Scores, entry => entry.AccountID == enrollment.AccountID)
		);

		List<bool> locks = _collection
			.Find(filter: filter)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.IsResetting || leaderboard.StartTime > Timestamp.Now))
			.ToList();

		if (locks.Any(isResetting => isResetting))
			throw new PlatformException("A leaderboard is locked; try again later.", code: ErrorCode.LeaderboardUnavailable);
	}

	public Leaderboard SetScore(Enrollment enrollment, long score, bool isIncrement = false)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);

		if (enrollment == null || score < 0)
			return null;

		UpdateDefinition<Leaderboard> update = Builders<Leaderboard>.Update
			.Set($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}", score)
			.Set($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_LAST_UPDATED}", TimestampMs.Now);

		if (session == null)
			return _collection.FindOneAndUpdate<Leaderboard>(
				filter: CreateFilter(enrollment, allowLocked: false),
				update: Builders<Leaderboard>.Update.Set("Scores.$.Score", score),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);
		return _collection.FindOneAndUpdate<Leaderboard>(
			filter: CreateFilter(enrollment, allowLocked: false),
			session: session,
			update: Builders<Leaderboard>.Update.Set("Scores.$.Score", score),
			options: new FindOneAndUpdateOptions<Leaderboard>()
			{
				ReturnDocument = ReturnDocument.After,
				IsUpsert = false
			}
		);
	}
	
	/// <summary>
	/// If a leaderboard is updated to remove tiers, it's possible for players to end up with a tier above that which
	/// actually exists.  This method is responsible for demoting players who are above that tier.  Ideally, this method
	/// should never get called; it's simply a safety measure.
	/// </summary>
	/// <param name="enrollment">The player's enrollment information, indicating which leaderboard type and tier we should use.</param>
	/// <param name="score">The amount of score to add to a player's record.</param>
	/// <returns>The appropriate leaderboard shard a player entered.</returns>
	/// <exception cref="UnknownLeaderboardException">Thrown when an eligible shard cannot be found.</exception>
	private Leaderboard RetryScoreWithDemotion(Enrollment enrollment, int score)
	{
		int[] tiers = _collection
			.Find(leaderboard => leaderboard.Type == enrollment.LeaderboardType)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Tier))
			.ToList()
			.Distinct()
			.OrderByDescending(_ => _)
			.ToArray();

		if (!tiers.Any())
			throw new UnknownLeaderboardException(enrollment);

		int previous = enrollment.Tier;
		enrollment.Tier = Math.Min(tiers.First(), enrollment.Tier);
		
		if (enrollment.Tier == previous)
			throw new UnknownLeaderboardException(enrollment);
		
		_enrollmentService.Update(enrollment);
		
		return AddScore(enrollment, score, withRetry: false) 
			?? throw new UnknownLeaderboardException(enrollment);
	}

	/// <summary>
	/// Attempts to add a score to a leaderboard.  Even a score of 0 will place a player into a shard.
	/// </summary>
	/// <param name="enrollment">The player's enrollment information, indicating which leaderboard type and tier we should use.</param>
	/// <param name="score">The amount of score to add to a player's record.</param>
	/// <param name="withRetry">If true, will attempt to demote the player to an appropriate tier before throwing an exception.</param>
	/// <param name="useGuild">If true, will attempt to return the shard for the guild.  If one is not found, it will be created.</param>
	/// <returns>A leaderboard if it was successful; otherwise null.</returns
	private Leaderboard AddScore(Enrollment enrollment, int score, bool withRetry, bool useGuild = false)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);

		Leaderboard output = AddToExistingScore(enrollment, score, useGuild, session);
		// If output is null, it means nothing was found for the user, which also means this user doesn't yet have a record in the leaderboard.
		output ??= AppendNewEntry(enrollment, score, session);
		
		// If output is null, it means we need a new shard.
		output ??= SpawnShard(enrollment, score, session);

		// If the output is STILL null, perhaps the leaderboard tiers were updated and the enrollment is asking for a tier that doesn't exist.
		if (withRetry)
			output ??= RetryScoreWithDemotion(enrollment, score);
		else if (output == null)
			return null;
		
		if (score < 0) // No need to go down this path if the score can't decrease.
			FloorScores(output, session);

		return output;
	}

	/// <summary>
	/// Attempts to add a score to a leaderboard.  Even a score of 0 will place a player into a shard.
	/// </summary>
	/// <param name="enrollment">The player's enrollment information, indicating which leaderboard type and tier we should use.</param>
	/// <param name="score">The amount of score to add to a player's record.</param>
	/// <returns>A leaderboard if it was successful; otherwise an UnknownLeaderboardException will be thrown.</returns>
	public Leaderboard AddScore(Enrollment enrollment, int score, bool useGuild) => AddScore(enrollment, score, withRetry: true, useGuild: useGuild);

	public Leaderboard[] GetShards(Enrollment enrollment) => _collection
		.Find(
			Builders<Leaderboard>.Filter.And(
				Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
				Builders<Leaderboard>.Filter.ElemMatch(leaderboard => leaderboard.Scores, Builders<Entry>.Filter.Eq(entry => entry.AccountID, enrollment.AccountID))
		))
		.ToList()
		.ToArray();

	public Leaderboard[] GetShards(Enrollment[] enrollments) => _collection
		.Find(
			Builders<Leaderboard>.Filter.And(
				Builders<Leaderboard>.Filter.In(leaderboard => leaderboard.Type, enrollments.Select(enrollment => enrollment.LeaderboardType)),
				Builders<Leaderboard>.Filter.ElemMatch(leaderboard => leaderboard.Scores, 
					Builders<Entry>.Filter.Eq(entry => entry.AccountID, enrollments.First().AccountID))
			))
		.ToList()
		.ToArray();

	public string[] ListLeaderboardTypes() => _collection
		.Find(leaderboard => true)
		.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Type))
		.ToList()
		.Distinct()
		.ToArray();

	public void BeginRollover(RolloverType type, out string[] ids, out string[] types)
	{
		RumbleJson[] data = _collection
			.Find(leaderboard => leaderboard.RolloverType == type)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => new RumbleJson
			{
				{ Leaderboard.DB_KEY_ID, leaderboard.Id },
				{ Leaderboard.DB_KEY_TYPE, leaderboard.Type }
			}))
			.ToList()
			.ToArray();
		
		ids = data
			.Select(generic => generic.Require<string>(Leaderboard.DB_KEY_ID))
			.ToArray();
		
		types = data
			.Select(json => json.Require<string>(Leaderboard.DB_KEY_TYPE))
			.Distinct()
			.ToArray();
	}

	// TODO: This really needs an out struct[].
	public void BeginRollover(RolloverType type, out RumbleJson[] leaderboards)
	{
		leaderboards = Array.Empty<RumbleJson>();
		
		FilterDefinition<Leaderboard> filter = Builders<Leaderboard>.Filter.And(
			Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.RolloverType, type),
			Builders<Leaderboard>.Filter.Lte(leaderboard => leaderboard.StartTime, Timestamp.Now)
		);
		long count = _collection.CountDocuments(filter);
		
		if (count == 0)
			return;

		// PLATF-6497: Something changed between 9/25 -> 10/13 that broke the projection here.  Projecting to a
		// RumbleJson throws a bizarre error message:
		//     One or more errors occurred. (When called from 'VisitListInit', rewriting a node of type
		//     'System.Linq.Expressions.NewExpression' must return a non-null value of the same type. Alternatively,
		//     override 'VisitListInit' and change it to not visit children of this type.)
		// The modified method chaining is a kluge; the intent is to eventually replace this with MINQ and a proper
		// fix, if needed, will come with that transition.
		leaderboards = _collection
			.Find(filter)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => new Leaderboard
			{
				ShardID = leaderboard.Id,
				Type = leaderboard.Type
			}))
			.ToList()
			.Select(leaderboard => new RumbleJson
			{
				{ Leaderboard.DB_KEY_ID, leaderboard.ShardID },
				{ Leaderboard.DB_KEY_TYPE, leaderboard.Type }
			})
			.ToArray();
	} 

	private async Task<Leaderboard> Close(string id) => await SetRolloverFlag(id, isResetting: true);

	public int DeleteType(IEnumerable<string> types)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);
		
		return session != null
			? (int)_collection
				.DeleteMany(
					filter: Builders<Leaderboard>.Filter.In(leaderboard => leaderboard.Type, types),
					session: session
				).DeletedCount
			: (int)_collection
				.DeleteMany(
					filter: Builders<Leaderboard>.Filter.In(leaderboard => leaderboard.Type, types)
				).DeletedCount;
	}
	private async Task<Leaderboard> Reopen(string id) => await SetRolloverFlag(id, isResetting: false);

	private async Task<Leaderboard> SetRolloverFlag(string id, bool isResetting)
	{
		// Since this is called from a timed service, it doesn't see controller attributes.
		// StartTransaction(out IClientSessionHandle session);
		
		return await _collection.FindOneAndUpdateAsync<Leaderboard>(
			filter: leaderboard => leaderboard.Id == id,
			// session: session,
			update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.IsResetting, isResetting),
			options: new FindOneAndUpdateOptions<Leaderboard>
			{
				ReturnDocument = ReturnDocument.After
			}
		);
	}

	/// <summary>
	/// Close the leaderboard, roll it over, then reopen it.  While a leaderboard has a flag for "isResetting",
	/// it cannot be modified.
	/// </summary>
	/// <param name="id">The MongoDB ID of the leaderboard to modify.</param>
	/// <returns>An awaitable task returning the reopened leaderboard.</returns>
	/// Note: The final null coalesce is to handle cases where we're rolling over a shard, and the shard has been deleted.
	public async Task<Leaderboard> Rollover(string id)
	{
		Leaderboard leaderboard = await Close(id);
		await Rollover(leaderboard);
		return await Reopen(id);
	}

	private bool LeaderboardIsActive(string leaderboardType) => _collection
		.CountDocuments(
			filter: Builders<Leaderboard>.Filter.Lte(field: leaderboard => leaderboard.StartTime, Timestamp.Now)
		) != 0;

	private async Task<Leaderboard> Rollover(Leaderboard leaderboard)
	{
#if DEBUG
		if (!leaderboard.Scores.Any())
			return leaderboard;
#endif
		// Unlike other methods here, we actually don't want to start a transaction here, since this gets called from
		// the ResetService.  Use the one generated there instead.
		List<Entry> ranks = leaderboard.CalculateRanks();

		if (!leaderboard.Scores.Any())
			Log.Verbose(Owner.Will, "Leaderboard shard has no valid scores to grant rewards to; if this is a guild shard, the guild likely disbanded.", data: new
			{
				ShardId = leaderboard.ShardID
			});
		else
		{
			string[] promotionPlayers = ranks
				.Where(entry => entry.Rank <= leaderboard.CurrentTierRules.PromotionRank && entry.Score > 0)
				.Select(entry => entry.AccountID)
				.ToArray();
			string[] demotionPlayers = ranks
				.Where(entry => entry.Rank >= leaderboard.CurrentTierRules.DemotionRank && leaderboard.CurrentTierRules.DemotionRank > -1)
				.Select(entry => entry.AccountID)
				.ToArray();

			bool useGuildRewards = !string.IsNullOrWhiteSpace(leaderboard.GuildId);
			Reward[] rewards = leaderboard.CurrentTierRewards
				.Where(reward => reward.ForGuild == useGuildRewards)
				.OrderByDescending(reward => reward.MinimumPercentile)
				.ThenByDescending(reward => reward.MinimumRank)
				.ToArray();

			int playerCount = leaderboard.Scores.Count;
			int playersProcessed = 0;

			_archiveService.Stash(leaderboard, out Leaderboard archive);

			if (!useGuildRewards || rewards.Any())
			{
				for (int index = ranks.Count - 1; playersProcessed < playerCount && index >= 0; index--)
				{
					float percentile = 100f * (float) playersProcessed / (float) playerCount;

					ranks[index].Prize = rewards
						.Where(reward => reward.MinimumRank > 0 && ranks[index].Rank <= reward.MinimumRank)
						.MinBy(reward => reward.MinimumRank)
						.Copy();

					ranks[index].Prize ??= rewards
						.Where(reward => reward.MinimumPercentile >= 0 && percentile >= reward.MinimumPercentile)
						.MaxBy(reward => reward.MinimumPercentile)
						.Copy();

					// TD-15557: Must null-check here; without it, a leaderboard with no / inadequate prize definitions
					// causes rollover to fail.
					// Add required fields for bulk sending and telemetry
					// ranks[index].Prize.Recipient = ranks[index].AccountID;
					if (ranks[index].Prize != null)
						ranks[index].Prize.RankingData = new RumbleJson
						{
							{"rewardType", "standard"},
							{"leaderboardId", leaderboard.Type},
							{"leaderboardRank", ranks[index].Rank},
							{"leaderboardScore", ranks[index].Score},
							{"leaderboardTier", leaderboard.Tier},
							{"leaderboardArchiveId", archive.Id}
						};

					playersProcessed++;
				}

				foreach (Entry entry in ranks.Where(e => e.Score > 0))
					_rewardService.Grant(entry.Prize, accountIds: entry.AccountID);
			}

			_enrollmentService.LinkArchive(leaderboard.Scores.Select(entry => entry.AccountID), leaderboard.Type, archive.Id, leaderboard.Id);
			
			if (string.IsNullOrWhiteSpace(leaderboard.GuildId) && leaderboard.MaxTier > 0)
			{
				if (promotionPlayers.Length + demotionPlayers.Length > 0)
					Log.Local(Owner.Will, $"ID: {leaderboard.Id} Demotion: {demotionPlayers.Length} Promotion: {promotionPlayers.Length}", emphasis: Log.LogType.WARN);
				if (leaderboard.Tier < leaderboard.MaxTier)
					_enrollmentService.PromotePlayers(promotionPlayers, leaderboard);		// Players above the minimum tier promotion rank get moved up.
				if (leaderboard.Tier > 0)													// People can't get demoted below 0.
					_enrollmentService.DemotePlayers(demotionPlayers, leaderboard);			// Players that were previously inactive need to be demoted one rank, if applicable.
			}

			// TODO: Design needs to provide details on how this message should be formatted.
			#if DEBUG
			try
			{
				string first = ranks.First().AccountID;
				_broadcastService.Announce(first, $"{first} placed first in Leaderboard {leaderboard.Type}!  This is a placeholder message.");
			}
			catch { }
			#endif
		}
		
		if (leaderboard.IsShard)
		{
			Delete(leaderboard);		// Leaderboard shards are not permanent.  IDs are to be reassigned to new Shards, so they need to be recreated from scratch.
			return leaderboard;
		}
		
		ResetScores(leaderboard.Id);
		return leaderboard;
	}

	private void ResetScores(string id) => _collection
		.UpdateOne(
			filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Id, id),
			update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.Scores, new List<Entry>())
		);

	public List<Entry> CalculateTopScores(Enrollment enrollment) => _collection
		.Find(filter: CreateFilter(enrollment, allowLocked: true))
		.Sort(Builders<Leaderboard>.Sort.Descending($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}"))
		.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Scores))
		.Limit(Leaderboard.PAGE_SIZE)
		.ToList()
		.FirstOrDefault();
	
	public Leaderboard FindById(string id) => _collection
		.Find(Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Id, id))
		.FirstOrDefault()
		?? throw new PlatformException("Leaderboard not found.");

	public long RemovePlayer(string accountId, string type) => _collection.UpdateMany(
		filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
		update: Builders<Leaderboard>.Update.PullFilter(
			field: leaderboard => leaderboard.Scores,
			filter: Builders<Entry>.Filter.Eq(entry => entry.AccountID, accountId)
		)
	).ModifiedCount;

	public int[] GetTiersOf(string type) => _collection
		.Find(Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type))
		.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Tier))
		.ToList()
		.Distinct()
		.OrderBy(_ => _)
		.ToArray();

	/// <summary>
	/// This is used exclusively for debugging archive leaderboards.
	/// </summary>
	/// <param name="leaderboard"></param>
	/// <returns></returns>
	public Leaderboard Unarchive(string id)
	{
		if (PlatformEnvironment.IsProd)
			throw new EnvironmentPermissionsException();
	
		Leaderboard leaderboard = _archiveService.FindById(id);
		leaderboard.ChangeId();
		_collection.InsertOne(leaderboard);

		return leaderboard;
	}

	public Leaderboard FindBaseLeaderboard(string type, int limit) => _collection
		.Find(Builders<Leaderboard>.Filter.And(
			Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
			Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.ShardID, null)
		))
		.FirstOrDefault();

	public long UpdateStartTime(string type, long timestamp) => _collection
		.UpdateMany(
			filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
			update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.StartTime, timestamp)
		).ModifiedCount;
}