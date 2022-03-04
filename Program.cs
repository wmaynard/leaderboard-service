using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rumble.Platform.LeaderboardService
{
	public class Program
	{
		public static void Main(string[] args)
		{
			if (args.Contains("-version"))
			{
				AssemblyName assembly = Assembly.GetExecutingAssembly().GetName();
				Console.WriteLine($"{assembly.Name}:{assembly.Version}");
				return;
			}
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder => { webBuilder.UseStartup<Startup>(); });
	}
}