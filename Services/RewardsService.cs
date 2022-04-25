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

	public long Grant(Reward reward, params string[] accountIds)
	{
		if (!accountIds.Any())
			return 0;

		if (_collection.CountDocuments(filter: Builders<RewardHistory>.Filter.In(history => history.AccountId, accountIds)) != accountIds.Length)
		{
			Log.Warn(Owner.Will, "Reward accounts were missing.  Creating them now.");
			foreach (string accountId in accountIds)
				Validate(accountId);
		}
		
		return _collection.UpdateMany( // TODO: Session?
			filter: Builders<RewardHistory>.Filter.In(history => history.AccountId, accountIds),
			update: Builders<RewardHistory>.Update.AddToSet(history => history.Rewards, reward),
			options: new UpdateOptions()
			{
				IsUpsert = false
			}
		).ModifiedCount;
	}

	/// <summary>
	/// Ensures the rewards record exists for an account.
	/// </summary>
	public RewardHistory Validate(string accountId) => _collection
		.Find(filter: Builders<RewardHistory>.Filter.Eq(history => history.AccountId, accountId))
		.FirstOrDefault()
		?? Create(new RewardHistory()
		{
			AccountId = accountId
		});

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
				reward.VisibleFrom = Timestamp.UnixTime;
				if (reward.Expiration == default)
					reward.Expiration = Timestamp.UnixTime + (long)new TimeSpan(days: 30, hours: 0, minutes: 0, seconds: 0).TotalSeconds;
				
				msg.Add(new GenericData()
				{
					{Reward.FRIENDLY_KEY_SUBJECT, reward.Subject}
				});
			}

			string url = PlatformEnvironment.Url("mail/admin/messages/send/bulk");
			_apiService
				.Request(url)
				.AddAuthorization(adminToken)
				.SetPayload(new MailboxMessage(history.AccountId, history.Rewards).Payload)
				.OnSuccess(action: (sender, apiResponse) =>
				{
					successes.Add(history.Id);
					Log.Local(Owner.Will, $"Sent {history.Rewards.Count} rewards to {history.AccountId}");
				})
				.OnFailure(action: (sender, apiResponse) =>
				{
					Log.Error(Owner.Will, "Unable to send leaderboard rewards.", data: new
					{
						mailResponse = apiResponse,
						accountId = history.AccountId,
						historyId = history.Id,
						url = url
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