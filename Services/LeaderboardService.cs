using System;
using System.Linq;
using MongoDB.Driver;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class LeaderboardService : PlatformMongoService<Leaderboard>
	{
		public LeaderboardService() : base("leaderboards") { }

		internal long Count(string type) => _collection.CountDocuments(filter: leaderboard => leaderboard.Type == type);
		

		// public Leaderboard SetScore(string accountId, string type, int score)
		// {
		// 	// StartTransactionIfRequested(out IClientSessionHandle session);
		// 	
		// 	// if (session != null)
		// 	Leaderboard result = _collection.FindOneAndUpdate<Leaderboard>(
		// 		filter: leaderboard => leaderboard.Type == type,
		// 		update: Builders<Leaderboard>.Update
		// 			.Set(leaderbo)
		// 			.Set(l => l.Scores[accountId], score),
		// 		options: new FindOneAndUpdateOptions<Leaderboard>()
		// 		{
		// 			ReturnDocument = ReturnDocument.After,
		// 			IsUpsert = false
		// 		}
		// 	);
		//
		// 	return result;
		// }

		// TODO: Fix filter to work with sharding
		public Leaderboard AddScore(string accountId, string type, int score)
		{
			Leaderboard output = null;

			Entry entry = new Entry()
			{
				AccountID = accountId,
				Score = score
			};

			try
			{
				output = _collection.FindOneAndUpdate<Leaderboard>(
					filter: leaderboard => leaderboard.Type == type,
					update: Builders<Leaderboard>.Update
						.Inc(leaderboard => leaderboard.Scores.First(entry => entry.AccountID == accountId).Score, score),
					options: new FindOneAndUpdateOptions<Leaderboard>()
					{
						ReturnDocument = ReturnDocument.After,
						IsUpsert = false
					}
				);
			}
			catch (FormatException)
			{
				output = _collection.FindOneAndUpdate<Leaderboard>(
					filter: leaderboard => leaderboard.Type == type,
					update: Builders<Leaderboard>.Update
						.AddToSet(leaderboard => leaderboard.Scores, entry),
					options: new FindOneAndUpdateOptions<Leaderboard>()
					{
						ReturnDocument = ReturnDocument.After,
						IsUpsert = false
					}
				);
			}
			catch (Exception ex)
			{
				var foo = "bar";
			}
			


			return output;


		}
	}
}