using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services
{
	public class ResetService : MasterService
	{
		public const string CONFIG_HOURLY_SETTING = "HourlyResetMinute";	// 0-59
		public const string CONFIG_DAILY_SETTING = "DailyResetTimeUTC_24h";	// 00:00 - 23:59
		public const string CONFIG_WEEKLY_SETTING = "WeeklyResetDay";		// NOT 0-index
		public const string CONFIG_MONTHLY_SETTING = "MonthlyResetDay";		// NOT 0-index
		public const string LAST_HOURLY_SETTING = "lastHourlyRollover";
		public const string LAST_DAILY_SETTING = "lastDailyRollover";
		public const string LAST_WEEKLY_SETTING = "lastWeeklyRollover";
		public const string LAST_MONTHLY_SETTING = "lastMonthlyRollover";
		
		
#pragma warning disable CS0649
		private DynamicConfigService _dynamicConfig;
		private LeaderboardService _leaderboardService;
#pragma warning restore CS0649
		
		private int HourlyResetMinute { get; set; }
		private TimeSpan DailyResetTime { get; set; }
		private int WeeklyResetDay { get; set; }
		private int MonthlyResetDay { get; set; }

		private DateTime LastHourlyRollover
		{
			get => Get<DateTime>(LAST_HOURLY_SETTING);
			set => Set(LAST_HOURLY_SETTING, value);
		}

		private DateTime LastDailyRollover
		{
			get => Get<DateTime>(LAST_DAILY_SETTING);
			set => Set(LAST_DAILY_SETTING, value);
		}

		private DateTime LastWeeklyRollover
		{
			get => Get<DateTime>(LAST_WEEKLY_SETTING);
			set => Set(LAST_WEEKLY_SETTING, value);
		}
		private DateTime LastMonthlyRollover
		{
			get => Get<DateTime>(LAST_MONTHLY_SETTING);
			set => Set(LAST_MONTHLY_SETTING, value);
		}
		
		public ResetService(ConfigService configService) : base(configService)
		{
			Do(UpdateConfig);
		}

		private void UpdateConfig()
		{
			HourlyResetMinute = _dynamicConfig?.GameConfig?.Optional<int?>(CONFIG_HOURLY_SETTING) ?? 0;
			DailyResetTime = TimeSpan.Parse(_dynamicConfig?.GameConfig.Optional<string>(CONFIG_DAILY_SETTING) ?? "02:00");
			WeeklyResetDay = _dynamicConfig?.GameConfig?.Optional<int?>(CONFIG_WEEKLY_SETTING) ?? 1;
			MonthlyResetDay = _dynamicConfig?.GameConfig?.Optional<int?>(CONFIG_MONTHLY_SETTING) ?? 1;
		}

		protected override async void Work()
		{
			if (!await Do(UpdateConfig))
				return;
			
			bool workPerformed = false;
			DateTime now = DateTime.UtcNow;
			
			// if (true)
			// Check daily leaderboards
			if (LastDailyRollover.Day != now.Day && PastResetTime(now))
				workPerformed = await Do(() =>
				{
					Log.Local(Owner.Will, "Roll over daily leaderboards!");
					_leaderboardService.Rollover(RolloverType.Daily);
					LastDailyRollover = now;
				});
			// Pause();
			
			// Check weekly leaderboards
			if (now.Subtract(LastWeeklyRollover).TotalDays > 7 && PastResetTime(now))
				workPerformed = await Do(() =>
				{
					Log.Local(Owner.Will, "Roll over weekly leaderboards!");
					_leaderboardService.Rollover(RolloverType.Weekly);
					LastWeeklyRollover = now;
				});
			
			// Check monthly leaderboards
			if (LastMonthlyRollover.Month != now.Month && LastMonthlyRollover.Day < now.Day && PastResetTime(now))
				workPerformed = await Do(() =>
				{
					Log.Local(Owner.Will, "Roll over monthly leaderboards!");
					_leaderboardService.Rollover(RolloverType.Monthly);
					LastMonthlyRollover = now;
				});
		}

		private bool PastResetTime(DateTime utc) => DailyResetTime.CompareTo(utc.TimeOfDay) <= 0;
	}
}