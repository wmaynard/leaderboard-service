using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models;

public class Attachment : PlatformDataModel
{
	internal const string DB_KEY_QUANTITY = "qty";
	internal const string DB_KEY_RESOURCE_ID = "name";
	internal const string DB_KEY_DATE_RECEIVED = "dt";
	internal const string DB_KEY_TYPE = "t";

	// These fields match those from mailbox-service
	public const string FRIENDLY_KEY_QUANTITY = "Quantity";
	public const string FRIENDLY_KEY_RESOURCE_ID = "RewardId";
	public const string FRIENDLY_KEY_TYPE = "Type";
	public const string FRIENDLY_KEY_DATE_RECEIVED = "receivedOn";
	
	[BsonElement(DB_KEY_QUANTITY), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_QUANTITY), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public int Quantity { get; set; }
	
	[BsonElement(DB_KEY_RESOURCE_ID), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_RESOURCE_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResourceID { get; set; }
	
	[BsonElement(DB_KEY_DATE_RECEIVED), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATE_RECEIVED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public long ReceivedOn { get; set; }
	
	[BsonElement(DB_KEY_TYPE), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TYPE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Type { get; set; }
}