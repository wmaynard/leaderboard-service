using System;
using System.Linq;
using System.Linq.Expressions;
using MongoDB.Driver;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class EnrollmentService : PlatformMongoService<Enrollment>
	{
		public EnrollmentService() : base("enrollments")
		{
			
		}

		// public string[] InactiveAccounts(string leaderboardType) => _collection
		// 	.Find(filter: enrollment => enrollment.LeaderboardType == leaderboardType && !enrollment.IsActive)
		// 	.Project<string>(Builders<Enrollment>.Projection.Include(enrollment => enrollment.AccountID))
		// 	.ToList()
		// 	.ToArray();

		public void DemoteInactiveAccounts(string leaderboardType) => _collection
			.UpdateMany(
				filter: enrollment => enrollment.LeaderboardType == leaderboardType && !enrollment.IsActive,
				update: Builders<Enrollment>.Update.Inc(enrollment => enrollment.Tier, -1),
				options: new UpdateOptions()
				{
					IsUpsert = false
				}
			);

		public Enrollment FindOrCreate(string accountId, string leaderboardType) => _collection
			.Find(filter: enrollment => enrollment.AccountID == accountId && enrollment.LeaderboardType == leaderboardType)
			.FirstOrDefault()
			?? Create(model: new Enrollment()
			{
				AccountID = accountId,
				LeaderboardType = leaderboardType,
				Tier = 1
			});

		public string[] FindActiveAccounts(string leaderboardType) => _collection
			.Find(enrollment => enrollment.LeaderboardType == leaderboardType && enrollment.IsActive)
			.Project<string>(Builders<Enrollment>.Projection.Include(enrollment => enrollment.AccountID))
			.ToList()
			.ToArray();
		
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

		private long AlterTier(string[] accountIds, string type, int delta = 0)
		{
			if (delta == 0)
				return 0; // throw exception?

			UpdateResult result = _collection.UpdateMany(
				filter: Builders<Enrollment>.Filter.In(enrollment => enrollment.AccountID, accountIds),
				update: Builders<Enrollment>.Update.Inc(enrollment => enrollment.Tier, delta),
				options: new UpdateOptions()
				{
					IsUpsert = false
				}
			);

			return result.ModifiedCount;
		}

		public long PromotePlayers(string[] accountIds, string leaderboardType) => AlterTier(accountIds, leaderboardType, delta: 1);
		public long DemotePlayers(string[] accountIds, string leaderboardType, int levels = 1) => AlterTier(accountIds, leaderboardType, levels * -1);
	}
}