using System.Linq;
using RCL.Logging;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class RewardsService : MinqTimerService<Reward>
{
	private const double TWELVE_HOURS_MS = 43_200_000;
	public RewardsService() : base("grants", interval: TWELVE_HOURS_MS) { }

	public long Grant(Reward reward, params string[] accountIds)
	{
		if (reward == null || !accountIds.Any())
			return 0;

		reward.AwardedOn = Timestamp.UnixTime;
		Reward[] toInsert = accountIds
			.Select(account =>
			{
				Reward due = reward.Copy();
				due.AccountId = account;
				return due;
			})
			.ToArray();
		mongo.Insert(toInsert);
		return toInsert.Length;
	}

	public Reward[] GetUnsentRewards(params string[] rewardIds) => mongo
		.Where(query => query
			.ContainedIn(reward => reward.Id, rewardIds)
			.EqualTo(reward => reward.SentStatus, Reward.Status.Tasked)
		)
		.ToArray();

	public long MarkAsSent(params string[] rewardIds) => mongo
		.Where(query => query.ContainedIn(reward => reward.Id, rewardIds))
		.Update(query => query.Set(reward => reward.SentStatus, Reward.Status.Sent));

	/// <summary>
	/// Searches the database for rewards that have yet to be tasked for sending.  This is different from UNSENT
	/// rewards in that 
	/// </summary>
	/// <param name="transaction"></param>
	/// <returns>An array of rewards that have not yet seen tasks created</returns>
	public Reward[] GetUntaskedRewards(out Transaction transaction)
	{
		Reward[] output = mongo
			.Where(query => query
				.EqualTo(reward => reward.SentStatus, Reward.Status.NotSent)
			)
			.Limit(5_000)
			.ToArray();

		mongo
			.WithTransaction(out transaction)
			.Where(query => query.ContainedIn(reward => reward.Id, output.Select(reward => reward.Id)))
			.Update(query => query.Set(reward => reward.SentStatus, Reward.Status.Tasked));

		return output;
	}

	protected override void OnElapsed() => mongo
		.Where(query => query
			.EqualTo(reward => reward.SentStatus, Reward.Status.Sent)
			.LessThanOrEqualTo(reward => reward.AwardedOn, Timestamp.SixMonthsAgo)
		)
		.OnRecordsAffected(result => Log.Info(Owner.Will, "Reward grants older than six months deleted", data: new
		{
			Help = "Only successfully sent rewards were affected",
			Affected = result.Affected
		}))
		.Delete();
}