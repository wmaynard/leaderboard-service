using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;

namespace leaderboard_service
{
	public class Startup : PlatformStartup
	{
		public void ConfigureServices(IServiceCollection services) => base.ConfigureServices(services, Owner.Will);
	}
}