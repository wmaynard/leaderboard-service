using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class LeaderboardEnrollment : PlatformDataModel
	{
		public string LeaderboardType { get; set; }
		// public string ShardId { get; set; }
		public int Tier { get; set; }
		public bool Inactive { get; set; } // Set to 0 if leaderboard rolls over and score is 0
		public int TimesCompleted { get; set; }
	}
}