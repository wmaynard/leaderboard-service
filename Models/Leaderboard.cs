using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
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

		public IEnumerable<Entry> TopScores => Scores.Take(PAGE_SIZE);
		public IEnumerable<Entry> NearbyScores { get; private set; }

		public OrderedDictionary Ordur { get; set; }
		public void SetNearbyScores(string accountId)
		{
			// int position = Scores.IndexOf(accountId);
			// int skip = Math.Max(0, position - PAGE_SIZE / 2);
			// NearbyScores = Scores.Skip(skip).Take(PAGE_SIZE);
		}

		public Leaderboard()
		{
			Scores = new List<Entry>();
			// Scores = new Dictionary<string, long>();
			// Ordur = new OrderedDictionary(comparer: Comparer)
		}
	}
	public enum RolloverType { Hourly, Daily, Weekly, Monthly, Annually, None }

}