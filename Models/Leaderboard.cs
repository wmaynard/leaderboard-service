using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;

namespace Rumble.Platform.LeaderboardService.Models
{
	[BsonIgnoreExtraElements]
	public class Leaderboard : PlatformCollectionDocument
	{
		internal const string DB_KEY_DESCRIPTION = "desc";
		internal const string DB_KEY_ID = "_id";
		internal const string DB_KEY_RESETTING = "lock";
		internal const string DB_KEY_ROLLOVER_TYPE = "rtype";
		internal const string DB_KEY_SCORES = "scores";
		internal const string DB_KEY_SHARD_ID = "shard";
		internal const string DB_KEY_START_TIME = "start";
		internal const string DB_KEY_TIER = "tier";
		internal const string DB_KEY_TIER_COUNT = "max";
		internal const string DB_KEY_TIER_RULES = "rules";
		internal const string DB_KEY_TIME_ENDED = "end";
		internal const string DB_KEY_TITLE = "title";
		internal const string DB_KEY_TYPE = "type";

		public const string FRIENDLY_KEY_DESCRIPTION = "description";
		public const string FRIENDLY_KEY_RESETTING = "locked";
		public const string FRIENDLY_KEY_ROLLOVER_TYPE = "rolloverType";
		public const string FRIENDLY_KEY_ROLLOVER_TYPE_STRING = "rolloverTypeVerbose";
		public const string FRIENDLY_KEY_START_TIME = "startsOn";
		public const string FRIENDLY_KEY_SCORES = "scores";
		public const string FRIENDLY_KEY_SHARD_ID = "shardId";
		public const string FRIENDLY_KEY_TIER = "tier";
		public const string FRIENDLY_KEY_TIER_COUNT = "tierCount";
		public const string FRIENDLY_KEY_TIER_RULES = "tierRules";
		public const string FRIENDLY_KEY_TIME_ENDED = "lastRollover";
		public const string FRIENDLY_KEY_TITLE = "title";
		public const string FRIENDLY_KEY_TYPE = "leaderboardId";

		public const int PAGE_SIZE = 50;
		
