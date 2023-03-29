using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class EnrollmentService : PlatformMongoService<Enrollment>
{
	public EnrollmentService() : base("enrollments") { }

	private string[] GetInactiveAccounts(string leaderboardType, bool forDemotion = true)
	{
		try
		{
			List<FilterDefinition<Enrollment>> filters = new List<FilterDefinition<Enrollment>>
			{
				Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, leaderboardType),
				Builders<Enrollment>.Filter.Eq(enrollment => enrollment.IsActive, false)
			};
			if (forDemotion)
				filters.Add(Builders<Enrollment>.Filter.Gt(enrollment => enrollment.Tier, 0));

			return _collection
				.Find(Builders<Enrollment>.Filter.And(filters))
				.Project<string>(Builders<Enrollment>.Projection.Expression(enrollment => enrollment.AccountID))
				.ToList()
				.ToArray();
		}
		catch (Exception e)
		{
			Log.Error(Owner.Will, "Could not retrieve inactive accounts.", data: new
			{
				LeaderboardType = leaderboardType
			}, e);
			return Array.Empty<string>();
		}
	}

	public Enrollment FindOrCreate(string accountId, string leaderboardType)
	{
		Enrollment output = _collection
			.Find(filter: enrollment => enrollment.AccountID == accountId && enrollment.LeaderboardType == leaderboardType)
			.FirstOrDefault();

		if (output != null)
			return output;
		
		output = Create(model: new Enrollment() // Session is handled in platform-common for this one
		{
			AccountID = accountId,
			LeaderboardType = leaderboardType,
			Tier = 0
		});

		return output;
	}

	public void LinkArchive(IEnumerable<string> accountIds, string leaderboardType, string archiveId, string leaderboardId) => _collection.UpdateMany(
		filter: Builders<Enrollment>.Filter.And(
			Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, leaderboardType),
			Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds)
		),
		update: Builders<Enrollment>.Update
			.AddToSet(enrollment => enrollment.PastLeaderboardIDs, archiveId)
			.Unset(enrollment => enrollment.CurrentLeaderboardID)
	);


	public void SetActiveTier(Enrollment enrollment, int activeTier) => _collection
		.UpdateOne(
			filter: Builders<Enrollment>.Filter.Eq(db => db.Id, enrollment.Id),
			update: Builders<Enrollment>.Update.Set(db => db.ActiveTier, activeTier));

	// TD-16450: Seasonal rewards were not being sent when and only when a player was only scoring once
	// per rollover.  There was a collision in PATCH /score where this method would accurately update
	// the enrollment to be active for the season, but the endpoint had a later enrollment update that
	// would override the active flag.  However, on subsequent scoring events, the endpoint would exit early
	// and flag the player as active before the second enrollment update.
	public void FlagAsActive(Enrollment enrollment, Leaderboard shard) => _collection
		.FindOneAndUpdate(
			filter: Builders<Enrollment>.Filter.Eq(db => db.Id, enrollment.Id),
			update: Builders<Enrollment>.Update
				.Set(db => db.IsActive, true)
				.Set(db => db.IsActiveInSeason, shard.SeasonsEnabled)
				.Set(db => db.CurrentLeaderboardID, shard.Id)
				.Set(db => db.ActiveTier,
					enrollment.Status == Enrollment.PromotionStatus.Acknowledged && enrollment.ActiveTier != enrollment.Tier
						? enrollment.Tier
						: enrollment.ActiveTier
				),
			options: new FindOneAndUpdateOptions<Enrollment>
			{
				ReturnDocument = ReturnDocument.After
			}
		);
	
	public Enrollment[] FlagAsInactive(string[] accountIds, string leaderboardType) => SetActiveFlag(accountIds, leaderboardType, active: false);
	private Enrollment[] SetActiveFlag(string[] accountIds, string type, bool active = true)
	{
		FilterDefinition<Enrollment> filter = Builders<Enrollment>.Filter.And(
			Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds),
			Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, type)
		);

		long affected = _collection.UpdateMany(
			filter: filter,
			update: Builders<Enrollment>.Update.Set(enrollment => enrollment.IsActive, active)
		).ModifiedCount;
		
		return _collection
			.Find(filter)
			.ToList()
			.ToArray();
	}

	/// <summary>
	/// Find all enrollments that have a higher tier than their max seasonal tier, then update the max seasonal tier to match.
	/// Requires a really ugly Mongo query because the update is dependent on the record it's looking at - and there's no
	/// clean way to do this from C# as far as I could find.
	/// </summary>
	/// <returns>The affected number of records</returns>
	private long UpdateSeasonalMaxTiers()
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

	public long AcknowledgeRollover(string accountId, string type) => _collection.UpdateMany(
			filter: Builders<Enrollment>.Filter.And(
				Builders<Enrollment>.Filter.Eq(enrollment => enrollment.AccountID, accountId),
				Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, type)
			),
			update: Builders<Enrollment>.Update
				.Set(enrollment => enrollment.Status, Enrollment.PromotionStatus.Acknowledged)
				.Set(enrollment => enrollment.SeasonEnded, false)
		).ModifiedCount;

	private long AlterTier(string[] accountIds, string type, int? maxTier = null, int delta = 0)
	{
		if (delta == 0)
		{
			_collection.UpdateMany(
				filter: Builders<Enrollment>.Filter.And(
					Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds),
					Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, type)
				),
				update: Builders<Enrollment>.Update.Set(result => result.Status, Enrollment.PromotionStatus.Unchanged),
				options: new UpdateOptions
				{
					IsUpsert = false
				}
			);
			return 0; // throw exception?
		}

		UpdateResult result = _collection.UpdateMany(
			filter: Builders<Enrollment>.Filter.And(
				Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds),
				Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, type)
			),
			update: Builders<Enrollment>.Update.Inc(enrollment => enrollment.Tier, delta)
				.Set(result => result.Status, delta > 0
					? Enrollment.PromotionStatus.Promoted
					: Enrollment.PromotionStatus.Demoted
				),
			options: new UpdateOptions
			{
				IsUpsert = false
			}
		);

		// TODO: This is a kluge to fix negative enrollment tiers
		_collection.UpdateMany(filter: enrollment => true, Builders<Enrollment>.Update.Max(enrollment => enrollment.Tier, 0));
		_collection.UpdateMany(filter: enrollment => enrollment.LeaderboardType == type, Builders<Enrollment>.Update.Min(enrollment => enrollment.Tier, maxTier ?? int.MaxValue));

		UpdateSeasonalMaxTiers();

		return result.ModifiedCount;
	}

	public string[] GetSeasonalRewardCandidates(string type, int tier)
	{
		string[] output = _collection
			.Find(enrollment => enrollment.LeaderboardType == type && enrollment.SeasonalMaxTier == tier && enrollment.IsActiveInSeason)
			.Project(Builders<Enrollment>.Projection.Expression(enrollment => enrollment.AccountID))
			.ToList()
			.ToArray();


		
		if (!PlatformEnvironment.IsProd && type == "ldr_lmt_preseason_20230209")
		{
			Enrollment test = _collection
				.Find(enrollment => enrollment.AccountID == "6372be1259c472bca7e60149" && enrollment.LeaderboardType == type)
				.FirstOrDefault();
			int enrollmentCount = _collection
				.Find(enrollment => enrollment.LeaderboardType == type)
				.Project(Builders<Enrollment>.Projection.Expression(enrollment => enrollment.AccountID))
				.ToList()
				.Count;
			int tierCount = _collection
				.Find(enrollment => enrollment.LeaderboardType == type && enrollment.SeasonalMaxTier == tier && enrollment.IsActiveInSeason)
				.Project(Builders<Enrollment>.Projection.Expression(enrollment => enrollment.AccountID))
				.ToList()
				.Count;
			int activeCount = _collection
				.Find(enrollment => enrollment.LeaderboardType == type && enrollment.SeasonalMaxTier == tier && enrollment.IsActiveInSeason)
				.Project(Builders<Enrollment>.Projection.Expression(enrollment => enrollment.AccountID))
				.ToList()
				.Count;
			
			Log.Info(Owner.Will, "Season rollover info counts", data: new
			{
				Type = type,
				Tier = tier,
				EnrollmentsInType = enrollmentCount,
				PlayersInTier = tierCount,
				PlayersActiveInTier = activeCount,
				CandidatesResult = output,
				TestAccEnrollment = test
			});
		}


		return output;
	}

	public long ResetSeasonalMaxTier(string type)
	{
		long output = _collection
			.UpdateMany(
				filter: enrollment => enrollment.LeaderboardType == type,
				update: Builders<Enrollment>.Update
					.Set(enrollment => enrollment.SeasonalMaxTier, -1)
					.Set(enrollment => enrollment.SeasonEnded, true)
					.Set(enrollment => enrollment.IsActiveInSeason, false)
			).ModifiedCount; 
		Log.Info(Owner.Will, "Marking all players as inactive for a leaderboard", data: new
		{
			Affected = output,
			Type = type
		});
		return output;
	}

	public long PromotePlayers(string[] accountIds, Leaderboard caller) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: 1);
	public long DemotePlayers(string[] accountIds, Leaderboard caller, int levels = 1) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: levels * -1);
	public long DemoteInactivePlayers(string leaderboardType) => AlterTier(GetInactiveAccounts(leaderboardType), leaderboardType, delta: -1);

	public long FlagAsInactive(string leaderboardType) => _collection
		.UpdateMany(
			filter: enrollment => enrollment.LeaderboardType == leaderboardType,
			update: Builders<Enrollment>.Update.Set(enrollment => enrollment.IsActive, false)
		).ModifiedCount;

	public List<Enrollment> Find(string accountId, string typeString = null)
	{
		typeString ??= "";
		FilterDefinition<Enrollment> filter = Builders<Enrollment>.Filter.Eq(enrollment => enrollment.AccountID, accountId);

		string[] types = typeString
			.Split(',')
			.Select(str => str.Trim())
			.Where(str => !string.IsNullOrWhiteSpace(str))
			.ToArray();

		if (types.Any())
			filter = Builders<Enrollment>.Filter.And(filter, Builders<Enrollment>.Filter.In(enrollment => enrollment.LeaderboardType, types));

		return _collection
			.Find(filter)
			.ToList();
	}

	public long SeasonDemotion(string type, int tier, int newTier) => _collection
		.UpdateMany(
			filter: enrollment => enrollment.LeaderboardType == type && enrollment.Tier == tier && enrollment.Tier > newTier,
			update: Builders<Enrollment>.Update
				.Set(enrollment => enrollment.Tier, newTier)
				.Set(enrollment => enrollment.Status, Enrollment.PromotionStatus.Demoted)
				.Set(enrollment => enrollment.SeasonFinalTier, tier)
		).ModifiedCount;
}
