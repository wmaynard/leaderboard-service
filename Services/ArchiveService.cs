using System.Collections.Generic;
using MongoDB.Driver;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Extensions;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class ArchiveService : PlatformMongoService<Leaderboard>
{
	public ArchiveService() : base("archives") { }

	public void Stash(Leaderboard leaderboard, out Leaderboard archive)
	{
		leaderboard.EndTime = Timestamp.UnixTimeMS;
		Leaderboard copy = leaderboard.Copy();
		copy.ChangeId();
		Update(copy, createIfNotFound: true);
		archive = copy;
	}

	public List<Leaderboard> Lookup(string type, int count = 1) => _collection
			.Find(leaderboard => leaderboard.Type == type)
			.SortByDescending(leaderboard => leaderboard.EndTime)
			.Limit(count)
			.ToList();

	public List<Leaderboard> Lookup(string type, string accountId, int count = 1) => _collection
		.Find(
			filter: Builders<Leaderboard>.Filter.And(
				Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Type, type),
				Builders<Leaderboard>.Filter.ElemMatch(
					field: leaderboard => leaderboard.Scores,
					filter: entry => entry.AccountID == accountId
				)
			)
		).SortByDescending(leaderboard => leaderboard.EndTime)
		.Limit(count)
		.ToList();

	public void DeleteOldArchives(int days)
	{
		long seconds = 60 * 60 * 24 * days;
		long affected = _collection
			.DeleteMany(archive => archive.EndTime < Timestamp.UnixTime - seconds)
			.DeletedCount;
		
		if (affected > 0)
			Log.Local(Owner.Default, $"Deleted {affected} archives older than {days} days.");
	}
	
	public Leaderboard FindById(string id) => _collection
		.Find(filter: Builders<Leaderboard>.Filter.Eq(leaderboard => leaderboard.Id, id))
		.FirstOrDefault()
		?? throw new PlatformException("No archive found");
}