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
			Tier = leaderboard.Tier,
			RolloversRemaining = leaderboard.RolloversRemaining
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
				.ToArray(),
			RolloversRemaining = group.First().RolloversRemaining
		})
		.OrderBy(stat => stat.LeaderboardId)
		.ThenBy(stat => stat.Tier)
		.ToArray();

	public Leaderboard Find(string id) => _collection.Find(filter: leaderboard => leaderboard.Id == id).FirstOrDefault();

	internal long Count(string type) => _collection.CountDocuments(filter: leaderboard => leaderboard.Type == type);

	public Leaderboard Find(string accountId, string type) => AddScore(_enrollmentService.FindOrCreate(accountId, type), 0);

	public long UpdateLeaderboardType(Leaderboard template)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);
		
		return session != null 
			? _collection.UpdateMany(
				filter: leaderboard => leaderboard.Type == template.Type,
				session: session,
				update: Builders<Leaderboard>.Update
					.Set(leaderboard => leaderboard.Description, template.Description)
					.Set(leaderboard => leaderboard.RolloversInSeason, template.RolloversInSeason)
					// .Set(leaderboard => leaderboard.RolloversRemaining, template.RolloversRemaining)
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
					.Set(leaderboard => leaderboard.RolloversInSeason, template.RolloversInSeason)
					// .Set(leaderboard => leaderboard.RolloversRemaining, template.RolloversRemaining)
					.Set(leaderboard => leaderboard.RolloverType, template.RolloverType)
					.Set(leaderboard => leaderboard.TierRules, template.TierRules)
					.Set(leaderboard => leaderboard.TierCount, template.TierCount)
					.Set(leaderboard => leaderboard.Title, template.Title)
					.Set(leaderboard => leaderboard.StartTime, template.StartTime)
					.Set(leaderboard => leaderboard.EndTime, template.EndTime)
			).ModifiedCount;
	}

	private FilterDefinition<Leaderboard> CreateFilter(Enrollment enrollment, bool allowLocked = false)
	{
		allowLocked = allowLocked || PlatformEnvironment.IsDev;
		if (!allowLocked)
			EnsureUnlocked(enrollment);
		
		FilterDefinitionBuilder<Leaderboard> filter = Builders<Leaderboard>.Filter;
		return filter.And(
			filter.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
			filter.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
			filter.ElemMatch(
				field: leaderboard => leaderboard.Scores, 
				filter: entry => entry.AccountID == enrollment.AccountID
			)
		); 
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
	private Leaderboard AddToExistingScore(Enrollment enrollment, long score, IClientSessionHandle session = null)
	{
		FilterDefinition<Leaderboard> filter = CreateFilter(enrollment, allowLocked: false);
		UpdateDefinition<Leaderboard> update = Builders<Leaderboard>.Update.Inc($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}", score);
		
		// This adds a timestamp to nonzero scores; it doesn't overwrite the above incrementation.
		if (score != 0)
			update = update.Set($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_LAST_UPDATED}", TimestampMs.Now);
		
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
	/// <returns>A leaderboard if it was successful; otherwise null.</returns
	private Leaderboard AddScore(Enrollment enrollment, int score, bool withRetry)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);

		Leaderboard output = AddToExistingScore(enrollment, score, session);
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
	public Leaderboard AddScore(Enrollment enrollment, int score) => AddScore(enrollment, score, withRetry: true);

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
		
		leaderboards = _collection
			.Find(filter)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => new RumbleJson
			{
				{ Leaderboard.DB_KEY_ID, leaderboard.Id },
				{ Leaderboard.DB_KEY_TYPE, leaderboard.Type }
			}))
			.ToList()
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

	public long DecreaseSeasonCounter(string type) => LeaderboardIsActive(type)
		? _collection.UpdateMany(
			filter: Builders<Leaderboard>.Filter.And(
				Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
				Builders<Leaderboard>.Filter.Gt(leaderboard => leaderboard.RolloversInSeason, 0)
			),
			update: Builders<Leaderboard>.Update.Inc(leaderboard => leaderboard.RolloversRemaining, -1)
		).ModifiedCount
		: 0;

	private async Task<Leaderboard> Rollover(Leaderboard leaderboard)
	{
#if DEBUG
		if (!leaderboard.Scores.Any())
			return leaderboard;
#endif
		// Unlike other methods here, we actually don't want to start a transaction here, since this gets called from
		// the ResetService.  Use the one generated there instead.
		List<Entry> ranks = leaderboard.CalculateRanks();
		string[] promotionPlayers = ranks
			.Where(entry => entry.Rank <= leaderboard.CurrentTierRules.PromotionRank && entry.Score > 0)
			.Select(entry => entry.AccountID)
			.ToArray();
		string[] demotionPlayers = ranks
			.Where(entry => entry.Rank >= leaderboard.CurrentTierRules.DemotionRank && leaderboard.CurrentTierRules.DemotionRank > -1)
			.Select(entry => entry.AccountID)
			.ToArray();

		Reward[] rewards = leaderboard.CurrentTierRewards
			.OrderByDescending(reward => reward.MinimumPercentile)
			.ThenByDescending(reward => reward.MinimumRank)
			.ToArray();

		int playerCount = leaderboard.Scores.Count;
		int playersProcessed = 0;

		_archiveService.Stash(leaderboard, out Leaderboard archive);
		
		for (int index = ranks.Count - 1; playersProcessed < playerCount && index >= 0; index--)
		{
			float percentile = 100f * (float)playersProcessed / (float)playerCount;

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
					{ "rewardType", "standard" },
					{ "leaderboardId", leaderboard.Type },
					{ "leaderboardRank", ranks[index].Rank },
					{ "leaderboardScore", ranks[index].Score },
					{ "leaderboardTier", leaderboard.Tier },
					{ "leaderboardArchiveId", archive.Id }
				};

			playersProcessed++;
		}

		foreach (Entry entry in ranks.Where(e => e.Score > 0))
			_rewardService.Grant(entry.Prize, accountIds: entry.AccountID);

		_enrollmentService.LinkArchive(leaderboard.Scores.Select(entry => entry.AccountID), leaderboard.Type, archive.Id, leaderboard.Id);

		// TD-16684: Prior to this ticket, promotion was only enabled if not using seasons, or if the season had at least one rollover remaining.
		// Consequently, players would miss out on the final promotion of the season.  This was built to specification via a Meet call, but
		// later was decided to be undesirable, as players felt the final week of a season was worthless.
		if (leaderboard.MaxTier > 0)
		{
			if (promotionPlayers.Length + demotionPlayers.Length > 0)
				Log.Local(Owner.Will, $"ID: {leaderboard.Id} Demotion: {demotionPlayers.Length} Promotion: {promotionPlayers.Length}", emphasis: Log.LogType.WARN);
			if (leaderboard.Tier < leaderboard.MaxTier)
				_enrollmentService.PromotePlayers(promotionPlayers, leaderboard);		// Players above the minimum tier promotion rank get moved up.
			if (leaderboard.Tier > 0)													// People can't get demoted below 0.
				_enrollmentService.DemotePlayers(demotionPlayers, leaderboard);			// Players that were previously inactive need to be demoted one rank, if applicable.
		}

		// TODO: Design needs to provide details on how this message should be formatted.
		try
		{
			string first = ranks.First().AccountID;
			#if DEBUG
			_broadcastService.Announce(first, $"{first} placed first in Leaderboard {leaderboard.Type}!  This is a placeholder message.");
			#endif
		}
		catch { }
		
		// Send the rollover Slack message
		if (playerCount > 0)
		{
			try
			{
				// TODO: Once Portal has a Leaderboards UI, remove this.
				
				string attachment = string.Join(Environment.NewLine, ranks.Where(entry => entry.Prize != null && entry.Score > 0).Select(rank => rank.ToString()));
				int rewardPlayerCount = ranks.Count(entry => entry.Prize != null && entry.Score > 0);
				int noRewardPlayerCount = ranks.Count(entry => entry.Prize == null);

				string message = $"Player ranks have been calculated.\n{rewardPlayerCount} player(s) received rewards.";

				if (noRewardPlayerCount > 0)
					message += $"\n{noRewardPlayerCount} player(s) did not receive rewards.";

				await SlackDiagnostics
					.Log(title: $"{leaderboard.Type} rollover triggered.", message)
					.Attach(name: "Rankings", content: attachment)
					.Send();
			}
			catch
			{
				Log.Error(Owner.Default, $"{leaderboard.Type} rollover information could not be sent to Slack.");
			}
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

	public void RolloverSeasonsIfNeeded(string[] types)
	{
		if (!types.Any())
		{
			Log.Info(Owner.Will, "No need to rollover seasons; no types were specified");
			return;
		}
		
		try
		{
			foreach (string type in types)
			{
				long affected = _collection.UpdateMany(
					filter: Builders<Leaderboard>.Filter.And(
						Builders<Leaderboard>.Filter.Lte(leaderboard => leaderboard.RolloversRemaining, 0),
						Builders<Leaderboard>.Filter.Gt(leaderboard => leaderboard.RolloversInSeason, 0),
						Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type)
					),
					update: PipelineDefinition<Leaderboard, Leaderboard>.Create($"{{ $set: {{ {Leaderboard.DB_KEY_SEASON_COUNTDOWN}: '${Leaderboard.DB_KEY_SEASON_ROLLOVERS}' }} }}")
				).ModifiedCount;
			
				if (affected == 0) // No leaderboards are ready for season rollover; we don't need to do anything to this type.
					continue;
			
				Log.Info(Owner.Will, "Reset season rollover counter.", data: new
				{
					Type = type
				});
			
				Leaderboard board = _collection.Find(leaderboard => leaderboard.Type == type).FirstOrDefault();
				RumbleJson rewardData = new RumbleJson();
					
				for (int tier = 0; tier <= board.MaxTier; tier++)
				{
					Reward prize = null;
					// Grant rewards based on the max season achieved.
					try
					{
						string[] accounts = _enrollmentService.GetSeasonalRewardCandidates(type, tier);
						prize = board.TierRules[tier].SeasonReward;
						prize.RankingData = new RumbleJson
						{
							{ "rewardType", "season" },
							{ "leaderboardId", type },
							{ "leaderboardSeasonalMaxTier", tier },
							{ "leaderboardSeasonalResetTier", board.TierRules[tier].MaxTierOnSeasonReset }
						};
						long granted = _rewardService.Grant(prize, accounts);
						if (granted > 0 && granted == accounts.Length)
							Log.Info(Owner.Will, $"Granted season rewards to players.", data: new
							{
								AccountIds = accounts,
								GrantedCount = granted,
								Count = accounts.Length
							});
						else if (granted > 0)
							Log.Error(Owner.Will, "Granted season rewards to some players but not all", data: new
							{
								AccountIds = accounts,
								GrantedCount = granted,
								Count = accounts.Length
							});
						rewardData[$"tier_{tier}"] = accounts.Length;
					}
					catch (Exception e)
					{
						Log.Error(Owner.Will, "Could not issue season rewards.", data: new
						{
							Prize = prize
						}, exception: e);
					}

					// Demote players based on the max season number.
					try
					{
						_enrollmentService.SeasonDemotion(board.Type, tier, Math.Min(tier, board.TierRules[tier].MaxTierOnSeasonReset));
					}
					catch (Exception e)
					{
						Log.Error(Owner.Default, "Unable to demote players during a season rollover.", exception: e);
					}
				}

				rewardData["total"] = rewardData.Sum(pair => (int)pair.Value);
				Log.Info(Owner.Will, "Season rewards sent.", data: new
				{
					Type = type,
					RewardData = rewardData
				});
				_enrollmentService.ResetSeasonalMaxTier(type);
			}
		}
		catch (Exception e)
		{
			Log.Error(Owner.Will, "Unable to perform season rollover tasks.", exception: e);
		}
	}
	public List<Entry> CalculateTopScores(Enrollment enrollment) => _collection
		.Find(filter: CreateFilter(enrollment, allowLocked: true))
		.Sort(Builders<Leaderboard>.Sort.Descending($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}"))
		.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Scores))
		.Limit(Leaderboard.PAGE_SIZE)
		.ToList()
		.FirstOrDefault();

	/// <summary>
	/// For an unknown reason, the RolloversRemaining field sometimes does not decrement, even though the modified count
	/// from DecreaseSeasonCounter says it affected all records.  It's possible that there's a write conflict somewhere
	/// or a transaction is preventing the write.  This method is not a permanent solution, but rather one of necessity
	/// in the interest of time.
	/// </summary>
	public void RolloverRemainingKluge(string type)
	{
		int minRolloversRemaining = _collection
			.Find(leaderboard => leaderboard.Type == type)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.RolloversRemaining))
			.ToList()
			.OrderBy(_ => _)
			.FirstOrDefault();

		long affected = _collection
			.UpdateMany(
				filter: Builders<Leaderboard>.Filter.And(
					Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
					Builders<Leaderboard>.Filter.Gt(leaderboard => leaderboard.RolloversRemaining, minRolloversRemaining)
				),
				update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.RolloversRemaining, minRolloversRemaining)
			).ModifiedCount;
		
		if (affected > 0)
			Log.Error(Owner.Will, "Rollover counts were out of sync but now fixed.  A Mongo transaction may be misbehaving.");
	}

	public long UpdateSeason(string type, int season, int remaining) => _collection
		.UpdateMany(
			filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
			update: season switch
			{
				> 0 when remaining > 0 => Builders<Leaderboard>.Update
					.Set(leaderboard => leaderboard.RolloversInSeason, season)
					.Set(leaderboard => leaderboard.RolloversRemaining, remaining),
				> 0 => Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.RolloversInSeason, season),
				0 when remaining > 0 => Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.RolloversRemaining, remaining),
				_ => throw new PlatformException("Invalid rollover changes provided.")
			}
		).ModifiedCount;
	
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
	
		Leaderboard leaderboard = _archiveService.Get(id);
		leaderboard.ChangeId();
		_collection.InsertOne(leaderboard);

		return leaderboard;
	}

	public Leaderboard FindBaseLeaderboard(string type, int limit) => _collection
		.Find(Builders<Leaderboard>.Filter.And(
			Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
			Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.ShardID, null)
		))
		// .Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Scores))
		.FirstOrDefault();
	// .OrderByDescending(entry => entry.Score)
	// .Take(limit)
	// .ToList();

	public Leaderboard[] GetRolloversRemaining() => _collection
		.Find(leaderboard => leaderboard.RolloversInSeason > 0)
		.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => new Leaderboard
		{
			Type = leaderboard.Type,
			RolloversRemaining = leaderboard.RolloversRemaining,
			RolloversInSeason = leaderboard.RolloversInSeason
		}))
		.ToList()
		.ToArray();

	public long UpdateStartTime(string type, long timestamp) => _collection
		.UpdateMany(
			filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
			update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.StartTime, timestamp)
		).ModifiedCount;
}