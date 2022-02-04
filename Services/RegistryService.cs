using System;
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
	}
}