using System;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using RCL.Logging;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;
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
    private readonly EnrollmentService _enrollment;
    private readonly LeaderboardService _leaderboard;
    private readonly RewardsService _rewardService;
    
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
    
    public RolloverService(DynamicConfigService config, EnrollmentService enrollment, LeaderboardService leaderboard, RewardsService rewards) 
        : base(collection: "rollover", primaryNodeTaskCount: 5, secondaryNodeTaskCount: 0)
    {
        _config = config;
        _enrollment = enrollment;
        _leaderboard = leaderboard;
        _rewardService = rewards;
    }

    private void CreateRolloverTasks(RolloverType rolloverType)
    {
        // _leaderboard.BeginRollover(rolloverType, out string[] ids, out string[] types);
        _leaderboard.BeginRollover(rolloverType, out RumbleJson[] data);

        foreach (string type in data.Select(json => json.Require<string>(Leaderboard.DB_KEY_TYPE)))
        {
            _enrollment.DemoteInactivePlayers(type);
            _enrollment.FlagAsInactive(type);
            _leaderboard.DecreaseSeasonCounter(type);
        }

        foreach (RumbleJson json in data)
            CreateTask(new RolloverData
            {
                LeaderboardId = json.Require<string>(Leaderboard.DB_KEY_ID),
                LeaderboardType = json.Require<string>(Leaderboard.DB_KEY_TYPE)
            });
    }

    public void ManualRollover()
    {
        foreach (RolloverType type in Enum.GetValues(typeof(RolloverType)))
            CreateRolloverTasks(type);
    }

    protected override void OnTasksCompleted(RolloverData[] data)
    {
        string[] types = data
            .Select(rolloverData => rolloverData.LeaderboardType)
            .Distinct()
            .ToArray();
        
        _leaderboard.RolloverSeasons(data
            .Select(rolloverData => rolloverData.LeaderboardType)
            .Distinct()
            .ToArray()
        );
        Log.Local(Owner.Will, "I'm done with my tasks!", emphasis: Log.LogType.ERROR);
    }

    protected override void PrimaryNodeWork()
    {
        UpdateLocalConfig();
        DateTime now = DateTime.UtcNow;
		
        // Log.Local(Owner.Will, $"LastDailyRollover: {LastDailyRollover} | {LastDailyRollover.Day}");
        // Check daily leaderboards
        if (LastDailyRollover.Day != now.Day && PastResetTime(now))
        {
            LastDailyRollover = now;
            CreateRolloverTasks(RolloverType.Daily);
        }
		
        // Check weekly leaderboards
        bool isRolloverDay = (int)now.DayOfWeek == WeeklyResetDay || true;
        if (isRolloverDay && now.Subtract(LastWeeklyRollover).TotalDays > 1 && PastResetTime(now))
        {
            LastWeeklyRollover = now;
            CreateRolloverTasks(RolloverType.Weekly);
        }

        // Check monthly leaderboards
        if (LastMonthlyRollover.Month != now.Month && LastMonthlyRollover.Day < now.Day && PastResetTime(now))
        {
            LastMonthlyRollover = now;
            CreateRolloverTasks(RolloverType.Monthly);
        }
        
        _rewardService.SendRewards(); // TODO: Create tasks for this
    }

    protected override async void ProcessTask(RolloverData data)
    {
        int errors = 0;
        string message = null;
        bool success = false;

        do
        {
            try
            {
                await _leaderboard.Rollover(data.LeaderboardId);
                Log.Info(Owner.Will, $"Rollover triggered.", data: new
                {
                    ServiceID = Id,
                    LeaderboardId = data.LeaderboardId
                });
                success = true;
            }
            catch (Exception e)
            {
                Log.Error(Owner.Will, $"Unable to rollover a leaderboard.", exception: e);
                message = e.Message;
            }
        } while (!success && ++errors < 10);

        if (!success)
            await SlackDiagnostics
                .Log(title: $"Leaderboard rollover failed! ({data.LeaderboardId})", message: message)
                .AddMessage($"The rollover was retried {errors} times, but could not be completed.")
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
        [BsonElement("leaderboardId")]
        public string LeaderboardId { get; set; }
        
        [BsonElement("leaderboardType")]
        public string LeaderboardType { get; set; }
    }
}