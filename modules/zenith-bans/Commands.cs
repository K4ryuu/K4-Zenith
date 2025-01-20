using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Entities;
using Menu;
using Menu.Enums;
using Microsoft.Extensions.Logging;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		private void Initialize_Commands()
		{
			_moduleServices?.RegisterModuleCommand("kick", "Kicks a player from the server.", OnKickCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/kick");
			_moduleServices?.RegisterModuleCommand("skick", "Kicks a player from the server.", OnSilentKickCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/silent-kick");

			_moduleServices?.RegisterModuleCommand("ban", "Bans a player from the server.", OnBanCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/ban");
			_moduleServices?.RegisterModuleCommand("unban", "Unbans a player from the server.", OnUnbanCommand, CommandUsage.CLIENT_AND_SERVER, 1, "<SteamID64>", "@zenith-admin/unban");

			_moduleServices?.RegisterModuleCommand("mute", "Mutes a player in the server.", OnMuteCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/mute");
			_moduleServices?.RegisterModuleCommand("unmute", "Unmutes a player in the server.", OnUnmuteCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/unmute");

			_moduleServices?.RegisterModuleCommand("gag", "Gags a player in the server.", OnGagCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/gag");
			_moduleServices?.RegisterModuleCommand("ungag", "Ungags a player in the server.", OnUngagCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/ungag");

			_moduleServices?.RegisterModuleCommand("silence", "Silences a player in the server.", OnSilenceCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/silence");
			_moduleServices?.RegisterModuleCommand("unsilence", "Unsilences a player in the server.", OnUnsilenceCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/unsilence");

			_moduleServices?.RegisterModuleCommand("warn", "Warns a player in the server.", OnWarnCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/warn");
			_moduleServices?.RegisterModuleCommand("warns", "Checks warnings for a player.", OnWarnCheckCommand, CommandUsage.CLIENT_ONLY);
			_moduleServices?.RegisterModuleCommand("clearwarns", "Clears all warnings for a player in the server.", OnClearWarnsCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/clearwarns");

			_moduleServices?.RegisterModuleCommand("comms", "Checks communication blocks for a player.", OnCommsCheckCommand, CommandUsage.CLIENT_ONLY);

			_moduleServices?.RegisterModuleCommand("banoffline", "Ban a recently disconnected player", OnBanOfflineCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/ban");

			_moduleServices?.RegisterModuleCommand("addadmin", "Adds a player as an admin.", OnAddAdminCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/addadmin");
			_moduleServices?.RegisterModuleCommand("removeadmin", "Removes a player's admin status.", OnRemoveAdminCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/removeadmin");
			_moduleServices?.RegisterModuleCommand("addofflineadmin", "Adds an offline player as an admin.", OnAddOfflineAdminCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/addadmin");
			_moduleServices?.RegisterModuleCommand("removeofflineadmin", "Removes an offline player's admin status.", OnRemoveOfflineAdminCommand, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/removeadmin");

			_moduleServices?.RegisterModuleCommand("psay", "Sends a private message to the targets.", OnPrivateSayCommand, CommandUsage.CLIENT_AND_SERVER, 2, "<player> <message>");
			_moduleServices?.RegisterModuleCommand("csay", "Sends a center message to the targets.", OnCSayCommand, CommandUsage.CLIENT_AND_SERVER, 2, "<player> <message>", permission: "@zenith-admin/csay");
			_moduleServices?.RegisterModuleCommand("asay", "Sends an alert message to the targets.", OnASayCommand, CommandUsage.CLIENT_AND_SERVER, 2, "<player> <message>", permission: "@zenith-admin/asay");
			_moduleServices?.RegisterModuleCommand("htmlsay", "Sends an HTML message to the targets.", OnHtmlSayCommand, CommandUsage.CLIENT_AND_SERVER, 2, "<player> <message>", permission: "@zenith-admin/htmlsay");

			_moduleServices?.RegisterModuleCommand("players", "List online players with all their informations.", OnPlayerList, CommandUsage.CLIENT_AND_SERVER, permission: "@zenith-admin/players");
			_moduleServices?.RegisterModuleCommand("chatspy", "Toggles chat spy mode for admins.", OnChatSpy, CommandUsage.CLIENT_ONLY, permission: "@zenith-admin/chatspy");

		}

		// +------------------------+
		// | Kick Command           |
		// +------------------------+

		private void OnKickCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandlePunishmentCommand(controller, info, PunishmentType.Kick, "KickDurations", "KickReasons");
		}

		// +------------------------+
		// | Silent Kick Command    |
		// +------------------------+

		private void OnSilentKickCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandlePunishmentCommand(controller, info, PunishmentType.SilentKick, "KickDurations", "KickReasons");
		}

		// +------------------------+
		// | Ban Command            |
		// +------------------------+

		private void OnBanCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandlePunishmentCommand(controller, info, PunishmentType.Ban, "BanDurations", "BanReasons");
		}

		// +------------------------+
		// | Unban Command          |
		// +------------------------+

		private void OnUnbanCommand(CCSPlayerController? controller, CommandInfo info)
		{
			bool forceReason = _coreAccessor.GetValue<bool>("Config", "ForceRemovePunishmentReason");
			int requiredArgs = forceReason ? 3 : 2;

			if (info.ArgCount < requiredArgs)
			{
				string usage = forceReason ? $"{info.GetArg(0)} <SteamID64/name> <reason>" : $"{info.GetArg(0)} <SteamID64/name> [reason]";
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", usage));
				return;
			}

			string input = info.GetArg(1);
			string? reason = info.ArgCount > 2
				? info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim()
				: null;

			if (forceReason && string.IsNullOrWhiteSpace(reason))
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.reason-required"));
				return;
			}

			if (SteamID.TryParse(input, out SteamID? steamID) && steamID != null)
			{
				RemovePunishment(controller, steamID, PunishmentType.Ban, reason);
			}
			else
			{
				_ = Task.Run(async () =>
				{
					var matchingPlayers = await FindPlayersByNameOrPartialNameAsync(input);

					Server.NextWorldUpdate(() =>
					{
						if (matchingPlayers.Count == 0)
						{
							_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.noplayersfound"));
						}
						else if (matchingPlayers.Count == 1)
						{
							RemovePunishment(controller, new SteamID(matchingPlayers.First().SteamID), PunishmentType.Ban, reason);
						}
						else
						{
							_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.multiple-players-found"));
							foreach (var player in matchingPlayers)
							{
								_moduleServices?.PrintForPlayer(controller, $"{player.PlayerName} ({player.SteamID})");
							}
						}
					});
				});
			}
		}

		// +------------------------+
		// | Mute Command           |
		// +------------------------+

		private void OnMuteCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandlePunishmentCommand(controller, info, PunishmentType.Mute, "MuteDurations", "MuteReasons");
		}

		// +------------------------+
		// | Unmute Command         |
		// +------------------------+

		private void OnUnmuteCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandleRemovePunishmentCommand(controller, info, PunishmentType.Mute);
		}

		// +------------------------+
		// | Gag Command            |
		// +------------------------+

		private void OnGagCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandlePunishmentCommand(controller, info, PunishmentType.Gag, "GagDurations", "GagReasons");
		}

		// +------------------------+
		// | Ungag Command          |
		// +------------------------+

		private void OnUngagCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandleRemovePunishmentCommand(controller, info, PunishmentType.Gag);
		}

		// +------------------------+
		// | Silence Command        |
		// +------------------------+

		private void OnSilenceCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandlePunishmentCommand(controller, info, PunishmentType.Silence, "SilenceDurations", "SilenceReasons");
		}

		// +------------------------+
		// | Unsilence Command      |
		// +------------------------+

		private void OnUnsilenceCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandleRemovePunishmentCommand(controller, info, PunishmentType.Silence);
		}

		// +------------------------+
		// | Warn Command           |
		// +------------------------+

		private void OnWarnCommand(CCSPlayerController? controller, CommandInfo info)
		{
			HandlePunishmentCommand(controller, info, PunishmentType.Warn, "WarnDurations", "WarnReasons");
		}

		// +------------------------+
		// | Clearwarns Command     |
		// +------------------------+

		private void OnClearWarnsCommand(CCSPlayerController? controller, CommandInfo info)
		{
			bool forceReason = _coreAccessor.GetValue<bool>("Config", "ForceRemovePunishmentReason");
			int requiredArgs = forceReason ? 3 : 2;

			if (info.ArgCount < requiredArgs)
			{
				string usage = forceReason ? $"{info.GetArg(0)} <player> <reason>" : $"{info.GetArg(0)} <player> [reason]";
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", usage));
				return;
			}

			string targetString = info.GetArg(1);
			string? reason = info.ArgCount > 2
				? info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim()
				: null;

			if (forceReason && string.IsNullOrWhiteSpace(reason))
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.reason-required"));
				return;
			}

			TargetResult targetResult = info.GetArgTargetResult(1);

			ProcessTargetAction(controller, targetResult, (target) => ClearWarns(controller, target, reason), (failureReason) =>
			{
				if (failureReason == TargetFailureReason.TargetNotFound)
				{
					if (SteamID.TryParse(targetString, out SteamID? steamId) && steamId != null)
					{
						ClearWarns(controller, steamId, reason);
					}
					else
					{
						_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.targetnotfound"));
					}
				}
			});
		}

		private void ClearWarns(CCSPlayerController? caller, CCSPlayerController target, string? reason)
		{
			string callerName = caller?.PlayerName ?? Localizer.ForPlayer(caller, "k4.general.console");
			ulong callerSteamId = caller?.SteamID ?? 0;

			ulong targetSteamId = target.SteamID;
			string targetName = target.PlayerName;

			_ = Task.Run(async () =>
			{
				bool removed = await RemovePunishmentAsync(targetSteamId, PunishmentType.Warn, callerSteamId, reason);

				Server.NextWorldUpdate(() =>
				{
					if (removed)
					{
						if (_playerCache.TryGetValue(targetSteamId, out var playerData))
						{
							playerData.Punishments.RemoveAll(p => p.Type == PunishmentType.Warn);
						}
						Logger.LogWarning($"Player {targetName} ({targetSteamId})'s warns were cleared by {callerName} {caller?.SteamID ?? 0}");
						_moduleServices?.PrintForAll(Localizer.ForPlayer(caller, "k4.chat.clearwarns", callerName, targetName));
					}
					else
					{
						_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.no-active-warns"));
					}
				});
			});
		}

		private void ClearWarns(CCSPlayerController? caller, SteamID steamId, string? reason)
		{
			string callerName = caller?.PlayerName ?? Localizer.ForPlayer(caller, "k4.general.console");
			ulong callerSteamId = caller?.SteamID ?? 0;

			ulong targetSteamId = steamId.SteamId64;

			_ = Task.Run(async () =>
			{
				bool removed = await RemovePunishmentAsync(targetSteamId, PunishmentType.Warn, callerSteamId, reason);
				string targetName = await GetPlayerNameAsync(targetSteamId);

				Server.NextWorldUpdate(() =>
				{
					if (removed)
					{
						Logger.LogWarning($"Player {targetName} ({targetSteamId})'s warns were cleared by {callerName} {caller?.SteamID ?? 0}");
						_moduleServices?.PrintForAll(Localizer.ForPlayer(caller, "k4.chat.clearwarns", callerName, targetName));
					}
					else
					{
						_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.no-active-warns"));
					}
				});
			});
		}

		// +------------------------+
		// | Check Commands         |
		// +------------------------+

		private void OnCommsCheckCommand(CCSPlayerController? caller, CommandInfo info)
		{
			if (info.ArgCount == 1 || caller == null)
			{
				// Check self
				CheckPlayerComms(caller, caller);
			}
			else if (AdminManager.PlayerHasPermissions(caller, "@zenith-admin/commscheck") || AdminManager.PlayerHasPermissions(caller, "@zenith/root") || AdminManager.PlayerHasPermissions(caller, "@css/root"))
			{
				// Admin checking another player
				ProcessTargetAction(caller, info.GetArgTargetResult(1), (target) => CheckPlayerComms(caller, target));
			}
			else
			{
				// No permission to check others, default to checking self
				CheckPlayerComms(caller, caller);
			}
		}

		private void OnWarnCheckCommand(CCSPlayerController? caller, CommandInfo info)
		{
			if (info.ArgCount == 1 || caller == null)
			{
				// Check self
				CheckPlayerWarns(caller, caller);
			}
			else if (AdminManager.PlayerHasPermissions(caller, "@zenith-admin/warncheck") || AdminManager.PlayerHasPermissions(caller, "@zenith/root") || AdminManager.PlayerHasPermissions(caller, "@css/root"))
			{
				// Admin checking another player
				ProcessTargetAction(caller, info.GetArgTargetResult(1), (target) => CheckPlayerWarns(caller, target));
			}
			else
			{
				// No permission to check others, default to checking self
				CheckPlayerWarns(caller, caller);
			}
		}

		// +------------------------+
		// | Banoffline Command     |
		// +------------------------+

		private void OnBanOfflineCommand(CCSPlayerController? controller, CommandInfo info)
		{
			if (controller == null) return;

			if (_disconnectedPlayers.Count == 0)
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.banoffline.no-disconnected-players"));
				return;
			}

			List<MenuItem> items = [];
			var playerMap = new Dictionary<int, DisconnectedPlayer>();

			int index = 0;
			foreach (var player in _disconnectedPlayers)
			{
				string timeSinceDisconnect = (DateTime.Now - player.DisconnectedAt).TotalMinutes.ToString("F0");
				items.Add(new MenuItem(MenuItemType.Button, [
					new MenuValue(Localizer.ForPlayer(controller, "k4.banoffline.player-info",
						player.PlayerName,
						player.SteamId,
						Localizer.ForPlayer(controller, "k4.general.time-ago", timeSinceDisconnect)
					))
				]));
				playerMap[index] = player;
				index++;
			}

			Menu.ShowScrollableMenu(controller, Localizer.ForPlayer(controller, "k4.banoffline.menu-title"), items, (buttons, menu, selected) =>
			{
				if (selected == null) return;

				switch (buttons)
				{
					case MenuButtons.Select:
						if (playerMap.TryGetValue(menu.Option, out var selectedPlayer))
						{
							StartOfflineBanProcess(controller, new SteamID(selectedPlayer.SteamId));
						}
						break;
				}
			}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && (GetZenithPlayer(controller)?.GetSetting<bool>("FreezeInMenu", "K4-Zenith") ?? true), disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
		}

		private void StartOfflineBanProcess(CCSPlayerController? controller, SteamID steamId)
		{
			ShowLengthSelectionMenu(controller, _coreAccessor.GetValue<List<int>>("Config", "BanDurations"), (duration) =>
			{
				ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", "BanReasons"), (reason) =>
				{
					ApplyPunishment(controller, steamId, PunishmentType.Ban, duration, reason);
				});
			});
		}

		// +------------------------+
		// | Add Admin Command      |
		// +------------------------+

		private void OnAddAdminCommand(CCSPlayerController? controller, CommandInfo info)
		{
			if (info.ArgCount < 2)
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", "addadmin <player> [group]"));
				return;
			}

			int argCount = info.ArgCount;
			string group = info.GetArg(2);
			if (info.ArgCount > 2 && info.GetArg(2).Length > 0)
				group = info.GetArg(2);

			ProcessTargetAction(controller, info.GetArgTargetResult(1), (target) =>
			{
				_ = Task.Run(async () =>
				{
					var groups = await GetAdminGroupsAsync();

					Server.NextWorldUpdate(() =>
					{
						if (argCount == 2)
						{
							if (controller != null)
								ShowGroupSelectionMenu(controller, groups, (group) => AddAdmin(controller, target, group));
							else
								_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", "addadmin <player> [group]"));
						}
						else
						{
							if (groups.Contains(group))
							{
								AddAdmin(controller, target, group);
							}
							else
							{
								_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.addadmin.invalid-group", group));
							}
						}
					});
				});
			});
		}

		// +------------------------+
		// | Remove Admin Command   |
		// +------------------------+

		private void OnRemoveAdminCommand(CCSPlayerController? controller, CommandInfo info)
		{
			if (info.ArgCount < 2)
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", "removeadmin <player>"));
				return;
			}

			ProcessTargetAction(controller, info.GetArgTargetResult(1), (target) => RemoveAdmin(controller, target));
		}

		// +---------------------------+
		// | Add Offline Admin Command |
		// +---------------------------+

		private void OnAddOfflineAdminCommand(CCSPlayerController? controller, CommandInfo info)
		{
			if (info.ArgCount < 2)
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", "addofflineadmin <SteamID64> [group]"));
				return;
			}

			SteamID steamId = GetSteamID(info.GetArg(1));
			if (!steamId.IsValid())
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-steamid"));
				return;
			}

			int argCount = info.ArgCount;
			string group = string.Empty;
			if (argCount > 2 && info.GetArg(2).Length > 0)
				group = info.GetArg(2);

			_ = Task.Run(async () =>
			{
				var groups = await GetAdminGroupsAsync();

				Server.NextWorldUpdate(() =>
				{
					if (argCount == 2)
					{
						if (controller != null)
							ShowGroupSelectionMenu(controller, groups, (group) => AddOfflineAdmin(controller, steamId, group));
						else
							_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", "addofflineadmin <SteamID64> [group]"));
					}
					else
					{
						if (groups.Contains(group))
						{
							AddOfflineAdmin(controller, steamId, group);
						}
						else
						{
							_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.addadmin.invalid-group", group));
						}
					}
				});
			});
		}

		// +------------------------------+
		// | Remove Offline Admin Command |
		// +------------------------------+

		private void OnRemoveOfflineAdminCommand(CCSPlayerController? controller, CommandInfo info)
		{
			if (info.ArgCount < 2)
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", "removeofflineadmin <SteamID64>"));
				return;
			}

			SteamID steamId = GetSteamID(info.GetArg(1));
			if (!steamId.IsValid())
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-steamid"));
				return;
			}

			RemoveOfflineAdmin(controller, steamId);
		}

		// +------------------------+
		// | Private Say Command     |
		// +------------------------+

		private void OnPrivateSayCommand(CCSPlayerController? controller, CommandInfo info)
		{
			string callerName = controller?.PlayerName ?? Localizer.ForPlayer(controller, "k4.general.console");
			string message = info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim();

			ProcessTargetAction(controller, info.GetArgTargetResult(1), (target) =>
			{
				target.PrintToChat($" {Localizer.ForPlayer(controller, "k4.chat.psay", callerName, target.PlayerName, message)}");
				controller?.PrintToChat($" {Localizer.ForPlayer(controller, "k4.chat.psay", callerName, target.PlayerName, message)}");
			}, checkImmunity: false, selfTarget: false);
		}

		// +------------------------+
		// | Center Say Command     |
		// +------------------------+

		private void OnCSayCommand(CCSPlayerController? controller, CommandInfo info)
		{
			string message = info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim();

			ProcessTargetAction(controller, info.GetArgTargetResult(1), (target) =>
			{
				target.PrintToCenter(message);
			}, checkImmunity: false);
		}

		// +------------------------+
		// | Alert Say Command      |
		// +------------------------+

		private void OnASayCommand(CCSPlayerController? controller, CommandInfo info)
		{
			string message = info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim();

			ProcessTargetAction(controller, info.GetArgTargetResult(1), (target) =>
			{
				target.PrintToCenterAlert(message);
			}, checkImmunity: false);
		}

		// +------------------------+
		// | HTML Say Command       |
		// +------------------------+

		private void OnHtmlSayCommand(CCSPlayerController? controller, CommandInfo info)
		{
			string message = info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim();

			ProcessTargetAction(controller, info.GetArgTargetResult(1), (target) =>
			{
				var zenithPlayer = GetZenithPlayer(target);
				zenithPlayer?.PrintToCenter(message);
			}, checkImmunity: false);
		}

		// +------------------------+
		// | Player List Command    |
		// +------------------------+

		private void OnPlayerList(CCSPlayerController? controller, CommandInfo info)
		{
			var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV).ToList();

			if (players.Count == 0)
				return;

			if (controller != null)
				controller.PrintToConsole(Localizer.ForPlayer(controller, "k4.console.playerinfo.liner"));
			else
				Server.PrintToConsole(Localizer.ForPlayer(controller, "k4.console.playerinfo.liner"));

			foreach (var player in players)
			{
				var zenithPlayer = GetZenithPlayer(player);
				if (zenithPlayer is null)
					continue;

				string country = zenithPlayer.ReplacePlaceholders("{userid}|{country_short}|{name}|{steamid}|{ip}");

				string[] parts = country.Split('|');

				if (controller != null)
					controller.PrintToConsole(zenithPlayer.ReplacePlaceholders(Localizer.ForPlayer(controller, "k4.console.playerinfo.line", $"{parts[0],-3}", $"{parts[1],-3}", $"{parts[2],-24}", $"{parts[3],-18}", $"{parts[4].Split(":")[0],-16}")));
				else
					Server.PrintToConsole(zenithPlayer.ReplacePlaceholders(Localizer.ForPlayer(controller, "k4.console.playerinfo.line", $"{parts[0],-3}", $"{parts[1],-3}", $"{parts[2],-24}", $"{parts[3],-18}", $"{parts[4].Split(":")[0],-16}")));
			}

			if (controller != null)
				controller.PrintToConsole(Localizer.ForPlayer(controller, "k4.console.playerinfo.liner"));
			else
				Server.PrintToConsole(Localizer.ForPlayer(controller, "k4.console.playerinfo.liner"));
		}

		// +------------------------+
		// | Chat Spy Command       |
		// +------------------------+

		private List<CCSPlayerController> ChatSpyPlayers = [];

		private void OnChatSpy(CCSPlayerController? controller, CommandInfo info)
		{
			if (controller == null) return;

			if (!ChatSpyPlayers.Remove(controller))
			{
				ChatSpyPlayers.Add(controller);
				controller.PrintToChat(Localizer.ForPlayer(controller, "k4.chatspy.enabled"));
			}
			else
			{
				controller.PrintToChat(Localizer.ForPlayer(controller, "k4.chatspy.disabled"));
			}
		}
	}
}