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
    public static long StepSize => Math.Max(DynamicConfig.Instance?.Optional("ladderStepSize", 100) ?? 100, 10);
    public static int TopPlayerCount => Math.Max(0, Math.Min(DynamicConfig.Instance?.Optional("ladderTopPlayerCount", 100) ?? 100, 1_000));
    public static long FallbackMaxPoints => Math.Max(DynamicConfig.Instance?.Optional("ladderResetMaxScore", 0) ?? 0, 0);
    public static long FinalStep => Math.Max(DynamicConfig.Instance?.Optional("ladderFinalStep", 10) ?? 10, 0);
    public static int CacheDuration => Math.Min(0, DynamicConfig.Instance?.Optional("ladderCacheDuration", 300) ?? 300);
    
    public LadderService() : base("ladder")
    {
        mongo.DefineIndexes(index => index
            .Add(info => info.AccountId)
            .Add(info => info.Score, ascending: false)
            .Add(info => info.Timestamp, ascending: false)
            .EnforceUniqueConstraint()
        );
    }

    public long ResetScores() => mongo
        .All()
        .Update(query => query
            .Minimum(info => info.Score, FallbackMaxPoints)
            .Minimum(info => info.MaxScore, FallbackMaxPoints)
        );

    public bool TryAddDummyScore()
    {
        if (PlatformEnvironment.IsProd)
        {
            Log.Verbose(Owner.Will, "Dummy scores are not allowed on prod.");
            return false;
        }
        try
        {
            Random rando = new Random();
            int score = rando.Next(0, 4000);
            mongo
                .Insert(new LadderInfo
                {
                    AccountId = rando.Next(0, int.MaxValue).ToString().PadLeft(24, '0'),
                    CreatedOn = Timestamp.UnixTime - rando.Next(0, 60 * 60 * 24 * 30),
                    Score = score,
                    MaxScore = rando.Next(0, 100) < 95
                        ? score
                        : score + rando.Next(1, 200),
                    Timestamp = Timestamp.UnixTime - rando.Next(0, 60 * 60)
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
            .Where(query => query.NotEqualTo(info => info.AccountId, null))
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
            .Upsert(query => query.SetOnInsert(info => info.CreatedOn, Timestamp.UnixTime));

        player.Rank = mongo
            .Count(query => query
                .GreaterThan(info => info.Score, player.Score)
            );
        output.Add(player);
        
        return output;
    }
    
    public LadderInfo AddScore(string accountId, long score)
    {
        // Special rules for Ladder scores:
        // Each "step" of the ladder has to be hit to go past it.  If we have steps every 100 points:
        //     * You have 89 points.  You score 20 points.  Your result is 100.
        //     * You have 100 points.  You score 20 points.  Your result is 120.
        //     * You have 107 points.  You lose 15 points.  Your result is 100.
        //     * You have 100 points.  You lose 15 points.  Your result is 85.
        // Beyond the top step, this logic does not apply.
        LadderInfo record = mongo
            .WithTransaction(out Transaction transaction)
            .Where(query => query.EqualTo(info => info.AccountId, accountId))
            .Upsert(query => query.SetOnInsert(info => info.CreatedOn, Timestamp.UnixTime));
        
        if (FinalStep == 0)        // Steps are disabled
            record.Score += score;
        else                       // Apply the Ladder step logic
        {
            int step = (int)(record.Score / StepSize);
            long floor = record.Score % StepSize == 0
                ? (step - 1) * StepSize
                : step * StepSize;
            long ceiling = (step + 1) * StepSize;
            
            if (step >= FinalStep)
                record.Score = Math.Max(floor, record.Score + score);
            else
            {
                // Apply the step floor / ceiling
                long newScore = Math.Min(Math.Max(floor, record.Score + score), ceiling);
                
                // Apply a floor of 0
                record.Score = Math.Max(0, newScore);
            }
        }

        record = mongo
            .WithTransaction(transaction)
            .Where(query => query.EqualTo(info => info.AccountId, accountId))
            .Upsert(query => query
                .Set(info => info.Score, record.Score)
                .Maximum(info => info.MaxScore, record.Score)
                .Set(info => info.Timestamp, score > 0 
                    ? Timestamp.UnixTime
                    : Timestamp.UnixTime - 5 // Edge case offset; when two players tie, this places the winner in a higher rank than the loser.
                )
            );
        
        transaction.Commit();

        return record;
    }
}