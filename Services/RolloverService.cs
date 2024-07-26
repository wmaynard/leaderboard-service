using System;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Rumble.Platform.Common.Enums;
using Rumble.Platform.Common.Exceptions;
using Rumble.Platform.Common.Interop;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Utilities.JsonTools;
using Rumble.Platform.LeaderboardService.Exceptions;
using Rumble.Platform.LeaderboardService.Models;

namespace Rumble.Platform.LeaderboardService.Services;

public class RolloverService : QueueService<RolloverService.RolloverData>
{
    public const string CONFIG_HOURLY_SETTING = "hourlyRolloverMinute";	    // 0-59
    public const string CONFIG_DAILY_SETTING = "rolloverTime";	            // 00:00 - 23:59
    public const string CONFIG_WEEKLY_SETTING = "weeklyRolloverDay";		// NOT 0-index
    public const string CONFIG_MONTHLY_SETTING = "monthlyRolloverDay";		// NOT 0-index
    public const string CONFIG_ARCHIVE_RETENTION = "archiveRetention";
    public const string LAST_HOURLY_SETTING = "lastHourlyRollover";
    public const string LAST_DAILY_SETTING = "lastDailyRollover";
    public const string LAST_WEEKLY_SETTING = "lastWeeklyRollover";
    public const string LAST_MONTHLY_SETTING = "lastMonthlyRollover";
    
    private readonly ArchiveService _archive;
    private readonly DynamicConfig _config;
    private readonly EnrollmentService _enrollment;
    private readonly LeaderboardService _leaderboard;
    private readonly RewardsService _rewardService;
    private readonly ApiService _api;
    
    private int HourlyResetMinute { get; set; }
    private TimeSpan DailyResetTime { get; set; }
    private int WeeklyResetDay { get; set; }
    private int MonthlyResetDay { get; set; }
    
    private int ArchiveRetentionDays { get; set; }

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
    
    public RolloverService(ArchiveService archive, DynamicConfig config, EnrollmentService enrollment, LeaderboardService leaderboard, RewardsService rewards, ApiService api) 
        : base(collection: "rollover", primaryNodeTaskCount: 5, secondaryNodeTaskCount: 0)
    {
        // _config = config;
        _archive = archive;
        _api = api;
        _config = config;
        _enrollment = enrollment;
        _leaderboard = leaderboard;
        _rewardService = rewards;
    }

    private void CreateRolloverTasks(RolloverType rolloverType)
    {
        // _leaderboard.BeginRollover(rolloverType, out string[] ids, out string[] types);
        _leaderboard.BeginRollover(rolloverType, out RumbleJson[] data);
        
        CreateUntrackedTasks(data
            .Select(json => new RolloverData
            {
                LeaderboardId = json.Require<string>(Leaderboard.DB_KEY_ID),
                LeaderboardType = json.Require<string>(Leaderboard.DB_KEY_TYPE)
            })
            .ToArray()
        );
    }

    public void ManualRollover()
    {
        if (TasksRemaining() > 0)
            throw new PlatformException("Rollover tasks still remain; wait for the current rollover to finish.");
        Confiscate();
        DeleteAcknowledgedTasks();
        foreach (RolloverType type in Enum.GetValues(typeof(RolloverType)))
            CreateRolloverTasks(type);
    }

    /// <summary>
    /// Executes after all leaderboard rollovers are acknowledged.
    /// </summary>
    protected override void OnTasksCompleted(RolloverData[] data)
    {
        string[] types = data
            .Select(rolloverData => rolloverData.LeaderboardType)
            .Distinct()
            .ToArray();
        
        foreach (string type in types)
        {
            _enrollment.DemoteInactivePlayers(type);
            _enrollment.FlagAsInactive(type);
        }
        
        _archive.DeleteOldArchives(ArchiveRetentionDays);
        Log.Local(Owner.Will, "Rollover complete.", emphasis: Log.LogType.ERROR);
    }

