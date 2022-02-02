using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Leaderboard : PlatformCollectionDocument
	{
		public const int PAGE_SIZE = 50;
		public string Type { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public long Rollover { get; set; }
		public RolloverType RolloverType { get; set; }
		public long LastReset { get; set; }
		public int Tier { get; set; }
		public int MaxTier { get; set; }
		public TierRules TierRules { get; set; }
		public int PlayersPerShard { get; set; }
		public string ShardID { get; set; } // can be null
		public Reward[] Rewards { get; set; }
		public List<Entry> Scores { get; set; }
		public bool IsResetting { get; set; }

		public GenericData GenerateScoreResponse(string accountId)
		{
			int rank = 1;

			Ranking toRankings(IGrouping<long, Entry> group)
			{
				Ranking output = new Ranking(rank, group);
				rank += output.NumberOfAccounts;
				return output;
			}

			List<Ranking> sorted = Scores
				.GroupBy(entry => entry.Score)
				.OrderByDescending(grouping => grouping.Key)
				.Select(toRankings)
				.ToList();

			List<Ranking> topScores = new List<Ranking>();
			for (int results = 0, index = 0; index < sorted.Count && results < PAGE_SIZE && results < Scores.Count; index++)
			{
				topScores.Add(sorted[index++]);
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

					if (before < 0 && after > sorted.Count)
					{
						Log.Warn(Owner.Default, "Not enough entries to generate nearby scores properly.  Nearby scores contains all scores.");
						break;
					}
					
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
				});
				throw;
			}


			return new GenericData()
			{
				{ "topScores", topScores },
				{ "nearbyScores", nearbyScores }
			};
		}

		public Leaderboard() => Scores = new List<Entry>();
	}
	public enum RolloverType { Hourly, Daily, Weekly, Monthly, Annually, None }

}