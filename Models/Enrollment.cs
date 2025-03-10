using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Models;

[BsonIgnoreExtraElements]
public class Enrollment : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID = "aid";
	internal const string DB_KEY_LEADERBOARD_ID = "current";
	internal const string DB_KEY_LEADERBOARD_TYPE = "type";
	internal const string DB_KEY_TIER = "tier";
	internal const string DB_KEY_ACTIVE = "active";
	internal const string DB_KEY_ACTIVE_TIER = "activeTier";
	internal const string DB_KEY_PAST_LEADERBOARDS = "past";
	internal const string DB_KEY_PROMOTION_STATUS = "promotion";

	public const string FRIENDLY_KEY_TIER = "tier";
	public const string FRIENDLY_KEY_ACTIVE_TIER = "activeTier";
	public const string FRIENDLY_KEY_LEADERBOARD_ID = "currentShardId";
	public const string FRIENDLY_KEY_IS_ACTIVE = "isActive";
	public const string FRIENDLY_KEY_PAST_LEADERBOARDS = "archives";
	public const string FRIENDLY_KEY_PROMOTION_STATUS = "promotionStatus";
	
	[BsonElement(DB_KEY_ACCOUNT_ID), BsonRequired]
	[JsonIgnore]
	[SimpleIndex]
	public string AccountID { get; set; }
	
	[BsonElement(DB_KEY_LEADERBOARD_ID)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_LEADERBOARD_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string CurrentLeaderboardID { get; set; }
	
	[BsonElement(DB_KEY_LEADERBOARD_TYPE), BsonRequired]
	[JsonInclude, JsonPropertyName(Leaderboard.FRIENDLY_KEY_TYPE)]
	[SimpleIndex]
	public string LeaderboardType { get; set; }
	
	[BsonElement(DB_KEY_TIER)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER)]
	[SimpleIndex]
	public int Tier { get; set; }
	
	[BsonElement(DB_KEY_ACTIVE_TIER)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACTIVE_TIER)]
	public int ActiveTier { get; set; }
	
	[BsonElement(DB_KEY_ACTIVE)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_IS_ACTIVE)]
	public bool IsActive { get; set; }
	
	[BsonElement(DB_KEY_PAST_LEADERBOARDS)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PAST_LEADERBOARDS)]
	public List<string> PastLeaderboardIDs { get; set; }
	
	[BsonElement(DB_KEY_PROMOTION_STATUS)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PROMOTION_STATUS)]
	public PromotionStatus Status { get; set; }
	
	[BsonElement("updated")]
	[JsonIgnore]
	public long UpdatedOn { get; set; }
	
	[BsonElement("guild")]
	[JsonInclude, JsonPropertyName("guildId")]
	public string GuildId { get; set; }

	public Enrollment()
	{
		PastLeaderboardIDs = new List<string>();
		Status = PromotionStatus.Acknowledged;
		Tier = 0;
	} 
	
	public enum PromotionStatus { Acknowledged = -1, Unchanged = 0, Demoted = 1, Promoted = 2 }
}