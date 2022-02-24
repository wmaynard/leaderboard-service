using System;
using System.Collections.Generic;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Enrollment : PlatformCollectionDocument
	{
		internal const string DB_KEY_ACCOUNT_ID = "AccountID";
		
		[BsonElement(DB_KEY_ACCOUNT_ID)]
		public string AccountID { get; set; }
		public string CurrentLeaderboardID { get; set; }
		public string LeaderboardType { get; set; }
		public int Tier { get; set; }
		public bool IsActive { get; set; }
		public List<string> PastLeaderboardIDs { get; set; }

		public Enrollment() => PastLeaderboardIDs = new List<string>();
	}
	

}