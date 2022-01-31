using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Item : PlatformDataModel
	{
		public int Quantity { get; set; }
		public string ResourceID { get; set; }
		public long ReceivedOn { get; set; }
	}
}