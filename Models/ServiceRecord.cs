using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class ServiceRecord : PlatformDataModel
	{
		public string LeaderboardType { get; set; }
		public int CurrentTier { get; set; }
	}
}