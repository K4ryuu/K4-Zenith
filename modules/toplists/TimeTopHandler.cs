using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using Dapper;
using MySqlConnector;
using Menu;
using Menu.Enums;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Translations;

namespace Zenith_TopLists;

public class TimeTopHandler
{
	private readonly TopListsPlugin _plugin;

	private enum TimeCategory
	{
		TotalPlaytime,
		TerroristPlaytime,
		CounterTerroristPlaytime,
		SpectatorPlaytime,
		AlivePlaytime,
		DeadPlaytime
	}

	public TimeTopHandler(TopListsPlugin plugin)
	{
		_plugin = plugin;
	}

	public void HandleTimeTopCommand(CCSPlayerController player, CommandInfo? command = null)
	{
		int playerCount = TopListsPlugin.DEFAULT_PLAYER_COUNT;
		if (command?.ArgCount > 1)
		{
			if (int.TryParse(command.ArgByIndex(1), out int customCount))
			{
				playerCount = Math.Max(1, Math.Min(customCount, 100)); // Limit between 1 and 100
			}
			else
			{
				_plugin.ModuleServices?.PrintForPlayer(player, _plugin.Localizer.ForPlayer(player, "timetop.invalid.count", TopListsPlugin.DEFAULT_PLAYER_COUNT));
			}
		}

		ShowTimeTopCategoryMenu(player, playerCount, command is null);
	}

	private void ShowTimeTopCategoryMenu(CCSPlayerController player, int playerCount, bool subMenu)
	{
		if (_plugin.CoreAccessor?.GetValue<bool>("Core", "CenterMenuMode") == true)
		{
			ShowCenterTimeTopCategoryMenu(player, playerCount, subMenu);
		}
		else
		{
			ShowChatTimeTopCategoryMenu(player, playerCount);
		}
	}

	private void ShowCenterTimeTopCategoryMenu(CCSPlayerController player, int playerCount, bool subMenu)
	{
		var items = Enum.GetValues(typeof(TimeCategory))
			.Cast<TimeCategory>()
			.Select(category => new MenuItem(MenuItemType.Button, [new MenuValue(_plugin.Localizer.ForPlayer(player, $"timetop.category.{category}"))]))
			.ToList();

		_plugin.menu?.ShowScrollableMenu(player, _plugin.Localizer.ForPlayer(player, "timetop.category.menu.title"), items, (buttons, menu, selected) =>
		{
			if (selected == null) return;

			if (menu.Option >= 0 && menu.Option < Enum.GetValues(typeof(TimeCategory)).Length)
			{
				TimeCategory selectedCategory = (TimeCategory)menu.Option;

				if (buttons == MenuButtons.Select)
				{
					ShowTimeTopMenu(player, selectedCategory, playerCount);
				}
			}
		}, subMenu, _plugin.CoreAccessor!.GetValue<bool>("Core", "FreezeInMenu") && (_plugin.GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, disableDeveloper: !_plugin.CoreAccessor!.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatTimeTopCategoryMenu(CCSPlayerController player, int playerCount)
	{
		var chatMenu = new ChatMenu(_plugin.Localizer.ForPlayer(player, "timetop.category.menu.title"));

		foreach (TimeCategory category in Enum.GetValues(typeof(TimeCategory)))
		{
			chatMenu.AddMenuOption(_plugin.Localizer.ForPlayer(player, $"timetop.category.{category}"), (p, _) =>
			{
				ShowTimeTopMenu(p, category, playerCount);
			});
		}

		MenuManager.OpenChatMenu(player, chatMenu);
	}

	private void ShowTimeTopMenu(CCSPlayerController player, TimeCategory category, int playerCount)
	{
		Task.Run(async () =>
		{
			var topPlayers = await GetTopPlayersTimeAsync(category, playerCount);

			Server.NextWorldUpdate(() =>
			{
				if (topPlayers.Count == 0)
				{
					_plugin.ModuleServices?.PrintForPlayer(player, _plugin.Localizer.ForPlayer(player, "timetop.no.data"));
					return;
				}

				try
				{
					if (_plugin.CoreAccessor?.GetValue<bool>("Core", "CenterMenuMode") == true)
					{
						ShowCenterTimeTopMenu(player, topPlayers);
					}
					else
					{
						ShowChatTimeTopMenu(player, topPlayers);
					}
				}
				catch (Exception ex)
				{
					_plugin.Logger.LogError($"Error showing time top menu: {ex.Message}");
				}
			});
		});
	}

	private void ShowCenterTimeTopMenu(CCSPlayerController player, List<(string Name, double Time)> topPlayers)
	{
		var items = topPlayers.Select((p, index) => new MenuItem(MenuItemType.Button, [new MenuValue(_plugin.Localizer.ForPlayer(player, "timetop.player.entry.center", index + 1, p.Name, FormatTime(player, p.Time)))])).ToList();

		_plugin.menu?.ShowScrollableMenu(player, _plugin.Localizer.ForPlayer(player, "top.menu.title", topPlayers.Count), items, (_, _, _) => { }, true, _plugin.CoreAccessor!.GetValue<bool>("Core", "FreezeInMenu") && (_plugin.GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, disableDeveloper: !_plugin.CoreAccessor!.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatTimeTopMenu(CCSPlayerController player, List<(string Name, double Time)> topPlayers)
	{
		var chatMenu = new ChatMenu(_plugin.Localizer.ForPlayer(player, "top.menu.title", topPlayers.Count));

		for (int i = 0; i < topPlayers.Count; i++)
		{
			var (Name, Time) = topPlayers[i];
			chatMenu.AddMenuOption(_plugin.Localizer.ForPlayer(player, "timetop.player.entry.chat", i + 1, Name, FormatTime(player, Time)), (_, _) => { });
		}

		MenuManager.OpenChatMenu(player, chatMenu);
	}

	private string FormatTime(CCSPlayerController player, double minutes)
	{
		TimeSpan time = TimeSpan.FromMinutes(minutes);
		return _plugin.Localizer.ForPlayer(player, "timetop.time.format", time.Days, time.Hours, time.Minutes);
	}

	private async Task<List<(string Name, double Time)>> GetTopPlayersTimeAsync(TimeCategory category, int limit)
	{
		var topPlayers = new List<(string Name, double Time)>();

		try
		{
			string? connectionString = _plugin.ModuleServices?.GetConnectionString();
			if (string.IsNullOrEmpty(connectionString))
			{
				throw new Exception("Database connection string is null or empty.");
			}

			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			var columnName = "K4-Zenith-TimeStats.storage";
			var query = $@"
				SELECT p.name,
					CAST(JSON_EXTRACT(p.`{columnName}`, '$.{category}') AS DECIMAL(10,2)) as Time
				FROM zenith_player_storage p
				WHERE JSON_VALID(p.`{columnName}`) = 1
				AND JSON_EXTRACT(p.`{columnName}`, '$.{category}') IS NOT NULL
				ORDER BY Time DESC
				LIMIT @Limit";

			var results = await connection.QueryAsync<(string Name, double Time)>(query, new { ColumnName = MySqlHelper.EscapeString(columnName), Limit = limit });

			topPlayers = [.. results.Select(player => (TopListsPlugin.TruncateString(player.Name), player.Time))];
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Error fetching top players for time category {category}: {ex.Message}");
		}

		return topPlayers;
	}
}