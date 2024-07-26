using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Interop;
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
        int includeInTransaction = season.Rewards.MaxBy(reward => reward.MinimumRank).MinimumRank;
        LadderInfo[] recipients = records.Take(includeInTransaction).ToArray();
        
        if (!recipients.Any())
            return;
        
        mongo
            .WithTransaction(transaction)
            .Insert(recipients
                .Select(record => record.CreateHistory(season))
                .ToArray()
            );
        SlackDiagnostics
            .Log($"Ladder rollover recipients | {season.SeasonId}", "See attachment for rollover data.")
            .Attach("data.csv", string.Join("\n", recipients
                .OrderByDescending(info => info.Score)
                .Select(info => $"{info.AccountId},{info.Score}{ (info.MaxScore > info.Score ? $" ({info.MaxScore})" : "")}")
            ))
            .Send()
            .Wait();
        Task.Run(() =>
        {
            try
            {
                LadderInfo[] others = records.Skip(includeInTransaction).ToArray();
                if (others.Any())
                    mongo
                        .Insert(others
                            .Select(record => record.CreateHistory(season))
                            .ToArray()
                        );
            }
            catch (Exception e)
            {
                Log.Error(Owner.Will, "Unable to insert ladder history records for players who didn't receive rewards", data: new
                {
                    Help = "This does not affect histories for players who were eligible for rollover rewards but limits the ability to undo a rollover for other players."
                }, exception: e);
            }
        });
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

        if (!(season?.Rewards?.Any() ?? false) || (rewardMax = season.Rewards.MaxBy(reward => reward.MinimumRank).MinimumRank) == 0)
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
            .Sort(sort => sort
                .OrderByDescending(history => history.Score)
                .ThenBy(history => history.LastUpdated)
            )
            .Limit(Math.Min(rewardMax, MAX_REWARD_COUNT))
            .ToArray();
        
        if (!histories.Any())
        {
            Log.Warn(Owner.Will, "No player scores found for season, cannot issue rewards", logData);
            return;
        }

        // PLATF-6682 | Fix reward grants for ladder rollover
        // Before we had a bad skip/take that was responsible for dropping rewards for ranks 3-6, 11-17, & 31-47.
        for (int i = 0; i < histories.Length; i++)
            histories[i].Rank = i + 1;
        
        foreach (LadderHistory history in histories)
            history.Reward = season
                .Rewards
                .OrderBy(reward => reward.MinimumRank)
                .FirstOrDefault(reward => history.Rank <= reward.MinimumRank);
        foreach (IGrouping<Reward, LadderHistory> group in histories.GroupBy(history => history.Reward))
            _rewardService.Grant(group.Key, group.Select(history => history.AccountId).ToArray());
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

    public void ReGrantRewards(string seasonId, long minimumTimestamp, bool onlyIfMissing = true)
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

        if (onlyIfMissing)
        {
            string[] accounts = _rewards.GetAccountIdsFromRewardNotes(rewards.Select(reward => reward.InternalNote).ToArray());
            Reward[] missing = rewards
                .Where(reward => !accounts.Contains(reward.AccountId))
                .ToArray();
            if (missing.Any())
                _rewards.Insert(missing);
            Log.Local(Owner.Will, $"REGRANTED {missing.Length} SEASON REWARDS", emphasis: Log.LogType.CRITICAL);
            Console.WriteLine($"REGRANTED {missing.Length} SEASON REWARDS");
        }
        else
        {
            _rewards.Insert(rewards.ToArray());

        
            Log.Local(Owner.Will, $"REGRANTED {rewards.Count} SEASON REWARDS", emphasis: Log.LogType.CRITICAL);
            Console.WriteLine($"REGRANTED {rewards.Count} SEASON REWARDS");
        }

    }
}
