using Microsoft.Extensions.DependencyInjection;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService
{
	public class Startup : PlatformStartup
	{
		public void ConfigureServices(IServiceCollection services) => base.ConfigureServices(services, Owner.Will, 100, 100, 100);
	}
}