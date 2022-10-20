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
	private readonly RewardsService _rewardsService;
	public EnrollmentService(RewardsService rewardsService) : base("enrollments") => _rewardsService = rewardsService; 

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
		_rewardsService.Validate(accountId);

		return output;
	}

	public void LinkArchive(IEnumerable<string> accountIds, string leaderboardType, string archiveId) => _collection.UpdateMany(
		filter: Builders<Enrollment>.Filter.And(
			Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, leaderboardType),
			Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds)
		),
		update: Builders<Enrollment>.Update.AddToSet(enrollment => enrollment.PastLeaderboardIDs, archiveId)
	);

	public void FlagAsActive(string accountId, string leaderboardType) => SetActiveFlag(new[] { accountId }, leaderboardType);
	public Enrollment[] FlagAsActive(string[] accountIds, string leaderboardType) => SetActiveFlag(accountIds, leaderboardType);
	public Enrollment[] FlagAsInactive(string[] accountIds, string leaderboardType) => SetActiveFlag(accountIds, leaderboardType, active: false);
	private Enrollment[] SetActiveFlag(string[] accountIds, string type, bool active = true)
	{
		Expression<Func<Enrollment, bool>> filter = enrollment => accountIds.Contains(enrollment.AccountID) && enrollment.LeaderboardType == type;
		_collection.UpdateMany(
			filter: filter,
			update: Builders<Enrollment>.Update.Set(enrollment => enrollment.IsActive, active)
		);
		return _collection
			.Find(filter)
			.ToList()
			.ToArray();
	}

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

	public string[] GetSeasonalRewardCandidates(string type, int tier) => _collection
		.Find(enrollment => enrollment.LeaderboardType == type && enrollment.SeasonalMaxTier == tier && enrollment.IsActiveInSeason)
		.Project(Builders<Enrollment>.Projection.Expression(enrollment => enrollment.AccountID))
		.ToList()
		.ToArray();

	public long ResetSeasonalMaxTier(string type) => _collection
		.UpdateMany(
			filter: enrollment => enrollment.LeaderboardType == type,
			update: Builders<Enrollment>.Update
				.Set(enrollment => enrollment.SeasonalMaxTier, -1)
				.Set(enrollment => enrollment.SeasonEnded, true)
				.Set(enrollment => enrollment.IsActiveInSeason, false)
		).ModifiedCount;

	public long PromotePlayers(string[] accountIds, Leaderboard caller) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: 1);
	public long DemotePlayers(string[] accountIds, Leaderboard caller, int levels = 1) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: levels * -1);
	public void DemoteInactivePlayers(string leaderboardType) => AlterTier(GetInactiveAccounts(leaderboardType), leaderboardType, delta: -1);

	public long FlagAsInactive(string leaderboardType) => _collection
		.UpdateMany(
			filter: enrollment => enrollment.LeaderboardType == leaderboardType,
			update: Builders<Enrollment>.Update.Set(enrollment => enrollment.IsActive, false)
		).ModifiedCount;

	public List<Enrollment> Find(string accountId, string typeString)
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
}
