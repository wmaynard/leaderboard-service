using System;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Web;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class ResetService : PlatformTimerService
	{
		public const string CONFIG_HOURLY_SETTING = "leaderboard_HourlyResetMinute";	// 0-59
		public const string CONFIG_DAILY_SETTING = "leaderboard_DailyResetTimeUTC_24h";	// 00:00 - 23:59
		public const string CONFIG_WEEKLY_SETTING = "leaderboard_WeeklyResetDay";		// NOT 0-index
		public const string CONFIG_MONTHLY_SETTING = "leaderboard_MonthlyResetDay";		// NOT 0-index
		
#pragma warning disable CS0649
		private DynamicConfigService _dynamicConfig;
#pragma warning restore CS0649
		
		private int HourlyResetMinute { get; set; }
		private TimeSpan DailyResetTime { get; set; }
		private int WeeklyResetDay { get; set; }
		private int MonthlyResetDay { get; set; }
		
		public ResetService() : base(intervalMS: 5_000, startImmediately: true)
		{
			
		}

		private void UpdateConfig()
		{
			HourlyResetMinute = _dynamicConfig.Values.Optional<int?>(CONFIG_HOURLY_SETTING) ?? 0;
			DailyResetTime = TimeSpan.Parse(_dynamicConfig.Values.Optional<string>(CONFIG_DAILY_SETTING) ?? "02:00");
			WeeklyResetDay = _dynamicConfig.Values.Optional<int?>(CONFIG_WEEKLY_SETTING) ?? 1;
			MonthlyResetDay = _dynamicConfig.Values.Optional<int?>(CONFIG_MONTHLY_SETTING) ?? 1;

		}

		protected override void OnElapsed()
		{
			UpdateConfig();
		}
	}
}