using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;
using CounterStrikeSharp.API.Modules.Timers;

namespace Zenith_TimeStats;

[MinimumApiVersion(260)]
public class Plugin : BasePlugin
{


	private IModuleConfigAccessor _coreAccessor = null!;
	private const string MODULE_ID = "TimeStats";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.7";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;

	private readonly Dictionary<CCSPlayerController, PlayerTimeData> _playerTimes = [];

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		try
		{
			_playerServicesCapability = new("zenith:player-services");
			_moduleServicesCapability = new("zenith:module-services");
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to initialize Zenith API: {ex.Message}");
			Logger.LogInformation("Please check if Zenith is installed, configured and loaded correctly.");

			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_moduleServices = _moduleServicesCapability.Get();
		if (_moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		_moduleServices.RegisterModuleConfig("Config", "PlaytimeCommands", "List of commands that shows player time statistics", new List<string> { "playtime", "mytime" });
		_moduleServices.RegisterModuleConfig("Config", "TodayCommands", "List of commands that shows today's playtime statistics", new List<string> { "today", "mytoday" });
		_moduleServices.RegisterModuleConfig("Config", "NotificationInterval", "Interval in seconds between playtime notifications", 300);

		_moduleServices.RegisterModuleSettings(new Dictionary<string, object?>
		{
			{ "ShowPlaytime", true }
		}, Localizer);

		_moduleServices.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "TotalPlaytime", 0.0 },
			{ "NonAFKPlaytime", 0.0 },
			{ "AFKPlaytime", 0.0 },
			{ "TerroristPlaytime", 0.0 },
			{ "CounterTerroristPlaytime", 0.0 },
			{ "SpectatorPlaytime", 0.0 },
			{ "AlivePlaytime", 0.0 },
			{ "DeadPlaytime", 0.0 },
			{ "LastNotification", 0L },
			{ "LastPlayDate", DateTime.Now.ToString("yyyy-MM-dd") },
			{ "TodayPlaytime", 0.0 }
		});

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithPlayerLoaded += OnZenithPlayerLoaded;
			_zenithEvents.OnZenithPlayerUnloaded += OnZenithPlayerUnloaded;
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
		RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
		RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

		AddTimer(10.0f, OnTimerElapsed, TimerFlags.REPEAT);

		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "PlaytimeCommands"), "Show the playtime informations.", OnPlaytimeCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "TodayCommands"), "Show today's playtime information.", OnTodayCommand, CommandUsage.CLIENT_ONLY);

		if (hotReload)
		{
			_moduleServices.LoadAllOnlinePlayerData();
			var players = Utilities.GetPlayers();
			long currentTime = DateTimeOffset.Now.ToUnixTimeSeconds();

			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
				{
					var zenithPlayer = GetZenithPlayer(player);
					if (zenithPlayer is null) continue;

					_playerTimes[player] = new PlayerTimeData
					{
						Zenith = zenithPlayer,
						LastUpdateTime = currentTime,
						CurrentTeam = player.Team,
						IsAlive = player.PlayerPawn.Value?.Health > 0
					};
				}
			}
		}

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer is null) return;

		_playerTimes[player] = new PlayerTimeData
		{
			Zenith = zenithPlayer,
			LastUpdateTime = DateTimeOffset.Now.ToUnixTimeSeconds(),
			CurrentTeam = CsTeam.Spectator,
			IsAlive = false
		};
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		if (@event.Userid == null) return HookResult.Continue;

		if (_playerTimes.TryGetValue(@event.Userid, out var timeData))
		{
			UpdatePlaytime(timeData);
			timeData.IsAlive = true;
		}
		return HookResult.Continue;
	}

	private void OnZenithPlayerUnloaded(CCSPlayerController player)
	{
		if (_playerTimes.TryGetValue(player, out var timeData))
			UpdatePlaytime(timeData);

		_playerTimes.Remove(player);
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		if (@event.Userid == null) return HookResult.Continue;

		if (_playerTimes.TryGetValue(@event.Userid, out var timeData))
		{
			UpdatePlaytime(timeData);
			timeData.IsAlive = false;
		}
		return HookResult.Continue;
	}

	private HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
	{
		if (@event.Userid == null) return HookResult.Continue;

		if (_playerTimes.TryGetValue(@event.Userid, out var timeData))
		{
			UpdatePlaytime(timeData);

			timeData.CurrentTeam = (CsTeam)@event.Team;
			timeData.IsAlive = @event.Team != (int)CsTeam.Spectator;
		}
		return HookResult.Continue;
	}

	private void OnTimerElapsed()
	{
		int interval = _coreAccessor.GetValue<int>("Config", "NotificationInterval");
		if (interval <= 0)
			return;

		foreach (var player in _playerTimes.Values)
		{
			UpdatePlaytime(player);

			bool hasPlaytime = player.Zenith.GetStorage<double>("TotalPlaytime") > 1 ||
								player.Zenith.GetStorage<double>("NonAFKPlaytime") > 1 ||
								player.Zenith.GetStorage<double>("TerroristPlaytime") > 1 ||
								player.Zenith.GetStorage<double>("CounterTerroristPlaytime") > 1 ||
								player.Zenith.GetStorage<double>("SpectatorPlaytime") > 1 ||
								player.Zenith.GetStorage<double>("AlivePlaytime") > 1 ||
								player.Zenith.GetStorage<double>("DeadPlaytime") > 1;

			if (hasPlaytime)
				CheckAndSendNotification(player.Zenith, interval);
		}
	}


	private static void UpdatePlaytime(PlayerTimeData data)
	{
		long currentTime = DateTimeOffset.Now.ToUnixTimeSeconds();
		double sessionDurationMinutes = Math.Round((currentTime - data.LastUpdateTime) / 60.0, 1);
		string currentDate = DateTime.Now.ToString("yyyy-MM-dd");

		string? lastPlayDate = data.Zenith.GetStorage<string>("LastPlayDate");
		if (lastPlayDate != currentDate)
		{
			data.Zenith.SetStorage("TodayPlaytime", 0.0);
			data.Zenith.SetStorage("LastPlayDate", currentDate);
		}

		double todayPlaytime = data.Zenith.GetStorage<double>("TodayPlaytime");
		todayPlaytime += sessionDurationMinutes;
		data.Zenith.SetStorage("TodayPlaytime", todayPlaytime);

		double totalPlaytime = data.Zenith.GetStorage<double>("TotalPlaytime");
		totalPlaytime += sessionDurationMinutes;
		data.Zenith.SetStorage("TotalPlaytime", totalPlaytime);


		string teamKey = data.CurrentTeam switch
		{
			CsTeam.Terrorist => "TerroristPlaytime",
			CsTeam.CounterTerrorist => "CounterTerroristPlaytime",
			_ => "SpectatorPlaytime"
		};
		double teamPlaytime = data.Zenith.GetStorage<double>(teamKey);
		teamPlaytime += sessionDurationMinutes;
		data.Zenith.SetStorage(teamKey, teamPlaytime);

		string lifeStatusKey = data.IsAlive ? "AlivePlaytime" : "DeadPlaytime";
		double lifeStatusPlaytime = data.Zenith.GetStorage<double>(lifeStatusKey);
		lifeStatusPlaytime += sessionDurationMinutes;
		data.Zenith.SetStorage(lifeStatusKey, lifeStatusPlaytime);
		var player = data.Zenith.Controller;
		if (data.Zenith.IsAFK) {
			double afkPlaytime = data.Zenith.GetStorage<double>("AFKPlaytime");
			afkPlaytime += sessionDurationMinutes;
			data.Zenith.SetStorage("AFKPlaytime", afkPlaytime);
		} else {
			double nonAFKPlaytime = data.Zenith.GetStorage<double>("NonAFKPlaytime");
			nonAFKPlaytime += sessionDurationMinutes;
			data.Zenith.SetStorage("NonAFKPlaytime", nonAFKPlaytime);
		}

		data.LastUpdateTime = currentTime;
	}

	private void CheckAndSendNotification(IPlayerServices playerServices, int interval)
	{
		bool showPlaytime = playerServices.GetSetting<bool>("ShowPlaytime");
		if (!showPlaytime) return;

		long currentTime = DateTimeOffset.Now.ToUnixTimeSeconds();
		long lastNotification = playerServices.GetStorage<long>("LastNotification");

		if (currentTime - lastNotification >= interval)
		{
			SendPlaytimeNotification(playerServices);
			playerServices.SetStorage("LastNotification", currentTime);
		}
	}

	private void SendPlaytimeNotification(IPlayerServices playerServices)
	{
		double totalPlaytime = playerServices.GetStorage<double>("TotalPlaytime");
		string formattedTime = FormatTime(totalPlaytime);

		string message = Localizer["timestats.notification", formattedTime];

		playerServices.Print(message);
	}

	public void OnTodayCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player is null) return;

		if (_playerTimes.TryGetValue(player, out var timeData))
		{
			UpdatePlaytime(timeData);
			SendTodayPlaytimeStats(timeData.Zenith);
		}
	}

	private void SendTodayPlaytimeStats(IPlayerServices playerServices)
	{
		double todayPlaytime = playerServices.GetStorage<double>("TodayPlaytime");

		if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
		{
			string htmlMessage = $@"
			<font color='#ff3333' class='fontSize-m'>{Localizer["timestats.today.title"]}</font><br>
			<font color='#FFFFFF' class='fontSize-sm'>{FormatTime(todayPlaytime)}</font>";

			playerServices.PrintToCenter(htmlMessage, _coreAccessor.GetValue<int>("Core", "CenterMessageTime"), ActionPriority.Low);
		}
		else
		{
			playerServices.Print(Localizer["timestats.today.chat.title", playerServices.Controller.PlayerName]);
			playerServices.Print(Localizer["timestats.today.chat.playtime", FormatTime(todayPlaytime)]);
		}
	}

	public void OnPlaytimeCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player is null) return;

		if (_playerTimes.TryGetValue(player, out var timeData))
		{
			UpdatePlaytime(timeData);
			SendDetailedPlaytimeStats(timeData.Zenith);
		}
	}

	private void SendDetailedPlaytimeStats(IPlayerServices playerServices)
	{
		double totalPlaytime = playerServices.GetStorage<double>("TotalPlaytime");
		double nonAFKPlaytime = playerServices.GetStorage<double>("NonAFKPlaytime");
		double AFKPlaytime = playerServices.GetStorage<double>("AFKPlaytime");
		double terroristPlaytime = playerServices.GetStorage<double>("TerroristPlaytime");
		double ctPlaytime = playerServices.GetStorage<double>("CounterTerroristPlaytime");
		double spectatorPlaytime = playerServices.GetStorage<double>("SpectatorPlaytime");
		double alivePlaytime = playerServices.GetStorage<double>("AlivePlaytime");
		double deadPlaytime = playerServices.GetStorage<double>("DeadPlaytime");

		if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
		{
			string htmlMessage = $@"
        <font color='#ff3333' class='fontSize-m'>{Localizer["timestats.center.title"]}</font><br>
        <font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.total.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{FormatTime(totalPlaytime)}</font><br>
        <font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.teams.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{Localizer["timestats.center.teams.value", FormatTime(terroristPlaytime), FormatTime(ctPlaytime)]}</font><br>
        <font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.spectator.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{FormatTime(spectatorPlaytime)}</font><br>
        <font color='#FF6666' class='fontSize-sm'>{Localizer["timestats.center.status.label"]}</font> <font color='#FFFFFF' class='fontSize-s'>{Localizer["timestats.center.status.value", FormatTime(alivePlaytime), FormatTime(deadPlaytime)]}</font>";

			playerServices.PrintToCenter(htmlMessage, _coreAccessor.GetValue<int>("Core", "CenterMessageTime"), ActionPriority.Low);
		}
		else
		{
			playerServices.Print(Localizer["timestats.chat.title", playerServices.Controller.PlayerName]);
			playerServices.Print(Localizer["timestats.chat.total", FormatTime(totalPlaytime)]);
			playerServices.Print(Localizer["timestats.chat.teams", FormatTime(terroristPlaytime), FormatTime(ctPlaytime)]);
			playerServices.Print(Localizer["timestats.chat.spectator", FormatTime(spectatorPlaytime)]);
			playerServices.Print(Localizer["timestats.chat.status", FormatTime(alivePlaytime), FormatTime(deadPlaytime)]);
		}
	}

	private string FormatTime(double minutes)
	{
		int totalMinutes = (int)Math.Floor(minutes);
		int days = totalMinutes / 1440;
		int hours = (totalMinutes % 1440) / 60;
		int mins = totalMinutes % 60;

		return Localizer["timestats.time.format", days, hours, mins];
	}

	public override void Unload(bool hotReload)
	{
		foreach (var player in _playerTimes.Values)
			UpdatePlaytime(player);

		_playerTimes.Clear();

		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}

	private void OnZenithCoreUnload(bool hotReload)
	{
		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(Path.Combine(ModulePath), DateTime.Now); }
				catch (Exception ex) { Logger.LogError($"Failed to update file: {ex.Message}"); }
			});
		}
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}
}

public class PlayerTimeData
{
	public required IPlayerServices Zenith { get; set; }
	public long LastUpdateTime { get; set; }
	public CsTeam CurrentTeam { get; set; }
	public bool IsAlive { get; set; }
	public string LastPlayDate { get; set; } = DateTime.Now.ToString("yyyy-MM-dd");
	public double TodayPlaytime { get; set; } = 0.0;
}
