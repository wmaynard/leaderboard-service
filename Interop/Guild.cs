using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Interop;

// MWE for GuildService responses
public class Guild : PlatformCollectionDocument
{
    [BsonElement("name")]
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [BsonIgnore]
    [JsonPropertyName("members")]
    public GuildMember[] Members { get; set; }
}

public class GuildMember : PlatformCollectionDocument
{
    [BsonElement(TokenInfo.DB_KEY_ACCOUNT_ID)]
    [JsonPropertyName(TokenInfo.FRIENDLY_KEY_ACCOUNT_ID)]
    public string AccountId { get; set; }
    
    [BsonElement("rank")]
    [JsonPropertyName("rank")]
    public Rank Rank { get; set; }
}

public enum Rank
{
    Applicant = 0,
    Member = 1,
    Elder = 2,
    Officer = 5,
    Leader = 10
}