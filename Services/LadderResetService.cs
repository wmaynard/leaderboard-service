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

public class LadderResetService : QueueService<LadderResetData>
{
    private string KEY_TIMESTAMPS = "ladderResetTimestamps";

    private LadderDefinitionService _seasons;

    public LadderResetService(LadderDefinitionService seasons) : base("ladderResets", intervalMs: 10_000)
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
}

// TODO: This is unused.  Create a model-less QueueService in platform-common?
public class LadderResetData : PlatformCollectionDocument { }

public class LadderDefinitionService : MinqService<LadderSeasonDefinition>
{
    public LadderDefinitionService() : base("ladderDefinitions") { }

    public LadderSeasonDefinition[] Define(params LadderSeasonDefinition[] seasons)
    {
        if (!seasons.Any())
            throw new PlatformException("No valid definitions found.");

        long existing = mongo
            .Where(query => query
                .EqualTo(definition => definition.Ended, true)
                .ContainedIn(definition => definition.SeasonId, seasons.Select(definition => definition.SeasonId))
            )
            .Count();

        if (existing > 0)
            throw new PlatformException("Season ID conflict; you cannot run a new season with an ID that's previously ended.");

        long deleted = mongo
            .WithTransaction(out Transaction transaction)
            .Where(query => query.EqualTo(definition => definition.Ended, false))
            .Delete();
        
        mongo
            .WithTransaction(transaction)
            .Insert(seasons);

        Commit(transaction);
        
        Log.Info(Owner.Will, "Altered upcoming ladder season definitions.", data: new
        {
            DeletedCount = deleted,
            InsertedCount = seasons.Length
        });

        return seasons;
    }
    
    public LadderSeasonDefinition GetCurrentSeason() => mongo
        .Where(query => query.EqualTo(definition => definition.Ended, false))
        .Limit(1)
        .Sort(sort => sort.OrderBy(definition => definition.EndTime))
        .ToArray()
        .FirstOrDefault();

    public void EndSeason(LadderSeasonDefinition season)
    {
        Log.Local(Owner.Will, $"Ending season {season.SeasonId}", emphasis: Log.LogType.WARN);
        if (season == null)
            throw new PlatformException("Cannot end a null season definition");
        
        mongo
            .WithTransaction(out Transaction transaction)
            .ExactId(season.Id)
            .Update(query => query.Set(definition => definition.Ended, true));

        Require<LadderHistoryService>().ClearHistories(transaction, season);
        Require<LadderService>().ResetScores(transaction, season);
        Require<LadderHistoryService>().GrantRewards(season);
        
        Commit(transaction);
    }
}