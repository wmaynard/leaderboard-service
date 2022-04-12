using System;
using System.Threading.Tasks;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using StackExchange.Redis;

namespace Rumble.Platform.LeaderboardService.Services;

/// <summary>
/// Experimental service.
/// The goal of this service is to have only one instance performing actions across all instances of a project.
/// For example, with high traffic, the cluster may spin up another container for chat-service.  Chat might need
/// to make sensitive updates and we want to avoid duplicate actions being taken.
/// </summary>
public abstract class MasterService : PlatformTimerService
{
	private const int MS_INTERVAL = 5_000;					// The interval to check in; recent check-ins indicate service is still active.
	private const int MS_TAKEOVER = MS_INTERVAL;// * 10;		// The threshold at which the previous MasterService should be replaced by the current one.
	public static int MaximumRetryTime => MS_TAKEOVER + MS_INTERVAL;
#pragma warning disable
	private readonly ConfigService _config;
#pragma warning restore
	
	private string ID { get; init; }

	protected MasterService(ConfigService configService) : base(intervalMS: MS_INTERVAL, startImmediately: true)
	{
		_config = configService;
		ID = Guid.NewGuid().ToString();	
	} 

	private string Name => GetType().Name;
	private string LastActiveKey => $"{Name}_lastActive";
	private bool IsPrimary => _config.Value<string>(Name) == ID;
	private bool IsWorking { get; set; }
	private long LastActivity => _config.Value<long>(LastActiveKey);
	
	/// <summary>
	/// Attempts to complete an action.  If the 
	/// </summary>
	/// <param name="action"></param>
	/// <returns></returns>
	public async Task<bool> Do(Action action, Func<bool> validation = null)
	{
		if (!IsPrimary)
		{
			if (validation != null)
				Schedule(action, MaximumRetryTime, validation);
			return false;
		}
			
		await Task.Run(action);
		return true;
	}

	private void Schedule(Action action, int ms, Func<bool> validation = null)
	{
		// TODO: Retry work; if it's false here, we aren't the primary node
		// Check that lastactive has changed since schedule was called and that the ID isn't us
	}

	protected T Get<T>(string key)
	{
		try
		{
			return _config.Value<T>($"{Name}_{key}");
		}
		catch
		{
			return default;
		}
	}

	protected async void Set(string key, object value) => await Do(() =>
	{
		_config.Update($"{Name}_{key}", value);
	});

	protected sealed override void OnElapsed()
	{
#if DEBUG
		return;
#endif
		if (IsPrimary)
		{
			_config.Update(LastActiveKey, UnixTimeMS);
			
			// We want the config to be updated regardless of whether or not our worker threads are processing.
			// If we don't update it and our service takes too long to work, another container will try to take over and
			// duplicate the work.
			if (IsWorking)
				return;
			
			IsWorking = true;
			Work();
			IsWorking = false;
		}
		else if (UnixTimeMS - LastActivity > MS_TAKEOVER)
			Confiscate();
	}

	protected abstract void Work();

	private void Confiscate()
	{
		_config.Update(Name, ID);
		_config.Update(LastActiveKey, UnixTimeMS);
		Work();
	}

	public override object HealthCheckResponseObject => new GenericData
	{
		{ 
			Name, new GenericData()
			{
				{ "ServiceId", ID},
				{ $"{Name}_isMasterNode", IsPrimary },
				{ $"{LastActiveKey}", $"{UnixTimeMS - LastActivity}ms ago" },
				{ "ConfigService",  _config.HealthCheckResponseObject }
			}
		}
	};
}