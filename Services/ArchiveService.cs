using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class ArchiveService : PlatformMongoService<Leaderboard>
	{
		private const int FALLBACK_DAYS_TO_KEEP = 30;
		private readonly DynamicConfigService _dynamicConfig;

		public int DaysToKeep => _dynamicConfig.GameConfig.Optional<int?>("leaderboard_ArchiveDaysToKeep") ?? FALLBACK_DAYS_TO_KEEP;

		public ArchiveService(DynamicConfigService configService) : base("archives") => _dynamicConfig = configService;

		public void Stash(Leaderboard leaderboard)
		{
			Update(leaderboard, true);
		}
	}

	public class ArchivedLeaderboard : Leaderboard
	{
	}
}