using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using ZenithAPI;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Commands;
using Menu;
using Menu.Enums;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Translations;

namespace Zenith_CustomTags;

[MinimumApiVersion(260)]
public class Plugin : BasePlugin
{
	private const string MODULE_ID = "CustomTags";

	private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.9";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;

	private readonly Dictionary<CCSPlayerController, IPlayerServices> _playerCache = [];

	public KitsuneMenu Menu { get; private set; } = null!;
	public IModuleConfigAccessor _coreAccessor = null!;

	private Dictionary<string, TagConfig>? _tagConfigs;
	private Dictionary<string, PredefinedTagConfig>? _predefinedConfigs;

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

		Menu = new KitsuneMenu(this);
		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		_moduleServices!.RegisterModuleStorage(new Dictionary<string, object?>
		{
			{ "ChoosenTag", "Default" },
		});

		EnsureConfigFileExists();
		EnsurePredefinedConfigFileExists();

		_moduleServices?.RegisterModuleCommands(["tags", "tag"], "Change player tag configuration", (player, info) =>
		{
			if (player == null) return;
			ShowTagSelectionMenu(player);
		}, CommandUsage.CLIENT_ONLY);

		if (hotReload)
		{
			_moduleServices?.LoadAllOnlinePlayerData();

			var players = Utilities.GetPlayers();
			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
					OnZenithPlayerLoaded(player);
			}
		}

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void ShowTagSelectionMenu(CCSPlayerController player)
	{
		_tagConfigs ??= GetTagConfigs();
		_predefinedConfigs ??= GetPredefinedTagConfigs();

		List<MenuItem> items = [];
		List<string> configKeys = [];
		HashSet<string> availableConfigs = new HashSet<string>();

		if (_tagConfigs.TryGetValue("all", out var allConfig) && allConfig.AvailableConfigs != null)
		{
			availableConfigs.UnionWith(allConfig.AvailableConfigs);
		}

		bool hasCustomConfig = false;
		if (_tagConfigs.TryGetValue(player.SteamID.ToString(), out var playerConfig))
		{
			hasCustomConfig = playerConfig.ChatColor != null || playerConfig.ClanTag != null ||
							  playerConfig.NameColor != null || playerConfig.NameTag != null;

			if (playerConfig.AvailableConfigs != null)
			{
				availableConfigs.UnionWith(playerConfig.AvailableConfigs);
			}
		}

		foreach (var kvp in _tagConfigs)
		{
			if (CheckPermissionOrSteamID(player, kvp.Key) && kvp.Value.AvailableConfigs != null)
			{
				availableConfigs.UnionWith(kvp.Value.AvailableConfigs);
			}
		}

		items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer.ForPlayer(player, "customtags.menu.none"))]));
		configKeys.Add("none");

		if (hasCustomConfig)
		{
			items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(Localizer.ForPlayer(player, "customtags.menu.default"))]));
			configKeys.Add("default");
		}

		foreach (var configName in availableConfigs)
		{
			if (_predefinedConfigs.TryGetValue(configName, out var config))
			{
				items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(config.Name)]));
				configKeys.Add(configName);
			}
		}

		if (items.Count == 1)
			items.Clear();

		try
		{
			if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
			{
				ShowCenterTagSelectionMenu(player, items, configKeys);
			}
			else
			{
				ShowChatTagSelectionMenu(player, configKeys);
			}
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error showing tag selection menu: {ex.Message}");
		}
	}

	private void ShowCenterTagSelectionMenu(CCSPlayerController player, List<MenuItem> items, List<string> configKeys)
	{
		if (Menu == null)
		{
			Logger.LogError("Menu object is null. Cannot show center tag selection menu.");
			return;
		}

		Menu.ShowScrollableMenu(player, Localizer.ForPlayer(player, "customtags.menu.title"), items, (buttons, menu, selected) =>
		{
			if (selected == null) return;

			if (menu.Option >= 0 && menu.Option < configKeys.Count)
			{
				string selectedConfigKey = configKeys[menu.Option];

				if (buttons == MenuButtons.Select)
				{
					ApplySelectedConfig(player, selectedConfigKey);
				}
			}
		}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatTagSelectionMenu(CCSPlayerController player, List<string> configKeys)
	{
		ChatMenu tagMenu = new ChatMenu(Localizer.ForPlayer(player, "customtags.menu.title"));

		foreach (var configKey in configKeys)
		{
			string displayName = configKey;
			if (_predefinedConfigs?.TryGetValue(configKey, out var config) == true)
			{
				displayName = config.Name;
			}
			else if (configKey == "none")
			{
				displayName = Localizer.ForPlayer(player, "customtags.menu.none");
			}
			else if (configKey == "default")
			{
				displayName = Localizer.ForPlayer(player, "customtags.menu.default");
			}

			tagMenu.AddMenuOption($"{ChatColors.Gold}{displayName}", (p, o) =>
			{
				ApplySelectedConfig(p, configKey);
			});
		}

		MenuManager.OpenChatMenu(player, tagMenu);
	}

	private void ApplySelectedConfig(CCSPlayerController player, string selectedConfigKey)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer == null)
		{
			Logger.LogError($"Failed to get player services for {player.PlayerName}");
			return;
		}

		if (selectedConfigKey == "default")
		{
			zenithPlayer.SetStorage("ChoosenTag", "Default");
			ApplyTagConfig(player);
			_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "customtags.applied.default"));
		}
		else if (selectedConfigKey == "none")
		{
			zenithPlayer.SetStorage("ChoosenTag", "None");
			ApplyNullConfig(zenithPlayer);
			_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "customtags.applied.none"));
		}
		else if (_predefinedConfigs?.TryGetValue(selectedConfigKey, out var selectedPredefinedConfig) == true)
		{
			zenithPlayer.SetStorage("ChoosenTag", selectedConfigKey);
			ApplyConfig(zenithPlayer, selectedPredefinedConfig);
			_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "customtags.applied.config", selectedPredefinedConfig.Name));
		}
		else
		{
			_moduleServices?.PrintForPlayer(player, $"Invalid tag configuration: {selectedConfigKey}");
		}
	}

	private void EnsureConfigFileExists()
	{
		string configPath = Path.Combine(ModuleDirectory, "tags.json");
		if (!File.Exists(configPath))
		{
			var defaultConfig = new Dictionary<string, TagConfig>
			{
				["all"] = new TagConfig
				{
					ClanTag = "Player | ",
					NameColor = "white",
					NameTag = "{white}[Player] ",
					AvailableConfigs = ["player"]
				},
				["@zenith/root"] = new TagConfig
				{
					ChatColor = "lightred",
					ClanTag = "OWNER | ",
					NameColor = "lightred",
					NameTag = "{lightred}[OWNER] ",
					AvailableConfigs = ["owner"]
				},
				["@css/admin"] = new TagConfig
				{
					ClanTag = "ADMIN | ",
					NameColor = "blue",
					NameTag = "{blue}[ADMIN] ",
					AvailableConfigs = ["admin"]
				},
				["76561198345583467"] = new TagConfig
				{
					ChatColor = "gold",
					ClanTag = "Zenith | ",
					NameColor = "gold",
					NameTag = "{gold}[Zenith] ",
					AvailableConfigs = ["vip", "donator"]
				}
			};

			var jsonConfig = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
			var jsonWithComments = @"// This configuration file defines tag settings for players.
// You can use the following keys to target specific players or groups:
// - ""all"": Applies to all players
// - ""#GroupName"": Applies to players in a specific group (e.g., ""#Owner"", ""#Admin"")
// - ""@Permission"": Applies to players with a specific permission (e.g., ""@css/admin"")
// - ""SteamID"": Applies to a specific player by their Steam ID

" + jsonConfig;

			File.WriteAllText(configPath, jsonWithComments);
		}
	}

	private void EnsurePredefinedConfigFileExists()
	{
		string configPath = Path.Combine(ModuleDirectory, "predefined_tags.json");
		if (!File.Exists(configPath))
		{
			var defaultConfig = new Dictionary<string, PredefinedTagConfig>
			{
				["player"] = new PredefinedTagConfig
				{
					Name = "Player",
					ChatColor = "white",
					ClanTag = "Player | ",
					NameColor = "white",
					NameTag = "{white}[Player] "
				},
				["owner"] = new PredefinedTagConfig
				{
					Name = "Owner",
					ChatColor = "lightred",
					ClanTag = "OWNER | ",
					NameColor = "lightred",
					NameTag = "{lightred}[OWNER] "
				},
				["admin"] = new PredefinedTagConfig
				{
					Name = "Admin",
					ChatColor = "blue",
					ClanTag = "ADMIN | ",
					NameColor = "blue",
					NameTag = "{blue}[ADMIN] "
				},
				["vip"] = new PredefinedTagConfig
				{
					Name = "VIP",
					ChatColor = "gold",
					ClanTag = "VIP | ",
					NameColor = "gold",
					NameTag = "{gold}[VIP] "
				},
				["donator"] = new PredefinedTagConfig
				{
					Name = "Donator",
					ChatColor = "green",
					ClanTag = "DONATOR | ",
					NameColor = "green",
					NameTag = "{green}[DONATOR] "
				}
			};

			var jsonConfig = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
			var jsonWithComments = @"// This configuration file defines predefined tag configurations that can be applied to players.
// These configurations can be referenced in the 'AvailableConfigs' list in the main tags.json file.

" + jsonConfig;

			File.WriteAllText(configPath, jsonWithComments);
		}
	}

	private Dictionary<string, TagConfig> GetTagConfigs()
	{
		try
		{
			string configPath = Path.Combine(ModuleDirectory, "tags.json");
			string json = File.ReadAllText(configPath);
			string strippedJson = StripComments(json);
			return JsonSerializer.Deserialize<Dictionary<string, TagConfig>>(strippedJson, _jsonOptions) ?? [];
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error reading tag configs: {ex.Message}");
			return [];
		}
	}

	private Dictionary<string, PredefinedTagConfig> GetPredefinedTagConfigs()
	{
		try
		{
			string configPath = Path.Combine(ModuleDirectory, "predefined_tags.json");
			string json = File.ReadAllText(configPath);
			string strippedJson = StripComments(json);
			return JsonSerializer.Deserialize<Dictionary<string, PredefinedTagConfig>>(strippedJson, _jsonOptions) ?? [];
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error reading predefined tag configs: {ex.Message}");
			return [];
		}
	}

	private static string StripComments(string json)
	{
		if (string.IsNullOrEmpty(json))
			return json;

		var result = new System.Text.StringBuilder(json.Length);
		using (var reader = new StringReader(json))
		{
			string? line;
			while ((line = reader.ReadLine()) != null)
			{
				string trimmedLine = line.TrimStart();
				if (!trimmedLine.StartsWith("//"))
				{
					result.AppendLine(line);
				}
			}
		}
		return result.ToString();
	}

	private void ApplyTagConfig(CCSPlayerController player)
	{
		try
		{
			var zenithPlayer = GetZenithPlayer(player);
			if (zenithPlayer == null)
			{
				Logger.LogError($"Failed to get player services for {player.PlayerName}");
				return;
			}

			_tagConfigs ??= GetTagConfigs();
			_predefinedConfigs ??= GetPredefinedTagConfigs();

			string choosenTag = zenithPlayer.GetStorage<string>("ChoosenTag") ?? "Default";

			if (choosenTag == "None")
			{
				ApplyNullConfig(zenithPlayer);
				return;
			}

			if (choosenTag != "Default" && _predefinedConfigs.TryGetValue(choosenTag, out var chosenPredefinedConfig))
			{
				bool existsForUser = false;
				foreach (var kvp in _tagConfigs)
				{
					if (CheckPermissionOrSteamID(player, kvp.Key) && kvp.Value.AvailableConfigs != null && kvp.Value.AvailableConfigs.Contains(choosenTag))
					{
						ApplyConfig(zenithPlayer, chosenPredefinedConfig);
						existsForUser = true;
					}
				}

				if (!existsForUser)
				{
					zenithPlayer.SetStorage("ChoosenTag", "Default");
				}
				else
					return;
			}

			bool configApplied = false;
			List<string> availableConfigs = [];

			if (_tagConfigs.TryGetValue("all", out var allConfig))
			{
				if (HasTagConfigValues(allConfig))
				{
					ApplyConfig(zenithPlayer, allConfig);
					configApplied = true;
				}
				if (allConfig.AvailableConfigs != null)
				{
					availableConfigs.AddRange(allConfig.AvailableConfigs);
				}
			}

			foreach (var kvp in _tagConfigs)
			{
				if (kvp.Key == "all")
					continue;

				if (CheckPermissionOrSteamID(player, kvp.Key))
				{
					var config = kvp.Value;
					if (HasTagConfigValues(config))
					{
						ApplyConfig(zenithPlayer, config);
						configApplied = true;
					}
					if (config.AvailableConfigs != null)
					{
						availableConfigs.AddRange(config.AvailableConfigs);
					}
					break;
				}
			}

			if (!configApplied && availableConfigs.Count > 0)
			{
				foreach (var configName in availableConfigs)
				{
					if (_predefinedConfigs.TryGetValue(configName, out var availablePredefinedConfig))
					{
						ApplyConfig(zenithPlayer, availablePredefinedConfig);
						_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "customtags.applied.default_predefined", availablePredefinedConfig.Name));
						zenithPlayer.SetStorage("ChoosenTag", configName);
						configApplied = true;
						break;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Logger.LogError($"Error applying tag config for player {player.PlayerName}: {ex.Message}");
		}
	}

	private static bool HasTagConfigValues(TagConfig config)
	{
		return !string.IsNullOrEmpty(config.ChatColor) ||
			   !string.IsNullOrEmpty(config.ClanTag) ||
			   !string.IsNullOrEmpty(config.NameColor) ||
			   !string.IsNullOrEmpty(config.NameTag);
	}

	private static bool CheckPermissionOrSteamID(CCSPlayerController player, string key)
	{
		if (key.StartsWith('#'))
		{
			return AdminManager.PlayerInGroup(player, key);
		}

		AdminData? adminData = AdminManager.GetPlayerAdminData(player);
		if (adminData != null)
		{
			string permissionKey = key.StartsWith('@') ? key : "@" + key;
			if (adminData.Flags.Any(flagEntry =>
				flagEntry.Value.Contains(permissionKey, StringComparer.OrdinalIgnoreCase) ||
				flagEntry.Value.Any(flag => permissionKey.StartsWith(flag, StringComparison.OrdinalIgnoreCase))))
			{
				return true;
			}
		}

		return SteamID.TryParse(key, out SteamID? keySteamID) &&
			   keySteamID != null &&
			   Equals(keySteamID, new SteamID(player.SteamID));
	}

	private static void ApplyConfig(IPlayerServices zenithPlayer, TagConfig config)
	{
		if (!string.IsNullOrEmpty(config.ChatColor))
			zenithPlayer.SetChatColor(config.ChatColor);

		if (!string.IsNullOrEmpty(config.ClanTag))
			zenithPlayer.SetClanTag(config.ClanTag);

		if (!string.IsNullOrEmpty(config.NameColor))
			zenithPlayer.SetNameColor(config.NameColor);

		if (!string.IsNullOrEmpty(config.NameTag))
			zenithPlayer.SetNameTag(config.NameTag);
	}

	private static void ApplyConfig(IPlayerServices zenithPlayer, PredefinedTagConfig config)
	{
		if (!string.IsNullOrEmpty(config.ChatColor))
			zenithPlayer.SetChatColor(config.ChatColor);

		if (!string.IsNullOrEmpty(config.ClanTag))
			zenithPlayer.SetClanTag(config.ClanTag);

		if (!string.IsNullOrEmpty(config.NameColor))
			zenithPlayer.SetNameColor(config.NameColor);

		if (!string.IsNullOrEmpty(config.NameTag))
			zenithPlayer.SetNameTag(config.NameTag);
	}

	private static void ApplyNullConfig(IPlayerServices player)
	{
		player.SetChatColor(null);
		player.SetClanTag(null);
		player.SetNameColor(null);
		player.SetNameTag(null);
	}

	private void OnZenithPlayerLoaded(CCSPlayerController player)
	{
		var zenithPlayer = GetZenithPlayer(player);
		if (zenithPlayer == null)
		{
			Logger.LogError($"Failed to get player services for {player.PlayerName}");
			return;
		}

		_playerCache[player] = zenithPlayer;
		ApplyTagConfig(player);
	}

	private void OnZenithPlayerUnloaded(CCSPlayerController player)
	{
		_playerCache.Remove(player);
	}

	public override void Unload(bool hotReload)
	{
		_playerCache.Clear();

		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}

	private void OnZenithCoreUnload(bool hotReload)
	{
		_playerCache.Clear();

		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(ModulePath, DateTime.Now); }
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

public class TagConfig
{
	public string? ChatColor { get; set; }
	public string? ClanTag { get; set; }
	public string? NameColor { get; set; }
	public string? NameTag { get; set; }
	public List<string> AvailableConfigs { get; set; } = [];
}

public class PredefinedTagConfig
{
	public string Name { get; set; } = "";
	public string? ChatColor { get; set; }
	public string? ClanTag { get; set; }
	public string? NameColor { get; set; }
	public string? NameTag { get; set; }
}