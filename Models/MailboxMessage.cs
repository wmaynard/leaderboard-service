using System.Collections.Generic;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Models;

public class MailboxMessage : PlatformDataModel
{
	public RumbleJson Payload { get; init; }

	public MailboxMessage(string accountId, IEnumerable<Reward> messages) =>
		Payload = new RumbleJson
		{
			{ "accountIds", new string[] { accountId } },
			{ "messages", messages }
		};
}