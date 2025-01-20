using System.Collections.Concurrent;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Dapper;
using Menu;
using Menu.Enums;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ZenithAPI;

namespace Zenith_TopLists;

[MinimumApiVersion(260)]
public class TopListsPlugin : BasePlugin
{
	private const string MODULE_ID = "Toplists";
	public const int DEFAULT_PLAYER_COUNT = 5;

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.6";

	private PluginCapability<IModuleServices>? _moduleServicesCapability;
	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	public IModuleServices? ModuleServices { get; private set; }
	public IZenithEvents? ZenithEvents { get; private set; }
	public IModuleConfigAccessor? CoreAccessor { get; private set; }

	public RankTopHandler? RankTopHandler { get; private set; }
	public TimeTopHandler? TimeTopHandler { get; private set; }
	public StatsTopHandler? StatsTopHandler { get; private set; }

	private readonly ConcurrentDictionary<ulong, Tuple<long, DateTime>> _topPlacementCache = new();
	private DateTime _topPlacementCacheTriggered = DateTime.MinValue;

	public KitsuneMenu? menu { get; private set; }

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		try
		{
			_moduleServicesCapability = new("zenith:module-services");
			_playerServicesCapability = new("zenith:player-services");

			ModuleServices = _moduleServicesCapability.Get();
			if (ModuleServices == null)
			{
				throw new Exception("Failed to get Module-Services API for Zenith.");
			}

			ZenithEvents = ModuleServices.GetEventHandler();
			if (ZenithEvents != null)
			{
				ZenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
				ZenithEvents.OnZenithPlayerLoaded += player => CacheTopPlacements();
			}
			else
			{
				Logger.LogError("Failed to get Zenith event handler.");
			}

			menu = new KitsuneMenu(this);

			CoreAccessor = ModuleServices.GetModuleConfigAccessor();

			ModuleServices.RegisterModuleConfig("Commands", "GeneralTopCommands", "Commands to use the general toplists", new List<string> { "top", "toplist" });
			ModuleServices.RegisterModuleConfig("Commands", "RankTopCommands", "Commands to use the rank toplists", new List<string> { "ranktop", "rtop" });
			ModuleServices.RegisterModuleConfig("Commands", "TimeTopCommands", "Commands to use the time toplists", new List<string> { "timetop", "ttop" });
			ModuleServices.RegisterModuleConfig("Commands", "StatsTopCommands", "Commands to use the statistic toplists", new List<string> { "stattop", "statstop", "stop" });

			RankTopHandler = new RankTopHandler(this);
			TimeTopHandler = new TimeTopHandler(this);
			StatsTopHandler = new StatsTopHandler(this);

			ModuleServices.RegisterModuleCommands(CoreAccessor.GetValue<List<string>>("Commands", "GeneralTopCommands"), "Shows the toplist main menu", OnTopCommand, CommandUsage.CLIENT_ONLY);
			ModuleServices.RegisterModuleCommands(CoreAccessor.GetValue<List<string>>("Commands", "RankTopCommands"), "Shows the top players by ranks", OnRankTopCommand, CommandUsage.CLIENT_ONLY);
			ModuleServices.RegisterModuleCommands(CoreAccessor.GetValue<List<string>>("Commands", "TimeTopCommands"), "Show top players by playtime", OnTimeTopCommand, CommandUsage.CLIENT_ONLY);
			ModuleServices.RegisterModuleCommands(CoreAccessor.GetValue<List<string>>("Commands", "StatsTopCommands"), "Show top players by statistics", OnStatsTopCommand, CommandUsage.CLIENT_ONLY);

			AddTimer(60.0f, CacheTopPlacements, TimerFlags.REPEAT);

			ModuleServices!.RegisterModulePlayerPlaceholder("rank_top_placement", p =>
			{
				if (p == null) return "N/A";

				var steamId = p.SteamID;

				if (_topPlacementCache.TryGetValue(steamId, out var cachedData))
				{
					return cachedData.Item1.ToString();
				}

				return "N/A";
			});

			Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to initialize Zenith API: {ex.Message}");
			Logger.LogInformation("Please check if Zenith is installed, configured and loaded correctly.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
		}
	}

