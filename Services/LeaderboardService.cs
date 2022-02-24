using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders.Physical;
using MongoDB.Bson;
using MongoDB.Driver;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class LeaderboardService : PlatformMongoService<Leaderboard>
	{
		private readonly ArchiveService _archiveService;
		private readonly EnrollmentService _enrollmentService;
		private readonly RewardsService _rewardService;
		// public LeaderboardService(ArchiveService service) : base("leaderboards") => _archiveService = service;
		public LeaderboardService(ArchiveService archives, EnrollmentService enrollments, RewardsService reward) : base("leaderboards")
		{
			_archiveService = archives;
			_enrollmentService = enrollments;
			_rewardService = reward;
		}

		internal long Count(string type) => _collection.CountDocuments(filter: leaderboard => leaderboard.Type == type);

		public Leaderboard Find(string accountId, string type) => AddScore(_enrollmentService.FindOrCreate(accountId, type), 0);

		public long UpdateLeaderboardType(Leaderboard template)
		{
			// string[] foo = _enrollmentService.FindActiveAccounts(template.Type);
			var maxTier = _collection
				.Find(filter: leaderboard => leaderboard.Type == template.Type)
				.Project<GenericData>(Builders<Leaderboard>.Projection.Include(leaderboard => leaderboard.Tier))
				.ToList()
				.Max(data => data.Require<int>(Leaderboard.DB_KEY_TIER));
				// .Max();
			
			
			long output = _collection.UpdateMany(
				filter: leaderboard => leaderboard.Type == template.Type,
				update: Builders<Leaderboard>.Update
					.Set(leaderboard => leaderboard.Description, template.Description)
					.Set(leaderboard => leaderboard.Title, template.Title)
					.Set(leaderboard => leaderboard.RolloverType, template.RolloverType)
					.Set(leaderboard => leaderboard.TierRules, template.TierRules)
					.Set(leaderboard => leaderboard.PlayersPerShard, template.PlayersPerShard)
					.Set(leaderboard => leaderboard.MaxTier, template.MaxTier)
			).ModifiedCount;
			return output;
		}

		private FilterDefinition<Leaderboard> CreateFilter(Enrollment enrollment)
		{
			FilterDefinitionBuilder<Leaderboard> filter = Builders<Leaderboard>.Filter;
			return filter.And(
				filter.Eq(leaderboard => leaderboard.Type, enrollment.LeaderboardType),
				filter.Eq(leaderboard => leaderboard.Tier, enrollment.Tier),
				filter.ElemMatch(leaderboard => leaderboard.Scores, entry => entry.AccountID == enrollment.AccountID)
			); 
		}
		
		public void GetScores(string accountId, string type)
		{
			// TODO: Aggregation in Mongo with the C# drivers is very, very poorly documented.
			// This should be done with a query, but in the interest of speed, will order with LINQ.
			BsonArray query = new BsonArray
			{
				new BsonDocument("$project",
					new BsonDocument("Scores", 1)),
				new BsonDocument("$unwind",
					new BsonDocument
					{
						{ "path", "$Scores" },
						{ "preserveNullAndEmptyArrays", false }
					}),
				new BsonDocument("$sort",
					new BsonDocument("Scores.Score", -1))
			};
		}
		
		// TODO: Fix filter to work with sharding
		public Leaderboard AddScore(Enrollment enrollment, int score)
		{
			Leaderboard output = _collection.FindOneAndUpdate<Leaderboard>(
				filter: CreateFilter(enrollment),
				update: Builders<Leaderboard>.Update.Inc("Scores.$.Score", score),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);
			var foo = _collection.Find(leaderboard => leaderboard.Type == enrollment.LeaderboardType).ToList();
			// If output is null, it means nothing was found for the user, which also means this user doesn't yet have a record in the leaderboard.
			output ??= _collection.FindOneAndUpdate<Leaderboard>(
				filter: leaderboard => leaderboard.Type == enrollment.LeaderboardType
					&& leaderboard.Tier == enrollment.Tier,
				update: Builders<Leaderboard>.Update
					.AddToSet(leaderboard => leaderboard.Scores, new Entry()
					{
						AccountID = enrollment.AccountID,
						Score = score
					}),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);

			return output;
		}

		public void Rollover(RolloverType type)
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
				

			// Async option for debugging since Rider's breakpoints trigger on every thread
			// foreach (string id in ids)
			// 	Rollover(id).Wait();
			
			
			Task<Leaderboard>[] tasks = ids
				.Select(Rollover)
				.ToArray();
			Task.WaitAll(tasks);
			
			
			// TODO: Send rewards
		}
		//
		// private string[] GetIDsToRollover(RolloverType type) => _collection
		// 	.Find<Leaderboard>(leaderboard => leaderboard.RolloverType == type)
		// 	.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Id))
		// 	.ToList()
		// 	.ToArray();

		private async Task<Leaderboard> Close(string id) => await SetRolloverFlag(id, isResetting: true);
		private async Task<Leaderboard> Reopen(string id) => await SetRolloverFlag(id, isResetting: false);
		
		private async Task<Leaderboard> SetRolloverFlag(string id, bool isResetting) => await _collection.FindOneAndUpdateAsync<Leaderboard>(
			filter: leaderboard => leaderboard.Id == id,
			update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.IsResetting, isResetting),
			options: new FindOneAndUpdateOptions<Leaderboard>()
			{
				ReturnDocument = ReturnDocument.After
			}
		);

		/// <summary>
		/// Close the leaderboard, roll it over, then reopen it.  While a leaderboard has a flag for "isResetting",
		/// it cannot be modified.
		/// </summary>
		/// <param name="id">The MongoDB ID of the leaderboard to modify.</param>
		/// <returns>An awaitable task returning the reopened leaderboard.</returns>
		/// Note: The final null coalesce is to handle cases where we're rolling over a shard, and the shard has been deleted.
		public async Task<Leaderboard> Rollover(string id) => await Reopen((await Rollover(await Close(id)))?.Id ?? id);

		private async Task<Leaderboard> Rollover(Leaderboard leaderboard)
		{
			List<Ranking> ranks = leaderboard.CalculateRanks();
			string[] promotionPlayers = ranks
				.Where(ranking => ranking.Rank <= leaderboard.CurrentTierRules.PromotionRank && ranking.Score > 0)
				.SelectMany(ranking => ranking.Accounts)
				.ToArray();
			string[] inactivePlayers = ranks
				.Where(ranking => ranking.Score == 0)
				.SelectMany(ranking => ranking.Accounts)
				.ToArray();
			string[] demotionPlayers = ranks
				.Where(ranking => ranking.Rank >= leaderboard.CurrentTierRules.DemotionRank && leaderboard.CurrentTierRules.DemotionRank > -1)
				.SelectMany(ranking => ranking.Accounts)
				.Union(inactivePlayers)
				.ToArray();
			
			Reward[] rewards = leaderboard.CurrentTierRewards
				.OrderByDescending(reward => reward.MinimumPercentile)
				.ThenByDescending(reward => reward.MinimumRank)
				.ToArray();

			int playerCount = leaderboard.Scores.Count;
			int playersProcessed = 0;

			for (int index = ranks.Count - 1; playersProcessed < playerCount && index >= 0; index--)
			{
				float percentile = 100f * (float)playersProcessed / (float)playerCount;

				ranks[index].Prize = rewards
					.Where(reward => reward.MinimumRank > 0 && ranks[index].Rank <= reward.MinimumRank)
					.OrderBy(reward => reward.MinimumRank)
					.FirstOrDefault();

				ranks[index].Prize ??= rewards
					.Where(reward => reward.MinimumPercentile >= 0 && percentile >= reward.MinimumPercentile)
					.OrderByDescending(reward => reward.MinimumPercentile)
					.FirstOrDefault();
				
				playersProcessed += ranks[index].NumberOfAccounts;
			}

			foreach (Reward reward in rewards)
				_rewardService.Grant(reward, ranks
					.Where(ranking => ranking.Prize?.TemporaryID == reward.TemporaryID)
					.SelectMany(ranking => ranking.Accounts)
					.ToArray());
			
			// await SlackDiagnostics.Log($"Leaderboard Rollover for {leaderboard.Id}", "Rewards calculated!")
			// 	.Tag(Owner.Will)
			// 	.Attach("data.txt", data.JSON)
			// 	.Send();

			_archiveService.Stash(leaderboard);
			leaderboard.Scores = new List<Entry>();

			string[] activePlayers = ranks
				.Where(ranking => ranking.Score != 0)
				.SelectMany(ranking => ranking.Accounts)
				.ToArray();
			// _enrollmentService.DemoteInactivePlayers(leaderboard);
			_enrollmentService.FlagAsActive(activePlayers, leaderboard.Type);			// If players were flagged as active last week, clear that flag now.
			if (leaderboard.Tier < leaderboard.MaxTier)
				_enrollmentService.PromotePlayers(promotionPlayers, leaderboard);		// Players above the minimum tier promotion rank get moved up.
			if (leaderboard.Tier > 1)													// People can't get demoted below 1.
				_enrollmentService.DemotePlayers(demotionPlayers, leaderboard);			// Players that were previously inactive need to be demoted one rank, if applicable.
			_enrollmentService.FlagAsInactive(inactivePlayers, leaderboard.Type);		// Players that scored 0 this week are to be flagged as inactive now.  Must happen after the demotion.
			
			if (!leaderboard.IsShard)	// This is a global leaderboard; we can leave the Scores field empty and just return.
			{
				Update(leaderboard);
				return leaderboard;
			}
			Delete(leaderboard);		// Leaderboard shards are not permanent.  IDs are to be reassigned to new Shards, so they need to be recreated from scratch.
			// TODO: Respawn and fill shards, as appropriate
			return null;
		}
	}
}
// View leaderboard
// Set score for leaderboard (Admin)
// MS remaining?