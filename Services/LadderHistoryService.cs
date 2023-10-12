using System;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Minq;
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

    public void GrantRewards(LadderSeasonDefinition season)
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

        if (rewardMax == 0)
        {
            Log.Warn(Owner.Will, "No rewards found for season, cannot issue any to players", logData);
            return;
        }

        RewardsService _rewardService = Require<RewardsService>();
        
        LadderHistory[] histories = mongo
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
            _rewardService.Grant(reward, eligible);
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
}
