using System.Collections.Generic;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Leaderboard : PlatformCollectionDocument
	{
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
		public Dictionary<string, long> Scores { get; set; }
		public bool IsResetting { get; set; }

		public Leaderboard()
		{
			Scores = new Dictionary<string, long>();
		}
	}
	public enum RolloverType { Hourly, Daily, Weekly, Monthly, Annually, None }

}