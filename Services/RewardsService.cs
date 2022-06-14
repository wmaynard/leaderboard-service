using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RCL.Logging;
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
		if (reward == null || !accountIds.Any())
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
			options: new UpdateOptions
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
		?? Create(new RewardHistory
		{
			AccountId = accountId
		});

	public void SendRewards()
	{
		string adminToken = _dynamicConfig.GameConfig.Require<string>("leaderboard_AdminToken");
		
		RewardHistory[] histories = Find(history => history.Rewards.Any(reward => reward.SentStatus == Reward.Status.NotSent));

		if (!histories.Any())
			return;


		List<string> successes = new List<string>();

		foreach (RewardHistory history in histories)
		{
			Reward[] toSend = history.Rewards.Where(reward => reward.SentStatus == Reward.Status.NotSent).ToArray();

			if (!toSend.Any())
				continue;
			
			// Prepare the rewards to send.  Mailbox requires a recipient accountId and fields for expiration / visibleFrom.
			foreach (Reward reward in toSend)
			{
				reward.Recipient = history.AccountId;
				reward.VisibleFrom = Timestamp.UnixTime;
				if (reward.Expiration == default)
					reward.Expiration = Timestamp.UnixTime + (long)new TimeSpan(days: 30, hours: 0, minutes: 0, seconds: 0).TotalSeconds;
			}

			string url = PlatformEnvironment.Url("mail/admin/messages/send/bulk");
			_apiService
				.Request(url)
				.AddAuthorization(adminToken)
				.SetPayload(new MailboxMessage(history.AccountId, toSend).Payload)
				.OnSuccess(action: (sender, apiResponse) =>
				{
					successes.Add(history.Id);
					Log.Local(Owner.Will, $"Sent {toSend.Length} rewards to {history.AccountId}");
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
		if (sent > 0)
			Log.Info(Owner.Will, $"Successfully sent players rewards.", data: new
			{
				Count = sent
			});
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