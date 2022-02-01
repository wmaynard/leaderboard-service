using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Entry : PlatformDataModel
	{
		[BsonIgnoreIfDefault]
		public int Rank { get; set; }
		public string AccountID { get; set; }
		public long Score { get; set; }
	}
}