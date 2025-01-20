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

public class StatsTopHandler
{
	private readonly TopListsPlugin _plugin;

	private enum StatsCategory
	{
		ChestHits,
		MVP,
		HitsGiven,
		HostageKilled,
		FirstBlood,
		Assists,
		FlashedKill,
		RoundWin,
		Shoots,
		GameLose,
		RightLegHits,
		NoScopeKill,
		RoundsT,
		BombDefused,
		UnusedHits,
		Headshots,
		HeadHits,
		HitsTaken,
		HostageRescued,
		RoundsOverall,
		GameWin,
		LeftArmHits,
		RevengeKill,
		AssistFlash,
		GearHits,
		StomachHits,
		RoundsCT,
		RoundLose,
		RightArmHits,
		BombPlanted,
		Kills,
		NeckHits,
		ThruSmokeKill,
		DominatedKill,
		LeftLegHits,
		Grenades,
		PenetratedKill,
		Deaths,
		SpecialHits
	}

	public StatsTopHandler(TopListsPlugin plugin)
	{
		_plugin = plugin;
	}

	public void HandleStatsTopCommand(CCSPlayerController player, CommandInfo? command = null)
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
				_plugin.ModuleServices?.PrintForPlayer(player, _plugin.Localizer.ForPlayer(player, "statstop.invalid.count", TopListsPlugin.DEFAULT_PLAYER_COUNT));
			}
		}

		ShowStatsTopCategoryMenu(player, playerCount, command is null);
	}

	private void ShowStatsTopCategoryMenu(CCSPlayerController player, int playerCount, bool subMenu)
	{
		if (_plugin.CoreAccessor?.GetValue<bool>("Core", "CenterMenuMode") == true)
		{
			ShowCenterStatsTopCategoryMenu(player, playerCount, subMenu);
		}
		else
		{
			ShowChatStatsTopCategoryMenu(player, playerCount);
		}
	}

	private void ShowCenterStatsTopCategoryMenu(CCSPlayerController player, int playerCount, bool subMenu)
	{
		var items = Enum.GetValues(typeof(StatsCategory))
			.Cast<StatsCategory>()
			.Select(category => new MenuItem(MenuItemType.Button, [new MenuValue(_plugin.Localizer.ForPlayer(player, $"statstop.category.{category}"))]))
			.ToList();

		_plugin.menu?.ShowScrollableMenu(player, _plugin.Localizer.ForPlayer(player, "statstop.category.menu.title"), items, (buttons, menu, selected) =>
		{
			if (selected == null) return;

			if (menu.Option >= 0 && menu.Option < Enum.GetValues(typeof(StatsCategory)).Length)
			{
				StatsCategory selectedCategory = (StatsCategory)menu.Option;

				if (buttons == MenuButtons.Select)
				{
					ShowStatsTopMenu(player, selectedCategory, playerCount);
				}
			}
		}, subMenu, _plugin.CoreAccessor!.GetValue<bool>("Core", "FreezeInMenu") && (_plugin.GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, disableDeveloper: !_plugin.CoreAccessor!.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatStatsTopCategoryMenu(CCSPlayerController player, int playerCount)
	{
		var chatMenu = new ChatMenu(_plugin.Localizer.ForPlayer(player, "statstop.category.menu.title"));

		foreach (StatsCategory category in Enum.GetValues(typeof(StatsCategory)))
		{
			chatMenu.AddMenuOption(_plugin.Localizer.ForPlayer(player, $"statstop.category.{category}"), (p, _) =>
			{
				ShowStatsTopMenu(p, category, playerCount);
			});
		}

		MenuManager.OpenChatMenu(player, chatMenu);
	}

	private void ShowStatsTopMenu(CCSPlayerController player, StatsCategory category, int playerCount)
	{
		Task.Run(async () =>
		{
			var topPlayers = await GetTopPlayersStatsAsync(category, playerCount);

			Server.NextWorldUpdate(() =>
			{
				if (topPlayers.Count == 0)
				{
					_plugin.ModuleServices?.PrintForPlayer(player, _plugin.Localizer.ForPlayer(player, "statstop.no.data"));
					return;
				}

				try
				{
					if (_plugin.CoreAccessor?.GetValue<bool>("Core", "CenterMenuMode") == true)
					{
						ShowCenterStatsTopMenu(player, topPlayers);
					}
					else
					{
						ShowChatStatsTopMenu(player, topPlayers);
					}
				}
				catch (Exception ex)
				{
					_plugin.Logger.LogError($"Error showing stats top menu: {ex.Message}");
				}
			});
		});
	}

	private void ShowCenterStatsTopMenu(CCSPlayerController player, List<(string Name, int Value)> topPlayers)
	{
		var items = topPlayers.Select((p, index) => new MenuItem(MenuItemType.Button, [new MenuValue(_plugin.Localizer.ForPlayer(player, "statstop.player.entry.center", index + 1, p.Name, $"{p.Value:N0}"))])).ToList();

		_plugin.menu?.ShowScrollableMenu(player, _plugin.Localizer.ForPlayer(player, "top.menu.title", topPlayers.Count), items, (_, _, _) => { }, true, _plugin.CoreAccessor!.GetValue<bool>("Core", "FreezeInMenu") && (_plugin.GetZenithPlayer(player)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), 5, disableDeveloper: !_plugin.CoreAccessor!.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatStatsTopMenu(CCSPlayerController player, List<(string Name, int Value)> topPlayers)
	{
		var chatMenu = new ChatMenu(_plugin.Localizer.ForPlayer(player, "top.menu.title", topPlayers.Count));

		for (int i = 0; i < topPlayers.Count; i++)
		{
			var (Name, Value) = topPlayers[i];
			chatMenu.AddMenuOption(_plugin.Localizer.ForPlayer(player, "statstop.player.entry.chat", i + 1, Name, $"{Value:N0}"), (_, _) => { });
		}

		MenuManager.OpenChatMenu(player, chatMenu);
	}

	private async Task<List<(string Name, int Value)>> GetTopPlayersStatsAsync(StatsCategory category, int limit)
	{
		var topPlayers = new List<(string Name, int Value)>();

		try
		{
			string? connectionString = _plugin.ModuleServices?.GetConnectionString();
			if (string.IsNullOrEmpty(connectionString))
			{
				throw new Exception("Database connection string is null or empty.");
			}

			using var connection = new MySqlConnection(connectionString);
			await connection.OpenAsync();

			var columnName = "K4-Zenith-Stats.storage";
			var query = $@"
				SELECT p.name,
					CAST(JSON_EXTRACT(p.`{columnName}`, '$.{category}') AS UNSIGNED) as Value
				FROM zenith_player_storage p
				WHERE JSON_VALID(p.`{columnName}`) = 1
				AND JSON_EXTRACT(p.`{columnName}`, '$.{category}') IS NOT NULL
				ORDER BY Value DESC
				LIMIT @Limit";

			var results = await connection.QueryAsync<(string Name, int Value)>(query, new { ColumnName = MySqlHelper.EscapeString(columnName), Limit = limit });

			topPlayers = [.. results.Select(player => (TopListsPlugin.TruncateString(player.Name), player.Value))];
		}
		catch (Exception ex)
		{
			_plugin.Logger.LogError($"Error fetching top players for stats category {category}: {ex.Message}");
		}

		return topPlayers;
	}
}