using System;
using System.Linq;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models;

public class Ranking : PlatformDataModel
{
	[JsonIgnore]
	public int NumberOfAccounts => Accounts.Length;
	public string[] Accounts { get; init; }
	public int Rank { get; init; }
	public long Score { get; init; }

	public Ranking(int rank, IGrouping<long, Entry> group)
	{
		Rank = rank;
		Score = group.Key;
		Accounts = group.Select(entry => entry.AccountID).ToArray();
	}

	public bool HasAccount(string accountId) => Accounts.Contains(accountId);

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool IsRequestingPlayer { get; set; }
	
	[JsonIgnore]
	[BsonIgnore]
	public Reward Prize { get; set; }

	public override string ToString() => $"{Rank} | {Score} points | {string.Join(", ", Accounts)}{(IsRequestingPlayer ? " (YOU)" : "")}";
}