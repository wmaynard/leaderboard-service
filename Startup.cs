using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Filters;

namespace Rumble.Platform.LeaderboardService;

public class Startup : PlatformStartup
{
	protected override PlatformOptions ConfigureOptions(PlatformOptions options) => options
		.SetProjectOwner(Owner.Will)
		.SetTokenAudience(Audience.LeaderboardService)
		.SetRegistrationName("Leaderboards")
		.SetPerformanceThresholds(warnMS: 30_000, errorMS: 60_000, criticalMS: 90_000)
		.DisableFeatures(CommonFeature.ConsoleObjectPrinting)
		.AddFilter<TrafficRejectionFilter>();
}