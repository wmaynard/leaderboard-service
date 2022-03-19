using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Exceptions;

public class InvalidLeaderboardException : PlatformException
{
	public string LeaderboardId { get; init; }
	public string LeaderboardType { get; init; }
	public string Detail { get; init; }
#if DEBUG
	public Leaderboard Leaderboard { get; init; }
#endif

	public InvalidLeaderboardException(Leaderboard leaderboard, string detail) : base("Leaderboard is invalid.")
	{
		LeaderboardId = leaderboard.Id;
		LeaderboardType = leaderboard.Type;
		Detail = detail;
#if DEBUG
		Leaderboard = leaderboard;
#endif
	}
	
}