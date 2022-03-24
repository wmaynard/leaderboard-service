using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Driver;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class EnrollmentService : PlatformMongoService<Enrollment>
{
	public EnrollmentService() : base("enrollments") { }

	private string[] GetInactiveAccounts(string leaderboardType)
	{
		try
		{
			return _collection
				.Find(enrollment => enrollment.LeaderboardType == leaderboardType && !enrollment.IsActive)
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
	
	public Enrollment FindOrCreate(string accountId, string leaderboardType) => _collection
		.Find(filter: enrollment => enrollment.AccountID == accountId && enrollment.LeaderboardType == leaderboardType)
		.FirstOrDefault()
		?? Create(model: new Enrollment() // Session is handled in platform-common for this one
		{
			AccountID = accountId,
			LeaderboardType = leaderboardType,
			Tier = 0
		});

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

	private long AlterTier(string[] accountIds, string type, int? maxTier = null, int delta = 0)
	{
		if (delta == 0)
			return 0; // throw exception?

		UpdateResult result = _collection.UpdateMany(
			filter: Builders<Enrollment>.Filter.And(
				Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds),
				Builders<Enrollment>.Filter.Eq(enrollment => enrollment.LeaderboardType, type)
			),
			update: Builders<Enrollment>.Update.Inc(enrollment => enrollment.Tier, delta)
			,
			options: new UpdateOptions()
			{
				IsUpsert = false
			}
		);
		// TODO: This is a kluge to fix negative enrollment tiers
		_collection.UpdateMany(filter: enrollment => true, Builders<Enrollment>.Update.Max(enrollment => enrollment.Tier, 1));
		_collection.UpdateMany(filter: enrollment => true, Builders<Enrollment>.Update.Min(enrollment => enrollment.Tier, maxTier ?? int.MaxValue));

		// TODO: Update the SeasonalMaxTier
		// This seems impossible to do with regular update definitions since Mongo requires a primitive value to be used.  An SO user achieved this with db.RunCommand().
		
		// From: https://stackoverflow.com/questions/3974985/update-mongodb-field-using-value-of-another-field
		// 		var command = new BsonDocument
		// 		{
		// 			{ "update", "CollectionToUpdate" },
		// 			{ "updates", new BsonArray 
		// 				{ 
		// 					new BsonDocument
		// 					{
		// 						// Any filter; here the check is if Prop1 does not exist
		// 						{ "q", new BsonDocument{ ["Prop1"] = new BsonDocument("$exists", false) }}, 
		// 						// set it to the value of Prop2
		// 						{ "u", new BsonArray { new BsonDocument { ["$set"] = new BsonDocument("Prop1", "$Prop2") }}},
		// 						{ "multi", true }
		// 					}
		// 				}
		// 			}
		// 		};
		//
		// 	database.RunCommand<BsonDocument>(command);
					// _collection.UpdateMany(
					// 	filter: Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds),
					// 	update: Builders<Enrollment>.Update.Max(enrollment => enrollment.SeasonalMaxTier, )
					// );

		return result.ModifiedCount;
	}

	public long PromotePlayers(string[] accountIds, Leaderboard caller) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: 1);
	public long DemotePlayers(string[] accountIds, Leaderboard caller, int levels = 1) => AlterTier(accountIds, caller.Type, caller.MaxTier, delta: levels * -1);
	public void DemoteInactivePlayers(string leaderboardType) => AlterTier(GetInactiveAccounts(leaderboardType), leaderboardType);
}
