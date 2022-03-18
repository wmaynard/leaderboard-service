using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;

namespace Rumble.Platform.LeaderboardService.Models
{
	[BsonIgnoreExtraElements]
	public class Leaderboard : PlatformCollectionDocument
	{
		internal const string DB_KEY_ID = "_id";
		internal const string DB_KEY_TIER = "tier";
		internal const string DB_KEY_TYPE = "type";
		internal const string DB_KEY_TITLE = "title";
		internal const string DB_KEY_DESCRIPTION = "desc";
		internal const string DB_KEY_ROLLOVER_TYPE = "rtype";
		internal const string DB_KEY_MAX_TIER = "max";
		internal const string DB_KEY_TIER_RULES = "rules";
		internal const string DB_KEY_SHARD_ID = "shard";
		internal const string DB_KEY_SCORES = "scores";
		internal const string DB_KEY_RESETTING = "lock";

		public const string FRIENDLY_KEY_TYPE = "leaderboardId";
		public const string FRIENDLY_KEY_TIER = "tier";
		public const string FRIENDLY_KEY_TITLE = "title";
		public const string FRIENDLY_KEY_DESCRIPTION = "description";
		public const string FRIENDLY_KEY_ROLLOVER_TYPE = "rolloverType";
		public const string FRIENDLY_KEY_ROLLOVER_TYPE_STRING = "rolloverTypeVerbose";
		public const string FRIENDLY_KEY_MAX_TIER = "maxTier";
		public const string FRIENDLY_KEY_TIER_RULES = "tierRules";
		public const string FRIENDLY_KEY_SHARD_ID = "shardId";
		public const string FRIENDLY_KEY_SCORES = "scores";
		public const string FRIENDLY_KEY_RESETTING = "locked";
		
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
		
		[BsonElement(DB_KEY_TIER)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER)]
		public int Tier { get; set; }
		
