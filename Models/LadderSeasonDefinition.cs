using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Models;

public class LadderSeasonDefinition : PlatformCollectionDocument
{
    private const int RANK_LIMIT = 1_000;
    
    public string SeasonId { get; set; }
    public string NextSeasonId { get; set; }
    public int FallbackScore { get; set; }
    public long EndTime { get; set; }
    public Reward[] Rewards { get; set; }
    
    [JsonIgnore]
    public bool Ended { get; set; }


    protected override void Validate(out List<string> errors)
    {
        errors = new List<string>();
        
        // if (EndTime < Timestamp.UnixTime)
        //     errors.Add($"{FRIENDLY_KEY_END_TIME} must be greater than the current time.");
        if (Rewards?.Any(reward => reward.MinimumRank > RANK_LIMIT) ?? false)
            errors.Add($"{Reward.FRIENDLY_KEY_MINIMUM_RANK} must be less than {RANK_LIMIT}");
    }
}