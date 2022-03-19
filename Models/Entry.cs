using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Entry : PlatformDataModel
	{
		internal const string DB_KEY_ACCOUNT_ID = "aid";
		internal const string DB_KEY_SCORE = "pts";

		public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
		public const string FRIENDLY_KEY_RANK = "rank";
		public const string FRIENDLY_KEY_SCORE = "score";
		
		[BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_RANK), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int Rank { get; set; }
		
		[BsonElement(DB_KEY_ACCOUNT_ID), BsonRequired]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string AccountID { get; set; }
		
		[BsonElement(DB_KEY_SCORE), BsonRequired]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCORE)]
		public long Score { get; set; }
	}
}