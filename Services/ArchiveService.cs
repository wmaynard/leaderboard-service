using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class ArchiveService : PlatformMongoService<Leaderboard>
	{
		private const int FALLBACK_DAYS_TO_KEEP = 30;
#pragma warning disable CS0169
		private DynamicConfigService _dynamicConfig;
#pragma warning restore CS0169

		public int DaysToKeep => _dynamicConfig.GameConfig.Optional<int?>("leaderboard_ArchiveDaysToKeep") ?? FALLBACK_DAYS_TO_KEEP;
		
		public ArchiveService() : base("archives") { }

		public void Stash(Leaderboard leaderboard)
		{
			Update(leaderboard, true);
		}
	}

	public class ArchivedLeaderboard : Leaderboard
	{
	}
}