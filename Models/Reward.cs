using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Reward : PlatformDataModel
	{
		public int Tier { get; set; }
		public Item[] Contents { get; set; }
		public int MinimumRank { get; set; }
		public int MinimumPercentile { get; set; }
	}
}