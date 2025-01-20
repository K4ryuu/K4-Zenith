namespace Zenith
{
	using System.Text.Json;
	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Core.Translations;
	using CounterStrikeSharp.API.Modules.Commands;
	using CounterStrikeSharp.API.Modules.Menu;
	using CounterStrikeSharp.API.Modules.Utils;
	using Menu;
	using Menu.Enums;
	using Microsoft.Extensions.Logging;
	using Zenith.Models;

	public sealed partial class Plugin : BasePlugin
	{
		public void Initialize_Settings()
		{
			var commands = _coreAccessor.GetValue<List<string>>("Commands", "SettingsCommands");

			foreach (var command in commands)
			{
				RegisterZenithCommand($"css_{command}", "Change player Zenith settings and storage", (CCSPlayerController? player, CommandInfo commandInfo) =>
				{
					if (player == null) return;

					var zenithPlayer = Player.Find(player);
					if (zenithPlayer == null) return;

					if (GetCoreConfig<bool>("Core", "CenterMenuMode"))
					{
						CreateCenterMenu(zenithPlayer);
					}
					else
					{
						CreateChatMenu(zenithPlayer);
					}
				});
			}
		}

		public void CreateChatMenu(Player player)
		{
			ChatMenu settingsMenu = new ChatMenu(Localizer.ForPlayer(player.Controller, "k4.settings.title"));

			foreach (var moduleItem in player.Settings)
			{
				string moduleID = moduleItem.Key;
				var moduleLocalizer = Player.GetModuleLocalizer(moduleID);

				foreach (var setting in moduleItem.Value)
				{
					string key = setting.Key;
					string displayName = moduleLocalizer != null ? $"{moduleLocalizer[$"settings.{key}"]}: " : $"{moduleID}.{key}: ";

					bool currentValue = player.GetSetting<bool>(key, moduleID);
					settingsMenu.AddMenuOption($"{ChatColors.Gold}{displayName}{ChatColors.Default}{(currentValue ? $"{ChatColors.Green}✔" : $"{ChatColors.Red}✘")}",
						(p, o) =>
						{
							bool newValue = !currentValue;
							player.SetSetting(key, newValue, false, moduleID);

							string localizedValue = Localizer.ForPlayer(player.Controller, newValue ? "k4.settings.enabled" : "k4.settings.disabled");
							localizedValue = newValue ? $"{ChatColors.Lime}{localizedValue}" : $"{ChatColors.LightRed}{localizedValue}";
							player.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {localizedValue}");
						}
					);
				}
			}

			MenuManager.OpenChatMenu(player.Controller!, settingsMenu);
		}

		public void CreateCenterMenu(Player player)
		{
			var items = new List<MenuItem>();
			var defaultValues = new Dictionary<int, object>();
			var dataMap = new Dictionary<int, (string ModuleID, string Key)>();

			int index = 0;

			foreach (var moduleItem in player.Settings)
			{
				string moduleID = moduleItem.Key;
				var moduleLocalizer = Player.GetModuleLocalizer(moduleID);

				foreach (var setting in moduleItem.Value)
				{
					string key = setting.Key;
					var defaultValue = setting.Value;
					var currentValue = player.GetSetting<object>(key, moduleID);
					currentValue ??= defaultValue;

					var displayName = moduleLocalizer != null ? $"{moduleLocalizer[$"settings.{key}"]}: " : $"{moduleID}.{key}: ";

					switch (currentValue)
					{
						case bool boolValue:
							items.Add(new MenuItem(MenuItemType.Bool, new MenuValue(displayName)));
							defaultValues[index] = boolValue;
							break;
						case JsonElement jsonElement:
							switch (jsonElement.ValueKind)
							{
								case JsonValueKind.True:
								case JsonValueKind.False:
									items.Add(new MenuItem(MenuItemType.Bool, new MenuValue(displayName)));
									defaultValues[index] = jsonElement.GetBoolean();
									break;
								default:
									Logger.LogWarning($"Unsupported JsonElement type for {moduleID}.{key}: {jsonElement.ValueKind} ({jsonElement.GetRawText()})");
									continue;
							}
							break;
						default:
							Logger.LogWarning($"Unknown setting type for {moduleID}.{key} ({currentValue?.GetType().Name ?? "null"})");
							continue;
					}

					dataMap[index] = (moduleID, key);
					index++;
				}
			}

			Menu?.ShowScrollableMenu(player.Controller!, Localizer.ForPlayer(player.Controller, "k4.settings.title"), items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				if (dataMap.TryGetValue(menu.Option, out var dataInfo))
				{
					string moduleID = dataInfo.ModuleID;
					string key = dataInfo.Key;
					var moduleLocalizer = Player.GetModuleLocalizer(moduleID);

					switch (buttons)
					{
						case MenuButtons.Select:
							if (selected.Type == MenuItemType.Bool)
							{
								bool newBoolValue = selected.Data[0] == 1;
								player.SetSetting(key, newBoolValue, false, moduleID);
								string localizedValue = Localizer.ForPlayer(player.Controller, newBoolValue ? "k4.settings.enabled" : "k4.settings.disabled");
								localizedValue = newBoolValue ? $"{ChatColors.Lime}{localizedValue}" : $"{ChatColors.LightRed}{localizedValue}";
								player.Print($"{moduleLocalizer?[$"settings.{key}"] ?? key}: {localizedValue}");
							}
							break;
					}
				}
			}, false, GetCoreConfig<bool>("Core", "FreezeInMenu") && player.GetSetting<bool>("FreezeInMenu", "K4-Zenith"), 5, defaultValues, !GetCoreConfig<bool>("Core", "ShowDevelopers"));
		}
	}
}
