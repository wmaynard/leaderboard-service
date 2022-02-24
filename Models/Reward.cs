using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Reward : PlatformDataModel
	{
		public string Subject { get; set; }
		public string Message { get; set; }
		public string BannerImage { get; set; }
		public string Icon { get; set; }
		public int Tier { get; set; }
		public Item[] Contents { get; set; } // TODO: GenericData?
		public int MinimumRank { get; set; }
		public int MinimumPercentile { get; set; }
		public long TimeAwarded { get; set; }
		public string LeaderboardId { get; set; }
		internal Status SentStatus { get; set; }
			
		internal enum Status { NotSent, IsSending, Sent }
		
		[BsonIgnore]
		[JsonIgnore]
		public string TemporaryID { get; set; }

		public Reward() => TemporaryID = Guid.NewGuid().ToString();
	}
}