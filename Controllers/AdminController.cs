using Microsoft.AspNetCore.Mvc;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Controllers
{
	public class AdminController : PlatformController
	{
		public override ActionResult HealthCheck()
		{
			throw new System.NotImplementedException();
		}
	}
}