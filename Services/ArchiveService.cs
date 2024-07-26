using System.Collections.Generic;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Minq;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class ArchiveService : MinqService<Leaderboard>
{
	public ArchiveService() : base("archives") { }

	public void Stash(Leaderboard leaderboard, out Leaderboard archive)
	{
		archive = leaderboard.Copy();
		archive.ChangeId();
		mongo.Insert(archive);
		
		// 2024.07.26 Will: Committing profanity to a codebase as a placeholder error during dev...
		// Whoops!  Now that Rumble is no longer around, though, I'm going to leave it here for posterity
		// rather than trying to cover it up with a rebase.
		// And hey!  This log never showed up!  I definitely would have been called out for it if it happened live!
		if (string.IsNullOrWhiteSpace(archive.Id))
			Log.Error(Owner.Will, "Shit");  
	}

	public List<Leaderboard> Lookup(string type, int count = 1) => mongo
		.Where(query => query.EqualTo(leaderboard => leaderboard.Type, type))
		.Sort(sort => sort.OrderByDescending(leaderboard => leaderboard.EndTime))
		.Limit(count)
		.ToList();
	
	public List<Leaderboard> Lookup(string type, string accountId, int count = 1) => mongo
		.Where(query => query
			.EqualTo(leaderboard => leaderboard.Type, type)
			.Where(leaderboard => leaderboard.Scores, subquery => subquery.EqualTo(entry => entry.AccountID, accountId))
		)
		.Sort(sort => sort.OrderByDescending(leaderboard => leaderboard.EndTime))
		.Limit(count)
		.ToList();

	public void DeleteOldArchives(int days) => mongo
		.Where(query => query.LessThan(leaderboard => leaderboard.EndTime, Timestamp.InThePast(days: days)))
		.Delete();

	public Leaderboard FindById(string id) => mongo
		.ExactId(id)
		.First();
}