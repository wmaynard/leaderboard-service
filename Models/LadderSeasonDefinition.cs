using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Models;

public class LadderSeasonDefinition : PlatformCollectionDocument
{
    private const int RANK_LIMIT = 1_000;

    public const string FRIENDLY_KEY_FALLBACK = "fallbackScore";
    public const string FRIENDLY_KEY_SEASON_ID = "seasonId";
    public const string FRIENDLY_KEY_END_TIME = "endTime";
    
    [BsonElement("sid")]
    [JsonPropertyName(FRIENDLY_KEY_SEASON_ID)]
    public string SeasonId { get; set; }
    
    [BsonElement("next")]
    [JsonPropertyName("nextSeasonId")]
    public string NextSeasonId { get; set; }
    
    [BsonElement("fallback")]
    [JsonPropertyName(FRIENDLY_KEY_FALLBACK)]
    public int FallbackScore { get; set; }
    
    [BsonElement("end")]
    [JsonPropertyName(FRIENDLY_KEY_END_TIME)]
    public long EndTime { get; set; }
    
    [BsonElement(TierRules.DB_KEY_REWARDS)]
    [JsonPropertyName(TierRules.FRIENDLY_KEY_REWARDS)]
    public Reward[] Rewards { get; set; }
    
    [JsonIgnore]
    public bool Ended { get; set; }


    protected override void Validate(out List<string> errors)
    {
        errors = new List<string>();
        
        if (EndTime <= 0)
            errors.Add($"{FRIENDLY_KEY_END_TIME} must be greater than or equal to 0.");
        if (FallbackScore < 0)
            errors.Add($"{FRIENDLY_KEY_FALLBACK} must be greater than or equal to 0.");
        if (Rewards?.Any(reward => reward.MinimumRank > RANK_LIMIT) ?? false)
            errors.Add($"{Reward.FRIENDLY_KEY_MINIMUM_RANK} must be less than {RANK_LIMIT}");
        if (string.IsNullOrWhiteSpace(SeasonId))
            errors.Add($"{FRIENDLY_KEY_SEASON_ID} must be a non-empty string.");
    }
}