using System.Collections.Generic;
using MongoDB.Driver;
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

		public void Stash(Leaderboard leaderboard, out string archiveId)
		{
			Update(leaderboard, true);
			archiveId = leaderboard.Id;
		}

		public List<Leaderboard> View(string type, string accountId, int count = 1)
		{
			// _collection.Find(
			// 	filter: Builders<Leaderboard>.Filter.And(
			// 		Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
			// 		Builders<Leaderboard>.Filter.ElemMatch(leaderboard => leaderboard.Scores)
			// 		
			// 	)
			// )
			return null;
		}
	}

	public class ArchivedLeaderboard : Leaderboard
	{
	}
}