using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Models;

public class Account : PlatformCollectionDocument
{
	public string AccountID { get; set; }
	public long LastActive { get; set; }
	public bool Disqualified { get; set; }
}