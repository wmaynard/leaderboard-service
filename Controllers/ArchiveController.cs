using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Services;

namespace Rumble.Platform.LeaderboardService.Controllers;

[ApiController, Route("leaderboard/archive"), RequireAuth]
public class ArchiveController : PlatformController
{
#pragma warning disable CS0649
	private readonly ArchiveService _archiveService;
	// private readonly ResetService _resetService;
#pragma warning restore CS0649
	
	public override ActionResult HealthCheck()
	{
		throw new System.NotImplementedException();
	}
}