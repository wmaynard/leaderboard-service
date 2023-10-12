using System;
using System.Linq;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

/// <summary>
/// Leverages the QueueService to ensure only one active instance of the leaderboards project is checking for
/// season end times.  When a season end time has passed, this begins the process of performing the reset.
/// </summary>
public class LadderResetService : QueueService<LadderResetService.LadderResetData>
{
    private string KEY_TIMESTAMPS = "ladderResetTimestamps";

    private SeasonDefinitionService _seasons;

    public LadderResetService(SeasonDefinitionService seasons) : base("ladderResets", intervalMs: 10_000)
        => _seasons = seasons;
    
    protected override void PrimaryNodeWork()
    {
        try
        {
            LadderSeasonDefinition season = _seasons.GetCurrentSeason();

            if (season == null)
                Log.Local(Owner.Will, "No current season!  Ladder cannot reset!  Ensure Design has defined a current season.");
            else if (season.EndTime <= Timestamp.UnixTime)
                _seasons.EndSeason(season);
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Unable to check or perform ladder score reset.", exception: e);
        }
    }

    protected override void OnTasksCompleted(LadderResetData[] data) { }
    protected override void ProcessTask(LadderResetData data) { }
    
    
    // TODO: This is unused.  Create a model-less QueueService in platform-common?
    public class LadderResetData : PlatformCollectionDocument { }
}