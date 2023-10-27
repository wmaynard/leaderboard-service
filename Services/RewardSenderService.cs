using System;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class RewardSenderService : QueueService<RewardSenderService.RewardData>
{
    private readonly ApiService _api;
    private readonly DynamicConfig _config;
    private readonly RewardsService _rewards;
    private readonly EnrollmentService _enrollments;
    
    public RewardSenderService(ApiService api, DynamicConfig config, EnrollmentService enrollments, RewardsService rewards) : base("outbox", intervalMs: 5_000, primaryNodeTaskCount: 50)
    {
        _api = api;
        _config = config;
        _rewards = rewards;
        _enrollments = enrollments;
    }

    protected override void OnTasksCompleted(RewardData[] data) => Log.Local(Owner.Will, "Rewards sender tasks completed.");

    protected override void PrimaryNodeWork()
    {
        Reward[] unsent = _rewards.GetUntaskedRewards(out Transaction transaction);
        if (!unsent.Any())
            return;

        try
        {
            Log.Local(Owner.Will, "Creating tasks to send rewards out to mail-service");
            // TODO: Update QueueServices to support transactions, MINQ
            CreateUntrackedTasks(unsent
                .GroupBy(reward => reward.AccountId)
                .Select(group => new RewardData
                {
                    RewardIds = group
                        .Select(reward => reward.Id)
                        .ToArray()
                })
                .ToArray()
            );
            
            // The transaction from RewardsService needs to be committed before they're marked as tasked.
            // Without this, we risk duplicating tasks.
            _rewards.Commit(transaction);
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Unable to create reward sending tasks", exception: e);
            _rewards.Abort(transaction);
        }
    }
    
    protected override void ProcessTask(RewardData data)
    {
        // Loading the rewards dynamically requires an additional DB read.  Performance-wise, this could be improved
        // by instead adding the Rewards to the data when we're creating tasks.  Doing so would mean one less read,
        // but would come at the cost of potential conflicts.  If we have a task to send out a reward and there's an issue
        // where a reward has already been sent, and we aren't checking the DB first, we may end up double-sending.
        // So while this is a slight performance hit, it's worth the tradeoff to be safe in our delivery.
        Reward[] rewards = GetUnsentRewards(data);
        MailboxMessage[] messages = ConvertRewardsToMessages(rewards);

        // This should only ever be one message, but we'll loop it regardless just as a redundancy
        foreach (MailboxMessage message in messages)
            SendMessage(message);
    }

    /// <summary>
    /// Takes the task's data and grabs the associated Rewards from it for processing.  Logs errors if there are discrepencies
    /// between expected rewards to send and what's actually found through a query in the database.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private Reward[] GetUnsentRewards(RewardData data)
    {
        Reward[] output = _rewards.GetUnsentRewards(data.RewardIds);
        
        if (!output.Any())
        {
            Log.Error(Owner.Will, "No unsent rewards found when at least one was expected.", data: new
            {
                ids = string.Join(',', data.RewardIds)
            });
            return output;
        }
        
        if (output.Length != data.RewardIds.Length)
            Log.Error(Owner.Will, "Some expected rewards to send could not be found, or were already processed.", data: new
            {
                ids = string.Join(',', data.RewardIds)
            });
        if (output.DistinctBy(reward => reward.AccountId).Count() > 1)
            Log.Error(Owner.Will, "Found rewards to send for multiple accounts; this should never be the case.", data: new
            {
                ids = string.Join(',', data.RewardIds),
                Help = "The rewards will be processed regardless but this should not happen"
            });

        return output;
    }
    
    /// <summary>
    /// Takes an array of rewards and converts them into an array of MailboxMessages, grouped by account ID.
    /// If multiple rewards exist for a specific account ID, they are bundled into a singular MailboxMessage.
    /// </summary>
    /// <param name="rewards"></param>
    /// <returns></returns>
    private MailboxMessage[] ConvertRewardsToMessages(params Reward[] rewards) => rewards
        .GroupBy(reward => reward.AccountId)
        .Select(group =>
        {
            Reward[] toSend = group
                .Select(reward =>
                {
                    reward.VisibleFrom = Timestamp.UnixTime;
                    if (reward.Expiration == default)
                        reward.Expiration = Timestamp.OneMonthFromNow;

                    #region REMOVE_WITH_SEASONS
                    if (reward.RankingData?.Optional<string>("rewardType") != "season")
                        return reward;
                    
                    try
                    {
                        Enrollment enrollment = _enrollments
                            .Find(reward.AccountId, reward.RankingData.Require<string>("leaderboardId"))
                            .First();
                        reward.RankingData["leaderboardCurrentTier"] = enrollment.Tier;
                        reward.RankingData["leaderboardSeasonFinal"] = enrollment.SeasonFinalTier;
                    }
                    catch (Exception e)
                    {
                        Log.Warn(Owner.Will, "Unable to attach player-specific tier information to seasonal rewards.", data: new
                        {
                            accountId = reward.AccountId
                        }, exception: e);
                    }

                    #endregion REMOVE_WITH_SEASONS

                    return reward;
                })
                .ToArray();
            return new MailboxMessage(group.Key, toSend);
        })
        .ToArray();

    private bool SendMessage(MailboxMessage message)
    {
        bool output = false;
        _api
            .Request("/mail/admin/messages/send/bulk")
            .AddAuthorization(_config.AdminToken)
            .SetPayload(message.Payload)
            .OnSuccess(response =>
            {
                try
                {
                    _rewards.MarkAsSent(message.RewardIds);
                    string accountId = message
                       .Payload
                       .Optional<string[]>("accountIds")
                       ?.FirstOrDefault()
                       ?? "(UNKNOWN)";
                    if (!PlatformEnvironment.IsProd)
                        Log.Local(Owner.Will, $"Sent {message.RewardIds.Length} rewards to {accountId}", emphasis: Log.LogType.WARN);
                    else
                        Log.Info(Owner.Will, "Sent rewards to a player, successful mail response.", data: new
                        {
                            Response = response,
                            Count = message.RewardIds.Length,
                            AccountId = accountId
                        });
                    output = true;
                }
                catch (Exception e)
                {
                    Log.Error(Owner.Will, "Something went wrong marking rewards as sent", exception: e);
                }
            })
            .OnFailure(_ => Log.Error(Owner.Will, "Unable to send "))
            .Post();
        return output;
    }
    
    public class RewardData : PlatformDataModel
    {
        public string[] RewardIds { get; set; }
    }
}