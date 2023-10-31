using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Models;

public class LadderInfo : PlatformCollectionDocument
{
    public const string DB_KEY_MAX_SCORE = "max";
    public const string FRIENDLY_KEY_MAX_SCORE = "maxScore";
    
    [BsonElement(Entry.DB_KEY_ACCOUNT_ID)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_ACCOUNT_ID)]
    public string AccountId { get; set; }
    
    [BsonElement(Entry.DB_KEY_SCORE)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_SCORE)]
    public long Score { get; set; }

    [BsonElement(DB_KEY_MAX_SCORE)]
    [JsonPropertyName(FRIENDLY_KEY_MAX_SCORE)]
    public long MaxScore { get; set; }
    
    [BsonElement(Entry.DB_KEY_LAST_UPDATED)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_LAST_UDPATED)]
    public long Timestamp { get; set; }
    
    [BsonElement("delta"), BsonIgnoreIfDefault]
    [JsonPropertyName("previousChange")]
    public long PreviousScoreChange { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName(Entry.FRIENDLY_KEY_RANK), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Rank { get; set; }

    public LadderHistory CreateHistory(LadderSeasonDefinition definition) => new()
    {
        Score = Score,
        MaxScore = MaxScore,
        AccountId = AccountId,
        SeasonDefinition = definition,
        LastUpdated = Timestamp
    };
}

