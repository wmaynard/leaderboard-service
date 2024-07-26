using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Models;

public class ShardStat : PlatformDataModel
{
    public string LeaderboardId { get; set; }
    public int Tier { get; set; }
    public long TotalShards => PlayerCounts.Length;
    public long[] PlayerCounts { get; set; }
    public long ActivePlayers { get; set; }
}