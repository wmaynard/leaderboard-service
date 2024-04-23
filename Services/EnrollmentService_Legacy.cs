using System;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class EnrollmentService_Legacy : PlatformMongoService<Enrollment>
{
    public EnrollmentService_Legacy() : base("enrollments") { }

    /// <summary>
    /// Find all enrollments that have a higher tier than their max seasonal tier, then update the max seasonal tier to match.
    /// Requires a really ugly Mongo query because the update is dependent on the record it's looking at - and there's no
    /// clean way to do this from C# as far as I could find.
    /// </summary>
    /// <returns>The affected number of records</returns>
    public long UpdateSeasonalMaxTiers()
    {
        try
        {
            return _collection.UpdateMany(
                filter: $"{{ $expr: {{ $lt: [ '${Enrollment.DB_KEY_SEASONAL_TIER}', '${Enrollment.DB_KEY_TIER}' ] }} }}", 
                update: PipelineDefinition<Enrollment, Enrollment>.Create($"{{ $set: {{ {Enrollment.DB_KEY_SEASONAL_TIER}: '${Enrollment.DB_KEY_TIER}' }} }}")
            ).ModifiedCount;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Unable to update seasonal max tier in enrollments.", exception: e);
        }

        return 0;
    }
}