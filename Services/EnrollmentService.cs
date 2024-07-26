using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Bson;
using MongoDB.Driver;
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

	public long CountActivePlayers(string leaderboardType) => mongo
		.Where(query => query
			.EqualTo(enrollment => enrollment.LeaderboardType, leaderboardType)
			.EqualTo(enrollment => enrollment.IsActive, true)
		)
		.Count();

	public Enrollment[] FindMultiple(string accountId, string[] types, string guildId = null)
	{
		Enrollment[] existing = mongo
			.Where(query => query
				.EqualTo(enrollment => enrollment.AccountID, accountId)
				.ContainedIn(enrollment => enrollment.LeaderboardType, types)
			)
			.Limit(500)
			.UpdateAndReturn(update =>
			{
				update
					.SetToCurrentTimestamp(enrollment => enrollment.UpdatedOn)
					.SetOnInsert(enrollment => enrollment.Tier, 0);
				
				if (!string.IsNullOrWhiteSpace(guildId))
					update.Set(enrollment => enrollment.GuildId, guildId);
			});

		string[] missing = types
			.Except(existing.Select(enrollment => enrollment.LeaderboardType))
			.ToArray();

		if (!missing.Any())
			return existing;

		Enrollment[] toAdd = missing.Select(type => new Enrollment
		{
			AccountID = accountId,
			LeaderboardType = type,
			Tier = 0,
			GuildId = guildId
		}).ToArray();
		
		mongo.Insert(toAdd);

		return existing.Union(toAdd).ToArray();
	}

	public Enrollment FindOrCreate(string accountId, string leaderboardType, string guildId = null) => mongo
		.Where(query => query
			.EqualTo(enrollment => enrollment.AccountID, accountId)
			.EqualTo(enrollment => enrollment.LeaderboardType, leaderboardType)
		)
		.Upsert(update =>
		{
			update
				.SetToCurrentTimestamp(enrollment => enrollment.UpdatedOn)
				.SetOnInsert(enrollment => enrollment.Tier, 0);
				
			if (!string.IsNullOrWhiteSpace(guildId))
				update.Set(enrollment => enrollment.GuildId, guildId);
		});

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

	public void FlagAsActive(Enrollment enrollment, Leaderboard shard) => mongo
		.Where(query => query.EqualTo(db => db.Id, enrollment.Id))
		.Limit(1)
		.Update(update => update
			.Set(db => db.IsActive, true)
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

	public Enrollment[] SetActiveFlag(string[] accountIds, string type, bool active = true) => mongo
		.Where(query => query
			.ContainedIn(enrollment => enrollment.AccountID, accountIds)
			.EqualTo(enrollment => enrollment.LeaderboardType, type)
		)
		.UpdateAndReturn(update => update.Set(enrollment => enrollment.IsActive, active));

	public long AcknowledgeRollover(string accountId, string type) => mongo
		.Where(query => query
			.EqualTo(enrollment => enrollment.AccountID, accountId)
			.EqualTo(enrollment => enrollment.LeaderboardType, type)
		)
		.Update(update => update.Set(enrollment => enrollment.Status, Enrollment.PromotionStatus.Acknowledged));

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
}