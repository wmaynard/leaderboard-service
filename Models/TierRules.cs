using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Models;

[BsonIgnoreExtraElements]
public class TierRules : PlatformDataModel
{
	internal const string DB_KEY_TIER = "tier";
	internal const string DB_KEY_PROMOTION_RANK = "promo";
	internal const string DB_KEY_PROMOTION_PERCENTAGE = "promo%";
	internal const string DB_KEY_DEMOTION_RANK = "demo";
	internal const string DB_KEY_DEMOTION_PERCENTAGE = "demo%";
	internal const string DB_KEY_PLAYERS_PER_SHARD = "cap";
	internal const string DB_KEY_REWARDS = "rewards";
	internal const string DB_KEY_SEASON_REWARDS = "sReward";
	internal const string DB_KEY_TIER_RESET = "resetTier";

	public const string FRIENDLY_KEY_TIER = "tier";
	public const string FRIENDLY_KEY_PROMOTION_RANK = "promotionRank";
	public const string FRIENDLY_KEY_PROMOTION_PERCENTAGE = "promotionPercentage";
	public const string FRIENDLY_KEY_DEMOTION_RANK = "demotionRank";
	public const string FRIENDLY_KEY_DEMOTION_PERCENTAGE = "demotionPercentage";
	public const string FRIENDLY_KEY_PLAYERS_PER_SHARD = "playersPerShard";
	public const string FRIENDLY_KEY_REWARDS = "rewards";
	public const string FRIENDLY_KEY_TIER_RESET = "maxTierOnSeasonEnd";
		
	[BsonElement(DB_KEY_TIER), BsonRequired]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER)]
	public int Tier { get; set; }
	
	[BsonElement(DB_KEY_PROMOTION_RANK), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PROMOTION_RANK), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int PromotionRank { get; set; }
	
	[BsonElement(DB_KEY_DEMOTION_RANK), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DEMOTION_RANK), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int DemotionRank { get; set; }
	
	[BsonElement(DB_KEY_PROMOTION_PERCENTAGE), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PROMOTION_PERCENTAGE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int PromotionPercentage { get; set; }
	
	[BsonElement(DB_KEY_DEMOTION_PERCENTAGE), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DEMOTION_PERCENTAGE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int DemotionPercentage { get; set; }
	
	[BsonElement(DB_KEY_PLAYERS_PER_SHARD), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_PLAYERS_PER_SHARD), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int PlayersPerShard { get; set; }
	
	[BsonElement(DB_KEY_REWARDS), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_REWARDS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Reward[] Rewards { get; set; }
	
	[BsonElement(DB_KEY_TIER_RESET)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER_RESET)]
	public int MaxTierOnSeasonReset { get; set; }
}