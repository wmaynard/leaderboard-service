using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models;

public class Enrollment : PlatformCollectionDocument
{
	internal const string DB_KEY_ACCOUNT_ID = "aid";
	internal const string DB_KEY_LEADERBOARD_ID = "current";
	internal const string DB_KEY_LEADERBOARD_TYPE = "type";
	internal const string DB_KEY_TIER = "tier";
	internal const string DB_KEY_SEASONAL_TIER = "seasonMax";
	internal const string DB_KEY_ACTIVE = "active";
	internal const string DB_KEY_PAST_LEADERBOARDS = "past";
	
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

	public Enrollment() => PastLeaderboardIDs = new List<string>();
}