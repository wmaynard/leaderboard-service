using Rumble.Platform.Common.Exceptions;

namespace Rumble.Platform.LeaderboardService.Exceptions;
public class UnknownLeaderboardException : PlatformException
{
	public string LeaderboardType { get; init; }
	
	public UnknownLeaderboardException(string type) : base($"No leaderboard of type '{type}' exists.")
	{
		LeaderboardType = type;
	}
}