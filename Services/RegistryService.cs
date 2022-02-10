using System;
using System.Linq;
using MongoDB.Driver;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class RegistryService : PlatformMongoService<Registration>
	{
		public RegistryService() : base("registry") { }

		public Registration Find(string accountId)
		{
			Registration output = _collection
				.Find(registration => registration.AccountId == accountId)
				.FirstOrDefault();
			if (output == null)
				return Create(new Registration(accountId));
			return output.Disqualified
				? throw new AccountDisqualifiedException(output.AccountId)
				: output;
		}

		public Registration[] FlagAsInactive(string[] accountIds, string leaderboardType) => ChangeActiveFlag(accountIds, leaderboardType, isActive: false);

		public Registration[] FlagAsActive(string[] accountIds, string leaderboardType) => ChangeActiveFlag(accountIds, leaderboardType, isActive: true);

		private Registration[] ChangeActiveFlag(string[] accountIds, string leaderboardType, bool isActive = true)
		{
			throw new NotImplementedException();
		}

		public Registration IncreaseTier(string accountId, string leaderboardType, int amount = 1)
		{
			throw new NotImplementedException();
		}

		public Registration ReduceTier(string accountId, string leaderboardType, int amount = 1) => IncreaseTier(accountId, leaderboardType, amount * -1);

		public Registration[] DemoteInactivePlayers(string[] accountIds, string leaderboardType)
		{
			throw new NotImplementedException();
		}

		// Kill leaderboard endpoint
		// Plan for guilds; shard out leaderboards that only allow certain guild IDs
		// Ignore the promotion on season reset?  Tentative 
		// GenericData for mailbox link
	}
}