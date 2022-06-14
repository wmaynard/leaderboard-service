using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders.Physical;
using MongoDB.Bson;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class LeaderboardService : PlatformMongoService<Leaderboard>
{
	private readonly ArchiveService _archiveService;
	private readonly EnrollmentService _enrollmentService;
	private readonly RewardsService _rewardService;
	private readonly BroadcastService _broadcastService;
	// public LeaderboardService(ArchiveService service) : base("leaderboards") => _archiveService = service;
	public LeaderboardService(ArchiveService archives, BroadcastService broadcast, EnrollmentService enrollments, RewardsService reward) : base("leaderboards")
	{
		_archiveService = archives;
		_broadcastService = broadcast;
		_enrollmentService = enrollments;
		_rewardService = reward;
	}

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
					.Set(leaderboard => leaderboard.Title, template.Title)
					.Set(leaderboard => leaderboard.RolloverType, template.RolloverType)
					.Set(leaderboard => leaderboard.TierRules, template.TierRules)
					.Set(leaderboard => leaderboard.TierCount, template.TierCount)
			).ModifiedCount
			: _collection.UpdateMany(
				filter: leaderboard => leaderboard.Type == template.Type,
				update: Builders<Leaderboard>.Update
					.Set(leaderboard => leaderboard.Description, template.Description)
					.Set(leaderboard => leaderboard.Title, template.Title)
					.Set(leaderboard => leaderboard.RolloverType, template.RolloverType)
					.Set(leaderboard => leaderboard.TierRules, template.TierRules)
					.Set(leaderboard => leaderboard.TierCount, template.TierCount)
			).ModifiedCount;
	}

	private FilterDefinition<Leaderboard> CreateFilter(Enrollment enrollment, bool allowLocked = false)
	{
		if (!allowLocked)
			EnsureUnlocked(enrollment);
		
		FilterDefinitionBuilder<Leaderboard> filter = Builders<Leaderboard>.Filter;
		return filter.And(
			filter.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
			filter.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
			filter.ElemMatch(leaderboard => leaderboard.Scores, entry => entry.AccountID == enrollment.AccountID)
		); 
	}

	private void EnsureUnlocked(Enrollment enrollment)
	{
		FilterDefinitionBuilder<Leaderboard> builder = Builders<Leaderboard>.Filter;
		FilterDefinition<Leaderboard> filter = builder.And(
			builder.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
			builder.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
			builder.ElemMatch(leaderboard => leaderboard.Scores, entry => entry.AccountID == enrollment.AccountID),
			builder.Lte(leaderboard => leaderboard.StartTime, Timestamp.UnixTime)
		);

		List<bool> locks = _collection
			.Find(filter: filter)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.IsResetting || leaderboard.StartTime > Timestamp.UnixTime))
			.ToList();

		if (locks.Any(isResetting => isResetting))
			throw new PlatformException("A leaderboard is locked; try again later.");
	}

	public Leaderboard SetScore(Enrollment enrollment, long score, bool isIncrement = false)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);

		if (enrollment == null || score < 0)
			return null;

		UpdateDefinition<Leaderboard> update = Builders<Leaderboard>.Update
			.Set($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}", score)
			.Set($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_LAST_UPDATED}", Timestamp.UnixTimeMS);

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

	// TODO: Fix filter to work with sharding
	public Leaderboard AddScore(Enrollment enrollment, int score)
	{
		StartTransactionIfRequested(out IClientSessionHandle session);
		
		UpdateDefinition<Leaderboard> update = Builders<Leaderboard>.Update.Inc($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}", score);
			
		if (score != 0)
			update = update.Set($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_LAST_UPDATED}", Timestamp.UnixTimeMS);

		Leaderboard output = _collection.FindOneAndUpdate<Leaderboard>(
			filter: CreateFilter(enrollment, allowLocked: false),
			session: session,
			update: update,
			options: new FindOneAndUpdateOptions<Leaderboard>()
			{
				ReturnDocument = ReturnDocument.After,
				IsUpsert = false
			}
		);

		if (session != null)
			// If output is null, it means nothing was found for the user, which also means this user doesn't yet have a record in the leaderboard.
			output ??= _collection.FindOneAndUpdate<Leaderboard>(
				filter: leaderboard => leaderboard.Type == enrollment.LeaderboardType
					&& leaderboard.Tier == enrollment.Tier,
				session: session,
				update: Builders<Leaderboard>.Update
					.AddToSet(leaderboard => leaderboard.Scores, new Entry()
					{
						AccountID = enrollment.AccountID,
						Score = Math.Max(score, 0), // Ensure the user's score is at least 0.
						LastUpdated = Timestamp.UnixTimeMS
					}),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);
		else
			output ??= _collection.FindOneAndUpdate<Leaderboard>(
				filter: leaderboard => leaderboard.Type == enrollment.LeaderboardType
					&& leaderboard.Tier == enrollment.Tier,
				update: Builders<Leaderboard>.Update
					.AddToSet(leaderboard => leaderboard.Scores, new Entry()
					{
						AccountID = enrollment.AccountID,
						Score = Math.Max(score, 0), // Ensure the user's score is at least 0.
						LastUpdated = Timestamp.UnixTimeMS
					}),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);

		// If the score is negative, there's a chance the user was pushed below 0.  This *should* be handled first in the client / server, which shouldn't send us values that
		// push someone below 0.
		// Negative values in the first place should be exceedingly rare, bordering on disallowed.  The one exception is the ability to use the leaderboards-service to track
		// trophy count for PvP, which does require fluctuating values.  All other scoring criteria should be additive only.
		// Still, we need to account for a scenario in which someone has been pushed below 0.  No one should be allowed to do that, so if that's the case, this update
		// will set all negative scores to 0.
		if (score < 0 && output.Scores.Any(entry => entry.Score < 0))
		{
			Log.Warn(Owner.Will, "Scores were found to be below zero.  Updating all scores to a minimum of 0.", data: new
			{
				LeaderboardId = output.Id,
				Type = output.Type,
				Enrollment = enrollment
			});
			if (session != null)
				output = _collection.FindOneAndUpdate<Leaderboard>(
					filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Id, output.Id),
					session: session,
					update: Builders<Leaderboard>.Update.Max($"{Leaderboard.DB_KEY_SCORES}.$[].{Entry.DB_KEY_SCORE}", 0), // Updates all scores to be minimum of 0.
					options: new FindOneAndUpdateOptions<Leaderboard>()
					{
						ReturnDocument = ReturnDocument.After,
						IsUpsert = false
					}
				);
			else
				output = _collection.FindOneAndUpdate<Leaderboard>(
					filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Id, output.Id),
					update: Builders<Leaderboard>.Update.Max($"{Leaderboard.DB_KEY_SCORES}.$[].{Entry.DB_KEY_SCORE}", 0), // Updates all scores to be minimum of 0.
					options: new FindOneAndUpdateOptions<Leaderboard>()
					{
						ReturnDocument = ReturnDocument.After,
						IsUpsert = false
					}
				);
		}

		return output;
	}

	public string[] ListLeaderboardTypes()
	{
		return _collection
			.Find(leaderboard => true)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Type))
			.ToList()
			.Distinct()
			.ToArray();
	}

	public async Task Rollover(RolloverType type)
	{
		// This gives us a collection of GenericData objects of just the ID and the Type of the leaderboards.
		// This is an optimization to prevent passing in huge amounts of data - once we hit a global release, retrieving all
		// leaderboard data would result in very large data sets, and would be very slow.  We should only grab what we need,
		// especially since rollover operations will already require a significant amount of time to complete.
		GenericData[] data = _collection
			.Find<Leaderboard>(leaderboard => leaderboard.RolloverType == type)
			.Project<GenericData>(Builders<Leaderboard>.Projection.Expression(leaderboard => new GenericData()
			{
				{ Leaderboard.DB_KEY_ID, leaderboard.Id },
				{ Leaderboard.DB_KEY_TYPE, leaderboard.Type }
			}))
			.ToList()
			.ToArray();
		
		// We need the leaderboard types to trigger inactive player demotions for all leaderboards of a specified type.
		// This was originally handled in the individual leaderboard rollover, but if there were 6 tiers of that leaderboard,
		// it would cause 6 demotions.  We only want to run one update for inactive players, and we need to do it before any
		// leaderboards roll over (which marks players with a score of 0 as inactive).
		string[] types = data
			.Select(generic => generic.Require<string>(Leaderboard.DB_KEY_TYPE))
			.Distinct()
			.ToArray();

		// We need the leaderboard IDs to individually trigger leaderboard rollover.
		string[] ids = data
			.Select(generic => generic.Require<string>(Leaderboard.DB_KEY_ID))
			.ToArray();
		
		if (ids.Length == 0)
			return;
		
		foreach (string leaderboardType in types)
			_enrollmentService.DemoteInactivePlayers(leaderboardType);

		foreach (string id in ids)
			await Rollover(id);
		
		_rewardService.SendRewards();
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
			options: new FindOneAndUpdateOptions<Leaderboard>()
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

	private async Task<Leaderboard> Rollover(Leaderboard leaderboard)
	{
		// Unlike other methods here, we actually don't want to start a transaction here, since this gets called from
		// the ResetService.  Use the one generated there instead.
		List<Entry> ranks = leaderboard.CalculateRanks();
		string[] promotionPlayers = ranks
			.Where(entry => entry.Rank <= leaderboard.CurrentTierRules.PromotionRank && entry.Score > 0)
			.Select(entry => entry.AccountID)
			.ToArray();
		string[] inactivePlayers = ranks
			.Where(entry => entry.Score == 0)
			.Select(entry => entry.AccountID)
			.ToArray();
		string[] demotionPlayers = ranks
			.Where(entry => entry.Rank >= leaderboard.CurrentTierRules.DemotionRank && leaderboard.CurrentTierRules.DemotionRank > -1)
			.Select(entry => entry.AccountID)
			.Union(inactivePlayers)
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
				.MinBy(reward => reward.MinimumRank);

			ranks[index].Prize ??= rewards
				.Where(reward => reward.MinimumPercentile >= 0 && percentile >= reward.MinimumPercentile)
				.MaxBy(reward => reward.MinimumPercentile);

			// Add required fields for bulk sending and telemetry
			// ranks[index].Prize.Recipient = ranks[index].AccountID;
			ranks[index].Prize.RankingData = new GenericData
			{
				{ "leaderboardId", leaderboard.Type },
				{ "leaderboardRank", ranks[index].Rank + 1 },
				{ "leaderboardScore", ranks[index].Score },
				{ "leaderboardTier", leaderboard.Tier },
				{ "leaderboardArchiveId", archive.Id }
			};

			playersProcessed++;
		}

		foreach (Entry entry in ranks.Where(e => e.Score > 0))
			_rewardService.Grant(entry.Prize, accountIds: entry.AccountID);

		_enrollmentService.LinkArchive(leaderboard.Scores.Select(entry => entry.AccountID), leaderboard.Type, archive.Id);
		
		leaderboard.Scores = new List<Entry>();

		string[] activePlayers = ranks
			.Where(entry => entry.Score != 0)
			.Select(entry => entry.AccountID)
			.ToArray();
		// _enrollmentService.DemoteInactivePlayers(leaderboard);
		_enrollmentService.FlagAsActive(activePlayers, leaderboard.Type);			// If players were flagged as active last week, clear that flag now.
		if (leaderboard.Tier < leaderboard.MaxTier)
			_enrollmentService.PromotePlayers(promotionPlayers, leaderboard);		// Players above the minimum tier promotion rank get moved up.
		if (leaderboard.Tier > 1)													// People can't get demoted below 1.
			_enrollmentService.DemotePlayers(demotionPlayers, leaderboard);			// Players that were previously inactive need to be demoted one rank, if applicable.
		_enrollmentService.FlagAsInactive(inactivePlayers, leaderboard.Type);		// Players that scored 0 this week are to be flagged as inactive now.  Must happen after the demotion.
		
		
		
		// TODO: Design needs to provide details on how this message should be formatted.
		try
		{
			string first = ranks.First().AccountID;
			#if DEBUG
			_broadcastService.Announce(first, $"{first} placed first in Leaderboard {leaderboard.Type}!  This is a placeholder message.");
			#endif
		}
		catch { }
		
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

			Log.Info(Owner.Default, $"{leaderboard.Type} rollover information sent to Slack.");
		}
		catch
		{
			Log.Error(Owner.Default, $"{leaderboard.Type} rollover information could not be sent to Slack.");
		}
		
		if (!leaderboard.IsShard)	// This is a global leaderboard; we can leave the Scores field empty and just return.
		{
			Update(leaderboard);
			return leaderboard;
		}
		Delete(leaderboard);		// Leaderboard shards are not permanent.  IDs are to be reassigned to new Shards, so they need to be recreated from scratch.
		// TODO: Respawn and fill shards, as appropriate
		return leaderboard;
	}

	public List<Entry> CalculateTopScores(Enrollment enrollment)
	{
		List<Entry> output = _collection
			.Find(filter: CreateFilter(enrollment, allowLocked: true))
			.Sort(Builders<Leaderboard>.Sort.Descending($"{Leaderboard.DB_KEY_SCORES}.$.{Entry.DB_KEY_SCORE}"))
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Scores))
			.Limit(Leaderboard.PAGE_SIZE)
			.ToList()
			.FirstOrDefault();
		return output;
	}
}