using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CounterStrikeSharp.API.Modules.Utils;
using Menu;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Ranks;

[MinimumApiVersion(260)]
public sealed partial class Plugin : BasePlugin
{
	private const string MODULE_ID = "Ranks";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.14";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;
	private DateTime _lastPlaytimeCheck = DateTime.Now;
	public KitsuneMenu Menu { get; private set; } = null!;

	public CCSGameRules? GameRules { get; private set; }
	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;
	private readonly HashSet<CCSPlayerController> _playerSpawned = [];
	private readonly Dictionary<CCSPlayerController, IPlayerServices> _playerCache = [];
	private bool _isGameEnd;
	public IModuleConfigAccessor _coreAccessor = null!;

	private readonly Dictionary<(string Section, string Key), object> _configCache = [];
	private Task? _backgroundTask;
	private readonly TimeSpan _playerCacheExpiration = TimeSpan.FromSeconds(3);
	private readonly CancellationTokenSource _cancellationTokenSource = new();

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		if (!InitializeZenithAPI())
			return;

		RegisterConfigs(_moduleServices!);
		RegisterModuleSettings();
		RegisterModuleStorage();
		RegisterPlaceholders();
		RegisterCommands();

		Initialize_Ranks();
		Initialize_Events();

		SetupZenithEvents();
		SetupGameRules(hotReload);

		_backgroundTask = RunBackgroundTasks(_cancellationTokenSource.Token);

		Menu = new KitsuneMenu(this);
		_coreAccessor = _moduleServices!.GetModuleConfigAccessor();

