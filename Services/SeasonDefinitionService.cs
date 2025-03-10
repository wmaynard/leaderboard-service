using System;
using System.Linq;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

/// <summary>
/// Allows for the definition of and logic for Ladder Seasons.
/// </summary>
public class SeasonDefinitionService : MinqService<LadderSeasonDefinition>
{
    public SeasonDefinitionService() : base("ladderDefinitions") { }

    /// <summary>
    /// Creates a record for provided seasons.  WARNING: This will also delete all existing, still active seasons.
    /// </summary>
    /// <param name="seasons"></param>
    /// <returns></returns>
    /// <exception cref="PlatformException"></exception>
    public LadderSeasonDefinition[] Define(params LadderSeasonDefinition[] seasons)
    {
        if (!seasons.Any())
            throw new PlatformException("No valid definitions found.");

        string[] ended = mongo
            .Where(query => query
                .EqualTo(definition => definition.Ended, true)
                .ContainedIn(definition => definition.SeasonId, seasons.Select(definition => definition.SeasonId))
            )
            .Project(definition => definition.SeasonId)
            .ToArray();

        // Ignore changes to any seasons that have already ended.
        if (ended.Any())
        {
            seasons = seasons
                .Where(season => !ended.Contains(season.SeasonId))
                .ToArray();
            Log.Info(Owner.Will, "Received update data for seasons that ended previously; it will be ignored", data: new
            {
                SeasonIds = string.Join(',', ended)
            });
            if (!seasons.Any())
                throw new PlatformException("No new or modified seasons found that have not ended; cannot define seasons.");
        }

        // Delete all seasons that have NOT ended.
        long deleted = mongo
            .WithTransaction(out Transaction transaction)
            .Where(query => query.EqualTo(definition => definition.Ended, false))
            .Delete();
        
        // Insert all the new, defined seasons.
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
        .Sort(sort => sort.OrderBy(definition => definition.EndTime))
        .Limit(1)
        .ToArray()
        .FirstOrDefault();

    /// <summary>
    /// Ends the provided season.  This sets Ended to true, deletes existing histories with the season ID, creates new
    /// histories with the season ID, resets all ladder scores to the defined FallbackScore and those below it to 0, then
    /// grants rewards as appropriate.  All of this is done in a transaction. 
    /// </summary>
    /// <param name="season"></param>
    /// <exception cref="PlatformException"></exception>
    public void EndSeason(LadderSeasonDefinition season)
    {
        Log.Local(Owner.Will, $"Ending season {season.SeasonId}", emphasis: Log.LogType.WARN);
        if (season == null)
            throw new PlatformException("Cannot end a null season definition");
        
        Log.Local(Owner.Will, "Setting season as ended.");
        Transaction transaction = null;
        mongo
            // .WithTransaction(out Transaction transaction)
            .ExactId(season.Id)
            .Update(query => query.Set(definition => definition.Ended, true));

        long start = Timestamp.Now;
        try
        {
            Log.Local(Owner.Will, $"Clearing histories with the season ID {season.SeasonId}");
            Require<LadderHistoryService>().ClearHistories(transaction, season);
            Log.Local(Owner.Will, "Resetting scores for season");
            Require<LadderService>().ResetScores(transaction, season);
            Log.Local(Owner.Will, "Granting rewards for season");
            Require<LadderHistoryService>().GrantRewards(transaction, season);
            
            Log.Local(Owner.Will, "Committing transaction");
            Commit(transaction);

            long secondsTaken = Timestamp.Now - start;
            SlackDiagnostics message = SlackDiagnostics
                .Log($"Ladder season ended: {season.SeasonId}", $"```{season.ToJson()}```");

            if (secondsTaken > 20)
                message
                    .Tag(Owner.Will)
                    .AddMessage($"Reset took {secondsTaken} seconds!  This is edging close to the transactional limit.");
            
            message
                .Send()
                .Wait();
        }
        catch (Exception e)
        {
            Log.Critical(Owner.Will, "Failed season rollover!  Transaction will be aborted.  This requires investigation.", data: new
            {
                SeasonId = season.Id,
                TimeTaken = Timestamp.Now - start
            }, exception: e);
            Abort(transaction);
        }
    }
}