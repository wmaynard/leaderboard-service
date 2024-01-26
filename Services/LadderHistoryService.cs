using System;
using System.Collections.Generic;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class LadderHistoryService : MinqTimerService<LadderHistory>
{
    private const long ONE_DAY = 24 * 60 * 60 * 1_000;
    
    public LadderHistoryService() : base("ladderHistories", interval: ONE_DAY) { }

    public void CreateFromCurrentInfo(Transaction transaction, LadderInfo[] records, LadderSeasonDefinition season)
    {
        mongo
            .WithTransaction(transaction)
            .Insert(records
                .Select(record => record.CreateHistory(season))
                .ToArray()
            );
    }

    public LadderHistory[] GetHistoricalSeasons(string accountId, int count = 5) => mongo
        .Where(query => query.EqualTo(history => history.AccountId, accountId))
        .Sort(sort => sort.OrderByDescending(history => history.CreatedOn))
        .Limit(count)
        .ToArray();

    public long ClearHistories(Transaction transaction, LadderSeasonDefinition season) => mongo
        .WithTransaction(transaction)
        .Where(query => query.EqualTo(history => history.SeasonDefinition.SeasonId, season.SeasonId))
        .Delete();

    public void GrantRewards(Transaction transaction, LadderSeasonDefinition season)
    {
        const int MAX_REWARD_COUNT = 10_000;
        int rewardMax = 0;

        object logData = new
        {
            SeasonDefinition = season
        };

        if (!(season?.Rewards?.Any() ?? false) 
            || (rewardMax = season.Rewards.MaxBy(reward => reward.MinimumRank).MinimumRank) == 0
        )
        {
            Log.Warn(Owner.Will, "No rewards found for season, cannot issue any to players", logData);
            return;
        }

        RewardsService _rewardService = Require<RewardsService>();
        
        LadderHistory[] histories = mongo
            .WithTransaction(transaction)
            .Where(query => query
                .EqualTo(history => history.SeasonDefinition.SeasonId, season.SeasonId)
                .GreaterThan(history => history.MaxScore, 0)
            )
            .Sort(sort => sort.OrderByDescending(history => history.Score))
            .Limit(Math.Min(rewardMax, MAX_REWARD_COUNT))
            .ToArray();

        if (!histories.Any())
        {
            Log.Warn(Owner.Will, "No player scores found for season, cannot issue rewards", logData);
            return;
        }

        int processed = 0;
        foreach (Reward reward in season.Rewards.OrderBy(r => r.MinimumRank))
        {
            string[] eligible = histories
                .Skip(processed)
                .Take(reward.MinimumRank - processed) // TODO: Does this fail when there aren't enough histories available?
                .Select(history => history.AccountId)
                .ToArray();
            
            long granted = _rewardService.Grant(reward, eligible);
            if (granted > 0)
                Log.Info(Owner.Will, "Granted season rewards to players", data: new
                {
                    Reward = reward,
                    AccountIds = eligible,
                    Count = eligible.Length,
                    GrantedCount = granted
                });
            processed += reward.MinimumRank;
        }
    }

    protected override void OnElapsed()
    {
        long affected = mongo
            .Where(query => query.LessThanOrEqualTo(history => history.CreatedOn, Timestamp.ThreeMonthsAgo))
            .Delete();
        if (affected > 0)
            Log.Info(Owner.Will, "Old ladder histories deleted", data: new
            {
                Count = affected
            });
    }

    public void ReGrantRewards(string seasonId, long minimumTimestamp)
    {
        LadderHistory[] top100 = mongo
            .Where(query => query
                .EqualTo(history => history.SeasonDefinition.SeasonId, seasonId)
                .GreaterThanOrEqualTo(history => history.CreatedOn, minimumTimestamp)
            )
            .Sort(sort => sort.OrderByDescending(history => history.Score))
            .Limit(100)
            .ToArray();

        List<Reward> rewards = new();
        RewardsService _rewards = Require<RewardsService>();
        for (int i = 0; i < top100.Length; i++)
        {
            int rank = i + 1;

            LadderHistory history = top100[i];
            Reward reward = history.SeasonDefinition.Rewards
                .Where(reward => reward.MinimumRank >= rank)
                .MinBy(reward => reward.MinimumRank);

            reward.AccountId = history.AccountId;
            rewards.Add(reward.Copy());
        }
        
        _rewards.Insert(rewards.ToArray());
        Log.Local(Owner.Will, $"REGRANTED {rewards.Count} SEASON REWARDS", emphasis: Log.LogType.CRITICAL);
        Console.WriteLine($"REGRANTED {rewards.Count} SEASON REWARDS");
    }
}
