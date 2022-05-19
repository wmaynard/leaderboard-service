using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

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
#pragma warning disable CS4014
		Do(UpdateLocalConfig);
#pragma warning restore CS4014
	}

	private void UpdateLocalConfig()
	{
		HourlyResetMinute = _dynamicConfig?.GameConfig?.Optional<int?>(CONFIG_HOURLY_SETTING) ?? 0;
		DailyResetTime = TimeSpan.Parse(_dynamicConfig?.GameConfig.Optional<string>(CONFIG_DAILY_SETTING) ?? "02:00");
		WeeklyResetDay = _dynamicConfig?.GameConfig?.Optional<int?>(CONFIG_WEEKLY_SETTING) ?? 1;
		MonthlyResetDay = _dynamicConfig?.GameConfig?.Optional<int?>(CONFIG_MONTHLY_SETTING) ?? 1;
	}

	protected override async void Work()
	{
		#if DEBUG
		return;
		#endif
		
		UpdateLocalConfig();
		DateTime now = DateTime.UtcNow;
		
		
		// TODO: Add leaderboards starting in future
		// TODO: ArchiveController
		// TODO: Archive ID list

		bool success = true;
		
		// Check daily leaderboards
		if (LastDailyRollover.Day != now.Day && PastResetTime(now))
			success &= await Reset(RolloverType.Daily, now);
		
		// Check weekly leaderboards
		bool isRolloverDay = (int)now.DayOfWeek == WeeklyResetDay || true;
		if (isRolloverDay && now.Subtract(LastWeeklyRollover).TotalDays > 1 && PastResetTime(now))
			success &= await Reset(RolloverType.Weekly, now);

		// Check monthly leaderboards
		if (LastMonthlyRollover.Month != now.Month && LastMonthlyRollover.Day < now.Day && PastResetTime(now))
			success &= await Reset(RolloverType.Monthly, now);

		if (!success)
			throw new PlatformException("Could not roll over at least one of the leaderboard types.  Check logs for more information.");
	}

	private bool PastResetTime(DateTime utc) => DailyResetTime.CompareTo(utc.TimeOfDay) <= 0;

	private async Task<bool> Reset(RolloverType rolloverType, DateTime start)
	{
		int errors = 0;
		string message = null;
		bool success = false;

		do
		{
			try
			{
				Log.Info(Owner.Will, $"{rolloverType} rollover triggered.");
				_leaderboardService.Rollover(rolloverType);
				
				success = true;
			}
			catch (InvalidLeaderboardException e)
			{
				Log.Error(Owner.Will, $"Unable to rollover {rolloverType} leaderboards.", exception: e);
				Log.Local(Owner.Will, e.Detail);
				message = e.Detail;
			}
			catch (AggregateException e)
			{
				Log.Error(Owner.Will, $"Unable to rollover {rolloverType} leaderboards.", exception: e);
				if (e.InnerException is InvalidLeaderboardException invalid)
				{
					Log.Local(Owner.Will, invalid.Detail);
					message = invalid.Detail;
				}
				else
					message = e.Message;
			}
		} while (!success && ++errors < 10);

		if (!success)
			await SlackDiagnostics
				.Log(title: $"Leaderboard rollover failed! ({rolloverType.ToString()})", message: message)
				.AddMessage($"The rollover was retried {errors} times, but could not be completed.  The rollover time was set to {start} to prevent rollover error log spam.")
				.DirectMessage(Owner.Will);
		
		switch (rolloverType)
		{
			case RolloverType.Hourly:
				break;
			case RolloverType.Daily:
				LastDailyRollover = start;
				break;
			case RolloverType.Weekly:
				LastWeeklyRollover = start;
				break;
			case RolloverType.Monthly:
				LastMonthlyRollover = start;
				break;
			case RolloverType.Annually:
				break;
			
			case RolloverType.None:
			default:
				break;
		}

		return success;
	}
}