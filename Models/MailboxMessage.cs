using System.Collections.Generic;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Models;

public class MailboxMessage : PlatformDataModel
{
	public GenericData Payload { get; init; }

	public MailboxMessage(string accountId, IEnumerable<Reward> messages) =>
		Payload = new GenericData()
		{
			{ "accountIds", new string[] { accountId } },
			{ "messages", messages }
		};
}