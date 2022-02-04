using System.Linq;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class SettingsService : PlatformMongoService<ServiceSettings>
	{
		public ServiceSettings Values
		{
			get => Find(settings => true).FirstOrDefault();
			set => Update(value);
		}
		
		public SettingsService() : base("settings") { }
	}
}