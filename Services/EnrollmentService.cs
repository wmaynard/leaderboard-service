using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;


public class EnrollmentService : MinqService<Enrollment>
{
	public EnrollmentService() : base("enrollments") { }

	public string[] GetInactiveAccounts(string leaderboardType, bool forDemotion = true) => mongo
		.Where(query =>
		{
			query
				.EqualTo(enrollment => enrollment.LeaderboardType, leaderboardType)
				.EqualTo(enrollment => enrollment.IsActive, false);
			if (forDemotion)
				query.GreaterThan(enrollment => enrollment.Tier, 0);
		})
		.Project(enrollment => enrollment.AccountID);

	public Enrollment FindOrCreate(string accountId, string leaderboardType) => mongo
		.Where(query => query
			.EqualTo(enrollment => enrollment.AccountID, accountId)
			.EqualTo(enrollment => enrollment.LeaderboardType, leaderboardType)
		)
		.Upsert(update => update.SetToCurrentTimestamp(enrollment => enrollment.UpdatedOn));

	public void LinkArchive(IEnumerable<string> accountIds, string leaderboardType, string archiveId, string leaderboardId) => mongo
		.Where(query => query
			.ContainedIn(enrollment => enrollment.AccountID, accountIds)
			.EqualTo(enrollment => enrollment.LeaderboardType, leaderboardType)
		)
		.Update(update => update
			.AddItems(enrollment => enrollment.PastLeaderboardIDs, 10, archiveId)
			.Set(enrollment => enrollment.CurrentLeaderboardID, null)
		);

	public void SetActiveTier(Enrollment enrollment, int activeTier) => mongo
		.Where(query => query.EqualTo(db => db.Id, enrollment.Id))
		.Limit(1)
		.Update(update => update.Set(db => db.ActiveTier, activeTier));

	// TD-16450: Seasonal rewards were not being sent when and only when a player was only scoring once
	// per rollover.  There was a collision in PATCH /score where this method would accurately update
	// the enrollment to be active for the season, but the endpoint had a later enrollment update that
	// would override the active flag.  However, on subsequent scoring events, the endpoint would exit early
	// and flag the player as active before the second enrollment update.
	public void FlagAsActive(Enrollment enrollment, Leaderboard shard) => mongo
		.Where(query => query.EqualTo(db => db.Id, enrollment.Id))
		.Limit(1)
		.Update(update => update
			.Set(db => db.IsActive, true)
			.Set(db => db.IsActiveInSeason, shard.SeasonsEnabled)
			.Set(db => db.CurrentLeaderboardID, shard.Id)
			.Set(db => db.ActiveTier, enrollment.Status == Enrollment.PromotionStatus.Acknowledged && enrollment.ActiveTier != enrollment.Tier
				? enrollment.Tier
				: enrollment.ActiveTier
			)
		);

	// TD-16450: This endpoint is a new admin tool to fix accounts incorrectly marked as inactive.
	public long SetCurrentlyActive(string[] accountIds, bool isActive) => mongo
		.Where(query => query.ContainedIn(enrollment => enrollment.AccountID, accountIds))
		.Update(update => update.Set(enrollment => enrollment.IsActive, isActive));
	
	public long SetActiveInSeason(string[] accountIds, bool isActive) => mongo
		.Where(query => query.ContainedIn(enrollment => enrollment.AccountID, accountIds))
		.Update(update => update.Set(enrollment => enrollment.IsActiveInSeason, isActive));

	public Enrollment[] SetActiveFlag(string[] accountIds, string type, bool active = true) => mongo
		.Where(query => query
			.ContainedIn(enrollment => enrollment.AccountID, accountIds)
			.EqualTo(enrollment => enrollment.LeaderboardType, type)
		)
		.UpdateAndReturn(update => update.Set(enrollment => enrollment.IsActive, active));

	// TODO: MINQ is missing the functionality to do this on its own; it will need to be added.
	private long UpdateSeasonalMaxTiers() => Require<EnrollmentService_Legacy>().UpdateSeasonalMaxTiers();