	private void CacheTopPlacements()
	{
		if ((DateTime.UtcNow - _topPlacementCacheTriggered).TotalSeconds < 3)
			return;

		var onlinePlayers = Utilities.GetPlayers()
			.Where(p => p != null && p.IsValid && !p.IsBot && !p.IsHLTV)
			.ToList();

		if (onlinePlayers.Count == 0)
			return;

		_topPlacementCacheTriggered = DateTime.UtcNow;

		Task.Run(async () =>
		{
			try
			{
				string? connectionString = ModuleServices?.GetConnectionString();
				if (string.IsNullOrEmpty(connectionString))
				{
					throw new InvalidOperationException("Database connection string is null or empty.");
				}

				using var connection = new MySqlConnection(connectionString);
				await connection.OpenAsync();

				var steamIds = onlinePlayers.Select(p => p.SteamID.ToString()).ToList();

				const string query = @"
					SELECT
						t1.steam_id,
						(SELECT COUNT(*) + 1
						FROM zenith_player_storage t2
						WHERE CAST(JSON_EXTRACT(t2.`K4-Zenith-Ranks.storage`, '$.Points') AS DECIMAL(10,2)) >
							CAST(JSON_EXTRACT(t1.`K4-Zenith-Ranks.storage`, '$.Points') AS DECIMAL(10,2))
						) as rank_position
					FROM zenith_player_storage t1
					WHERE
						FIND_IN_SET(t1.steam_id, @SteamIds) > 0
						AND JSON_VALID(t1.`K4-Zenith-Ranks.storage`) = 1
						AND JSON_EXTRACT(t1.`K4-Zenith-Ranks.storage`, '$.Points') IS NOT NULL";

				string steamIdString = string.Join(",", steamIds);

				var results = await connection.QueryAsync<(string SteamId, long Placement)>(
					query,
					new { SteamIds = steamIdString }
				);

				foreach (var (SteamId, Placement) in results)
				{
					if (onlinePlayers.Any(p => p.SteamID.ToString() == SteamId))
					{
						var steamId = onlinePlayers.First(p => p.SteamID.ToString() == SteamId).SteamID;
						_topPlacementCache[steamId] = Tuple.Create(Placement, DateTime.UtcNow);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogError("Failed to cache top placements: {Error}", ex.Message);
			}
		});
	}

	private void OnTopCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || RankTopHandler == null) return;

		if (CoreAccessor?.GetValue<bool>("Core", "CenterMenuMode") == true)
		{
			ShowCenterMainTopMenu(player);
		}
		else
		{
			ShowChatMainTopMenu(player);
		}
	}

	private void ShowCenterMainTopMenu(CCSPlayerController player)
	{
		var items = new List<MenuItem>
	{
		new MenuItem(MenuItemType.Button, [new MenuValue(Localizer.ForPlayer(player, "top.menu.rank"))]),
		new MenuItem(MenuItemType.Button, [new MenuValue(Localizer.ForPlayer(player, "top.menu.time"))]),
		new MenuItem(MenuItemType.Button, [new MenuValue(Localizer.ForPlayer(player, "top.menu.stats"))])
	};

		menu?.ShowScrollableMenu(player, Localizer.ForPlayer(player, "top.menu.main.title"), items, (buttons, menu, selected) =>
		{
			if (selected == null) return;

			if (buttons == MenuButtons.Select)
			{
				switch (menu.Option)
				{
					case 0:
						RankTopHandler?.HandleRankTopCommand(player);
						break;
					case 1:
						TimeTopHandler?.HandleTimeTopCommand(player);
						break;
					case 2:
						StatsTopHandler?.HandleStatsTopCommand(player);
						break;
				}
			}
		}, false, CoreAccessor!.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, disableDeveloper: !CoreAccessor!.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatMainTopMenu(CCSPlayerController player)
	{
		var chatMenu = new ChatMenu(Localizer.ForPlayer(player, "top.menu.main.title"));

		chatMenu.AddMenuOption(Localizer.ForPlayer(player, "top.menu.rank"), (p, _) =>
		{
			RankTopHandler?.HandleRankTopCommand(p);
		});

		chatMenu.AddMenuOption(Localizer.ForPlayer(player, "top.menu.time"), (p, _) =>
		{
			TimeTopHandler?.HandleTimeTopCommand(p);
		});

		chatMenu.AddMenuOption(Localizer.ForPlayer(player, "top.menu.stats"), (p, _) =>
		{
			StatsTopHandler?.HandleStatsTopCommand(p);
		});

		MenuManager.OpenChatMenu(player, chatMenu);
	}

	private void OnRankTopCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || RankTopHandler == null) return;
		RankTopHandler.HandleRankTopCommand(player, command);
	}

	private void OnTimeTopCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || TimeTopHandler == null) return;
		TimeTopHandler.HandleTimeTopCommand(player, command);
	}

	private void OnStatsTopCommand(CCSPlayerController? player, CommandInfo command)
	{
		if (player == null || StatsTopHandler == null) return;
		StatsTopHandler.HandleStatsTopCommand(player, command);
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

	public override void Unload(bool hotReload)
	{
		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}

	public static string TruncateString(string input, int maxLength = 12)
	{
		if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
			return input;

		return string.Concat(input.AsSpan(0, maxLength), "...");
	}

	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}
}
