using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Entry : PlatformDataModel
	{
		internal const string DB_KEY_ACCOUNT_ID = "aid";
		internal const string DB_KEY_LAST_UPDATED = "ts";
		internal const string DB_KEY_SCORE = "pts";

		public const string FRIENDLY_KEY_ACCOUNT_ID = "accountId";
		public const string FRIENDLY_KEY_LAST_UDPATED = "lastUpdated";
		public const string FRIENDLY_KEY_RANK = "rank";
		public const string FRIENDLY_KEY_SCORE = "score";

		[BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_RANK), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public int Rank { get; set; }
		
		[BsonElement(DB_KEY_ACCOUNT_ID), BsonRequired]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ACCOUNT_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		[SimpleIndex]
		[CompoundIndex(Leaderboard.GROUP_SHARD)]
		public string AccountID { get; set; }
		
		[BsonElement(DB_KEY_SCORE), BsonRequired]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCORE)]
		public long Score { get; set; }
		
		[BsonElement(DB_KEY_LAST_UPDATED)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_LAST_UDPATED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long LastUpdated { get; set; }
		
		[JsonIgnore]
		[BsonIgnore]
		public Reward Prize { get; set; }

		public override string ToString()
		{
			string rank = Rank.ToString().PadLeft(5, ' ');
			string score = Score.ToString().PadRight(7);

			return $"{rank} | {AccountID} | {score} | {Prize.ToString()}";
		}
	}
}