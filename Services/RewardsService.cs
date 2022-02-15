using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class RewardsService : PlatformMongoService<RewardHistory>
	{
		public RewardsService() : base("rewards") { }

		public long Grant(Reward reward, params string[] accountIds) => _collection.UpdateMany(
				filter: Builders<RewardHistory>.Filter.In(history => history.AccountId, accountIds),
				update: Builders<RewardHistory>.Update.AddToSet(history => history.Rewards, reward),
				options: new UpdateOptions()
				{
					IsUpsert = true
				}
			).ModifiedCount;

		// public void Grant(string accountId, Reward newReward)
		// {
		// 	if (newReward.LeaderboardId == null)
		// 		throw new Exception("All rewards must include the leaderboard objectId that spawned them.");
		// 	RewardHistory record = _collection.FindOneAndUpdate<RewardHistory>(
		// 		filter: history => history.AccountId == accountId,
		// 		update: Builders<RewardHistory>.Update.AddToSet(history => history.Rewards, newReward),
		// 		options: new FindOneAndUpdateOptions<RewardHistory>()
		// 		{
		// 			ReturnDocument = ReturnDocument.After,
		// 			IsUpsert = true
		// 		}
		// 	);
		// 	if (record.Rewards.Select(reward => reward.LeaderboardId).Distinct().Count() != record.Rewards.Count)
		// 		Log.Error(Owner.Will, "A player was granted more than one award by a leaderboard.");
		// }

		public void SendRewards()
		{
			RewardHistory[] histories = Find(history => history.Rewards.Any(reward => reward.SentStatus == Reward.Status.NotSent))
				.ToArray();
			
			// TODO: Send to mailbox-service

		}
	}

	public class RewardHistory : PlatformCollectionDocument
	{
		public string AccountId { get; set; }
		public List<Reward> Rewards { get; set; }

		public RewardHistory() => Rewards = new List<Reward>();
		
		
	}
}