using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		public Dictionary<CCSPlayerController, CounterStrikeSharp.API.Modules.Timers.Timer> _disconnectTImers = [];

		private void Initialize_Events()
		{
			RegisterEventHandler((EventPlayerConnectFull @event, GameEventInfo info) =>
			{
				CCSPlayerController? player = @event.Userid;
				if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
					return HookResult.Continue;

				ProcessPlayerData(player, true);
				return HookResult.Continue;
			});

			RegisterEventHandler((EventPlayerDisconnect @event, GameEventInfo info) =>
			{
				CCSPlayerController? player = @event.Userid;
				if (player?.IsValid == true && !player.IsBot && !player.IsHLTV)
				{
					ulong steamID = player.SteamID;
					_ = Task.Run(async () =>
					{
						await HandlePlayerDisconnectAsync(steamID);
					});

					AddDisconnectedPlayer(new DisconnectedPlayer
					{
						SteamId = steamID,
						PlayerName = player.PlayerName,
						DisconnectedAt = DateTime.Now
					});
					_playerCache.Remove(steamID);
				}
				return HookResult.Continue;
			});

			RegisterEventHandler((EventPlayerHurt @event, GameEventInfo info) =>
			{
				CCSPlayerController? attacker = @event.Attacker;
				if (attacker == null || !attacker.IsValid || attacker.IsBot || attacker.IsHLTV || !_disconnectTImers.ContainsKey(attacker))
					return HookResult.Continue;

				CCSPlayerController? victim = @event.Userid;
				if (victim == null || !victim.IsValid || victim.PlayerPawn.Value == null)
					return HookResult.Continue;

				CCSPlayerPawn playerPawn = victim.PlayerPawn.Value;

				victim.Health += @event.DmgHealth;
				Utilities.SetStateChanged(victim, "CBaseEntity", "m_iHealth");

				playerPawn.ArmorValue += @event.DmgArmor;
				Utilities.SetStateChanged(playerPawn, "CCSPlayerPawn", "m_ArmorValue");
				return HookResult.Continue;
			}, HookMode.Pre);

			AddCommandListener("say", OnAdminChatAll);
			AddCommandListener("say_team", OnAdminChat);
		}

		private HookResult OnAdminChatAll(CCSPlayerController? player, CommandInfo info)
		{
			if (player == null || !player.IsValid || player.AuthorizedSteamID == null)
				return HookResult.Continue;

			if (info.GetArg(1).Length == 0)
				return HookResult.Continue;

			if (!AdminManager.PlayerHasPermissions(player, "@zenith/admin") && !AdminManager.PlayerHasPermissions(player, "@zenith/root"))
				return HookResult.Continue;

			string message = info.GetArg(1);
			if (message[0] == '@')
			{
				var players = Utilities.GetPlayers();
				string adminName = Localizer.ForPlayer(player, "k4.general.admin");
				message = message.Replace("@", string.Empty);

				foreach (var target in players)
				{
					if (target != null && target.IsValid && !target.IsBot && !target.IsHLTV)
					{
						if (ShouldShowActivity(player.SteamID, target, true))
						{
							_moduleServices?.PrintForPlayer(target, Localizer.ForPlayer(target, "k4.chat.announce", player.PlayerName, message), false);
						}
						else if (ShouldShowActivity(player.SteamID, target, false))
						{
							_moduleServices?.PrintForPlayer(target, Localizer.ForPlayer(target, "k4.chat.announce", adminName, message), false);
						}
					}
				}
				return HookResult.Handled;
			}

			return HookResult.Continue;
		}

		private HookResult OnAdminChat(CCSPlayerController? player, CommandInfo info)
		{
			if (player == null || !player.IsValid || player.AuthorizedSteamID == null)
				return HookResult.Continue;

			if (info.GetArg(1).Length == 0)
				return HookResult.Continue;

			if (!AdminManager.PlayerHasPermissions(player, "@zenith/admin") && !AdminManager.PlayerHasPermissions(player, "@zenith/root"))
				return HookResult.Continue;

			string message = info.GetArg(1);
			if (message[0] == '@')
			{
				var players = Utilities.GetPlayers();
				message = message.Replace("@", string.Empty);

				foreach (var target in players)
				{
					if (target != null && target.IsValid && !target.IsBot && !target.IsHLTV && (AdminManager.PlayerHasPermissions(target, "@zenith/admin") || AdminManager.PlayerHasPermissions(target, "@zenith/root")))
					{
						_moduleServices?.PrintForPlayer(target, Localizer.ForPlayer(target, "k4.chat.adminsonly", player.PlayerName, message), false);
					}
				}
				return HookResult.Handled;
			}

			return HookResult.Continue;
		}

		private void OnZenithChatMessage(CCSPlayerController player, string message, string formattedMessage)
		{
			if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
				return;

			if (string.IsNullOrEmpty(message))
				return;

			foreach (var admin in ChatSpyPlayers)
			{
				if (admin.IsValid && !admin.IsBot && !admin.IsHLTV && admin.Connected == PlayerConnectedState.PlayerConnected)
				{
					if (admin.Team != player.Team)
						_moduleServices?.PrintForPlayer(admin, formattedMessage, false);
				}
				else
					ChatSpyPlayers.Remove(admin);
			}
		}
	}
}