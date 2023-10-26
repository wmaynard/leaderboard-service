using System;
using System.Collections.Generic;
using RCL.Logging;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Tests;

public static class StartupTests
{
    public static void TestLadderSeason()
    {
        ApiService api = PlatformService.Require<ApiService>();
        LadderService ladder = PlatformService.Require<LadderService>();
        SeasonDefinitionService seasons = PlatformService.Require<SeasonDefinitionService>();
        LadderHistoryService histories = PlatformService.Require<LadderHistoryService>();
        RewardsService rewards = PlatformService.Require<RewardsService>();

        ladder.WipeDatabase();
        seasons.WipeDatabase();
        histories.WipeDatabase();
        rewards.WipeDatabase();
        
        Random rando = new();

        List<string> tokens = new();

        for (int i = 0; i < 20; i++)
        {
        	string accountId = $"deadbeefdeadbeefdead{i.ToString().PadLeft(4, '0')}";
        	tokens.Add(api.GenerateToken(accountId: accountId, screenname: accountId[20..], email: null, discriminator: 1234));
        }

        LadderSeasonDefinition season = new()
        {
        	EndTime = Timestamp.FiveMinutesFromNow,
        	FallbackScore = 500,
        	Rewards = new[]
        	{
        		new Reward
        		{
        			Contents = new Attachment[]
        			{
        				new ()
        				{
        					Quantity = 500,
        					ResourceID = "soft_currency",
        					Type = "soft_currency"
        				}
        			},
        			MinimumRank = 20
        		}
        	},
        	SeasonId = "wdm_test_season"
        };
        season.Validate();
        
        seasons.Insert(season);

        foreach (string token in tokens)
        {
        	api
        		.Request("http://localhost:5091/leaderboard/ladder/score")
        		.AddAuthorization(token)
        		.SetPayload(new RumbleJson
        		{
        			{ "score", rando.Next(1, 300) }
        		})
        		.OnSuccess(_ => Log.Local(Owner.Will, "success"))
        		.OnFailure(_ => Log.Local(Owner.Will, "failure"))
        		.Patch();
        }
        
        seasons.EndSeason(season);
    }
}