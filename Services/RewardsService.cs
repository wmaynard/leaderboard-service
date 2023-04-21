using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class RewardsService : PlatformMongoService<RewardHistory>
{
	private readonly ApiService _apiService;
	private readonly EnrollmentService _enrollmentService;
	private readonly DynamicConfig _dynamicConfig;

	public RewardsService(ApiService apiService, DynamicConfig dynamicConfig, EnrollmentService enrollmentService) : base("rewards")
	{
		_apiService = apiService;
		_dynamicConfig = dynamicConfig;
		_enrollmentService = enrollmentService;
	}

	public long Grant(Reward reward, params string[] accountIds)
	{
		if (reward == null || !accountIds.Any())
			return 0;

		return _collection.UpdateMany( // TODO: Session?
			filter: Builders<RewardHistory>.Filter.In(history => history.AccountId, accountIds),
			update: Builders<RewardHistory>.Update.AddToSet(history => history.Rewards, reward),
			options: new UpdateOptions
			{
				IsUpsert = true
			}
		).ModifiedCount;
	}

	// TODO: Create tasks for this as well
	public void SendRewards()
	{
		string adminToken = _dynamicConfig.AdminToken;
		
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

				if (reward.RankingData?.Optional<string>("rewardType") != "season")
					continue;
				try
				{
					string type = reward.RankingData.Require<string>("leaderboardId");
					Enrollment enrollment = _enrollmentService.Find(history.AccountId, type).First();
					reward.RankingData["leaderboardCurrentTier"] = enrollment.Tier;
					reward.RankingData["leaderboardSeasonFinalTier"] = enrollment.SeasonFinalTier;
				}
				catch (Exception e)
				{
					Log.Warn(Owner.Will, "Unable to attach player-specific tier information to seasonal rewards.", data: new
					{
						accountId = history.AccountId
					}, exception: e);
				}
			}

			string url = PlatformEnvironment.Url("mail/admin/messages/send/bulk");
			_apiService
				.Request(url)
				.AddAuthorization(adminToken)
				.SetPayload(new MailboxMessage(history.AccountId, toSend).Payload)
				.OnSuccess(_ =>
				{
					successes.Add(history.Id);
					Log.Local(Owner.Will, $"Sent {toSend.Length} rewards to {history.AccountId}");
				})
				.OnFailure(response => _apiService.Alert(
					title: "Unable to send leaderboard rewards.",
					message: "Mail service returned a bad response when trying to send rewards to a player.",
					countRequired: 1,
					timeframe: 30_000,
					owner: Owner.Will,
					impact: ImpactType.ServicePartiallyUsable,
					data: new RumbleJson
					{
						{ "accountId", history.AccountId },
						{ "response", response.AsRumbleJson },
						{ "attemptedRewards", toSend }
					}
				))
				.Post();
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