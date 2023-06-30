using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Models;

public class LadderInfo : PlatformCollectionDocument
{
    [BsonElement(Entry.DB_KEY_ACCOUNT_ID)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_ACCOUNT_ID)]
    public string AccountId { get; set; }
    
    [BsonElement(Entry.DB_KEY_SCORE)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_SCORE)]
    public long Score { get; set; }

    [BsonElement("max")]
    [JsonPropertyName("maxScore")]
    public long MaxScore { get; set; }
    
    [BsonElement(Entry.DB_KEY_LAST_UPDATED)]
    [JsonPropertyName(Entry.FRIENDLY_KEY_LAST_UDPATED)]
    public long Timestamp { get; set; }

    [BsonIgnore]
    [JsonPropertyName("stepScore")]
    public long StepScore => Score % LadderService.StepSize;
    
    [BsonIgnore]
    [JsonPropertyName("step")]
    public int Step => (int)(Score / LadderService.StepSize);
    
    [BsonIgnore]
    [JsonPropertyName(Entry.FRIENDLY_KEY_RANK), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Rank { get; set; }
    
    [BsonElement("created")]
    [JsonIgnore]
    public long CreatedOn { get; set; }
}