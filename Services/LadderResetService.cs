using System;
using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Services;

public class LadderResetService : QueueService<LadderResetData>
{
    private string KEY_TIMESTAMPS = "ladderResetTimestamps";
    
    public LadderResetService() : base("ladderResets", intervalMs: 10_000) { }
    
    protected override void PrimaryNodeWork()
    {
        try
        {
            long[] resets = DynamicConfig.Instance?
                .Optional(KEY_TIMESTAMPS, "")
                ?.Split(",")
                .Select(str => long.TryParse(str.Trim(), out long ts) ? ts : 0)
                .Where(integer => integer > 0)
                .OrderBy(_ => _)
                .ToArray() 
                ?? Array.Empty<long>();

            if (!resets.Any(ts => ts < Timestamp.UnixTime))
                return;

            long affected = Require<LadderService>().ResetScores();
            Log.Info(Owner.Will, "Reset all leaderboard ladder scores.", data: new
            {
                Affected = affected
            });

            string updatedConfigValue = string.Join(",", resets
                .Where(value => value > Timestamp.UnixTime)
                .Select(value => value.ToString())
            );

            Require<DynamicConfig>().Update(Audience.LeaderboardService, KEY_TIMESTAMPS, updatedConfigValue);
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Unable to check or perform ladder score reset.", exception: e);
        }
    }

    protected override void OnTasksCompleted(LadderResetData[] data) { }
    protected override void ProcessTask(LadderResetData data) { }
}

public class LadderResetData : PlatformCollectionDocument
{

}