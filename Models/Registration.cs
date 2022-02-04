using System.Collections.Generic;
using System.Linq;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models
{
	public class Registration : PlatformCollectionDocument
	{
		[SimpleIndex("accountId", "Account ID")]
		public string AccountId { get; set; }
		public long LastScoreTimestamp { get; set; }
		public bool Disqualified { get; set; }
		
		public List<LeaderboardEnrollment> Enrollments { get; set; }
		public List<Reward> RewardsDue { get; set; }

		public Registration(string accountId)
		{
			AccountId = accountId;
			LastScoreTimestamp = 0;
			Disqualified = false;
			Enrollments = new List<LeaderboardEnrollment>();
			RewardsDue = new List<Reward>();
		}
		
		public bool TryEnroll(Leaderboard leaderboard)
		{
			LeaderboardEnrollment enrollment = Enrollments.FirstOrDefault(enrollment => enrollment.LeaderboardType == leaderboard.Type);

			if (enrollment == null)
			{
				Enrollments.Add(new LeaderboardEnrollment()
				{
					LeaderboardType = leaderboard.Type,
					Tier = leaderboard.Tier
				});
				return true;
			}

			if (enrollment.Tier != leaderboard.Tier)
			{
				enrollment.Tier = leaderboard.Tier;
				return true;
			}

			return false;
		}
	}
}