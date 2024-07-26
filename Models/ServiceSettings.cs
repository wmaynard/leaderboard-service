using System;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Models;

public class ServiceSettings : PlatformCollectionDocument
{
	public DateTime LastHourlyRollover { get; set; }
	public DateTime LastDailyRollover { get; set; }
	public DateTime LastWeeklyRollover { get; set; }
	public DateTime LastMonthlyRollover { get; set; }
}