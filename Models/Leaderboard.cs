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
		internal const string DB_KEY_TIER = "Tier";
		internal const string DB_KEY_TYPE = "Type";
		
		public const string FRIENDLY_KEY_TYPE = "leaderboardId";
		public const string FRIENDLY_KEY_TIER = "Tier";
		public const string FRIENDLY_KEY_TITLE = "title";
		public const string FRIENDLY_KEY_DESCRIPTION = "description";
		public const string FRIENDLY_KEY_MAX_TIER = "maxTier";
		public const string FRIENDLY_KEY_TIER_RULES = "tierRules";
		
		public const int PAGE_SIZE = 50;
		
		[BsonElement(DB_KEY_TYPE), BsonRequired]
		[JsonPropertyName(FRIENDLY_KEY_TYPE)]
		public string Type { get; set; }
		
		[JsonPropertyName(FRIENDLY_KEY_TITLE)]
		public string Title { get; set; }
		public string Description { get; set; }
		public long Rollover { get; set; }
		public RolloverType RolloverType { get; set; }
		public string RolloverTypeString => RolloverType.ToString();
		public long LastReset { get; set; }
		
		[BsonElement(DB_KEY_TIER)]
		[JsonInclude, JsonPropertyName(FRIENDLY_KEY_TIER)]
		public int Tier { get; set; }
		[JsonPropertyName(FRIENDLY_KEY_MAX_TIER)]
		public int MaxTier { get; set; }
		[JsonPropertyName(FRIENDLY_KEY_TIER_RULES)]
		public TierRules[] TierRules { get; set; }
		public TierRules CurrentTierRules => TierRules.FirstOrDefault(rules => rules.Tier == Tier)
			?? throw new InvalidLeaderboardException(this, $"Leaderboard tier rules not defined for leaderboard {Type}-{Id}.");

		public Reward[] CurrentTierRewards => CurrentTierRules.Rewards
			?? throw new InvalidLeaderboardException(this, $"Leaderboard tier rewards not defined for leaderboard {Type}-{Id}.");
		public int PlayersPerShard { get; set; }
		public string ShardID { get; set; } // can be null
		public List<Entry> Scores { get; set; }
		public bool IsResetting { get; set; }

		internal bool IsShard => ShardID != null;
		
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