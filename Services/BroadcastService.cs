using System;
using RCL.Logging;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Data;

namespace Rumble.Platform.LeaderboardService.Services;

public class BroadcastService : PlatformService
{
#pragma warning disable
	private readonly ApiService _apiService;
	private readonly DC2Service _dc2Service;
#pragma warning disable
	
	public void Announce(string accountId, string message)
	{
		try
		{
			string adminToken = _dc2Service.AdminToken;

			_apiService
				.Request(PlatformEnvironment.Url("/chat/messages/broadcast"))
				.AddAuthorization(adminToken)
				.SetPayload(new RumbleJson
				{
					{ "aid", accountId },
					{ "lastRead", 0 },
					{
						"message", new RumbleJson
						{
							{ "text", message }
						}
					}
				}).OnFailure((sender, response) =>
				{
					Log.Error(Owner.Will, "Unable to broadcast chat message for a leaderboard rollover.", data: new
					{
						AccountId = accountId,
						Message = message
					});
				}).Post(out RumbleJson response, out int code);
		}
		catch (Exception e)
		{
			Log.Error(Owner.Will, "Something went wrong in sending the chat broadcast for leaderboard rollover.", exception: e);
		}
		
	}
}