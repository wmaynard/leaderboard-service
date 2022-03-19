using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.LeaderboardService.Exceptions;

public class AccountDisqualifiedException : PlatformException
{
	public string AccountId { get; init; }
	public AccountDisqualifiedException(string accountId) : base("Account is disqualified from participating in leaderboards.")
	{
		AccountId = accountId;
	}
}