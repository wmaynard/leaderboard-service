using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class LadderService : MinqService<LadderInfo>
{
    private const string CACHE_KEY_POPULATION_STATS = "populationStats";
    public static int TopPlayerCount => Math.Max(0, Math.Min(1_000, DynamicConfig.Instance?.Optional("ladderTopPlayerCount", 100) ?? 100));
    public static int CacheDuration => Math.Max(0, DynamicConfig.Instance?.Optional("ladderCacheDuration", 300) ?? 300);
    
    public LadderService() : base("ladder")
    {
        mongo.DefineIndexes(index => index
            .Add(info => info.AccountId)
            .Add(info => info.Score, ascending: false)
            .Add(info => info.Timestamp, ascending: false)
            .EnforceUniqueConstraint()
        );
    }
    
    public long ResetScores(Transaction transaction, LadderSeasonDefinition season)
    {
        LadderHistoryService historyService = Require<LadderHistoryService>();
        long affected = 0;

        // If any accounts were inactive, nuke their existing data.
        long inactive = mongo
            .WithTransaction(transaction)
            .Where(query => query
                .GreaterThan(history => history.Score, 0)
                .EqualTo(history => history.IsActive, false)
            )
            .Update(update => update
                .Set(history => history.Score, 0)
                .Set(history => history.MaxScore, 0)
                .Set(history => history.PreviousScoreChange, 0)
            );
        
        if (inactive > 0)
            Log.Info(Owner.Will, "Inactive ladder players found; scores reset to 0.", data: new
            {
                SeasonId = season.Id,
                SeasonName = season.SeasonId,
                InactiveCount = inactive
            });
        
        // Create our ladder history documents.
        // CAUTION: With enough records, there's a chance this segment of code could take longer than 30s to process,
        // which would cause the transaction to timeout and fail.  If this happens we may need to split this into multiple
        // transactions or write a custom aggregation pipeline.  MINQ does not currently support aggregations.
        mongo
            .WithTransaction(transaction)
            .Where(query => query.GreaterThanOrEqualTo(history => history.MaxScore, 0))
            .Process(batchSize: 10_000, onBatch: batchData =>
            {
                historyService.CreateFromCurrentInfo(transaction, batchData.Results, season);
                affected += batchData.Results.Length;
            });

        // We can either use DC to determine the fallback score - or we can use the season's definition.
        // TODO: Update documentation for ladder management here
        int fallbackScore = DynamicConfig.Instance?.Optional<int?>("ladderResetMaxScore") ?? season.FallbackScore;

        // Reset players with points below the fallback score to 0.
        if (fallbackScore > 0)
            mongo
                .WithTransaction(transaction)
                .Where(query => query.LessThan(info => info.Score, fallbackScore))
                .Update(query => query
                    .Set(info => info.Score, 0)
                    .Set(info => info.MaxScore, 0)
                    .Set(info => info.PreviousScoreChange, 0)
                );

        // Reset players with enough points to the fallback score.
        mongo
            .WithTransaction(transaction)
            .Where(query => query.GreaterThanOrEqualTo(info => info.Score, fallbackScore))
            .Update(query => query
                .Set(info => info.Score, fallbackScore)
                .Set(info => info.MaxScore, fallbackScore)
                .Set(info => info.PreviousScoreChange, 0)
                .Set(info => info.IsActive, false)
            );
        
        return affected;
    }

    public bool TryAddDummyScore()
    {
        if (PlatformEnvironment.IsProd)
        {
            Log.Verbose(Owner.Will, "Dummy scores are not allowed on prod.");
            return false;
        }
        try
        {
            Random rando = new();
            int score = rando.Next(0, 4000);
            mongo
                .Insert(new LadderInfo
                {
                    AccountId = rando.Next(0, int.MaxValue).ToString().PadLeft(24, '0'),
                    CreatedOn = Timestamp.Now - rando.Next(0, 60 * 60 * 24 * 30),
                    Score = score,
                    MaxScore = rando.Next(0, 100) < 95
                        ? score
                        : score + rando.Next(1, 200),
                    Timestamp = Timestamp.Now - rando.Next(0, 60 * 60)
                });
            return true;
        }
        catch (Exception e)
        {
            Log.Local(Owner.Will, "Unable to insert dummy record.");
            return false;
        }
    }
    
    public List<LadderInfo> GetRankings(string accountId = null)
    {
        List<LadderInfo> output = mongo
            .All()
            .Limit(TopPlayerCount)
            .Sort(sort => sort
                .OrderByDescending(info => info.Score)
                .ThenByDescending(info => info.Timestamp)
            )
            .Cache(CacheDuration)
            .ToList();

        for (int i = 0; i < output.Count; i++)
            output[i].Rank = i + 1;

        if (accountId == null || output.Any(info => info.AccountId == accountId))
            return output;
        
        LadderInfo player = mongo
            .Where(query => query.EqualTo(info => info.AccountId, accountId))
            .Upsert(query => query
                .SetOnInsert(info => info.CreatedOn, Timestamp.Now)
                .SetOnInsert(info => info.Timestamp, Timestamp.Now)
            );

        player.Rank = mongo
            .Count(query => query
                .Or(or => or
                    .GreaterThan(info => info.Score, player.Score)
                    .And(and => and
                        .EqualTo(info => info.Score, player.Score)
                        .LessThan(info => info.Timestamp, player.Timestamp)
                    )
                )
            ) + 1;
        output.Add(player);
        
        return output;
    }

    public LadderInfo[] GetPlayerScores(string[] accountIds)
    {
        LadderInfo[] output = mongo
            .Where(query => query.ContainedIn(info => info.AccountId, accountIds))
            .ToArray();

        if (output.Length == accountIds.Length)
            return output;

        LadderInfo[] toCreate = accountIds
            .Except(output.Select(info => info.AccountId))
            .Select(id => new LadderInfo
            {
                AccountId = id,
                CreatedOn = Timestamp.Now
            })
            .ToArray();
        
        mongo.Insert(toCreate);
        return output.Concat(toCreate).ToArray();
    }
    
    public LadderInfo AddScore(string accountId, long score)
    {
        LadderInfo output = mongo
            .WithTransaction(out Transaction transaction)
            .Where(query => query.EqualTo(info => info.AccountId, accountId))
            .Upsert(query => query
                .Increment(info => info.Score, score)
                .Set(info => info.PreviousScoreChange, score)
                .Set(info => info.IsActive, true)
                .SetToCurrentTimestamp(info => info.Timestamp)
                .SetOnInsert(info => info.CreatedOn, Timestamp.Now)
            );

        switch (score)
        {
            case > 0:
                output = mongo
                    .WithTransaction(transaction)
                    .Where(query => query.EqualTo(info => info.AccountId, accountId))
                    .Upsert(query => query
                        .Maximum(info => info.MaxScore, output.Score)
                        .Set(info => info.IsActive, true)
                    );
                break;
            case < 0 when output.Score < 0:
                output = mongo
                    .WithTransaction(transaction)
                    .Where(query => query.EqualTo(info => info.AccountId, accountId))
                    .Upsert(query => query
                        .Maximum(info => info.Score, 0)
                        .Set(info => info.IsActive, true)
                    );
                break;
        }

        Commit(transaction);
        return output;

        // On 2023.09.08, design decided to remove the breakpoint functionality in favor of Platform just naively tracking points.
        // Consequently this method now just becomes a simple addition with no logic in it.  However, it was mentioned that
        // we may want to bring this functionality back, so we'll leave this block commented out for the time being.

        // Special rules for Ladder scores:
        // Each "step" of the ladder has to be hit to go past it.  If we have steps every 100 points:
        //     * You have 89 points.  You score 20 points.  Your result is 100.
        //     * You have 100 points.  You score 20 points.  Your result is 120.
        //     * You have 107 points.  You lose 15 points.  Your result is 100.
        //     * You have 100 points.  You lose 15 points.  Your result is 85.
        // Beyond the top step, this logic does not apply.
        // LadderInfo record = mongo
        //     .WithTransaction(out Transaction transaction)
        //     .Where(query => query.EqualTo(info => info.AccountId, accountId))
        //     .Upsert(query => query.SetOnInsert(info => info.CreatedOn, Timestamp.UnixTime));
        //
        // if (FinalStep == 0)        // Steps are disabled
        //     record.Score += score;
        // else                       // Apply the Ladder step logic
        // {
        //     int step = (int)(record.Score / StepSize);
        //     long floor = record.Score % StepSize == 0
        //         ? (step - 1) * StepSize
        //         : step * StepSize;
        //     long ceiling = (step + 1) * StepSize;
        //     
        //     if (step >= FinalStep)
        //         record.Score = Math.Max(floor, record.Score + score);
        //     else
        //     {
        //         // Apply the step floor / ceiling
        //         long newScore = Math.Min(Math.Max(floor, record.Score + score), ceiling);
        //         
        //         // Apply a floor of 0
        //         record.Score = Math.Max(0, newScore);
        //     }
        // }
        //
        // record = mongo
        //     .WithTransaction(transaction)
        //     .Where(query => query.EqualTo(info => info.AccountId, accountId))
        //     .Upsert(query => query
        //         .Set(info => info.Score, record.Score)
        //         .Maximum(info => info.MaxScore, record.Score)
        //         .Set(info => info.Timestamp, score > 0 
        //             ? Timestamp.UnixTime
        //             : Timestamp.UnixTime - 5 // Edge case offset; when two players tie, this places the winner in a higher rank than the loser.
        //         )
        //     );
        //
        // transaction.Commit();
        //
        // return record;
    }

    public PopulationStats GetPopulationStats()
    {
        PopulationStats output = new();
        if (Optional<CacheService>()?.HasValue(CACHE_KEY_POPULATION_STATS, out output) ?? false)
            return output;
        
        long[] scores = mongo
            .Where(query => query.EqualTo(info => info.IsActive, true))
            .Project(info => info.Score);

        switch (scores.Length)
        {
            case 0:
                break;
            case 1:
                output.ActivePlayers = 1;
                output.MeanScore = scores.First();
                output.SumOfSquares = Math.Pow(scores.First(), 2);
                output.StandardDeviation = 0;
                output.TotalScore = scores.First();
                break;
            default:
                output.ActivePlayers = scores.Length;
                output.MeanScore = scores.Average();
                output.SumOfSquares = scores.Select(score => Math.Pow(score - output.MeanScore, 2)).Sum();
                output.Variance = output.SumOfSquares / (output.ActivePlayers - 1);
                output.StandardDeviation = Math.Sqrt(output.Variance);
                output.TotalScore = scores.Sum();
                break;
        }
        
        Optional<CacheService>().Store(CACHE_KEY_POPULATION_STATS, output, IntervalMs.TwoHours);

        return output;
    }
}