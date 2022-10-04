using System;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class RolloverService : QueueService<RolloverService.RolloverData>
{
    public const string CONFIG_HOURLY_SETTING = "leaderboard_HourlyResetMinute";	// 0-59
    public const string CONFIG_DAILY_SETTING = "leaderboard_DailyResetTimeUTC_24h";	// 00:00 - 23:59
    public const string CONFIG_WEEKLY_SETTING = "leaderboard_WeeklyResetDay";		// NOT 0-index
    public const string CONFIG_MONTHLY_SETTING = "leaderboard_MonthlyResetDay";		// NOT 0-index
    public const string LAST_HOURLY_SETTING = "lastHourlyRollover";
    public const string LAST_DAILY_SETTING = "lastDailyRollover";
    public const string LAST_WEEKLY_SETTING = "lastWeeklyRollover";
    public const string LAST_MONTHLY_SETTING = "lastMonthlyRollover";
    
    private readonly DynamicConfigService _config;
    private readonly LeaderboardService _leaderboard;
    
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
    
    public RolloverService(DynamicConfigService config, LeaderboardService leaderboard) 
        : base(collection: "rollover", primaryNodeTaskCount: 5, secondaryNodeTaskCount: 0)
    {
        _config = config;
        _leaderboard = leaderboard;
    }

    protected override void PrimaryNodeWork()
    {
        UpdateLocalConfig();
        DateTime now = DateTime.UtcNow;
		
        Log.Local(Owner.Will, $"LastDailyRollover: {LastDailyRollover} | {LastDailyRollover.Day}");
        // Check daily leaderboards
        if (LastDailyRollover.Day != now.Day && PastResetTime(now))
        {
            LastDailyRollover = now;
            // TODO: future improvement - roll over each leaderboard individually as opposed to by type
            CreateTask(new RolloverData
            {
                RolloverType = RolloverType.Daily
            });
        }
		
        // Check weekly leaderboards
        bool isRolloverDay = (int)now.DayOfWeek == WeeklyResetDay || true;
        if (isRolloverDay && now.Subtract(LastWeeklyRollover).TotalDays > 1 && PastResetTime(now))
        {
            LastWeeklyRollover = now;
            CreateTask(new RolloverData
            {
                RolloverType = RolloverType.Weekly
            });
        }

            // Check monthly leaderboards
        if (LastMonthlyRollover.Month != now.Month && LastMonthlyRollover.Day < now.Day && PastResetTime(now))
        {
            LastMonthlyRollover = now;
            CreateTask(new RolloverData
            {
                RolloverType = RolloverType.Monthly
            });
        }
    }

    protected override async void ProcessTask(RolloverData data)
    {
        _leaderboard.Rollover(data.RolloverType).Wait();
        
        int errors = 0;
        string message = null;
        bool success = false;
        DateTime now = DateTime.Now;
        RolloverType rolloverType = data.RolloverType;

        do
        {
            try
            {
                Log.Info(Owner.Will, $"{rolloverType} rollover triggered.", data: new
                {
                    ServiceID = Id
                });
                await _leaderboard.Rollover(rolloverType);

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
                .AddMessage($"The rollover was retried {errors} times, but could not be completed.  The rollover time was set to {now} to prevent rollover error log spam.")
                .DirectMessage(Owner.Will);
    }
    
    private bool PastResetTime(DateTime utc) => DailyResetTime.CompareTo(utc.TimeOfDay) <= 0;
    private void UpdateLocalConfig()
    {
        HourlyResetMinute = _config?.GameConfig?.Optional<int?>(CONFIG_HOURLY_SETTING) ?? 0;
        DailyResetTime = TimeSpan.Parse(_config?.GameConfig?.Optional<string>(CONFIG_DAILY_SETTING) ?? "02:00");
        WeeklyResetDay = _config?.GameConfig?.Optional<int?>(CONFIG_WEEKLY_SETTING) ?? 1;
        MonthlyResetDay = _config?.GameConfig?.Optional<int?>(CONFIG_MONTHLY_SETTING) ?? 1;
    }
    
    public class RolloverData : PlatformDataModel
    {
        [BsonElement("rolloverType")]
        public RolloverType RolloverType { get; set; }
    }
}

// TODO: Don't decrease season counter when nobody is in a leaderboard