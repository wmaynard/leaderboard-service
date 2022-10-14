using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Exceptions;
public class UnknownLeaderboardException : PlatformException
{
	public string LeaderboardType { get; init; }
	public int Tier { get; init; }
	
	public UnknownLeaderboardException(Enrollment enrollment) : base($"A leaderboard or specific tier of leaderboard could not be found.")
	{
		LeaderboardType = enrollment.LeaderboardType;
		Tier = enrollment.Tier;
	}
}