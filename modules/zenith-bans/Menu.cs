using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Menu;
using Menu.Enums;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		private void ShowPlayerSelectionMenu(CCSPlayerController? caller, Action<CCSPlayerController> callback, bool includeBots = false)
		{
			if (caller == null) return;

			var players = Utilities.GetPlayers().Where(p => p != null && p.IsValid && (includeBots || (!p.IsBot && !p.IsHLTV)) && p != caller && AdminManager.CanPlayerTarget(caller, p)).ToList();

			if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
			{
				ShowCenterPlayerSelectionMenu(caller, players, callback);
			}
			else
			{
				ShowChatPlayerSelectionMenu(caller, players, callback);
			}
		}

		private void ShowCenterPlayerSelectionMenu(CCSPlayerController caller, List<CCSPlayerController> players, Action<CCSPlayerController> callback)
		{
			List<MenuItem> items = [];
			var playerMap = new Dictionary<int, CCSPlayerController>();

			for (int i = 0; i < players.Count; i++)
			{
				var player = players[i];
				items.Add(new MenuItem(MenuItemType.Button, [new MenuValue($"#{player.UserId} | {player.PlayerName}")]));
				playerMap[i] = player;
			}

			if (items.Count == 0)
			{
				items.Add(new MenuItem(MenuItemType.Text, new MenuValue(Localizer.ForPlayer(caller, "k4.general.noplayersfound")) { Prefix = "<font color='#FF6666'>", Suffix = "</font>" }));
			}

			Menu.ShowScrollableMenu(caller, Localizer.ForPlayer(caller, "k4.menu.selectplayer"), items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				if (buttons == MenuButtons.Select && playerMap.TryGetValue(menu.Option, out var targetPlayer))
				{
					callback(targetPlayer);
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(caller)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		private void ShowChatPlayerSelectionMenu(CCSPlayerController caller, List<CCSPlayerController> players, Action<CCSPlayerController> callback)
		{
			ChatMenu playerMenu = new ChatMenu(Localizer.ForPlayer(caller, "k4.menu.selectplayer"));

			foreach (var player in players)
			{
				playerMenu.AddMenuOption($"{ChatColors.Gold}#{player.UserId} | {player.PlayerName}", (c, o) => callback(player));
			}

			if (playerMenu.MenuOptions.Count == 0)
			{
				playerMenu.AddMenuOption($"{ChatColors.LightRed}{Localizer.ForPlayer(caller, "k4.general.noplayersfound")}", (p, o) => { }, true);
			}

			MenuManager.OpenChatMenu(caller, playerMenu);
		}

		private void ShowLengthSelectionMenu(CCSPlayerController? caller, List<int> lengthList, Action<int> callback)
		{
			if (caller == null) return;

			if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
			{
				ShowCenterLengthSelectionMenu(caller, lengthList, callback);
			}
			else
			{
				ShowChatLengthSelectionMenu(caller, lengthList, callback);
			}
		}

		private void ShowCenterLengthSelectionMenu(CCSPlayerController caller, List<int> lengthList, Action<int> callback)
		{
			List<MenuItem> items = [];

			foreach (var length in lengthList)
			{
				if (length == 0)
				{
					string permanent = Localizer.ForPlayer(caller, "k4.general.permanent");
					items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(permanent.ToUpper())]));
				}
				else
				{
					items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(length.ToString())]));
				}
			}

			Menu.ShowScrollableMenu(caller, Localizer.ForPlayer(caller, "k4.menu.selectlength"), items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				if (buttons == MenuButtons.Select)
				{
					Server.PrintToChatAll($"{caller.PlayerName} selected option: {menu.Option} - length size: {lengthList.Count}");
					callback(lengthList[menu.Option]);
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(caller)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		private void ShowChatLengthSelectionMenu(CCSPlayerController caller, List<int> lengthList, Action<int> callback)
		{
			ChatMenu lengthMenu = new ChatMenu(Localizer.ForPlayer(caller, "k4.menu.selectlength"));

			for (int i = 0; i < lengthList.Count; i++)
			{
				int length = lengthList[i];
				string displayText = length == 0 ? Localizer.ForPlayer(caller, "k4.general.permanent") : length.ToString();
				lengthMenu.AddMenuOption($"{ChatColors.Gold}{displayText.ToUpper()}", (c, o) => callback(length));
			}

			MenuManager.OpenChatMenu(caller, lengthMenu);
		}

		public void ShowReasonSelectionMenu(CCSPlayerController? caller, List<string> reasonList, Action<string> callback)
		{
			if (caller == null) return;

			if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
			{
				ShowCenterReasonSelectionMenu(caller, reasonList, callback);
			}
			else
			{
				ShowChatReasonSelectionMenu(caller, reasonList, callback);
			}
		}

		private void ShowCenterReasonSelectionMenu(CCSPlayerController caller, List<string> reasonList, Action<string> callback)
		{
			List<MenuItem> items = reasonList.Select(reason => new MenuItem(MenuItemType.Button, [new MenuValue(reason)])).ToList();

			Menu.ShowScrollableMenu(caller, "Select Reason", items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				if (buttons == MenuButtons.Select)
				{
					callback(reasonList[menu.Option]);
					Menu.ClearMenus(caller);
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(caller)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		private void ShowChatReasonSelectionMenu(CCSPlayerController caller, List<string> reasonList, Action<string> callback)
		{
			ChatMenu reasonMenu = new ChatMenu("Select Reason");

			foreach (var reason in reasonList)
			{
				reasonMenu.AddMenuOption($"{ChatColors.Gold}{reason}", (c, o) => callback(reason));
			}

			MenuManager.OpenChatMenu(caller, reasonMenu);
		}

		private void ShowGroupSelectionMenu(CCSPlayerController controller, List<string> groups, Action<string> callback)
		{
			if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
			{
				ShowCenterGroupSelectionMenu(controller, groups, callback);
			}
			else
			{
				ShowChatGroupSelectionMenu(controller, groups, callback);
			}
		}

		private void ShowCenterGroupSelectionMenu(CCSPlayerController controller, List<string> groups, Action<string> callback)
		{
			List<MenuItem> items = groups.Select(group => new MenuItem(MenuItemType.Button, [new MenuValue(group)])).ToList();

			Menu.ShowScrollableMenu(controller, Localizer.ForPlayer(controller, "k4.addadmin.select-group"), items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				if (buttons == MenuButtons.Select)
				{
					callback(groups[menu.Option]);
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(controller)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		private void ShowChatGroupSelectionMenu(CCSPlayerController controller, List<string> groups, Action<string> callback)
		{
			ChatMenu groupMenu = new ChatMenu(Localizer.ForPlayer(controller, "k4.addadmin.select-group"));

			foreach (var group in groups)
			{
				groupMenu.AddMenuOption($"{ChatColors.Gold}{group}", (c, o) => callback(group));
			}

			MenuManager.OpenChatMenu(controller, groupMenu);
		}
	}
}
