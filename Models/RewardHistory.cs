using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models;

public class RewardHistory : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID = "aid";
	internal const string DB_KEY_REWARDS = "items";

	public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
	public const string FRIENDLY_KEY_REWARDS = "rewards";
		
	[BsonElement(DB_KEY_ACCOUNT_ID), BsonRequired]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID)]
	public string AccountId { get; set; }
		
	[BsonElement(DB_KEY_REWARDS), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_REWARDS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public List<Reward> Rewards { get; set; }

	public RewardHistory() => Rewards = new List<Reward>();
		
		
}