		[BsonElement(DB_KEY_MAX_TIER)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_MAX_TIER)]
		public int MaxTier { get; set; }
		
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
		
		[BsonElement(DB_KEY_SCORES), BsonIgnoreIfNull]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_SCORES), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public List<Entry> Scores { get; set; }
		
		[BsonElement(DB_KEY_RESETTING), BsonIgnoreIfDefault]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_RESETTING), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
		public bool IsResetting { get; set; }

		[BsonIgnore]
		[JsonIgnore]
		internal bool IsShard => ShardID != null;
		
		[BsonIgnore]
		[JsonIgnore]
		internal List<Ranking> RecentRanks { get; private set; }

		internal List<Ranking> CalculateRanks()
		{
			int rank = 1;
			Ranking toRankings(IGrouping<long, Entry> group)
			{
				Ranking output = new Ranking(rank, group);
				rank += output.NumberOfAccounts;
				return output;
			}
			return RecentRanks = Scores
				.GroupBy(entry => entry.Score)
				.OrderByDescending(grouping => grouping.Key)
				.Select(toRankings)
				.ToList();
		}

		public GenericData GenerateScoreResponse(string accountId)
		{
			List<Ranking> sorted = CalculateRanks();

			List<Ranking> topScores = new List<Ranking>();
			for (int results = 0, index = 0; index < sorted.Count && results < PAGE_SIZE && results < Scores.Count; index++)
			{
				topScores.Add(sorted[index]);
				results += topScores.Last().NumberOfAccounts;
			}

			List<Ranking> nearbyScores = new List<Ranking>();
			try
			{
				Ranking playerRank = sorted.First(ranking => ranking.HasAccount(accountId));
				nearbyScores.Add(playerRank);
				playerRank.IsRequestingPlayer = true;
				// TODO: If we don't care about nearby scores, we can get rid of this loop.
				int playerIndex = sorted.IndexOf(sorted.First(ranking => ranking.HasAccount(accountId)));
				for (int offset = 1; nearbyScores.Sum(ranking => ranking.NumberOfAccounts) < PAGE_SIZE; offset++)
				{
					int before = playerIndex - offset;
					int after = playerIndex + offset;

					// Not enough entries to generate nearby scores properly.  Nearby scores contains all scores.
					if (before < 0 && after > sorted.Count)
						break;
					
					if (before >= 0)
						nearbyScores.Insert(0, sorted[before]);
					if (after < sorted.Count)
						nearbyScores.Add(sorted[after]);
				}
			}
			catch (Exception e)
			{
				Log.Error(Owner.Default, $"Player does not have a placement in leaderboard '{Type}'", data: new
				{
					LeaderboardId = Id,
					LeaderboardType = Type,
					AccountId = accountId
				}, exception: e);
				throw;
			}


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

		public bool Validate(out string[] errors)
		{
			List<string> messages = new List<string>();
			bool output = true;

			bool Test(bool condition, string error)
			{
				if (!condition)
					messages.Add(error);
				return condition;
			}

			output &= Test(condition: !string.IsNullOrWhiteSpace(Type), error: $"{FRIENDLY_KEY_TYPE} not provided.");
			output &= Test(condition: !string.IsNullOrWhiteSpace(Title), error: $"{FRIENDLY_KEY_TITLE} not provided.");
			output &= Test(condition: !string.IsNullOrWhiteSpace(Description), error: $"{FRIENDLY_KEY_DESCRIPTION} not provided.");
			output &= Test(condition: MaxTier >= 0, error: $"{FRIENDLY_KEY_MAX_TIER} must be greater than or equal to 0.");
			output &= Test(condition: TierRules.Any(), error: $"{FRIENDLY_KEY_TIER_RULES} must be defined.");

			if (TierRules != null)
			{
				foreach (TierRules rules in TierRules)
				{
					output &= Test(
						condition: rules.PromotionRank < rules.DemotionRank || rules.DemotionRank <= 0, 
						error: $"'{Models.TierRules.FRIENDLY_KEY_PROMOTION_RANK}' must be greater than '{Models.TierRules.FRIENDLY_KEY_DEMOTION_RANK}'."
					);
					output &= Test(
						condition: rules.PlayersPerShard > -1 || rules.PlayersPerShard >= Math.Max(rules.PromotionRank, rules.DemotionRank),
						error: $"'{Models.TierRules.FRIENDLY_KEY_PLAYERS_PER_SHARD}' must be greater than promotion and demotion rules if it's a non-negative number."
					);
				}
				for (int tier = 0; tier <= MaxTier; tier++)
					output &= Test(condition: TierRules.Count(rules => rules.Tier == tier) == 1, error: $"{FRIENDLY_KEY_TIER_RULES} invalid for tier {tier}.");

				foreach (Reward reward in TierRules.SelectMany(rules => rules.Rewards))
				{
					output &= Test(condition: !string.IsNullOrWhiteSpace(reward.Subject), error: $"Reward value '{Reward.FRIENDLY_KEY_SUBJECT}' not provided.");
					output &= Test(condition: !string.IsNullOrWhiteSpace(reward.Message), error: $"Reward value '{Reward.FRIENDLY_KEY_BODY}' not provided.");
					output &= Test(condition: reward.Contents != null && reward.Contents.Any(), error: $"Reward value '{Reward.FRIENDLY_KEY_ATTACHMENTS}' not provided.");
					output &= Test(condition: reward.Tier >= 0, error: $"Reward value '{Reward.FRIENDLY_KEY_TIER}' must be greater than or equal to 0.");
					output &= Test(condition: reward.MinimumRank >= 1 || (reward.MinimumPercentile >= 0 && reward.MinimumPercentile <= 100), error: "Reward criteria invalid.");

					if (reward.Contents == null)
						continue;

					foreach (Attachment item in reward.Contents)
					{
						output &= Test(condition: item.Quantity > 0, error: $"Reward attachment value '{Attachment.FRIENDLY_KEY_QUANTITY}' must be greater than 0.");
						output &= Test(condition: !string.IsNullOrWhiteSpace(item.Type), error: $"Reward attachment value '{Attachment.FRIENDLY_KEY_TYPE}' not provided.");
						output &= Test(condition: !string.IsNullOrWhiteSpace(item.ResourceID), error: $"Reward attachment value '{Attachment.FRIENDLY_KEY_RESOURCE_ID}' not provided.");
					}
				}
			}

			if (!output)
				Log.Error(Owner.Will, $"Leaderboard {Type} failed validation.", data: new
				{
					Messages = messages
				});
			errors = messages.ToArray();
			return output;
		}

		internal void ResetID() => Id = null;

	}

}