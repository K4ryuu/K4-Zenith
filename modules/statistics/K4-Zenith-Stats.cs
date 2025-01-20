using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZenithAPI;
using Menu;
using Menu.Enums;
using MySqlConnector;
using Dapper;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Menu;
using System.Reflection;
using CounterStrikeSharp.API.Core.Translations;

namespace Zenith_Stats;

[MinimumApiVersion(260)]
public class Plugin : BasePlugin
{
	public CCSGameRules? GameRules = null;
	public IModuleConfigAccessor _coreAccessor = null!;
	private const string MODULE_ID = "Stats";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.7";

	public KitsuneMenu Menu { get; private set; } = null!;
	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private EventManager? _eventManager;
	private IModuleServices? _moduleServices;
	private readonly Dictionary<ulong, PlayerStats> _playerStats = [];
	private readonly HashSet<CCSPlayerController> playerSpawned = [];

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

		Menu = new KitsuneMenu(this);
		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		RegisterModuleConfigs();
		RegisterModuleStorage();
		RegisterModuleCommands();
		RegisterModulePlaceholders();

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithPlayerLoaded += OnZenithPlayerLoaded;
			_zenithEvents.OnZenithPlayerUnloaded += OnZenithPlayerUnloaded;
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
			_zenithEvents.OnZenithStorageReset += (moduleID) =>
			{
				if (moduleID == Assembly.GetExecutingAssembly().GetName().Name)
				{
					_playerStats.Clear();

					var players = Utilities.GetPlayers();
					foreach (var player in players)
					{
						if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
							OnZenithPlayerLoaded(player);
					}
				}
			};
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		Initialize_Events();