	public long AcknowledgeRollover(string accountId, string type) => mongo
		.Where(query => query
			.EqualTo(enrollment => enrollment.AccountID, accountId)
			.EqualTo(enrollment => enrollment.LeaderboardType, type)
		)
		.Update(update => update
			.Set(enrollment => enrollment.Status, Enrollment.PromotionStatus.Acknowledged)
			.Set(enrollment => enrollment.SeasonEnded, false)
		);

	private long AlterTier(string[] accountIds, string type, int? maxTier = null, int delta = 0)
	{
		long output = mongo
			.Where(query => query
				.ContainedIn(enrollment => enrollment.AccountID, accountIds)
				.EqualTo(enrollment => enrollment.LeaderboardType, type)
			)
			.Update(update =>
			{
				if (delta == 0)
					update.Set(enrollment => enrollment.Status, Enrollment.PromotionStatus.Unchanged);
				else
					update
						.Set(enrollment => enrollment.Tier, delta)
						.Set(enrollment => enrollment.Status, delta > 0
							? Enrollment.PromotionStatus.Promoted
							: Enrollment.PromotionStatus.Demoted
						);
			});

		// TODO: This is a kluge to fix negative enrollment tiers
		mongo
			.Where(query => query.LessThan(enrollment => enrollment.Tier, 0))
			.Update(update => update.Set(enrollment => enrollment.Tier, 0));
		mongo
			.Where(query => query.EqualTo(enrollment => enrollment.LeaderboardType, type))
			.Update(update => update.Minimum(enrollment => enrollment.Tier, maxTier ?? int.MaxValue));
		return output;
	}

	public string[] GetSeasonalRewardCandidates(string type, int tier) => mongo
		.Where(query => query
			.EqualTo(enrollment => enrollment.LeaderboardType, type)
			.EqualTo(enrollment => enrollment.SeasonalMaxTier, tier)
			.EqualTo(enrollment => enrollment.IsActiveInSeason, true)
		)
		.Project(enrollment => enrollment.AccountID);

	public long ResetSeasonalMaxTier(string type) => mongo
		.Where(query => query.EqualTo(enrollment => enrollment.LeaderboardType, type))
		.Update(update => update
			.Set(enrollment => enrollment.SeasonalMaxTier, -1)
			.Set(enrollment => enrollment.SeasonEnded, true)
			.Set(enrollment => enrollment.IsActiveInSeason, false)
		);
	
	public long PromotePlayers(string[] accountIds, Leaderboard caller) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: 1);
	public long DemotePlayers(string[] accountIds, Leaderboard caller, int levels = 1) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: levels * -1);
	public long DemoteInactivePlayers(string leaderboardType) => AlterTier(GetInactiveAccounts(leaderboardType), leaderboardType, delta: -1);

	public long FlagAsInactive(string type) => mongo
		.Where(query => query.EqualTo(enrollment => enrollment.LeaderboardType, type))
		.Update(update => update.Set(enrollment => enrollment.IsActive, false));

	public List<Enrollment> Find(string accountId, string typeString = null) => mongo
		.Where(query =>
		{
			query.EqualTo(enrollment => enrollment.AccountID, accountId);

			string[] types = (typeString ?? "")
				.Split(',')
				.Select(str => str.Trim())
				.Where(str => !string.IsNullOrWhiteSpace(str))
				.ToArray();

			if (types.Any())
				query.ContainedIn(enrollment => enrollment.LeaderboardType, types);
		})
		.ToList();

	public long SeasonDemotion(string type, int tier, int newTier) => mongo
		.Where(query => query
			.EqualTo(enrollment => enrollment.LeaderboardType, type)
			.EqualTo(enrollment => enrollment.Tier, tier)
			.GreaterThan(enrollment => enrollment.Tier, newTier)
		)
		.Update(update => update
			.Set(enrollment => enrollment.Tier, newTier)
			.Set(enrollment => enrollment.Status, Enrollment.PromotionStatus.Demoted)
			.Set(enrollment => enrollment.SeasonFinalTier, tier)
		);
}