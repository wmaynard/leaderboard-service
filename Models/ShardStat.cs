using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Models;

public class ShardStat : PlatformDataModel
{
    public string LeaderboardId { get; set; }
    public int Tier { get; set; }
    public long TotalShards => PlayerCounts.Length;
    public long[] PlayerCounts { get; set; }
}