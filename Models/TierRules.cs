using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class TierRules : PlatformDataModel
	{
		public int Tier { get; set; }
		public int PromotionRank { get; set; }
		public int DemotionRank { get; set; }
		public int PromotionPercentage { get; set; }
		public int DemotionPercentage { get; set; }
	}
}