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
			return _collection.FindOneAndUpdate<Leaderboard>(
				filter: CreateFilter(enrollment),
				update: Builders<Leaderboard>.Update.Inc("Scores.$.Score", score),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			) ?? _collection.FindOneAndUpdate<Leaderboard>(								// If output is null, it means nothing was found for the user, which also means this user 
				filter: leaderboard => leaderboard.Type == enrollment.LeaderboardType	// doesn't yet have a record in the leaderboard.
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
		}

		public void Rollover(RolloverType type)
		{
			string[] output = GetIDsToRollover(type);

			if (output.Length == 0)
				return;

			foreach (string id in output)
				Rollover(id).Wait();
			
			// TODO: Send rewards
			
			// Task<Leaderboard>[] tasks = output
			// 	.Select(Rollover)
			// 	.ToArray();
			
			// Task.WaitAll(tasks);

			// Leaderboard[] ending = Find(leaderboard => leaderboard.RolloverType == type).ToArray(); // TODO: FindOneAndUpdate.ReturnAfter to lock leaderboard
			// foreach (Leaderboard leaderboard in ending)
			// 	Rollover(leaderboard);
		}

		private string[] GetIDsToRollover(RolloverType type) => _collection
			.Find<Leaderboard>(leaderboard => leaderboard.RolloverType == type)
			.Project(Builders<Leaderboard>.Projection.Expression(leaderboard => leaderboard.Id))
			.ToList()
			.ToArray();

		private async Task<Leaderboard> Close(string id) => await _collection.FindOneAndUpdateAsync<Leaderboard>(
			filter: leaderboard => leaderboard.Id == id,
			update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.IsResetting, true),
			options: new FindOneAndUpdateOptions<Leaderboard>()
			{
				ReturnDocument = ReturnDocument.After
			}
		);

		public async Task<Leaderboard> Rollover(string id) => await Rollover(await Close(id));

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
			
			// This grants all rewards to players that qualify for them.  If a 1st place reward instead is supposed to override previous
			// rewards, we'll need to fix the logic here.  TODO.
			foreach (Reward reward in rewards)
				_rewardService
					.Grant(reward, ranks
						.Where(ranking => ranking.Rank <= reward.MinimumRank)
						.SelectMany(ranking => ranking.Accounts)
						.ToArray()
					);

			GenericData data = new GenericData()
			{
				{ "ranks", ranks },
				{ "rewards", rewards }
			};
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
			_enrollmentService.DemoteInactiveAccounts(leaderboard.Type);
			_enrollmentService.FlagAsActive(activePlayers, leaderboard.Type);			// If players were flagged as active last week, clear that flag now.
			if (leaderboard.Tier < leaderboard.MaxTier)
				_enrollmentService.PromotePlayers(promotionPlayers, leaderboard.Type);	// Players above the minimum tier promotion rank get moved up.
			if (leaderboard.Tier > 1)													// People can't get demoted below 1.
				_enrollmentService.DemotePlayers(demotionPlayers, leaderboard.Type);	// Players that were previously inactive need to be demoted one rank, if applicable.
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