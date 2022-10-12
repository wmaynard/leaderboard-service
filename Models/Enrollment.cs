using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Models;

[BsonIgnoreExtraElements]
public class Enrollment : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID = "aid";
	internal const string DB_KEY_LEADERBOARD_ID = "current";
	internal const string DB_KEY_LEADERBOARD_TYPE = "type";
	internal const string DB_KEY_TIER = "tier";
	internal const string DB_KEY_SEASONAL_TIER = "seasonMax";
	internal const string DB_KEY_ACTIVE = "active";
	internal const string DB_KEY_PAST_LEADERBOARDS = "past";
	internal const string DB_KEY_PROMOTION_STATUS = "promotion"; 
	
	[BsonElement(DB_KEY_ACCOUNT_ID), BsonRequired]
	public string AccountID { get; set; }
	
	[BsonElement(DB_KEY_LEADERBOARD_ID)]
	public string CurrentLeaderboardID { get; set; }
	
	[BsonElement(DB_KEY_LEADERBOARD_TYPE), BsonRequired]
	public string LeaderboardType { get; set; }
	
	[BsonElement(DB_KEY_TIER)]
	public int Tier { get; set; }
	
	[BsonElement(DB_KEY_SEASONAL_TIER)]
	public int SeasonalMaxTier { get; set; }
	
	[BsonElement(DB_KEY_ACTIVE)]
	public bool IsActive { get; set; }
	
	[BsonElement(DB_KEY_PAST_LEADERBOARDS)]
	public List<string> PastLeaderboardIDs { get; set; }
	
	[BsonElement(DB_KEY_PROMOTION_STATUS)]
	public PromotionStatus Status { get; set; }

	public Enrollment() => PastLeaderboardIDs = new List<string>();
	
	public enum PromotionStatus { Acknowledged = -1, Unchanged = 0, Demoted = 1, Promoted = 2 }
}