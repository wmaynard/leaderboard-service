using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders.Physical;
using MongoDB.Bson;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class LeaderboardService : PlatformMongoService<Leaderboard>
	{
		private readonly ArchiveService _archiveService;

		private readonly RegistryService _registryService;
		// public LeaderboardService(ArchiveService service) : base("leaderboards") => _archiveService = service;
		public LeaderboardService(ArchiveService archives, RegistryService registry) : base("leaderboards")
		{
			_archiveService = archives;
			_registryService = registry;
		}

		internal long Count(string type) => _collection.CountDocuments(filter: leaderboard => leaderboard.Type == type);

		public Leaderboard Find(string accountId, string type) => AddScore(accountId, type, 0);

		private FilterDefinition<Leaderboard> CreateFilter(string accountId, string type)
		{
			FilterDefinitionBuilder<Leaderboard> filter = Builders<Leaderboard>.Filter;
			return filter.And(
				filter.Eq(leaderboard => leaderboard.Type, type),
				filter.ElemMatch(leaderboard => leaderboard.Scores, entry => entry.AccountID == accountId)
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
		public Leaderboard AddScore(string accountId, string type, int score)
		{
			Leaderboard output = null;

			// We shouldn't really be seeing requests to add 0 to the score, but just in case, we'll assume it might happen.
			// In such a case, just look for the account - don't try to issue any updates.
			output = score == 0
				? _collection
					.Find<Leaderboard>(CreateFilter(accountId, type))
					.FirstOrDefault()
				: _collection.FindOneAndUpdate<Leaderboard>(
					filter: CreateFilter(accountId, type),
					update: Builders<Leaderboard>.Update.Inc("Scores.$.Score", score),
					options: new FindOneAndUpdateOptions<Leaderboard>()
					{
						ReturnDocument = ReturnDocument.After,
						IsUpsert = false
					}
				);
			// If output is null, it means nothing was found for the user, which also means this user doesn't yet have a record in the leaderboard.
			return output ?? _collection.FindOneAndUpdate<Leaderboard>(
					filter: leaderboard => leaderboard.Type == type,
					update: Builders<Leaderboard>.Update
						.AddToSet(leaderboard => leaderboard.Scores, new Entry()
						{
							AccountID = accountId,
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

			Task<Leaderboard>[] tasks = output
				.Select(Rollover)
				.ToArray();
			
			Task.WaitAll(tasks);

			Leaderboard[] ending = Find(leaderboard => leaderboard.RolloverType == type).ToArray(); // TODO: FindOneAndUpdate.ReturnAfter to lock leaderboard
			foreach (Leaderboard leaderboard in ending)
				Rollover(leaderboard);
		}
		
		private string[] GetIDsToRollover(RolloverType type) => _collection
			.Find<Leaderboard>(leaderboard => leaderboard.RolloverType == type)
			.Project<string>(Builders<Leaderboard>.Projection.Include(leaderboard => leaderboard.Id))
			.ToList()
			.ToArray();

		// private async Task<Leaderboard> Close(RolloverType type) => await _collection.FindOneAndUpdateAsync<Leaderboard>(
		// 		filter: leaderboard => leaderboard.RolloverType == type, 
		// 		update: Builders<Leaderboard>.Update.Set(leaderboard => leaderboard.IsResetting, true),
		// 		options: new FindOneAndUpdateOptions<Leaderboard>()
		// 		{
		// 			ReturnDocument = ReturnDocument.After
		// 		}
		// 	);

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
			// TODO: Promotions
			// TODO: Issue rewards
			_archiveService.Stash(leaderboard);
			leaderboard.Scores = new List<Entry>();

			string[] activePlayers = ranks
				.Where(ranking => ranking.Score != 0)
				.SelectMany(ranking => ranking.Accounts)
				.ToArray();
			string[] inactivePlayers = ranks
				.Where(ranking => ranking.Score == 0)
				.SelectMany(ranking => ranking.Accounts)
				.ToArray();
			_registryService.FlagAsActive(activePlayers, leaderboard.Type);				// If players were flagged as active last week, clear that flag now.
			_registryService.DemoteInactivePlayers(inactivePlayers, leaderboard.Type);	// Players that were previously inactive need to be demoted one rank, if applicable.
			_registryService.FlagAsInactive(inactivePlayers, leaderboard.Type);			// Players that scored 0 this week are to be flagged as inactive now.
			
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