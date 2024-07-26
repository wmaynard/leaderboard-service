using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Utilities.JsonTools;

namespace Rumble.Platform.LeaderboardService.Models;

public class MailboxMessage : PlatformDataModel
{
	public RumbleJson Payload { get; init; }
	[JsonIgnore]
	public string[] RewardIds { get; set; } // Used to mark rewards as sent after a successful mail query

	public MailboxMessage(string accountId, params Reward[] messages)
	{
		Payload = new RumbleJson
		{
			{ "accountIds", new [] { accountId } },
			{ "messages", messages }
		};
		RewardIds = messages
			.Select(message => message.Id)
			.ToArray();
	}
}