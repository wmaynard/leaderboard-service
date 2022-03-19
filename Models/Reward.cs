using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models;

public class Reward : PlatformDataModel
{
	internal const string DB_KEY_SUBJECT = "sub";
	internal const string DB_KEY_BODY = "msg";
	internal const string DB_KEY_BANNER = "banner";
	internal const string DB_KEY_ICON = "icon";
	internal const string DB_KEY_INTERNAL_NOTE = "note";
	internal const string DB_KEY_VISIBLE_FROM = "time";
	internal const string DB_KEY_EXPIRATION = "exp";
	internal const string DB_KEY_ATTACHMENTS = "attachments";
	internal const string DB_KEY_STATUS = "status";
	internal const string DB_KEY_TIER = "tier";

	public const string FRIENDLY_KEY_SUBJECT = "subject";
	public const string FRIENDLY_KEY_BODY = "body";
	public const string FRIENDLY_KEY_BANNER = "banner";
	public const string FRIENDLY_KEY_ICON = "icon";
	public const string FRIENDLY_KEY_INTERNAL_NOTE = "internalNote";
	public const string FRIENDLY_KEY_VISIBLE_FROM = "visibleFrom";
	public const string FRIENDLY_KEY_EXPIRATION = "expiration";
	public const string FRIENDLY_KEY_ATTACHMENTS = "attachments";
	public const string FRIENDLY_KEY_TIER = "tier";

	[BsonElement(DB_KEY_SUBJECT), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SUBJECT), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Subject { get; set; }
	
	[BsonElement(DB_KEY_BODY), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_BODY), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Message { get; set; }
	
	[BsonElement(DB_KEY_BANNER), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_BANNER), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string BannerImage { get; set; }
	
	[BsonElement(DB_KEY_ICON), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ICON), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Icon { get; set; }
	
	[BsonElement(DB_KEY_TIER)]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER)]
	public int Tier { get; set; }
	
	[BsonElement(DB_KEY_ATTACHMENTS), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ATTACHMENTS), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Attachment[] Contents { get; set; }
	
	[BsonElement(DB_KEY_INTERNAL_NOTE), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_INTERNAL_NOTE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string InternalNote { get; set; }
	
	[BsonElement(DB_KEY_VISIBLE_FROM), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_VISIBLE_FROM), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public long VisibleFrom { get; set; }
	
	[BsonElement(DB_KEY_EXPIRATION), BsonIgnoreIfDefault]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_EXPIRATION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public long Expiration { get; set; }
	public int MinimumRank { get; set; }
	public int MinimumPercentile { get; set; }
	public long TimeAwarded { get; set; }
	public string LeaderboardId { get; set; }
	
	[BsonElement(DB_KEY_STATUS)]
	internal Status SentStatus { get; set; }
		
	internal enum Status { NotSent, IsSending, Sent }
	
	[BsonIgnore]
	[JsonIgnore]
	public string TemporaryID { get; set; }

	public Reward() => TemporaryID = Guid.NewGuid().ToString();
}