		[BsonElement(DB_KEY_TYPE), BsonRequired]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TYPE)]
		public string Type { get; set; }
		
		[BsonElement(DB_KEY_TITLE), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TITLE), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Title { get; set; }
		
		[BsonElement(DB_KEY_DESCRIPTION), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_DESCRIPTION), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Description { get; set; }
		
		[BsonElement(DB_KEY_ROLLOVER_TYPE)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ROLLOVER_TYPE)]
		public RolloverType RolloverType { get; set; }
		
		[BsonIgnore]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_ROLLOVER_TYPE_STRING), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string RolloverTypeString => RolloverType.ToString();
		
		[BsonIgnore]
		[JsonIgnore]
		public bool IsFull => Scores.Count >= CurrentTierRules.PlayersPerShard;
		
		[BsonElement(DB_KEY_TIER)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER)]
		public int Tier { get; set; }
		
		[BsonElement(DB_KEY_TIER_COUNT)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER_COUNT)]
		public int TierCount { get; set; }
		
		[BsonIgnore]
		[JsonIgnore]
		public int MaxTier => Math.Max(0, TierCount -1 );
		
		[BsonElement(DB_KEY_TIER_RULES), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER_RULES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public TierRules[] TierRules { get; set; }
		
		[BsonIgnore]
		[JsonIgnore]
		public TierRules CurrentTierRules => TierRules.FirstOrDefault(rules => rules.Tier == Tier)
			?? throw new InvalidLeaderboardException(this, $"Leaderboard tier rules not defined for leaderboard {Type}-{Id}.");

		[BsonIgnore]
		[JsonIgnore]
		public Reward[] CurrentTierRewards => CurrentTierRules.Rewards
			?? throw new InvalidLeaderboardException(this, $"Leaderboard tier rewards not defined for leaderboard {Type}-{Id}.");
		
		[BsonElement(DB_KEY_SHARD_ID), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SHARD_ID), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string ShardID { get; set; } // can be null
		
		[BsonElement(DB_KEY_START_TIME), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_START_TIME), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long StartTime { get; set; }
		
		[BsonElement(DB_KEY_SCORES), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCORES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public List<Entry> Scores { get; set; }
		
		[BsonElement(DB_KEY_RESETTING), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_RESETTING), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool IsResetting { get; set; }

		[BsonIgnore]
		[JsonIgnore]
		internal bool IsShard => ShardID != null;
		
		// [BsonIgnore]
		// [JsonIgnore]
		// internal List<Ranking> RecentRanks { get; private set; }
		
		[BsonElement(DB_KEY_TIME_ENDED), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIME_ENDED), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public long EndTime { get; internal set; }

		internal List<Entry> CalculateRanks()
		{
			List<Entry> output = Scores
				.OrderByDescending(entry => entry.Score)
				.ThenBy(entry => entry.LastUpdated)
				.ToList();

			foreach (Entry entry in output)
				entry.Rank = output.IndexOf(entry) + 1;

			return output;
		}

		public GenericData GenerateScoreResponse(string accountId)
		{
			List<Entry> sorted = CalculateRanks();

			int playerIndex = -1;
			for (int i = 0; i < sorted.Count; i++)
			{
				sorted[i].Rank = i + 1;
				if (sorted[i].AccountID == accountId)
					playerIndex = i;
			}

			IEnumerable<Entry> topScores = sorted.Take(PAGE_SIZE);
			IEnumerable<Entry> nearbyScores = sorted
				.Skip(Math.Max(playerIndex - PAGE_SIZE / 2, 0))
				.Take(PAGE_SIZE);

			if (playerIndex == -1)
				Log.Error(Owner.Default, $"Player does not have a placement in leaderboard '{Type}'", data: new
				{
					LeaderboardId = Id,
					LeaderboardType = Type,
					AccountId = accountId
				});
			
			return new GenericData()
			{
				{ "topScores", topScores },
				{ "nearbyScores", nearbyScores }
			};
		}

		public Leaderboard()
		{
			Tier = 1;
			Scores = new List<Entry>();
		}

		protected override void Validate(out List<string> errors)
		{
			
			errors = null;

			TierRules ??= Array.Empty<TierRules>();
			Test(condition: !string.IsNullOrWhiteSpace(Type), error: $"{FRIENDLY_KEY_TYPE} not provided.", ref errors);
			Test(condition: !string.IsNullOrWhiteSpace(Title), error: $"{FRIENDLY_KEY_TITLE} not provided.", ref errors);
			Test(condition: !string.IsNullOrWhiteSpace(Description), error: $"{FRIENDLY_KEY_DESCRIPTION} not provided.", ref errors);
			Test(condition: TierCount > 0, error: $"{FRIENDLY_KEY_TIER_COUNT} must be greater than 0.", ref errors);
			Test(condition: TierRules.Any(), error: $"{FRIENDLY_KEY_TIER_RULES} must be defined for {Type}.", ref errors);

			foreach (TierRules rules in TierRules)
			{
				Test(
					condition: rules.PromotionRank < rules.DemotionRank || rules.DemotionRank <= 0, 
					error: $"'{Models.TierRules.FRIENDLY_KEY_PROMOTION_RANK}' must be greater than '{Models.TierRules.FRIENDLY_KEY_DEMOTION_RANK}'.",
					ref errors
				);
				Test(
					condition: rules.PlayersPerShard > -1 || rules.PlayersPerShard >= Math.Max(rules.PromotionRank, rules.DemotionRank),
					error: $"'{Models.TierRules.FRIENDLY_KEY_PLAYERS_PER_SHARD}' must be greater than promotion and demotion rules if it's a non-negative number.", 
					ref errors
				);
			}
			for (int tier = 0; tier <= MaxTier; tier++)
				Test(condition: TierRules.Count(rules => rules.Tier == tier) == 1, error: $"{FRIENDLY_KEY_TIER_RULES} invalid for {tier}-{Type}.", ref errors);

			foreach (Reward reward in TierRules.SelectMany(rules => rules.Rewards))
			{
				Test(condition: !string.IsNullOrWhiteSpace(reward.Subject), error: $"Reward value '{Reward.FRIENDLY_KEY_SUBJECT}' not provided.", ref errors);
				Test(condition: !string.IsNullOrWhiteSpace(reward.Message), error: $"Reward value '{Reward.FRIENDLY_KEY_BODY}' not provided.", ref errors);
				Test(condition: reward.Contents != null && reward.Contents.Any(), error: $"Reward value '{Reward.FRIENDLY_KEY_ATTACHMENTS}' not provided.", ref errors);
				Test(condition: reward.Tier >= 0, error: $"Reward value '{Reward.FRIENDLY_KEY_TIER}' must be greater than or equal to 0.", ref errors);
				Test(condition: reward.MinimumRank >= 1 || (reward.MinimumPercentile >= 0 && reward.MinimumPercentile <= 100), error: "Reward criteria invalid.", ref errors);

				if (reward.Contents == null)
					continue;

				foreach (Attachment item in reward.Contents)
				{
					Test(condition: item.Quantity > 0, error: $"Reward attachment value '{Attachment.FRIENDLY_KEY_QUANTITY}' must be greater than 0.", ref errors);
					Test(condition: !string.IsNullOrWhiteSpace(item.Type), error: $"Reward attachment value '{Attachment.FRIENDLY_KEY_TYPE}' not provided.", ref errors);
					Test(condition: !string.IsNullOrWhiteSpace(item.ResourceID), error: $"Reward attachment value '{Attachment.FRIENDLY_KEY_RESOURCE_ID}' not provided.", ref errors);
				}
			}
		}
	}
}