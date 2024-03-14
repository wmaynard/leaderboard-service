using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Models;

public class LadderHistory : PlatformCollectionDocument
{
    [BsonElement(Entry.DB_KEY_SCORE)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_SCORE)]
    public long Score { get; set; }
    
    [BsonElement(LadderInfo.DB_KEY_MAX_SCORE)]
    [JsonPropertyName(LadderInfo.FRIENDLY_KEY_MAX_SCORE)]
    public long MaxScore { get; set; }
    
    [BsonElement(TokenInfo.DB_KEY_ACCOUNT_ID)]
    [JsonPropertyName(TokenInfo.FRIENDLY_KEY_ACCOUNT_ID)]
    public string AccountId { get; set; }
    
    [BsonElement(Entry.DB_KEY_LAST_UPDATED)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_LAST_UDPATED)]
    public long LastUpdated { get; set; }
    
    [BsonElement("season")]
    [JsonPropertyName("season")]
    public LadderSeasonDefinition SeasonDefinition { get; set; }
    
    [BsonIgnore]
    [JsonIgnore]
    public int Rank { get; set; }
    
    [BsonIgnore]
    [JsonIgnore]
    public Reward Reward { get; set; }
}