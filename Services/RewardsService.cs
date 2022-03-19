using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class RewardsService : PlatformMongoService<RewardHistory>
{
	private readonly ApiService _apiService;
	private readonly DynamicConfigService _dynamicConfig;

	public RewardsService(ApiService apiService, DynamicConfigService config) : base("rewards")
	{
		_apiService = apiService;
		_dynamicConfig = config;
	}

	public long Grant(Reward reward, params string[] accountIds) => accountIds.Length == 0
		? 0
		: _collection.UpdateMany(
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
		string adminToken = _dynamicConfig.GameConfig.Require<string>("leaderboard_AdminToken");
		string platformUrl = _dynamicConfig.GameConfig.Require<string>("platformUrl_C#");
		RewardHistory[] histories = Find(history => history.Rewards.Any(reward => reward.SentStatus == Reward.Status.NotSent));

		if (!histories.Any())
			return;


		// List<MailboxMessage> messages = new List<MailboxMessage>();
		List<string> successes = new List<string>();

		foreach (RewardHistory history in histories)
		{
			List<GenericData> msg = new List<GenericData>();
			foreach (Reward reward in history.Rewards)
			{
				if (reward.Expiration == default)
					reward.Expiration = Timestamp.UnixTimeUTCMS + (long)new TimeSpan(days: 30, hours: 0, minutes: 0, seconds: 0).TotalMilliseconds;
				
				msg.Add(new GenericData()
				{
					{Reward.FRIENDLY_KEY_SUBJECT, reward.Subject}
				});
			}

			_apiService
				.Request($"{platformUrl}mail/admin/messages/send/bulk")
				.AddAuthorization(adminToken)
				.SetPayload(new MailboxMessage(history.AccountId, history.Rewards).Payload)
				.OnSuccess(action: (sender, apiResponse) =>
				{
					successes.Add(history.Id);
				})
				.OnFailure(action: (sender, apiResponse) =>
				{
					Log.Error(Owner.Will, "Unable to send leaderboard rewards.", data: new
					{
						mailResponse = apiResponse,
						accountId = history.AccountId,
						historyId = history.Id
					});
				})
				.Post(out GenericData response, out int code);
		}

		long sent = MarkAsSent(successes);
		Log.Info(Owner.Will, $"Successfully sent {sent} players rewards.");
	}
	
	private long MarkAsSent(List<string> ids) => _collection.UpdateMany(
		filter: Builders<RewardHistory>.Filter.In(history => history.Id, ids),
		update: Builders<RewardHistory>.Update.Set($"{RewardHistory.DB_KEY_REWARDS}.$[].{Reward.DB_KEY_STATUS}", Reward.Status.Sent),
		options: new UpdateOptions()
		{
			IsUpsert = false
		}
	).ModifiedCount;
}