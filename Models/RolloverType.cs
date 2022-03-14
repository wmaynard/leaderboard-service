using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Rumble.Platform.LeaderboardService.Models
{
	public enum RolloverType
	{
		[Display(Name = "hourly")] Hourly, 
		[Display(Name = "daily")] Daily, 
		[Display(Name = "weekly")] Weekly, 
		[Display(Name = "monthly")] Monthly, 
		[Display(Name = "annually")] Annually, 
		[Display(Name = "none")] None
	}
}