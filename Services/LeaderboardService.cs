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
		

		public Leaderboard SetScore(string accountId, string type, int score)
		{
			// StartTransactionIfRequested(out IClientSessionHandle session);
			
			// if (session != null)
			Leaderboard result = _collection.FindOneAndUpdate<Leaderboard>(
				filter: leaderboard => leaderboard.Type == type,
				update: Builders<Leaderboard>.Update
					.Set(l => l.Scores[accountId], score),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);

			return result;
		}

		// TODO: Fix filter to work with sharding
		public Leaderboard AddScore(string accountId, string type, int score)
		{
			return _collection.FindOneAndUpdate<Leaderboard>(
				filter: leaderboard => leaderboard.Type == type,
				update: Builders<Leaderboard>.Update
					.Inc(leaderboard => leaderboard.Scores[accountId], score),
				options: new FindOneAndUpdateOptions<Leaderboard>()
				{
					ReturnDocument = ReturnDocument.After,
					IsUpsert = false
				}
			);
		}
	}
}