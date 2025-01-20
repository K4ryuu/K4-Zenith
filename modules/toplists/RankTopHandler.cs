using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Commands;
using Dapper;
using MySqlConnector;
using Menu;
using Menu.Enums;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Core.Translations;

namespace Zenith_TopLists;

public class RankTopHandler
{
	private readonly TopListsPlugin _plugin;

	public RankTopHandler(TopListsPlugin plugin)
	{
		_plugin = plugin;
	}

	public void HandleRankTopCommand(CCSPlayerController player, CommandInfo? command = null)
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
				_plugin.ModuleServices?.PrintForPlayer(player, _plugin.Localizer.ForPlayer(player, "ranktop.invalid.count", TopListsPlugin.DEFAULT_PLAYER_COUNT));
			}
		}

		ShowRankTopMenu(player, playerCount, command is null);
	}

	private void ShowRankTopMenu(CCSPlayerController player, int playerCount, bool subMenu)
	{
		Task.Run(async () =>
		{
			var topPlayers = await GetTopPlayersAsync(playerCount);

			Server.NextWorldUpdate(() =>
			{
				if (topPlayers.Count == 0)
				{
					_plugin.ModuleServices?.PrintForPlayer(player, _plugin.Localizer.ForPlayer(player, "ranktop.no.data"));
					return;
				}

				try
				{
					if (_plugin.CoreAccessor?.GetValue<bool>("Core", "CenterMenuMode") == true)
					{
						ShowCenterRankTopMenu(player, topPlayers, subMenu);
					}
					else
					{
						ShowChatRankTopMenu(player, topPlayers);
					}
				}
				catch (Exception ex)
				{
					_plugin.Logger.LogError($"Error showing rank top menu: {ex.Message}");
				}
			});
		});
	}

	private void ShowCenterRankTopMenu(CCSPlayerController player, List<(string Name, int Points)> topPlayers, bool subMenu)
	{
		var items = topPlayers.Select((p, index) => new MenuItem(MenuItemType.Button, [new MenuValue(_plugin.Localizer.ForPlayer(player, "ranktop.player.entry.center", index + 1, p.Name, $"{p.Points:N0}"))])).ToList();

		_plugin.menu?.ShowScrollableMenu(player, _plugin.Localizer.ForPlayer(player, "top.menu.title", topPlayers.Count), items, (_, _, _) => { }, subMenu, _plugin.CoreAccessor!.GetValue<bool>("Core", "FreezeInMenu") && (_plugin.GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, disableDeveloper: !_plugin.CoreAccessor!.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatRankTopMenu(CCSPlayerController player, List<(string Name, int Points)> topPlayers)
	{
		var chatMenu = new ChatMenu(_plugin.Localizer.ForPlayer(player, "top.menu.title", topPlayers.Count));

		for (int i = 0; i < topPlayers.Count; i++)
		{
			var (Name, Points) = topPlayers[i];
			chatMenu.AddMenuOption(_plugin.Localizer.ForPlayer(player, "ranktop.player.entry.chat", i + 1, Name, $"{Points:N0}"), (_, _) => { });
		}

		MenuManager.OpenChatMenu(player, chatMenu);
	}

	private async Task<List<(string Name, int Points)>> GetTopPlayersAsync(int limit)
	{
		var topPlayers = new List<(string Name, int Points)>();

		try
		{
			string? connectionString = _plugin.ModuleServices?.GetConnectionString();
			if (string.IsNullOrEmpty(connectionString))
			{
				throw new Exception("Database connection string is null or empty.");
			}

			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			var columnName = "K4-Zenith-Ranks.storage";
			var query = $@"
				SELECT p.name,
					   CAST(JSON_EXTRACT(p.`{columnName}`, '$.Points') AS UNSIGNED) as Points
				FROM zenith_player_storage p
				WHERE JSON_VALID(p.`{columnName}`) = 1
				AND JSON_EXTRACT(p.`{columnName}`, '$.Points') IS NOT NULL
				ORDER BY Points DESC
				LIMIT @Limit";

			var results = await connection.QueryAsync<(string Name, int Points)>(query, new { ColumnName = columnName, Limit = limit });

			topPlayers = results.Select(player => (TopListsPlugin.TruncateString(player.Name), player.Points)).ToList();
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Error fetching top players: {ex.Message}");
		}

		return topPlayers;
	}
}