    protected override void PrimaryNodeWork()
    {
        UpdateLocalConfig();
        DateTime now = DateTime.UtcNow;
		
        // Check hourly leaderboards
        if (PastHourlyResetTime(now))
        {
            LastHourlyRollover = new DateTime(
                year: now.Year,
                month: now.Month,
                day: now.Day,
                hour: now.Hour,
                minute: HourlyResetMinute,
                second: 0
            );
            CreateRolloverTasks(RolloverType.Hourly);
        }
        
        // Check daily leaderboards
        if (LastDailyRollover.Day != now.Day && PastDailyResetTime(now))
        {
            LastDailyRollover = now;
            CreateRolloverTasks(RolloverType.Daily);
        }
		
        // Check weekly leaderboards
        bool isRolloverDay = (int)now.DayOfWeek == WeeklyResetDay;
        if (isRolloverDay && now.Subtract(LastWeeklyRollover).TotalDays > 1 && PastDailyResetTime(now))
        {
            LastWeeklyRollover = now;
            CreateRolloverTasks(RolloverType.Weekly);
        }

        // Check monthly leaderboards
        if (LastMonthlyRollover.Month != now.Month && LastMonthlyRollover.Day < now.Day && PastDailyResetTime(now))
        {
            LastMonthlyRollover = now;
            CreateRolloverTasks(RolloverType.Monthly);
        }
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
                success = true;
            }
            catch (Exception e)
            {
                Log.Error(Owner.Will, $"Unable to rollover a leaderboard.", data: new
                {
                    RolloverData = data
                }, exception: e);

                message = e.Message;
            }
        } while (!success && ++errors < 10);

        if (!success)
            _api.Alert(
                title: "Leaderboard rollover failed",
                message: $"{data.LeaderboardId} rollover was unable to succeed after multiple attempts.",
                countRequired: 1,
                timeframe: 30_000,
                owner: Owner.Will,
                impact: ImpactType.ServicePartiallyUsable,
                data: new RumbleJson
                {
                    { "taskData", data },
                    { "lastErrorMessage", message },
                    { "errorCount", errors }
                },
                confluenceLink: "https://rumblegames.atlassian.net/wiki/spaces/TH/pages/3558014977/leaderboard-service+Leaderboard+rollover+failed"
            );
    }

    private bool PastHourlyResetTime(DateTime utc)
    {
        if (HourlyResetMinute > 59)
        {
            Log.Error(Owner.Will, "HourlyResetMinute is set to an invalid value in Dynamic Config; it must be 0-59", data: new
            {
                CurrentValue = HourlyResetMinute,
                Impact = "Hourly rollover cannot take place"
            });
            return false;
        }
        
        Log.Verbose(Owner.Will, LastHourlyRollover.ToString());

        return utc.Subtract(LastHourlyRollover).TotalMinutes > 60;
    }

    private bool PastDailyResetTime(DateTime utc) => DailyResetTime.CompareTo(utc.TimeOfDay) <= 0;
    private void UpdateLocalConfig()
    {
        HourlyResetMinute = _config?.Optional<int?>(CONFIG_HOURLY_SETTING) ?? 0;
        DailyResetTime = TimeSpan.Parse(_config?.Optional<string>(CONFIG_DAILY_SETTING) ?? "02:00");
        WeeklyResetDay = _config?.Optional<int?>(CONFIG_WEEKLY_SETTING) ?? 1;
        MonthlyResetDay = _config?.Optional<int?>(CONFIG_MONTHLY_SETTING) ?? 1;
        ArchiveRetentionDays = _config?.Optional<int?>(CONFIG_ARCHIVE_RETENTION) ?? 60;
    }
    
    public class RolloverData : PlatformDataModel
    {
        [BsonElement("leaderboardId")]
        public string LeaderboardId { get; set; }
        
        [BsonElement("leaderboardType")]
        public string LeaderboardType { get; set; }
    }
}