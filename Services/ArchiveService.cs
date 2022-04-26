using System.Collections.Generic;
using MongoDB.Driver;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class ArchiveService : PlatformMongoService<Leaderboard>
{
	private const int FALLBACK_DAYS_TO_KEEP = 30;
	private readonly DynamicConfigService _dynamicConfig;

	public int DaysToKeep => _dynamicConfig.GameConfig.Optional<int?>("leaderboard_ArchiveDaysToKeep") ?? FALLBACK_DAYS_TO_KEEP;

	public ArchiveService(DynamicConfigService configService) : base("archives") => _dynamicConfig = configService;

	public void Stash(Leaderboard leaderboard, out string archiveId)
	{
		leaderboard.EndTime = Timestamp.UnixTimeUTCMS;
		leaderboard.ResetID();
		Update(leaderboard, true);
		archiveId = leaderboard.Id;
	}

	public List<Leaderboard> Lookup(string type, int count = 1) => _collection
			.Find(leaderboard => leaderboard.Type == type)
			.SortByDescending(leaderboard => leaderboard.EndTime)
			.Limit(count)
			.ToList();

	public List<Leaderboard> Lookup(string type, string accountId, int count = 1) => _collection
		.Find(
			filter: Builders<Leaderboard>.Filter.And(
				Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
				Builders<Leaderboard>.Filter.ElemMatch(
					field: leaderboard => leaderboard.Scores,
					filter: entry => entry.AccountID == accountId
				)
			)
		).SortByDescending(leaderboard => leaderboard.EndTime)
		.Limit(count)
		.ToList();
}