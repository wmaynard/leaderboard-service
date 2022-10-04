using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

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
	internal const string DB_KEY_ATTACHMENTS = "items";
	internal const string DB_KEY_STATUS = "status";
	internal const string DB_KEY_TIER = "tier";
	internal const string DB_KEY_MINIMUM_RANK = "min";
	internal const string DB_KEY_MINIMUM_RANK_PERCENT = "min%";
	internal const string DB_KEY_DATA = "data";

	public const string FRIENDLY_KEY_RECIPIENT = "accountId";
	public const string FRIENDLY_KEY_SUBJECT = "subject";
	public const string FRIENDLY_KEY_BODY = "body";
	public const string FRIENDLY_KEY_BANNER = "banner";
	public const string FRIENDLY_KEY_ICON = "icon";
	public const string FRIENDLY_KEY_INTERNAL_NOTE = "internalNote";
	public const string FRIENDLY_KEY_VISIBLE_FROM = "visibleFrom";
	public const string FRIENDLY_KEY_EXPIRATION = "expiration";
	public const string FRIENDLY_KEY_ATTACHMENTS = "attachments";
	public const string FRIENDLY_KEY_TIER = "tier";
	public const string FRIENDLY_KEY_DATA = "data";
	
	[BsonIgnore]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_RECIPIENT), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Recipient { get; set; }
	
	[BsonElement(DB_KEY_DATA), BsonIgnoreIfNull]
	[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DATA), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RumbleJson RankingData { get; set; }

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
	
	[BsonElement(DB_KEY_MINIMUM_RANK)]
	public int MinimumRank { get; set; }
	[BsonElement(DB_KEY_MINIMUM_RANK_PERCENT)]
	public int MinimumPercentile { get; set; }
	// public long TimeAwarded { get; set; }
	// public string LeaderboardId { get; set; }
	
	[BsonElement(DB_KEY_STATUS)]
	internal Status SentStatus { get; set; }
		
	internal enum Status { NotSent, Sent }
	
	[BsonIgnore]
	[JsonIgnore]
	public string TemporaryID { get; set; }

	public Reward() => TemporaryID = Guid.NewGuid().ToString();

	public override bool Equals(object? obj)
	{
		if (obj is not Reward other)
			return false;
		
		return Tier == other.Tier
			&& Subject == other.Subject
			&& SentStatus == other.SentStatus
			&& MinimumPercentile == other.MinimumPercentile
			&& MinimumRank == other.MinimumRank
			&& Expiration == other.Expiration
			&& InternalNote == other.InternalNote;
	}

	public override string ToString() => Contents.Aggregate("", (current, attachment) => current + $"{attachment.Quantity}x {attachment.ResourceID}, ")[..^2];
}