		if (hotReload)
		{
			_moduleServices!.LoadAllOnlinePlayerData();

			var players = Utilities.GetPlayers();
			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
					OnZenithPlayerLoaded(player);
			}
		}

		AddTimer(5.0f, () =>
		{
			UserMessage message = UserMessage.FromId(350);
			message.Recipients.AddAllPlayers();
			message.Send();
		}, TimerFlags.REPEAT);

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private async Task RunBackgroundTasks(CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				await Task.Delay(_cacheCleanupInterval, cancellationToken);
				if (!cancellationToken.IsCancellationRequested)
				{
					CleanupCache();
					CheckPlaytime();
				}
			}
		}
		catch (TaskCanceledException)
		{
			// We do cancel the task, so this is expected.
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error occurred during background tasks: {ex.Message}");
		}
	}

	private void CleanupCache()
	{
		var expiredTime = DateTime.Now - _playerCacheExpiration;
		var expiredKeys = _playerRankCache.Where(kvp => kvp.Value.LastUpdate < expiredTime)
										  .Select(kvp => kvp.Key)
										  .ToList();
		foreach (var key in expiredKeys)
		{
			_playerRankCache.Remove(key);
		}
	}

	private void CheckPlaytime()
	{
		Server.NextWorldUpdate(() =>
		{
			int interval = GetCachedConfigValue<int>("Points", "PlaytimeInterval");
			if (interval <= 0) return;

			if ((DateTime.Now - _lastPlaytimeCheck).TotalMinutes >= interval)
			{
				int playtimePoints = GetCachedConfigValue<int>("Points", "PlaytimePoints");
				foreach (var player in GetValidPlayers())
				{
					ModifyPlayerPoints(player, playtimePoints, "k4.events.playtime");
				}
				_lastPlaytimeCheck = DateTime.Now;
			}
		});
	}

	private T GetCachedConfigValue<T>(string section, string key) where T : notnull
	{
		var cacheKey = (section, key);
		if (_configCache.TryGetValue(cacheKey, out var value))
			return (T)value;

		value = _configAccessor.GetValue<T>(section, key);
		_configCache[cacheKey] = value;
		return (T)value;
	}

	private bool InitializeZenithAPI()
	{
		try
		{
			_playerServicesCapability = new("zenith:player-services");
			_moduleServicesCapability = new("zenith:module-services");
			_moduleServices = _moduleServicesCapability.Get();

			if (_moduleServices == null)
				throw new InvalidOperationException("Failed to get Module-Services API for Zenith.");

			return true;
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to initialize Zenith API: {ex.Message}");
			Logger.LogInformation("Please check if Zenith is installed, configured and loaded correctly.");
			UnloadPlugin();
			return false;
		}
	}

	private void RegisterModuleSettings()
	{
		_moduleServices!.RegisterModuleSettings(new Dictionary<string, object?>
		{
			{ "ShowRankChanges", true },
		}, Localizer);
	}

	public Dictionary<string, object?> _defaultStorage = [];

	private void RegisterModuleStorage()
	{
		_defaultStorage = new Dictionary<string, object?>
		{
			{ "Points", _configAccessor.GetValue<long>("Settings", "StartPoints") },
			{ "Rank", "k4.phrases.rank.none" }
		};

		_moduleServices!.RegisterModuleStorage(_defaultStorage);
	}

	private void RegisterPlaceholders()
	{
		_moduleServices!.RegisterModulePlayerPlaceholder("rank_color", GetRankColor);
		_moduleServices.RegisterModulePlayerPlaceholder("rank", GetRankName);
		_moduleServices.RegisterModulePlayerPlaceholder("points", GetPlayerPoints);
	}

	private void RegisterCommands()
	{
		_moduleServices!.RegisterModuleCommands(_configAccessor.GetValue<List<string>>("Commands", "RankCommands"), "Show the rank informations.", OnRankCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices!.RegisterModuleCommands(["zgivepoint", "zgivepoints"], "Gives Zenith Rank point to the player.", OnGivePoints, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <amount>", "@zenith/point-admin");
		_moduleServices!.RegisterModuleCommands(["ztakepoint", "ztakepoints"], "Takes Zenith Rank point from the player.", OnTakePoints, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <amount>", "@zenith/point-admin");
		_moduleServices!.RegisterModuleCommands(["zsetpoint", "zsetpoints"], "Sets Zenith Rank point for the player.", OnSetPoints, CommandUsage.CLIENT_AND_SERVER, 2, "<target> <amount>", "@zenith/point-admin");
		_moduleServices!.RegisterModuleCommands(["zresetpoint", "zresetpoints"], "Resets Zenith storages for the player.", OnResetPoints, CommandUsage.CLIENT_AND_SERVER, 1, "<target>", "@zenith/point-admin");
		_moduleServices!.RegisterModuleCommands(["ranks"], "Shows the rank informations.", OnRanksCommand, CommandUsage.CLIENT_ONLY);
	}

	private void SetupZenithEvents()
	{
		_zenithEvents = _moduleServices!.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithPlayerLoaded += OnZenithPlayerLoaded;
			_zenithEvents.OnZenithPlayerUnloaded += OnZenithPlayerUnloaded;
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
			_zenithEvents.OnZenithStorageReset += (moduleID) =>
			{
				if (moduleID == Assembly.GetExecutingAssembly().GetName().Name)
				{
					_playerRankCache.Clear();
					_roundPoints.Clear();
				}
			};
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}
	}

	private void SetupGameRules(bool hotReload)
	{
		if (hotReload)
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		var handler = GetZenithPlayer(player);
		if (handler == null)
		{
			Logger.LogError($"Failed to get player services for {player.PlayerName}");
			return;
		}

		_playerCache[player] = handler;
		_playerSpawned.Add(player);
	}

	private void OnZenithPlayerUnloaded(CCSPlayerController player)
	{
		_playerRankCache.Remove(player.SteamID);
		_playerCache.Remove(player);
		_playerSpawned.Remove(player);
	}

	private void OnZenithCoreUnload(bool hotReload)
	{
		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(ModulePath, DateTime.Now); }
				catch (Exception ex) { Logger.LogError($"Failed to update file: {ex.Message}"); }
			});
		}
	}

	public override void Unload(bool hotReload)
	{
		_cancellationTokenSource.Cancel();
		_backgroundTask?.Wait();
		_cancellationTokenSource.Dispose();

		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}

	private void UnloadPlugin()
	{
		Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
	}

	private string GetRankColor(CCSPlayerController p)
	{
		if (_playerCache.TryGetValue(p, out var player))
		{
			var playerData = GetOrUpdatePlayerRankInfo(player);
			return playerData.Rank?.ChatColor.ToString() ?? ChatColors.Default.ToString();
		}

		return ChatColors.Default.ToString();
	}

	private string GetRankName(CCSPlayerController p)
	{
		if (_playerCache.TryGetValue(p, out var player))
		{
			var playerData = GetOrUpdatePlayerRankInfo(player);
			return playerData.Rank?.Name ?? Localizer.ForPlayer(p, "k4.phrases.rank.none");
		}

		return Localizer.ForPlayer(p, "k4.phrases.rank.none");
	}

	private string GetPlayerPoints(CCSPlayerController p)
	{
		if (_playerCache.TryGetValue(p, out var player))
			return player.GetStorage<long>("Points").ToString();

		return "0";
	}

	private class PlayerRankInfo
	{
		public Rank? Rank { get; set; }
		public Rank? NextRank { get; set; }
		public long Points { get; set; }
		public DateTime LastUpdate { get; set; }
		public KillStreakInfo KillStreak { get; set; } = new KillStreakInfo();
	}

	private class KillStreakInfo
	{
		public int KillCount { get; set; }
		public long LastKillTime { get; set; }
	}
}
