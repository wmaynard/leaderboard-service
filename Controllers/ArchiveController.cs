using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[ApiController, Route("leaderboard/archive"), RequireAuth]
public class ArchiveController : PlatformController
{
#pragma warning disable
	private readonly ArchiveService _archiveService;
#pragma warning restore

	[HttpGet, Route("")]
	public ActionResult Check()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		int count = Optional<int?>("count") ?? 1;

		string accountId = Token.IsAdmin
			? Optional<string>(TokenInfo.FRIENDLY_KEY_ACCOUNT_ID)
			: Token.AccountId;

		if (count <= 0)
			throw new PlatformException("If specified, archive count must be greater than 0.");

		return Ok(new
		{
			Leaderboards = accountId == null
				? _archiveService.Lookup(type, count)
				: _archiveService.Lookup(type, accountId, count)
		});
	}
}