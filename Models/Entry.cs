using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Entry : PlatformDataModel
	{
		public string AccountID { get; set; }
		public long Score { get; set; }
	}
}