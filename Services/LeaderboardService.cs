using System;
using System.Linq;
using Microsoft.Extensions.FileProviders.Physical;
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

		// TODO: Fix filter to work with sharding
		public Leaderboard AddScore(string accountId, string type, int score)
		{
			Leaderboard output = null;

			FilterDefinitionBuilder<Leaderboard> filter = Builders<Leaderboard>.Filter;
			output = _collection.FindOneAndUpdate<Leaderboard>(
				filter: filter.And(
					filter.Eq(leaderboard => leaderboard.Type, type),
					filter.ElemMatch(leaderboard => leaderboard.Scores, entry => entry.AccountID == accountId)
				),
				update: Builders<Leaderboard>.Update.Inc("Scores.$.Score", score),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);
			// If output is null, it means nothing was updated, which also means this user doesn't yet have a record in the leaderboard.
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