		RegisterListener<Listeners.OnMapStart>(OnMapStart);
		RegisterListener<Listeners.OnMapEnd>(OnMapEnd);
		RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart);
		RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);

		if (hotReload)
		{
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;

			_moduleServices!.LoadAllOnlinePlayerData();
			var players = Utilities.GetPlayers();
			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
					OnZenithPlayerLoaded(player);
			}
		}

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void RegisterModuleConfigs()
	{
		_moduleServices!.RegisterModuleConfig("Config", "StatisticCommands", "List of commands that shows player statistics", new List<string> { "stats", "stat", "statistics" });
		_moduleServices.RegisterModuleConfig("Config", "MapStatisticCommands", "List of commands that shows map statistics", new List<string> { "mapstats", "mapstat", "mapstatistics" });
		_moduleServices.RegisterModuleConfig("Config", "WeaponStatisticCommands", "List of commands that shows weapon statistics", new List<string> { "weaponstats", "weaponstat", "weaponstatistics" });
		_moduleServices.RegisterModuleConfig("Config", "WarmupStats", "Allow stats during warmup", false);
		_moduleServices.RegisterModuleConfig("Config", "StatsForBots", "Allow stats for bots", false);
		_moduleServices.RegisterModuleConfig("Config", "EnableRequirementMessages", "Enable or disable the messages for stats being disabled", true);
		_moduleServices.RegisterModuleConfig("Config", "MinPlayers", "Minimum number of players required for stats", 4);
		_moduleServices.RegisterModuleConfig("Config", "FFAMode", "Enable FFA mode", false);
		_moduleServices.RegisterModuleConfig("Config", "EnableWeaponStats", "Enable weapon-based statistics", true);
		_moduleServices.RegisterModuleConfig("Config", "EnableMapStats", "Enable map-based statistics", true);
	}

	private void RegisterModuleStorage()
	{
		_moduleServices!.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "Kills", 0 }, { "FirstBlood", 0 }, { "Deaths", 0 }, { "Assists", 0 },
			{ "Shoots", 0 }, { "HitsTaken", 0 }, { "HitsGiven", 0 }, { "Headshots", 0 },
			{ "HeadHits", 0 }, { "ChestHits", 0 }, { "StomachHits", 0 }, { "LeftArmHits", 0 },
			{ "RightArmHits", 0 }, { "LeftLegHits", 0 }, { "RightLegHits", 0 }, { "NeckHits", 0 },
			{ "UnusedHits", 0 }, { "GearHits", 0 }, { "SpecialHits", 0 }, { "Grenades", 0 },
			{ "MVP", 0 }, { "RoundWin", 0 }, { "RoundLose", 0 }, { "GameWin", 0 },
			{ "GameLose", 0 }, { "RoundsOverall", 0 }, { "RoundsCT", 0 }, { "RoundsT", 0 },
			{ "BombPlanted", 0 }, { "BombDefused", 0 }, { "HostageRescued", 0 }, { "HostageKilled", 0 },
			{ "NoScopeKill", 0 }, { "PenetratedKill", 0 }, { "ThruSmokeKill", 0 }, { "FlashedKill", 0 },
			{ "DominatedKill", 0 }, { "RevengeKill", 0 }, { "AssistFlash", 0 }
		});
	}

	private void RegisterModuleCommands()
	{
		_moduleServices!.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "StatisticCommands"), "Show the player statistics.", OnStatsCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "WeaponStatisticCommands"), "Show the player statistics for weapons.", OnWeaponStatsCommand, CommandUsage.CLIENT_ONLY);
		_moduleServices.RegisterModuleCommands(_coreAccessor.GetValue<List<string>>("Config", "MapStatisticCommands"), "Show the player statistics for maps.", OnMapStatsCommand, CommandUsage.CLIENT_ONLY);
	}

	private void RegisterModulePlaceholders()
	{
		_moduleServices!.RegisterModulePlayerPlaceholder("kda", p => { if (_playerStats.TryGetValue(p.SteamID, out var stats)) return CalculateKDA(stats.ZenithPlayer); return "N/A"; });
		_moduleServices.RegisterModulePlayerPlaceholder("kpr", p => { if (_playerStats.TryGetValue(p.SteamID, out var stats)) return CalculateKPR(stats.ZenithPlayer); return "N/A"; });
		_moduleServices.RegisterModulePlayerPlaceholder("accuracy", p => { if (_playerStats.TryGetValue(p.SteamID, out var stats)) return CalculateAccuracy(stats.ZenithPlayer); return "N/A"; });
		_moduleServices.RegisterModulePlayerPlaceholder("kd", p => { if (_playerStats.TryGetValue(p.SteamID, out var stats)) return CalculateKD(stats.ZenithPlayer); return "N/A"; });
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
		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}

	private string CalculateKD(IPlayerServices? player)
	{
		if (player == null || !_playerStats.TryGetValue(player.Controller.SteamID, out var stats)) return "N/A";
		int kills = stats.GetGlobalStat("Kills");
		int deaths = stats.GetGlobalStat("Deaths");
		double kd = deaths == 0 ? kills : (double)kills / deaths;
		return kd.ToString("F2");
	}

	private string CalculateKDA(IPlayerServices? player)
	{
		if (player == null || !_playerStats.TryGetValue(player.Controller.SteamID, out var stats)) return "N/A";
		int kills = stats.GetGlobalStat("Kills");
		int deaths = stats.GetGlobalStat("Deaths");
		int assists = stats.GetGlobalStat("Assists");
		double kda = (kills + assists) / (double)(deaths == 0 ? 1 : deaths);
		return kda.ToString("F2");
	}

	private string CalculateKPR(IPlayerServices? player)
	{
		if (player == null || !_playerStats.TryGetValue(player.Controller.SteamID, out var stats)) return "N/A";
		int kills = stats.GetGlobalStat("Kills");
		int rounds = stats.GetGlobalStat("RoundsOverall");
		double kpr = rounds == 0 ? kills : (double)kills / rounds;
		return kpr.ToString("F2");
	}

	private string CalculateAccuracy(IPlayerServices? player)
	{
		if (player == null || !_playerStats.TryGetValue(player.Controller.SteamID, out var stats)) return "N/A";
		int shoots = stats.GetGlobalStat("Shoots");
		int hitsGiven = stats.GetGlobalStat("HitsGiven");
		double accuracy = (shoots == 0) ? 0 : Math.Min((double)hitsGiven / shoots * 100, 100);
		return accuracy.ToString("F2") + "%";
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer != null)
		{
			_playerStats[zenithPlayer.SteamID] = new PlayerStats(zenithPlayer, this);
		}
		else
		{
			Logger.LogError($"Failed to get player services for {player.PlayerName}");
		}
	}

	private void OnZenithPlayerUnloaded(CCSPlayerController player)
	{
		ulong steamID = player.SteamID;
		if (_playerStats.TryGetValue(steamID, out var stats))
		{
			Task.Run(async () =>
			{
				try
				{
					await stats.SaveWeaponStats();
					await stats.SaveMapStats();
					_playerStats.Remove(steamID);
				}
				catch (Exception ex)
				{
					Logger.LogError($"Error saving stats for player {ex.Message}");
				}
			});
		}
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}

	private void OnMapStart(string mapName)
	{
		AddTimer(1.0f, () =>
		{
			GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules;
			foreach (var player in GetValidPlayers())
			{
				_playerStats[player.SteamID] = new PlayerStats(player, this);
			}
		});
	}

	private void OnMapEnd()
	{
		_ = Task.Run(async () =>
		{
			foreach (var playerStats in _playerStats.Values)
			{
				await playerStats.SaveWeaponStats();
				await playerStats.SaveMapStats();
				playerStats.ResetStats();
			}
		});
		_playerStats.Clear();
		playerSpawned.Clear();
	}

	private HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
	{
		playerSpawned.Clear();
		return HookResult.Continue;
	}

	private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
	{
		CCSPlayerController? player = @event.Userid;
		if (player == null || player.IsBot || player.IsHLTV)
			return HookResult.Continue;

		if (_coreAccessor.GetValue<bool>("Config", "EnableRequirementMessages"))
		{
			int requiredPlayers = _coreAccessor.GetValue<int>("Config", "MinPlayers");
			if (requiredPlayers > Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot && !p.IsHLTV) &&
				_playerStats.TryGetValue(player.SteamID, out var stats) &&
				stats.SpawnMessageTimer == null)
			{
				_moduleServices!.PrintForPlayer(player, Localizer.ForPlayer(player, "k4.stats.stats_disabled", requiredPlayers));
				stats.SpawnMessageTimer = AddTimer(3.0f, () => { stats.SpawnMessageTimer = null; });
			}
		}

		playerSpawned.Add(player);
		return HookResult.Continue;
	}

	public IEnumerable<IPlayerServices> GetValidPlayers()
	{
		foreach (var player in _playerStats.Values)
			yield return player.ZenithPlayer;
	}

	private void Initialize_Events()
	{
		_eventManager = new EventManager(this);

		RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
		RegisterEventHandler<EventGrenadeThrown>(OnGrenadeThrown, HookMode.Post);
		RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt, HookMode.Post);
		RegisterEventHandler<EventRoundStart>(OnRoundStart, HookMode.Post);
		RegisterEventHandler<EventBombPlanted>(OnBombPlanted, HookMode.Post);
		RegisterEventHandler<EventHostageRescued>(OnHostageRescued, HookMode.Post);
		RegisterEventHandler<EventHostageKilled>(OnHostageKilled, HookMode.Post);
		RegisterEventHandler<EventBombDefused>(OnBombDefused, HookMode.Post);
		RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
		RegisterEventHandler<EventWeaponFire>(OnWeaponFire, HookMode.Post);
		RegisterEventHandler<EventRoundMvp>(OnRoundMvp, HookMode.Post);
		RegisterEventHandler<EventCsWinPanelMatch>(OnCsWinPanelMatch, HookMode.Pre);
	}

	private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
	{
		if (_eventManager != null)
			_eventManager.FirstBlood = false;
		return HookResult.Continue;
	}

	private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandlePlayerDeath(@event);
		return HookResult.Continue;
	}

	private HookResult OnGrenadeThrown(EventGrenadeThrown @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleGrenadeThrown(@event);
		return HookResult.Continue;
	}

	private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandlePlayerHurt(@event);
		return HookResult.Continue;
	}

	private (bool isAllowed, DateTime lastCheck) _statsAllowedCache = (false, DateTime.MinValue);
	private const int CACHE_UPDATE_INTERVAL_SECONDS = 10;

	private bool IsStatsAllowed()
	{
		if ((DateTime.Now - _statsAllowedCache.lastCheck).TotalSeconds < CACHE_UPDATE_INTERVAL_SECONDS)
		{
			return _statsAllowedCache.isAllowed;
		}

		int notBots = _playerStats.Count;
		bool warmupStats = _coreAccessor.GetValue<bool>("Config", "WarmupStats");
		int minPlayers = _coreAccessor.GetValue<int>("Config", "MinPlayers");

		bool isAllowed = GameRules != null && (!GameRules.WarmupPeriod || warmupStats) && (minPlayers <= notBots);

		_statsAllowedCache = (isAllowed, DateTime.Now);
		return isAllowed;
	}

	private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleBombPlanted(@event);
		return HookResult.Continue;
	}

	private HookResult OnHostageRescued(EventHostageRescued @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleHostageRescued(@event);
		return HookResult.Continue;
	}

	private HookResult OnHostageKilled(EventHostageKilled @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleHostageKilled(@event);
		return HookResult.Continue;
	}

	private HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleBombDefused(@event);
		return HookResult.Continue;
	}

	private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleRoundEnd(@event);
		return HookResult.Continue;
	}

	private HookResult OnWeaponFire(EventWeaponFire @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleWeaponFire(@event);
		return HookResult.Continue;
	}

	private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleRoundMvp(@event);
		return HookResult.Continue;
	}

	private HookResult OnCsWinPanelMatch(EventCsWinPanelMatch @event, GameEventInfo info)
	{
		if (!IsStatsAllowed()) return HookResult.Continue;
		_eventManager?.HandleCsWinPanelMatch(@event);
		return HookResult.Continue;
	}

	private void OnStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null) return;

		if (_playerStats.TryGetValue(player.SteamID, out var stats))
		{
			if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
			{
				ShowCenterStatsMenu(player, stats);
			}
			else
			{
				ShowChatStatsMenu(player, stats);
			}
		}
	}

	private void OnWeaponStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null) return;

		if (!_playerStats.TryGetValue(player.SteamID, out var stats) || stats.WeaponStats.Count == 0)
			return;

		if (!_coreAccessor.GetValue<bool>("Config", "EnableWeaponStats"))
		{
			stats.ZenithPlayer.Print(Localizer.ForPlayer(player, "k4.stats.weapon-disabled"));
			return;
		}

		if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
		{
			ShowCenterWeaponStatsMenu(player, stats);
		}
		else
		{
			ShowChatWeaponStatsMenu(player, stats);
		}
	}

	private void OnMapStatsCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null) return;

		if (!_playerStats.TryGetValue(player.SteamID, out var stats))
			return;

		if (!_coreAccessor.GetValue<bool>("Config", "EnableMapStats"))
		{
			stats.ZenithPlayer.Print(Localizer.ForPlayer(player, "k4.stats.map-disabled"));
			return;
		}

		if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
		{
			ShowCenterMapStats(player, stats);
		}
		else
		{
			ShowChatMapStats(player, stats);
		}
	}

	private void ShowCenterStatsMenu(CCSPlayerController player, PlayerStats stats)
	{
		List<MenuItem> items =
		[
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.accuracy")}:</font> {CalculateAccuracy(stats.ZenithPlayer)}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.kpr")}:</font> {CalculateKPR(stats.ZenithPlayer)}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.kda")}:</font> {CalculateKDA(stats.ZenithPlayer)}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.kd")}:</font> {CalculateKD(stats.ZenithPlayer)}")),
		];

		var statNames = new[]
		{
			"Kills", "FirstBlood", "Deaths", "Assists", "Shoots", "HitsTaken", "HitsGiven",
			"Headshots", "HeadHits", "ChestHits", "StomachHits", "LeftArmHits", "RightArmHits",
			"LeftLegHits", "RightLegHits", "NeckHits", "GearHits", "Grenades", "MVP",
			"RoundWin", "RoundLose", "GameWin", "GameLose", "RoundsOverall", "RoundsCT",
			"RoundsT", "BombPlanted", "BombDefused", "HostageRescued", "HostageKilled",
			"NoScopeKill", "PenetratedKill", "ThruSmokeKill", "FlashedKill", "DominatedKill",
			"RevengeKill", "AssistFlash"
		};

		foreach (var statName in statNames)
		{
			int value = stats.GetGlobalStat(statName);
			if (value != 0)
			{
				string localizedName = Localizer.ForPlayer(player, $"k4.stats.{statName.ToLower()}");
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{localizedName}:</font> {value:N0}")));
			}
		}

		if (items.Count == 4) // Only the initial 4 items
		{
			items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.no_stats")}</font>")));
		}

		Menu?.ShowScrollableMenu(player, Localizer.ForPlayer(player, "k4.stats.title"), items, (buttons, menu, selected) =>
		{
			// No selection handle as all items are just for display
		}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatStatsMenu(CCSPlayerController player, PlayerStats stats)
	{
		ChatMenu statsMenu = new ChatMenu(Localizer.ForPlayer(player, "k4.stats.title"));

		statsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.accuracy")}{ChatColors.Default}: {CalculateAccuracy(stats.ZenithPlayer)}", (p, o) => { }, true);
		statsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.kpr")}{ChatColors.Default}: {CalculateKPR(stats.ZenithPlayer)}", (p, o) => { }, true);
		statsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.kda")}{ChatColors.Default}: {CalculateKDA(stats.ZenithPlayer)}", (p, o) => { }, true);
		statsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.kd")}{ChatColors.Default}: {CalculateKD(stats.ZenithPlayer)}", (p, o) => { }, true);

		var statNames = new[]
		{
			"Kills", "FirstBlood", "Deaths", "Assists", "Shoots", "HitsTaken", "HitsGiven",
			"Headshots", "HeadHits", "ChestHits", "StomachHits", "LeftArmHits", "RightArmHits",
			"LeftLegHits", "RightLegHits", "NeckHits", "GearHits", "Grenades", "MVP",
			"RoundWin", "RoundLose", "GameWin", "GameLose", "RoundsOverall", "RoundsCT",
			"RoundsT", "BombPlanted", "BombDefused", "HostageRescued", "HostageKilled",
			"NoScopeKill", "PenetratedKill", "ThruSmokeKill", "FlashedKill", "DominatedKill",
			"RevengeKill", "AssistFlash"
		};

		foreach (var statName in statNames)
		{
			int value = stats.GetGlobalStat(statName);
			if (value != 0)
			{
				string localizedName = Localizer.ForPlayer(player, $"k4.stats.{statName.ToLower()}");
				statsMenu.AddMenuOption($"{ChatColors.Gold}{localizedName}{ChatColors.Default}: {value:N0}", (p, o) => { }, true);
			}
		}

		if (statsMenu.MenuOptions.Count == 4) // Only the initial 4 items
		{
			statsMenu.AddMenuOption($"{ChatColors.LightRed}{Localizer.ForPlayer(player, "k4.stats.no_stats")}", (p, o) => { }, true);
		}

		MenuManager.OpenChatMenu(player, statsMenu);
	}

	private void ShowCenterWeaponStatsMenu(CCSPlayerController player, PlayerStats stats)
	{
		List<MenuItem> items = [];
		var defaultValues = new Dictionary<int, object>();
		var weaponStatsMap = new Dictionary<int, string>();

		int index = 0;
		foreach (var weaponStat in stats.WeaponStats)
		{
			if (string.IsNullOrEmpty(weaponStat.Key)) continue;

			string weaponName = weaponStat.Key.ToUpper();
			items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(weaponName)]));
			defaultValues[index] = weaponName;
			weaponStatsMap[index] = weaponStat.Key;
			index++;
		}

		if (items.Count == 0)
		{
			items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.no_stats")}</font>")));
		}

		Menu.ShowScrollableMenu(player, Localizer.ForPlayer(player, "k4.weaponstats.title"), items, (buttons, menu, selected) =>
		{
			if (selected == null) return;

			if (buttons == MenuButtons.Select && weaponStatsMap.TryGetValue(menu.Option, out var weaponKey))
			{
				if (stats.WeaponStats.TryGetValue(weaponKey, out var weaponStat))
				{
					ShowCenterWeaponDetails(player, weaponStat);
				}
			}
		}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, defaultValues, !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatWeaponStatsMenu(CCSPlayerController player, PlayerStats stats)
	{
		ChatMenu weaponStatsMenu = new ChatMenu(Localizer.ForPlayer(player, "k4.weaponstats.title"));

		foreach (var weaponStat in stats.WeaponStats)
		{
			if (string.IsNullOrEmpty(weaponStat.Key)) continue;

			string weaponName = weaponStat.Key.ToUpper();
			weaponStatsMenu.AddMenuOption($"{ChatColors.Gold}{weaponName}", (p, o) =>
			{
				ShowChatWeaponDetails(p, weaponStat.Value);
			});
		}

		if (weaponStatsMenu.MenuOptions.Count == 0)
		{
			weaponStatsMenu.AddMenuOption($"{ChatColors.LightRed}{Localizer.ForPlayer(player, "k4.stats.no_stats")}", (p, o) => { }, true);
		}

		MenuManager.OpenChatMenu(player, weaponStatsMenu);
	}

	private void ShowCenterWeaponDetails(CCSPlayerController player, WeaponStats weaponStat)
	{
		List<MenuItem> items =
		[
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.kills")}:</font> {weaponStat.Kills:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.shoots")}:</font> {weaponStat.Shots:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.hitsgiven")}:</font> {weaponStat.Hits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.accuracy")}:</font> {(weaponStat.Shots > 0 ? Math.Min((float)weaponStat.Hits / weaponStat.Shots * 100, 100) : 0):F2}%")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.headshots")}:</font> {weaponStat.Headshots:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.chesthits")}:</font> {weaponStat.ChestHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.stomachhits")}:</font> {weaponStat.StomachHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.leftarmhits")}:</font> {weaponStat.LeftArmHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.rightarmhits")}:</font> {weaponStat.RightArmHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.leftleghits")}:</font> {weaponStat.LeftLegHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.rightleghits")}:</font> {weaponStat.RightLegHits:N0}")),
			new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.neckhits")}:</font> {weaponStat.NeckHits:N0}"))
		];

		Menu?.ShowScrollableMenu(player, weaponStat.Weapon.ToUpper(), items, (buttons, menu, selected) => { }, true, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatWeaponDetails(CCSPlayerController player, WeaponStats weaponStat)
	{
		ChatMenu detailsMenu = new ChatMenu(weaponStat.Weapon.ToUpper());

		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.kills")}{ChatColors.Default}: {weaponStat.Kills:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.shoots")}{ChatColors.Default}: {weaponStat.Shots:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.hitsgiven")}{ChatColors.Default}: {weaponStat.Hits:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.accuracy")}{ChatColors.Default}: {(weaponStat.Shots > 0 ? Math.Min((float)weaponStat.Hits / weaponStat.Shots * 100, 100) : 0):F2}%", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.headshots")}{ChatColors.Default}: {weaponStat.Headshots:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.chesthits")}{ChatColors.Default}: {weaponStat.ChestHits:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.stomachhits")}{ChatColors.Default}: {weaponStat.StomachHits:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.leftarmhits")}{ChatColors.Default}: {weaponStat.LeftArmHits:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.rightarmhits")}{ChatColors.Default}: {weaponStat.RightArmHits:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.leftleghits")}{ChatColors.Default}: {weaponStat.LeftLegHits:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.rightleghits")}{ChatColors.Default}: {weaponStat.RightLegHits:N0}", (p, o) => { }, true);
		detailsMenu.AddMenuOption($"{ChatColors.Gold}{Localizer.ForPlayer(player, "k4.stats.neckhits")}{ChatColors.Default}: {weaponStat.NeckHits:N0}", (p, o) => { }, true);

		MenuManager.OpenChatMenu(player, detailsMenu);
	}

	private void ShowCenterMapStats(CCSPlayerController player, PlayerStats playerStats)
	{
		List<MenuItem> items = [];

		var statNames = new[]
		{
			"Kills", "FirstBlood", "Deaths", "Assists", "Shoots", "HitsTaken", "HitsGiven",
			"Headshots", "HeadHits", "ChestHits", "StomachHits", "LeftArmHits", "RightArmHits",
			"LeftLegHits", "RightLegHits", "NeckHits", "GearHits", "Grenades", "MVP",
			"RoundWin", "RoundLose", "GameWin", "GameLose", "RoundsOverall", "RoundsCT",
			"RoundsT", "BombPlanted", "BombDefused", "HostageRescued", "HostageKilled",
			"NoScopeKill", "PenetratedKill", "ThruSmokeKill", "FlashedKill", "DominatedKill",
			"RevengeKill", "AssistFlash"
		};

		foreach (var statName in statNames)
		{
			int value = playerStats.GetMapStat(statName);
			if (value != 0)
			{
				string localizedName = Localizer.ForPlayer(player, $"k4.stats.{statName.ToLower()}");
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{localizedName}:</font> {value:N0}")));
			}
		}

		if (items.Count == 0)
		{
			items.Add(new MenuItem(MenuItemType.Text, new MenuValue($"<font color='#FF6666'>{Localizer.ForPlayer(player, "k4.stats.no_stats")}</font>")));
		}

		Menu?.ShowScrollableMenu(player, playerStats.CurrentMapStats.MapName.ToUpper(), items, (buttons, menu, selected) =>
		{
			// No selection handle as all items are just for display
		}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatMapStats(CCSPlayerController player, PlayerStats playerStats)
	{
		ChatMenu mapStatsMenu = new ChatMenu(playerStats.CurrentMapStats.MapName.ToUpper());

		var statNames = new[]
		{
			"Kills", "FirstBlood", "Deaths", "Assists", "Shoots", "HitsTaken", "HitsGiven",
			"Headshots", "HeadHits", "ChestHits", "StomachHits", "LeftArmHits", "RightArmHits",
			"LeftLegHits", "RightLegHits", "NeckHits", "GearHits", "Grenades", "MVP",
			"RoundWin", "RoundLose", "GameWin", "GameLose", "RoundsOverall", "RoundsCT",
			"RoundsT", "BombPlanted", "BombDefused", "HostageRescued", "HostageKilled",
			"NoScopeKill", "PenetratedKill", "ThruSmokeKill", "FlashedKill", "DominatedKill",
			"RevengeKill", "AssistFlash"
		};

		foreach (var statName in statNames)
		{
			int value = playerStats.GetMapStat(statName);
			if (value != 0)
			{
				string localizedName = Localizer.ForPlayer(player, $"k4.stats.{statName.ToLower()}");
				mapStatsMenu.AddMenuOption($"{ChatColors.Gold}{localizedName}{ChatColors.Default}: {value:N0}", (p, o) => { }, true);
			}
		}

		if (mapStatsMenu.MenuOptions.Count == 0)
		{
			mapStatsMenu.AddMenuOption($"{ChatColors.LightRed}{Localizer.ForPlayer(player, "k4.stats.no_stats")}", (p, o) => { }, true);
		}

		MenuManager.OpenChatMenu(player, mapStatsMenu);
	}

	public class EventManager
	{
		private readonly Plugin _plugin;
		public bool FirstBlood = false;

		public EventManager(Plugin plugin)
		{
			_plugin = plugin;
		}

		public void HandlePlayerDeath(EventPlayerDeath @event)
		{
			bool statsForBots = _plugin._coreAccessor.GetValue<bool>("Config", "StatsForBots");

			var victim = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var victimPlayer) ? victimPlayer : null : null;
			var attacker = @event.Attacker != null ? _plugin._playerStats.TryGetValue(@event.Attacker.SteamID, out var attackerPlayer) ? attackerPlayer : null : null;
			var assister = @event.Assister != null ? _plugin._playerStats.TryGetValue(@event.Assister.SteamID, out var assisterPlayer) ? assisterPlayer : null : null;

			if (victim != null)
			{
				if (statsForBots || attacker != null)
				{
					victim.IncrementStat("Deaths");
				}
			}

			if (attacker != null && attacker != victim)
			{
				if (statsForBots || victim != null)
				{
					attacker.IncrementStat("Kills");
					if (!FirstBlood)
					{
						FirstBlood = true;
						attacker.IncrementStat("FirstBlood");
					}
					if (@event.Noscope) attacker.IncrementStat("NoScopeKill");
					if (@event.Penetrated > 0) attacker.IncrementStat("PenetratedKill");
					if (@event.Thrusmoke) attacker.IncrementStat("ThruSmokeKill");
					if (@event.Attackerblind) attacker.IncrementStat("FlashedKill");
					if (@event.Dominated > 0) attacker.IncrementStat("DominatedKill");
					if (@event.Revenge > 0) attacker.IncrementStat("RevengeKill");
					if (@event.Headshot) attacker.IncrementStat("Headshots");

					attacker.AddWeaponKill(@event);
				}
			}

			if (assister != null)
			{
				if (statsForBots || (victim != null && attacker != null))
				{
					assister.IncrementStat("Assists");
					if (@event.Assistedflash) assister.IncrementStat("AssistFlash");
				}
			}
		}

		public void HandleGrenadeThrown(EventGrenadeThrown @event)
		{
			if (@event.Userid != null && _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var stats))
				stats.IncrementStat("Grenades");
		}

		public void HandlePlayerHurt(EventPlayerHurt @event)
		{
			var victim = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var victimPlayer) ? victimPlayer : null : null;
			var attacker = @event.Attacker != null ? _plugin._playerStats.TryGetValue(@event.Attacker.SteamID, out var attackerPlayer) ? attackerPlayer : null : null;

			victim?.IncrementStat("HitsTaken");

			if (attacker != null && attacker != victim)
				attacker.AddWeaponHit(@event.Weapon, @event.Hitgroup);
		}

		public void HandleBombPlanted(EventBombPlanted @event)
		{
			var player = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var playerStats) ? playerStats : null : null;
			player?.IncrementStat("BombPlanted");
		}

		public void HandleHostageRescued(EventHostageRescued @event)
		{
			var player = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var playerStats) ? playerStats : null : null;
			player?.IncrementStat("HostageRescued");
		}

		public void HandleHostageKilled(EventHostageKilled @event)
		{
			var player = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var playerStats) ? playerStats : null : null;
			player?.IncrementStat("HostageKilled");
		}

		public void HandleBombDefused(EventBombDefused @event)
		{
			var player = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var playerStats) ? playerStats : null : null;
			player?.IncrementStat("BombDefused");
		}

		public void HandleRoundEnd(EventRoundEnd @event)
		{
			foreach (var playerStats in _plugin._playerStats.Values)
			{
				if (!_plugin.playerSpawned.Contains(playerStats.ZenithPlayer.Controller))
					continue;

				CsTeam team = playerStats.ZenithPlayer.Controller.Team;
				if (team <= CsTeam.Spectator)
					continue;

				playerStats.IncrementStat("RoundsOverall");
				if (team == CsTeam.Terrorist)
					playerStats.IncrementStat("RoundsT");
				else if (team == CsTeam.CounterTerrorist)
					playerStats.IncrementStat("RoundsCT");

				if ((int)team == @event.Winner)
					playerStats.IncrementStat("RoundWin");
				else
					playerStats.IncrementStat("RoundLose");
			}
		}

		public void HandleWeaponFire(EventWeaponFire @event)
		{
			var player = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var playerStats) ? playerStats : null : null;
			if (player != null && !@event.Weapon.Contains("knife") && !@event.Weapon.Contains("bayonet"))
				player.AddWeaponShot(@event.Weapon);
		}

		public void HandleRoundMvp(EventRoundMvp @event)
		{
			var player = @event.Userid != null ? _plugin._playerStats.TryGetValue(@event.Userid.SteamID, out var playerStats) ? playerStats : null : null;
			player?.IncrementStat("MVP");
		}

		public void HandleCsWinPanelMatch(EventCsWinPanelMatch @event)
		{
			bool ffaMode = _plugin._coreAccessor.GetValue<bool>("Config", "FFAMode");
			var players = new List<PlayerStats>();

			foreach (var playerStat in _plugin._playerStats.Values)
			{
				if (playerStat.ZenithPlayer.Controller.IsValid && playerStat.ZenithPlayer.Controller.PlayerPawn.Value!.IsValid)
				{
					players.Add(playerStat);
				}
			}

			if (ffaMode)
			{
				HandleFFAMode(players);
			}
			else
			{
				HandleTeamMode(players);
			}
		}

		private static void HandleFFAMode(List<PlayerStats> players)
		{
			PlayerStats? winner = null;
			int highestScore = int.MinValue;

			foreach (var player in players)
			{
				int score = player.ZenithPlayer.Controller.Score;
				if (score > highestScore)
				{
					highestScore = score;
					winner = player;
				}
			}

			winner?.IncrementStat("GameWin");

			foreach (var player in players)
			{
				if (player != winner)
				{
					player.IncrementStat("GameLose");
				}
			}
		}

		private static void HandleTeamMode(List<PlayerStats> players)
		{
			int ctScore = 0;
			int tScore = 0;

			var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
			foreach (var team in teams)
			{
				if (team.Teamname == "CT")
					ctScore = team.Score;
				else if (team.Teamname == "TERRORIST")
					tScore = team.Score;
			}

			CsTeam winnerTeam = ctScore > tScore ? CsTeam.CounterTerrorist :
								tScore > ctScore ? CsTeam.Terrorist :
								CsTeam.None;

			if (winnerTeam > CsTeam.Spectator)
			{
				foreach (var player in players)
				{
					if (player.ZenithPlayer.Controller.Team > CsTeam.Spectator)
					{
						player.IncrementStat(player.ZenithPlayer.Controller.Team == winnerTeam ? "GameWin" : "GameLose");
					}
				}
			}
		}
	}

	public class PlayerStats
	{
		private readonly Plugin _plugin;
		public IPlayerServices ZenithPlayer { get; }
		public MapStats CurrentMapStats { get; private set; }
		public CounterStrikeSharp.API.Modules.Timers.Timer? SpawnMessageTimer = null;

		private readonly Dictionary<string, DateTime> _lastShotTime = [];
		private const double HIT_COOLDOWN = 0.1;

		private readonly string _steamId;
		private readonly string _currentMapName;

		public PlayerStats(IPlayerServices player, Plugin plugin)
		{
			ZenithPlayer = player;
			_plugin = plugin;
			_steamId = player.SteamID.ToString();
			_currentMapName = Server.MapName;
			CurrentMapStats = new MapStats { MapName = _currentMapName };

			if (_plugin._moduleServices == null)
				return;

			_ = Task.Run(async () =>
			{
				await LoadCurrentMapStats(_plugin._moduleServices);
				await LoadWeaponStats(_plugin._moduleServices);
			});
		}

		private async Task LoadWeaponStats(IModuleServices _moduleServices)
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableWeaponStats"))
				return;

			string connectionString = _moduleServices.GetConnectionString();
			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			string query = $@"
				SELECT `weapon`, `kills`, `shots`, `hits`, `headshots`, `chest_hits`, `stomach_hits`, `left_arm_hits`, `right_arm_hits`, `left_leg_hits`, `right_leg_hits`, `neck_hits`, `gear_hits`
				FROM `zenith_weapon_stats`
				WHERE `steam_id` = @SteamId";

			try
			{
				var results = await connection.QueryAsync<dynamic>(query, new { SteamId = _steamId });
				foreach (var result in results)
				{
					WeaponStats[result.weapon] = new WeaponStats
					{
						Weapon = result.weapon,
						Kills = result.kills,
						Shots = result.shots,
						Hits = result.hits,
						Headshots = result.headshots,
						ChestHits = result.chest_hits,
						StomachHits = result.stomach_hits,
						LeftArmHits = result.left_arm_hits,
						RightArmHits = result.right_arm_hits,
						LeftLegHits = result.left_leg_hits,
						RightLegHits = result.right_leg_hits,
						NeckHits = result.neck_hits,
						GearHits = result.gear_hits
					};
				}
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error loading weapon stats: {ex.Message}");
			}
		}

		private async Task LoadCurrentMapStats(IModuleServices _moduleServices)
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableMapStats"))
				return;

			string connectionString = _moduleServices.GetConnectionString();

			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			string query = $@"
				SELECT * FROM `zenith_map_stats`
				WHERE `steam_id` = @SteamId AND `map_name` = @MapName";

			try
			{
				var result = await connection.QueryFirstOrDefaultAsync<dynamic>(query, new { SteamId = _steamId, MapName = _currentMapName });

				if (result != null)
				{
					CurrentMapStats = new MapStats
					{
						SteamId = result.steam_id,
						MapName = result.map_name,
						Kills = result.kills,
						FirstBlood = result.first_blood,
						Deaths = result.deaths,
						Assists = result.assists,
						Shoots = result.shoots,
						HitsTaken = result.hits_taken,
						HitsGiven = result.hits_given,
						Headshots = result.headshots,
						HeadHits = result.head_hits,
						ChestHits = result.chest_hits,
						StomachHits = result.stomach_hits,
						LeftArmHits = result.left_arm_hits,
						RightArmHits = result.right_arm_hits,
						LeftLegHits = result.left_leg_hits,
						RightLegHits = result.right_leg_hits,
						NeckHits = result.neck_hits,
						GearHits = result.gear_hits,
						Grenades = result.grenades,
						MVP = result.mvp,
						RoundWin = result.round_win,
						RoundLose = result.round_lose,
						GameWin = result.game_win,
						GameLose = result.game_lose,
						RoundsOverall = result.rounds_overall,
						RoundsCT = result.rounds_ct,
						RoundsT = result.rounds_t,
						BombPlanted = result.bomb_planted,
						BombDefused = result.bomb_defused,
						HostageRescued = result.hostage_rescued,
						HostageKilled = result.hostage_killed,
						NoScopeKill = result.no_scope_kill,
						PenetratedKill = result.penetrated_kill,
						ThruSmokeKill = result.thru_smoke_kill,
						FlashedKill = result.flashed_kill,
						DominatedKill = result.dominated_kill,
						RevengeKill = result.revenge_kill,
						AssistFlash = result.assist_flash
					};
				}
				else
				{
					CurrentMapStats = new MapStats { SteamId = _steamId, MapName = _currentMapName };
				}
			}
			catch
			{
				CurrentMapStats = new MapStats { SteamId = _steamId, MapName = _currentMapName };
			}
		}

		public Dictionary<string, WeaponStats> WeaponStats { get; private set; } = [];

		private static readonly string[] sourceArray = ["m4a1", "hkp2000", "usp_silencer", "weapon_m4a1_silencer", "weapon_mp7", "weapon_mp5sd", "deagle", "revolver"];

		public int GetGlobalStat(string statName) => ZenithPlayer.GetStorage<int>(statName);

		public void SetGlobalStat(string statName, int value) => ZenithPlayer.SetStorage(statName, value);

		public int GetMapStat(string statName)
		{
			var property = typeof(MapStats).GetProperty(statName);
			return property != null ? (int)property.GetValue(CurrentMapStats)! : 0;
		}

		public void SetMapStat(string statName, int value)
		{
			var property = typeof(MapStats).GetProperty(statName);
			property?.SetValue(CurrentMapStats, value);
		}

		public void IncrementStat(string statName)
		{
			SetGlobalStat(statName, GetGlobalStat(statName) + 1);
			SetMapStat(statName, GetMapStat(statName) + 1);
		}

		public void AddWeaponKill(EventPlayerDeath @event)
		{
			string weapon = NormalizeWeaponName(@event.Weapon);

			if (weapon == "world")
				return;

			if (!WeaponStats.TryGetValue(weapon, out var weaponStat))
			{
				weaponStat = new WeaponStats { Weapon = weapon };
				WeaponStats[weapon] = weaponStat;
			}

			weaponStat.Kills++;

			if (@event.Headshot)
				weaponStat.Headshots++;
		}

		public void AddWeaponShot(string weapon)
		{
			weapon = NormalizeWeaponName(weapon);

			if (weapon == "world")
				return;

			if (!WeaponStats.TryGetValue(weapon, out var weaponStat))
			{
				weaponStat = new WeaponStats { Weapon = weapon };
				WeaponStats[weapon] = weaponStat;
			}
			weaponStat.Shots++;
			IncrementStat("Shoots");
		}

		public void AddWeaponHit(string weapon, int hitgroup)
		{
			weapon = NormalizeWeaponName(weapon);

			if (weapon == "world")
				return;

			if (!WeaponStats.TryGetValue(weapon, out var weaponStat))
			{
				weaponStat = new WeaponStats { Weapon = weapon };
				WeaponStats[weapon] = weaponStat;
			}

			if (!_lastShotTime.TryGetValue(weapon, out DateTime lastShot) || (DateTime.Now - lastShot).TotalSeconds >= HIT_COOLDOWN)
			{
				weaponStat.Hits++;
				IncrementStat("HitsGiven");

				switch ((HitGroup_t)hitgroup)
				{
					case HitGroup_t.HITGROUP_HEAD:
						weaponStat.HeadHits++;
						IncrementStat("HeadHits");
						break;
					case HitGroup_t.HITGROUP_CHEST:
						weaponStat.ChestHits++;
						IncrementStat("ChestHits");
						break;
					case HitGroup_t.HITGROUP_STOMACH:
						weaponStat.StomachHits++;
						IncrementStat("StomachHits");
						break;
					case HitGroup_t.HITGROUP_LEFTARM:
						weaponStat.LeftArmHits++;
						IncrementStat("LeftArmHits");
						break;
					case HitGroup_t.HITGROUP_RIGHTARM:
						weaponStat.RightArmHits++;
						IncrementStat("RightArmHits");
						break;
					case HitGroup_t.HITGROUP_LEFTLEG:
						weaponStat.LeftLegHits++;
						IncrementStat("LeftLegHits");
						break;
					case HitGroup_t.HITGROUP_RIGHTLEG:
						weaponStat.RightLegHits++;
						IncrementStat("RightLegHits");
						break;
					case HitGroup_t.HITGROUP_NECK:
						weaponStat.NeckHits++;
						IncrementStat("NeckHits");
						break;
					case HitGroup_t.HITGROUP_GEAR:
						weaponStat.GearHits++;
						IncrementStat("GearHits");
						break;
				}

				_lastShotTime[weapon] = DateTime.Now;
			}
		}

		private string NormalizeWeaponName(string weapon)
		{
			weapon = weapon.Replace("weapon_", string.Empty);

			if (weapon.Contains("knife") || weapon.Contains("bayonet"))
				return "knife";

			if (sourceArray.Contains(weapon))
			{
				var activeWeapon = ZenithPlayer.Controller.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
				if (activeWeapon != null)
				{
					switch (activeWeapon.AttributeManager.Item.ItemDefinitionIndex)
					{
						case 60: return "m4a1-s";
						case 61: return "usp-s";
						case 23: return "mp5-sd";
						case 64: return "revolver";
						case 63: return "cz75-auto";
					}
				}
			}

			return weapon;
		}

		public async Task SaveWeaponStats()
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableWeaponStats") || _plugin._moduleServices == null)
				return;

			try
			{
				string connectionString = _plugin._moduleServices.GetConnectionString();
				using var connection = new MySqlConnection(connectionString);
				await connection.OpenAsync();

				string query = $@"
					INSERT INTO `zenith_weapon_stats`
					(`steam_id`, `weapon`, `kills`, `shots`, `hits`, `headshots`, `chest_hits`, `stomach_hits`, `left_arm_hits`, `right_arm_hits`, `left_leg_hits`, `right_leg_hits`, `neck_hits`, `gear_hits`)
					VALUES (@SteamId, @Weapon, @Kills, @Shots, @Hits, @Headshots, @ChestHits, @StomachHits, @LeftArmHits, @RightArmHits, @LeftLegHits, @RightLegHits, @NeckHits, @GearHits)
					ON DUPLICATE KEY UPDATE
						`kills` = @Kills, `shots` = @Shots, `hits` = @Hits, `headshots` = @Headshots,
						`chest_hits` = @ChestHits, `stomach_hits` = @StomachHits, `left_arm_hits` = @LeftArmHits,
						`right_arm_hits` = @RightArmHits, `left_leg_hits` = @LeftLegHits, `right_leg_hits` = @RightLegHits,
						`neck_hits` = @NeckHits, `gear_hits` = @GearHits";

				foreach (var weaponStat in WeaponStats.Values)
				{
					await connection.ExecuteAsync(query, new
					{
						SteamId = _steamId,
						weaponStat.Weapon,
						weaponStat.Kills,
						weaponStat.Shots,
						weaponStat.Hits,
						weaponStat.Headshots,
						weaponStat.ChestHits,
						weaponStat.StomachHits,
						weaponStat.LeftArmHits,
						weaponStat.RightArmHits,
						weaponStat.LeftLegHits,
						weaponStat.RightLegHits,
						weaponStat.NeckHits,
						weaponStat.GearHits
					});
				}
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error saving weapon stats for {_steamId}: {ex.Message}");
			}

			WeaponStats.Clear();
		}

		public async Task SaveMapStats()
		{
			if (!_plugin._coreAccessor.GetValue<bool>("Config", "EnableMapStats") || _plugin._moduleServices == null)
				return;

			try
			{
				string connectionString = _plugin._moduleServices.GetConnectionString();
				using var connection = new MySqlConnection(connectionString);
				await connection.OpenAsync();

				string query = $@"
					INSERT INTO `zenith_map_stats`
					(`steam_id`, `map_name`, `kills`, `first_blood`, `deaths`, `assists`, `shoots`, `hits_taken`, `hits_given`,
					`headshots`, `head_hits`, `chest_hits`, `stomach_hits`, `left_arm_hits`, `right_arm_hits`, `left_leg_hits`, `right_leg_hits`,
					`neck_hits`, `gear_hits`, `grenades`, `mvp`, `round_win`, `round_lose`, `game_win`, `game_lose`,
					`rounds_overall`, `rounds_ct`, `rounds_t`, `bomb_planted`, `bomb_defused`, `hostage_rescued`, `hostage_killed`, `no_scope_kill`,
					`penetrated_kill`, `thru_smoke_kill`, `flashed_kill`, `dominated_kill`, `revenge_kill`, `assist_flash`)
					VALUES (@SteamId, @MapName, @Kills, @FirstBlood, @Deaths, @Assists, @Shoots, @HitsTaken, @HitsGiven,
					@Headshots, @HeadHits, @ChestHits, @StomachHits, @LeftArmHits, @RightArmHits, @LeftLegHits, @RightLegHits,
					@NeckHits, @GearHits, @Grenades, @MVP, @RoundWin, @RoundLose, @GameWin, @GameLose,
					@RoundsOverall, @RoundsCT, @RoundsT, @BombPlanted, @BombDefused, @HostageRescued, @HostageKilled, @NoScopeKill,
					@PenetratedKill, @ThruSmokeKill, @FlashedKill, @DominatedKill, @RevengeKill, @AssistFlash)
					ON DUPLICATE KEY UPDATE
					`kills` = @Kills, `first_blood` = @FirstBlood, `deaths` = @Deaths, `assists` = @Assists,
					`shoots` = @Shoots, `hits_taken` = @HitsTaken, `hits_given` = @HitsGiven, `headshots` = @Headshots,
					`head_hits` = @HeadHits, `chest_hits` = @ChestHits, `stomach_hits` = @StomachHits, `left_arm_hits` = @LeftArmHits,
					`right_arm_hits` = @RightArmHits, `left_leg_hits` = @LeftLegHits, `right_leg_hits` = @RightLegHits,
					`neck_hits` = @NeckHits, `gear_hits` = @GearHits, `grenades` = @Grenades, `mvp` = @MVP,
					`round_win` = @RoundWin, `round_lose` = @RoundLose, `game_win` = @GameWin, `game_lose` = @GameLose,
					`rounds_overall` = @RoundsOverall, `rounds_ct` = @RoundsCT, `rounds_t` = @RoundsT,
					`bomb_planted` = @BombPlanted, `bomb_defused` = @BombDefused, `hostage_rescued` = @HostageRescued,
					`hostage_killed` = @HostageKilled, `no_scope_kill` = @NoScopeKill, `penetrated_kill` = @PenetratedKill,
					`thru_smoke_kill` = @ThruSmokeKill, `flashed_kill` = @FlashedKill, `dominated_kill` = @DominatedKill,
					`revenge_kill` = @RevengeKill, `assist_flash` = @AssistFlash";

				await connection.ExecuteAsync(query, new
				{
					SteamId = _steamId,
					MapName = _currentMapName,
					CurrentMapStats.Kills,
					CurrentMapStats.FirstBlood,
					CurrentMapStats.Deaths,
					CurrentMapStats.Assists,
					CurrentMapStats.Shoots,
					CurrentMapStats.HitsTaken,
					CurrentMapStats.HitsGiven,
					CurrentMapStats.Headshots,
					CurrentMapStats.HeadHits,
					CurrentMapStats.ChestHits,
					CurrentMapStats.StomachHits,
					CurrentMapStats.LeftArmHits,
					CurrentMapStats.RightArmHits,
					CurrentMapStats.LeftLegHits,
					CurrentMapStats.RightLegHits,
					CurrentMapStats.NeckHits,
					CurrentMapStats.GearHits,
					CurrentMapStats.Grenades,
					CurrentMapStats.MVP,
					CurrentMapStats.RoundWin,
					CurrentMapStats.RoundLose,
					CurrentMapStats.GameWin,
					CurrentMapStats.GameLose,
					CurrentMapStats.RoundsOverall,
					CurrentMapStats.RoundsCT,
					CurrentMapStats.RoundsT,
					CurrentMapStats.BombPlanted,
					CurrentMapStats.BombDefused,
					CurrentMapStats.HostageRescued,
					CurrentMapStats.HostageKilled,
					CurrentMapStats.NoScopeKill,
					CurrentMapStats.PenetratedKill,
					CurrentMapStats.ThruSmokeKill,
					CurrentMapStats.FlashedKill,
					CurrentMapStats.DominatedKill,
					CurrentMapStats.RevengeKill,
					CurrentMapStats.AssistFlash
				});
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error saving map stats for {_steamId}: {ex.Message}");
			}
		}

		public void ResetStats()
		{
			foreach (var prop in typeof(MapStats).GetProperties())
			{
				if (prop.PropertyType == typeof(int))
				{
					prop.SetValue(CurrentMapStats, 0);
				}
			}
			WeaponStats.Clear();
			CurrentMapStats = new MapStats { SteamId = _steamId, MapName = _currentMapName };
		}
	}

	public class WeaponStats
	{
		public required string Weapon { get; set; }
		public int Kills { get; set; }
		public int Shots { get; set; }
		public int Hits { get; set; }
		public int Headshots { get; set; }
		public int HeadHits { get; set; }
		public int ChestHits { get; set; }
		public int StomachHits { get; set; }
		public int LeftArmHits { get; set; }
		public int RightArmHits { get; set; }
		public int LeftLegHits { get; set; }
		public int RightLegHits { get; set; }
		public int NeckHits { get; set; }
		public int GearHits { get; set; }
	}

	public class MapStats
	{
		[Column("steam_id")]
		public string SteamId { get; set; } = string.Empty;

		[Column("map_name")]
		public string MapName { get; set; } = string.Empty;

		public int Kills { get; set; }

		[Column("first_blood")]
		public int FirstBlood { get; set; }

		public int Deaths { get; set; }
		public int Assists { get; set; }
		public int Shoots { get; set; }

		[Column("hits_taken")]
		public int HitsTaken { get; set; }

		[Column("hits_given")]
		public int HitsGiven { get; set; }

		public int Headshots { get; set; }

		[Column("head_hits")]
		public int HeadHits { get; set; }

		[Column("chest_hits")]
		public int ChestHits { get; set; }

		[Column("stomach_hits")]
		public int StomachHits { get; set; }

		[Column("left_arm_hits")]
		public int LeftArmHits { get; set; }

		[Column("right_arm_hits")]
		public int RightArmHits { get; set; }

		[Column("left_leg_hits")]
		public int LeftLegHits { get; set; }

		[Column("right_leg_hits")]
		public int RightLegHits { get; set; }

		[Column("neck_hits")]
		public int NeckHits { get; set; }

		[Column("unused_hits")]
		public int UnusedHits { get; set; }

		[Column("gear_hits")]
		public int GearHits { get; set; }

		[Column("special_hits")]
		public int SpecialHits { get; set; }

		public int Grenades { get; set; }
		public int MVP { get; set; }

		[Column("round_win")]
		public int RoundWin { get; set; }

		[Column("round_lose")]
		public int RoundLose { get; set; }

		[Column("game_win")]
		public int GameWin { get; set; }

		[Column("game_lose")]
		public int GameLose { get; set; }

		[Column("rounds_overall")]
		public int RoundsOverall { get; set; }

		[Column("rounds_ct")]
		public int RoundsCT { get; set; }

		[Column("rounds_t")]
		public int RoundsT { get; set; }

		[Column("bomb_planted")]
		public int BombPlanted { get; set; }

		[Column("bomb_defused")]
		public int BombDefused { get; set; }

		[Column("hostage_rescued")]
		public int HostageRescued { get; set; }

		[Column("hostage_killed")]
		public int HostageKilled { get; set; }

		[Column("no_scope_kill")]
		public int NoScopeKill { get; set; }

		[Column("penetrated_kill")]
		public int PenetratedKill { get; set; }

		[Column("thru_smoke_kill")]
		public int ThruSmokeKill { get; set; }

		[Column("flashed_kill")]
		public int FlashedKill { get; set; }

		[Column("dominated_kill")]
		public int DominatedKill { get; set; }

		[Column("revenge_kill")]
		public int RevengeKill { get; set; }

		[Column("assist_flash")]
		public int AssistFlash { get; set; }
	}
}