using System;
using System.Linq;
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
		public LeaderboardService() : base("leaderboards") { }

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
	}
}