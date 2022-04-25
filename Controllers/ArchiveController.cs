using System.Linq;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Exceptions;
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

	[Route("")]
	public ActionResult Check()
	{
		string type = Require<string>(Leaderboard.FRIENDLY_KEY_TYPE);
		int count = Optional<int?>("count") ?? 1;

		if (count <= 0)
			throw new PlatformException("If specified, archive count must be greater than 0.");

		return Ok(new
		{
			Leaderboards = _archiveService.Lookup(type, Token.AccountId, count)
		});
	}
	
	public override ActionResult HealthCheck()
	{
		throw new System.NotImplementedException();
	}
}