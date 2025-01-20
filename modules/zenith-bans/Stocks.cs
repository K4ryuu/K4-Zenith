using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using ZenithAPI;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		private async Task InitializeServerIpAsync(int port)
		{
			try
			{
				using var client = new HttpClient();
				var externalIpString = await client.GetStringAsync("http://icanhazip.com");
				_serverIp = $"{externalIpString.Trim()}:{port}";

				Server.NextWorldUpdate(() =>
				{
					var players = Utilities.GetPlayers();
					foreach (var player in players)
					{
						if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
						{
							ProcessPlayerData(player, false);
						}
					}
				});
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to get server IP: {ex.Message}");
				_serverIp = "all";
			}
		}

		private void ProcessTargetAction(CCSPlayerController? caller, CCSPlayerController target, Action<CCSPlayerController> action, Action<TargetFailureReason>? onFail = null, bool checkImmunity = true, bool selfTarget = true)
		{
			if (!selfTarget && caller == target)
			{
				_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.selftarget"));
				onFail?.Invoke(TargetFailureReason.TargetSelf);
				return;
			}

			if (checkImmunity && !AdminManager.CanPlayerTarget(caller, target))
			{
				_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.targetimmunity", target.PlayerName));
				onFail?.Invoke(TargetFailureReason.TargetImmunity);
				return;
			}

			action.Invoke(target);
		}

		private void ProcessTargetAction(CCSPlayerController? caller, TargetResult targetResult, Action<CCSPlayerController> action, Action<TargetFailureReason>? onFail = null, bool checkImmunity = true, bool selfTarget = true)
		{
			if (!targetResult.Any())
			{
				onFail?.Invoke(TargetFailureReason.TargetNotFound);
				return;
			}

			foreach (var target in targetResult.Players)
			{
				ProcessTargetAction(caller, target, action, onFail, checkImmunity, selfTarget);
			}
		}

		public IPlayerServices? GetZenithPlayer(CCSPlayerController? player) =>
			player == null ? null : _playerServicesCapability?.Get(player);

		private void HandlePunishmentCommand(CCSPlayerController? controller, CommandInfo info, PunishmentType type, string durationConfigKey, string reasonConfigKey)
		{
			try
			{
				if (controller == null)
				{
					HandleConsolePunishmentCommand(info, type);
					return;
				}

				switch (info.ArgCount)
				{
					case 1:
						ShowPlayerSelectionMenu(controller, target => HandlePlayerSelection(controller, target, type, durationConfigKey, reasonConfigKey));
						break;
					case 2:
						HandleTargetSelection(controller, info, type, durationConfigKey, reasonConfigKey);
						break;
					default:
						HandleFullCommand(controller, info, type);
						break;
				}
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error in HandlePunishmentCommand: {ex.Message}\nStackTrace: {ex.StackTrace}");
				_moduleServices?.PrintForPlayer(controller, "An error occurred while processing the command.");
			}
		}

		private void HandleConsolePunishmentCommand(CommandInfo info, PunishmentType type)
		{
			int minArgs = type == PunishmentType.Kick || type == PunishmentType.SilentKick || type == PunishmentType.Warn ? 3 : 4;
			if (info.ArgCount < minArgs)
			{
				string usage = type == PunishmentType.Kick || type == PunishmentType.SilentKick || type == PunishmentType.Warn ? "<player> <reason>" : "<player> <duration> <reason>";
				_moduleServices?.PrintForPlayer(null, Localizer["k4.general.console-usage", info.GetArg(0), usage]);
				return;
			}

			string reason;
			int? duration = null;
			if (type == PunishmentType.Kick || type == PunishmentType.SilentKick || type == PunishmentType.Warn)
			{
				reason = info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim();
			}
			else
			{
				if (!int.TryParse(info.GetArg(2), out int parsedDuration))
				{
					_moduleServices?.PrintForPlayer(null, Localizer["k4.general.invalid-punish-length"]);
					return;
				}
				duration = parsedDuration;
				reason = info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Replace(info.GetArg(2), string.Empty).Trim();
			}

			TargetResult targetResult = info.GetArgTargetResult(1);
			string targetString = info.GetArg(1);

			ProcessTargetAction(null, targetResult, target => ApplyPunishment(null, target, type, duration, reason), failureReason =>
			{
				if (failureReason == TargetFailureReason.TargetNotFound && SteamID.TryParse(targetString, out SteamID? steamId) && steamId?.IsValid() == true)
				{
					ApplyPunishment(null, steamId, type, duration, reason);
				}
				else if (failureReason == TargetFailureReason.TargetNotFound)
				{
					_moduleServices?.PrintForPlayer(null, Localizer["k4.general.targetnotfound"]);
				}
			});
		}

		private void HandlePlayerSelection(CCSPlayerController controller, CCSPlayerController target, PunishmentType type, string durationConfigKey, string reasonConfigKey)
		{
			if (type == PunishmentType.Kick || type == PunishmentType.SilentKick || type == PunishmentType.Warn)
			{
				ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), reason =>
				{
					ProcessTargetAction(controller, target, t => ApplyPunishment(controller, t, type, null, reason));
				});
			}
			else
			{
				var durations = _coreAccessor.GetValue<List<int>>("Config", durationConfigKey);
				ShowLengthSelectionMenu(controller, durations, duration =>
				{
					ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), reason =>
					{
						ProcessTargetAction(controller, target, t => ApplyPunishment(controller, t, type, duration, reason));
					});
				});
			}
		}

		private void HandleTargetSelection(CCSPlayerController controller, CommandInfo info, PunishmentType type, string durationConfigKey, string reasonConfigKey)
		{
			TargetResult targetResult = info.GetArgTargetResult(1);

			if (type == PunishmentType.Kick || type == PunishmentType.SilentKick || type == PunishmentType.Warn)
			{
				ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), reason =>
				{
					ProcessTargetAction(controller, targetResult, target => ApplyPunishment(controller, target, type, null, reason));
				});
			}
			else
			{
				var durations = _coreAccessor.GetValue<List<int>>("Config", durationConfigKey);
				ShowLengthSelectionMenu(controller, durations, duration =>
				{
					ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", reasonConfigKey), reason =>
					{
						ProcessTargetAction(controller, targetResult, target => ApplyPunishment(controller, target, type, duration, reason));
					});
				});
			}
		}

		private void HandleFullCommand(CCSPlayerController controller, CommandInfo info, PunishmentType type)
		{
			TargetResult targetResult = info.GetArgTargetResult(1);
			string targetString = info.GetArg(1);

			if (type == PunishmentType.Kick || type == PunishmentType.SilentKick || type == PunishmentType.Warn)
			{
				string reason = info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim();
				ProcessTargetAction(controller, targetResult, target => ApplyPunishment(controller, target, type, null, reason));
			}
			else if (int.TryParse(info.GetArg(2), out int duration))
			{
				if (info.ArgCount == 3 && _coreAccessor.GetValue<bool>("Config", "ForcePunishmentReasons"))
				{
					ShowReasonSelectionMenu(controller, _coreAccessor.GetValue<List<string>>("Config", $"{type.ToString().ToLower()}Reasons"), reason =>
					{
						ProcessTargetAction(controller, targetResult, target => ApplyPunishment(controller, target, type, duration, reason));
					});
				}
				else
				{
					string reason = info.ArgCount > 3 ? info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Replace(info.GetArg(2), string.Empty).Trim() : Localizer.ForPlayer(controller, "k4.general.no-reason");
					ProcessTargetAction(controller, targetResult, target => ApplyPunishment(controller, target, type, duration, reason), failureReason =>
					{
						if (failureReason == TargetFailureReason.TargetNotFound && SteamID.TryParse(targetString, out SteamID? steamId) && steamId?.IsValid() == true)
						{
							ApplyPunishment(controller, steamId, type, duration, reason);
						}
						else if (failureReason == TargetFailureReason.TargetNotFound)
						{
							_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-target"));
						}
					});
				}
			}
			else
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-punish-length"));
			}
		}

		private void ApplyPunishment(CCSPlayerController? caller, SteamID steamId, PunishmentType type, int? duration, string reason)
		{
			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];
			ulong? callerSteamId = caller?.SteamID;
			ulong targetSteamId = steamId.SteamId64;

			_ = Task.Run(async () =>
			{
				string targetName = await GetPlayerNameAsync(targetSteamId);
				await ApplyPunishmentInternal(callerName, callerSteamId, targetSteamId, targetName, type, duration, reason);
			});
		}

		private void ApplyPunishment(CCSPlayerController? caller, CCSPlayerController target, PunishmentType type, int? duration, string reason)
		{
			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];
			ulong? callerSteamId = caller?.SteamID;
			ulong targetSteamId = target.SteamID;
			string targetName = target.PlayerName;

			_ = Task.Run(async () =>
			{
				await ApplyPunishmentInternal(callerName, callerSteamId, targetSteamId, targetName, type, duration, reason);
			});
		}

		private async Task ApplyPunishmentInternal(string callerName, ulong? callerSteamId, ulong targetSteamId, string targetName, PunishmentType type, int? duration, string reason)
		{
			var activePunishments = await GetActivePunishmentsAsync(targetSteamId);

			if (!ValidatePunishment(type, activePunishments, callerSteamId))
				return;

			int punishmentId = await AddPunishmentAsync(targetSteamId, type, duration, reason, callerSteamId);

			Server.NextWorldUpdate(() =>
			{
				if (type != PunishmentType.SilentKick)
				{
					BroadcastPunishment(type, targetName, targetSteamId, duration, reason, callerName, callerSteamId);
					SendDiscordWebhook(type, targetName, targetSteamId, duration, reason, callerName, callerSteamId);
				}

				var target = Utilities.GetPlayerFromSteamId(targetSteamId);
				if (target != null && _playerCache.TryGetValue(targetSteamId, out var playerData))
				{
					UpdatePlayerCache(playerData, punishmentId, type, duration, callerName, reason, callerSteamId);
					ApplyPunishmentEffect(target, type, reason);
				}
			});
		}

		private bool ValidatePunishment(PunishmentType type, List<Punishment> activePunishments, ulong? callerSteamId)
		{
			bool isValid = true;
			string errorMessage = "";

			switch (type)
			{
				case PunishmentType.Silence:
					if (activePunishments.Any(p => p.Type == PunishmentType.Silence) ||
						(activePunishments.Any(p => p.Type == PunishmentType.Mute) && activePunishments.Any(p => p.Type == PunishmentType.Gag)))
					{
						isValid = false;
						errorMessage = Localizer["k4.general.punishment-already-active", "silence"];
					}
					break;
				case PunishmentType.Mute:
				case PunishmentType.Gag:
					if (activePunishments.Any(p => p.Type == PunishmentType.Silence) || activePunishments.Any(p => p.Type == type))
					{
						isValid = false;
						errorMessage = Localizer["k4.general.punishment-already-active", type.ToString().ToLower()];
					}
					break;
				case PunishmentType.Warn:
					break;
				default:
					if (type != PunishmentType.Kick && type != PunishmentType.SilentKick && activePunishments.Any(p => p.Type == type))
					{
						isValid = false;
						errorMessage = Localizer["k4.general.punishment-already-active", type.ToString().ToLower()];
					}
					break;
			}

			if (!isValid)
			{
				Server.NextWorldUpdate(() =>
				{
					CCSPlayerController? caller = callerSteamId != null ? Utilities.GetPlayerFromSteamId((ulong)callerSteamId) : null;
					_moduleServices?.PrintForPlayer(caller, errorMessage);
				});
			}

			return isValid;
		}

		private void UpdatePlayerCache(PlayerData playerData, int punishmentId, PunishmentType type, int? duration, string callerName, string reason, ulong? callerSteamId)
		{
			playerData.Punishments.Add(new Punishment
			{
				Id = punishmentId,
				Type = type,
				Duration = duration,
				ExpiresAt = duration.HasValue && duration.Value != 0 ? new MySqlDateTime(DateTime.Now.AddMinutes(duration.Value)) : null,
				PunisherName = string.IsNullOrEmpty(callerName) ? Localizer["k4.general.console"] : callerName,
				Reason = reason,
				AdminSteamId = callerSteamId
			});
		}

		private void BroadcastPunishment(PunishmentType type, string targetName, ulong targetSteamId, int? duration, string reason, string callerName, ulong? callerSteamId)
		{
			string durationString = "";
			if (type != PunishmentType.Kick && type != PunishmentType.Warn && type != PunishmentType.SilentKick)
			{
				durationString = duration == 0 || duration == null ?
					Localizer["k4.general.permanent"] :
					$"{duration} {Localizer["k4.general.minutes"]}";
			}

			string localizationKey = GetLocalizationKey(type, duration);

			Logger.LogWarning($"Player {targetName} ({targetSteamId}) was {type.ToString().ToLower()}ed by {callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")} {(type != PunishmentType.Kick && type != PunishmentType.Warn && type != PunishmentType.SilentKick ? $"for {durationString} " : "")}({reason})");

			var players = Utilities.GetPlayers();

			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
				{
					if (ShouldShowActivity(callerSteamId, player, true))
					{
						BroadcastToPlayer(player, localizationKey, callerName, targetName, durationString, reason);
					}
					else if (ShouldShowActivity(callerSteamId, player, false))
					{
						BroadcastToPlayer(player, localizationKey, Localizer.ForPlayer(player, "k4.general.admin"), targetName, durationString, reason);
					}
				}
			}
		}

		private string GetLocalizationKey(PunishmentType type, int? duration)
		{
			if (type == PunishmentType.Kick || type == PunishmentType.Warn || type == PunishmentType.SilentKick)
				return $"k4.chat.{type.ToString().ToLower()}";

			return duration == 0 || duration == null ?
				$"k4.chat.{type.ToString().ToLower()}.permanent" :
				$"k4.chat.{type.ToString().ToLower()}";
		}

		private void BroadcastToPlayer(CCSPlayerController player, string localizationKey, string adminName, string targetName, string durationString, string reason)
		{
			if (localizationKey.EndsWith(".permanent"))
			{
				_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, localizationKey, adminName, targetName, reason));
			}
			else if (localizationKey.Contains("kick") || localizationKey.Contains("warn"))
			{
				_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, localizationKey, adminName, targetName, reason));
			}
			else
			{
				_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, localizationKey, adminName, targetName, durationString, reason));
			}
		}

		private void SendDiscordWebhook(PunishmentType type, string targetName, ulong targetSteamId, int? duration, string reason, string callerName, ulong? callerSteamId)
		{
			string durationString = duration == 0 || duration == null ?
				Localizer["k4.general.permanent"] :
				$"{duration} {Localizer["k4.general.minutes"]}";

			SendDiscordWebhookAsync("k4.discord.punishment", type == PunishmentType.Ban, new Dictionary<string, string>
			{
				["player"] = $"[{targetName}](https://steamcommunity.com/profiles/{targetSteamId}) ({targetSteamId})",
				["type"] = type.ToString(),
				["duration"] = type == PunishmentType.Warn || type == PunishmentType.SilentKick || type == PunishmentType.Kick ? "-" : durationString,
				["reason"] = reason,
				["admin"] = $"{(callerSteamId.HasValue ? $"[{callerName}](https://steamcommunity.com/profiles/{callerSteamId})" : callerName)} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}"
			});
		}

		private bool ShouldShowActivity(ulong? adminSteamId, CCSPlayerController player, bool showName)
		{
			if (!adminSteamId.HasValue) return true;
			int _showActivity = _coreAccessor.GetValue<int>("Core", "ShowActivity");

			bool isRoot = AdminManager.PlayerHasPermissions(player, "@zenith/root");
			bool isPlayerAdmin = AdminManager.PlayerHasPermissions(player, "@zenith/admin");

			if (isRoot && (_showActivity & 16) != 0) return true;

			if (isPlayerAdmin)
			{
				if ((_showActivity & 4) == 0) return false;
				if (showName && (_showActivity & 8) == 0) return false;
			}
			else
			{
				if ((_showActivity & 1) == 0) return false;
				if (showName && (_showActivity & 2) == 0) return false;
			}

			return true;
		}

		private void ApplyWarnBan(CCSPlayerController target)
		{
			int banLength = _coreAccessor.GetValue<int>("Config", "WarnBanLength");
			string reason = Localizer["k4.general.max-warnings-reached"];

			Logger.LogWarning($"Player {target.PlayerName} ({target.SteamID}) was banned for reaching the maximum number of warnings");

			ApplyPunishment(null, target, PunishmentType.Ban, banLength, reason);

			_ = Task.Run(async () =>
			{
				await RemovePunishmentAsync(target.SteamID, PunishmentType.Warn, null, "Reached maximum number of warnings");
				if (_playerCache.TryGetValue(target.SteamID, out var playerData))
				{
					playerData.Punishments.RemoveAll(p => p.Type == PunishmentType.Warn);
				}
			});
		}

		private void ApplyPunishmentEffect(CCSPlayerController target, PunishmentType type, string reason)
		{
			var zenithPlayer = GetZenithPlayer(target);
			if (zenithPlayer == null)
			{
				Logger.LogWarning($"Failed to get ZenithPlayer for {target.PlayerName} ({target.SteamID})");
				return;
			}

			if (type == PunishmentType.Silence && _playerCache.TryGetValue(target.SteamID, out var playerData))
			{
				playerData.Punishments.RemoveAll(p => p.Type == PunishmentType.Mute || p.Type == PunishmentType.Gag);

				_ = Task.Run(async () =>
				{
					await RemovePunishmentAsync(target.SteamID, PunishmentType.Mute, null, "Removed by silence");
					await RemovePunishmentAsync(target.SteamID, PunishmentType.Gag, null, "Removed by silence");
				});
			}

			switch (type)
			{
				case PunishmentType.Mute:
					zenithPlayer.SetMute(true, ActionPriority.High);
					break;
				case PunishmentType.Gag:
					zenithPlayer.SetGag(true, ActionPriority.High);
					break;
				case PunishmentType.Silence:
					zenithPlayer.SetMute(true, ActionPriority.High);
					zenithPlayer.SetGag(true, ActionPriority.High);
					break;
				case PunishmentType.Ban:
					DisconnectPlayer(target, NetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED, reason, false);
					break;
				case PunishmentType.Kick:
				case PunishmentType.SilentKick:
					DisconnectPlayer(target, NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED, reason, true);
					break;
			}
		}

		private void HandleRemovePunishmentCommand(CCSPlayerController? controller, CommandInfo info, PunishmentType type)
		{
			if (info.ArgCount < 2)
			{
				_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.invalid-usage", $"un{type.ToString().ToLower()} <player>"));
				return;
			}

			string? reason = info.ArgCount > 2 ? info.GetCommandString.Replace(info.GetArg(0), string.Empty).Replace(info.GetArg(1), string.Empty).Trim() : null;

			ProcessTargetAction(controller, info.GetArgTargetResult(1), target => RemovePunishment(controller, target, type, reason), failureReason =>
			{
				if (failureReason == TargetFailureReason.TargetNotFound)
				{
					if (SteamID.TryParse(info.GetArg(1), out SteamID? steamId) && steamId?.IsValid() == true)
					{
						RemovePunishment(controller, steamId, type, reason);
					}
					else
					{
						_moduleServices?.PrintForPlayer(controller, Localizer.ForPlayer(controller, "k4.general.targetnotfound"));
					}
				}
			});
		}

		private async Task<bool> ValidateRemovalPermissionAsync(ulong targetSteamId, PunishmentType type, uint? removerImmunity = null)
		{
			// Ha nincs bekapcsolva a validáció, akkor mindig engedélyezett
			if (!_coreAccessor.GetValue<bool>("Config", "RemovalValidations"))
				return true;

			// Keressük meg az aktív büntetést
			var punishments = _playerCache.TryGetValue(targetSteamId, out var playerData)
				? playerData.Punishments
				: await GetActivePunishmentsAsync(targetSteamId);

			var punishment = punishments.FirstOrDefault(p => p.Type == type);
			if (punishment == null)
				return false;

			// Ha nincs remover (konzol), akkor mindig true
			if (removerImmunity == null)
				return true;

			// Ha konzol adta a büntetést
			if (!punishment.AdminSteamId.HasValue)
			{
				int consoleImmunity = _coreAccessor.GetValue<int>("Config", "ConsoleImmunity");
				return removerImmunity >= consoleImmunity;
			}

			// Keressük meg a büntetést adó admin immunitását
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $@"
				SELECT COALESCE(pr.immunity, 0) as immunity
				FROM zenith_bans_players p
				LEFT JOIN zenith_bans_player_ranks pr ON p.id = pr.player_id
				WHERE p.steam_id = @SteamId
				AND (pr.server_ip = @ServerIp OR pr.server_ip = 'all')
				ORDER BY CASE
					WHEN pr.server_ip = @ServerIp THEN 0
					WHEN pr.server_ip = 'all' THEN 1
					ELSE 2
				END
				LIMIT 1";

			var punisherImmunity = await connection.QueryFirstOrDefaultAsync<int>(query,
				new { SteamId = punishment.AdminSteamId, ServerIp = _serverIp });

			return removerImmunity >= punisherImmunity;
		}

		private void RemovePunishment(CCSPlayerController? caller, CCSPlayerController target, PunishmentType type, string? reason)
		{
			if (_coreAccessor.GetValue<bool>("Config", "ForceRemovePunishmentReason") && string.IsNullOrWhiteSpace(reason))
			{
				_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.reason-required"));
				return;
			}

			ulong targetSteamId = target.SteamID;

			if (_playerCache.TryGetValue(targetSteamId, out var playerData))
			{
				var activePunishment = playerData.Punishments.FirstOrDefault(p => p.Type == type);
				if (activePunishment == null)
				{
					_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.no-active-punishment", type.ToString().ToLower()));
					return;
				}
			}

			string targetName = target.PlayerName;
			ulong? callerSteamId = caller?.SteamID;
			uint? callerImmunity = caller != null ? AdminManager.GetPlayerImmunity(caller) : null;
			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];

			_ = Task.Run(async () =>
			{
				bool hasPermission = await ValidateRemovalPermissionAsync(targetSteamId, type, callerImmunity);
				if (!hasPermission)
				{
					Server.NextWorldUpdate(() =>
					{
						_moduleServices?.PrintForPlayer(caller, "You don't have permission to remove this punishment due to immunity levels.");
					});
					return;
				}

				bool removed = await RemovePunishmentAsync(targetSteamId, type, callerSteamId, reason);
				ProcessRemovePunishment(removed, caller, callerName, callerSteamId, targetSteamId, targetName, type, reason);
			});
		}

		private void RemovePunishment(CCSPlayerController? caller, SteamID steamId, PunishmentType type, string? reason)
		{
			if (_coreAccessor.GetValue<bool>("Config", "ForceRemovePunishmentReason") && string.IsNullOrWhiteSpace(reason))
			{
				_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.reason-required"));
				return;
			}

			string callerName = caller?.PlayerName ?? Localizer["k4.general.console"];
			ulong? callerSteamId = caller?.SteamID;
			ulong targetSteamId = steamId.SteamId64;
			uint? callerImmunity = caller != null ? AdminManager.GetPlayerImmunity(caller) : null;

			_ = Task.Run(async () =>
			{
				var activePunishments = await GetActivePunishmentsAsync(targetSteamId);
				if (!activePunishments.Any(p => p.Type == type))
				{
					Server.NextWorldUpdate(() =>
					{
						_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.no-active-punishment", type.ToString().ToLower()));
					});
					return;
				}

				bool hasPermission = await ValidateRemovalPermissionAsync(targetSteamId, type, callerImmunity);
				if (!hasPermission)
				{
					Server.NextWorldUpdate(() =>
					{
						_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.no-permission-due-to-immunity"));
					});
					return;
				}

				bool removed = await RemovePunishmentAsync(targetSteamId, type, callerSteamId, reason);
				string targetName = await GetPlayerNameAsync(targetSteamId);
				ProcessRemovePunishment(removed, caller, callerName, callerSteamId, targetSteamId, targetName, type, reason);
			});
		}

		private void ProcessRemovePunishment(bool removed, CCSPlayerController? caller, string callerName, ulong? callerSteamId, ulong targetSteamId, string targetName, PunishmentType type, string? removeReason)
		{
			Server.NextWorldUpdate(() =>
			{
				if (removed)
				{
					if (_playerCache.TryGetValue(targetSteamId, out var playerData))
					{
						playerData.Punishments.RemoveAll(p => p.Type == type);
					}

					var target = Utilities.GetPlayerFromSteamId(targetSteamId);
					if (target != null)
					{
						RemovePunishmentEffect(target, type);
					}

					string logMessage = $"Player {targetName} ({targetSteamId}) was un{type.ToString().ToLower()}ed by {callerName} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}";
					if (!string.IsNullOrEmpty(removeReason))
					{
						logMessage += $" Reason: {removeReason}";
					}
					Logger.LogWarning(logMessage);

					BroadcastRemovePunishment(callerName, callerSteamId, targetName, type, removeReason);

					SendDiscordWebhookAsync("k4.discord.unpunishment", type == PunishmentType.Ban, new Dictionary<string, string>
					{
						["player"] = $"[{targetName}](https://steamcommunity.com/profiles/{targetSteamId}) ({targetSteamId})",
						["type"] = type.ToString(),
						["admin"] = $"{(callerSteamId.HasValue ? $"[{callerName}](https://steamcommunity.com/profiles/{callerSteamId})" : callerName)} {(callerSteamId.HasValue ? $"({callerSteamId})" : "")}",
						["reason"] = removeReason ?? "No reason provided"
					});
				}
				else
				{
					_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "k4.general.no-active-punishment", type.ToString().ToLower()));
				}
			});
		}

		private void BroadcastRemovePunishment(string callerName, ulong? callerSteamId, string targetName, PunishmentType type, string? removeReason)
		{
			var players = Utilities.GetPlayers();
			string punishmentKey = $"k4.chat.un{type.ToString().ToLower()}";
			string punishmentKeyWithReason = $"{punishmentKey}.withreason";

			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
				{
					if (ShouldShowActivity(callerSteamId, player, true))
					{
						if (!string.IsNullOrEmpty(removeReason))
						{
							_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, punishmentKeyWithReason, callerName, targetName, removeReason));
						}
						else
						{
							_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, punishmentKey, callerName, targetName));
						}
					}
					else if (ShouldShowActivity(callerSteamId, player, false))
					{
						string adminName = Localizer.ForPlayer(player, "k4.general.admin");

						if (!string.IsNullOrEmpty(removeReason))
						{
							_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, punishmentKeyWithReason, adminName, targetName, removeReason));
						}
						else
						{
							_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, punishmentKey, adminName, targetName));
						}
					}
				}
			}
		}

		private void RemovePunishmentEffect(CCSPlayerController target, PunishmentType type)
		{
			var zenithPlayer = GetZenithPlayer(target);
			switch (type)
			{
				case PunishmentType.Mute:
					zenithPlayer?.SetMute(false, ActionPriority.High);
					break;
				case PunishmentType.Gag:
					zenithPlayer?.SetGag(false, ActionPriority.High);
					break;
				case PunishmentType.Silence:
					zenithPlayer?.SetMute(false, ActionPriority.High);
					zenithPlayer?.SetGag(false, ActionPriority.High);
					break;
			}
		}

		private void CheckPlayerComms(CCSPlayerController? checker, CCSPlayerController? target)
		{
			if (target == null || checker == null) return;

			_ = Task.Run(async () =>
			{
				var punishments = _playerCache.TryGetValue(target.SteamID, out var playerData)
					? playerData.Punishments
					: await GetActivePunishmentsAsync(target.SteamID);

				var mutePunishment = punishments.FirstOrDefault(p => p.Type == PunishmentType.Mute || p.Type == PunishmentType.Silence);
				var gagPunishment = punishments.FirstOrDefault(p => p.Type == PunishmentType.Gag || p.Type == PunishmentType.Silence);

				Server.NextWorldUpdate(() =>
				{
					_moduleServices?.PrintForPlayer(checker, Localizer.ForPlayer(checker, "k4.commscheck.header", target.PlayerName));
					ProcessCommsPunishment(checker, mutePunishment, "k4.commscheck.mute", "k4.commscheck.no-mute");
					ProcessCommsPunishment(checker, gagPunishment, "k4.commscheck.gag", "k4.commscheck.no-gag");
				});
			});
		}

		private void ProcessCommsPunishment(CCSPlayerController checker, Punishment? punishment, string activeKey, string inactiveKey)
		{
			if (punishment != null)
			{
				string duration = GetPunishmentDuration(checker, punishment);
				bool showAdminName = ShouldShowActivity(punishment.AdminSteamId, checker, true);
				bool showAnonymous = !showAdminName && ShouldShowActivity(punishment.AdminSteamId, checker, false);

				if (showAdminName || showAnonymous)
				{
					string adminName = showAdminName
						? (string.IsNullOrEmpty(punishment.PunisherName) ? Localizer.ForPlayer(checker, "k4.general.console") : punishment.PunisherName)
						: Localizer.ForPlayer(checker, "k4.general.admin");

					_moduleServices?.PrintForPlayer(checker, Localizer.ForPlayer(checker, activeKey, adminName, duration), false);
				}
			}
			else
			{
				_moduleServices?.PrintForPlayer(checker, Localizer.ForPlayer(checker, inactiveKey), false);
			}
		}

		private string GetPunishmentDuration(CCSPlayerController checker, Punishment punishment)
		{
			if (!punishment.ExpiresAt.HasValue)
				return Localizer.ForPlayer(checker, "k4.general.permanent");

			var timeLeft = punishment.ExpiresAt.Value.GetDateTime() - DateTime.Now;

			if (timeLeft <= TimeSpan.Zero)
				return "0 " + Localizer.ForPlayer(checker, "k4.general.minutes");

			return $"{Math.Ceiling(timeLeft.TotalMinutes)} {Localizer.ForPlayer(checker, "k4.general.minutes")}";
		}

		public void AddDisconnectedPlayer(DisconnectedPlayer player)
		{
			_disconnectedPlayers.Insert(0, player);

			if (_disconnectedPlayers.Count > _coreAccessor.GetValue<int>("Config", "DisconnectMaxPlayers"))
			{
				_disconnectedPlayers.RemoveAt(_disconnectedPlayers.Count - 1);
			}
		}

		private void CheckPlayerWarns(CCSPlayerController? checker, CCSPlayerController? target)
		{
			if (target == null) return;

			ulong steamId = target.SteamID;

			_ = Task.Run(async () =>
			{
				var punishments = _playerCache.TryGetValue(steamId, out var playerData) ? playerData.Punishments : await GetActivePunishmentsAsync(steamId);
				var warnings = punishments.Where(p => p.Type == PunishmentType.Warn).ToList();

				Server.NextWorldUpdate(() =>
				{
					_moduleServices?.PrintForPlayer(checker, Localizer.ForPlayer(checker, "k4.warncheck.header", target.PlayerName, warnings.Count));

					if (warnings.Count > 0)
					{
						for (int i = 0; i < warnings.Count; i++)
						{
							var warning = warnings[i];
							string adminName = string.IsNullOrEmpty(warning.PunisherName) ? Localizer.ForPlayer(checker, "k4.general.console") : warning.PunisherName;
							_moduleServices?.PrintForPlayer(checker, Localizer.ForPlayer(checker, "k4.warncheck.warn", i + 1, adminName, warning.Reason), false);
						}
					}
					else
					{
						_moduleServices?.PrintForPlayer(checker, Localizer.ForPlayer(checker, "k4.warncheck.no-warns"), false);
					}
				});
			});
		}

		public void ProcessPlayerData(CCSPlayerController player, bool connect)
		{
			if (string.IsNullOrEmpty(player.IpAddress) || player.IpAddress.Contains("127.0.0.1") || !player.UserId.HasValue)
				return;

			if (player.AuthorizedSteamID is null)
			{
				Logger.LogError($"Failed to get authorize SteamID with Steam API for {player.PlayerName}");
				player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED_NOSTEAMLOGIN);
				return;
			}

			ulong steamId = player.AuthorizedSteamID.SteamId64;
			string playerName = player.PlayerName;
			string ipAddress = player.IpAddress.Split(":")[0];

			_ = Task.Run(async () =>
			{
				PlayerData? playerData = await LoadOrUpdatePlayerDataAsync(steamId, playerName, ipAddress);
				if (playerData == null)
				{
					Logger.LogError($"Failed to load player data for {playerName} ({steamId})");
					return;
				}

				bool applyIPBans = _coreAccessor.GetValue<bool>("Config", "ApplyIPBans");
				bool isIpBanned = applyIPBans && await IsIpBannedAsync(ipAddress);
				bool connectAdminInfo = _coreAccessor.GetValue<bool>("Config", "ConnectAdminInfo");

				Server.NextWorldUpdate(() =>
				{
					if (isIpBanned)
					{
						player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
						return;
					}

					if (playerData.Punishments.Any(p => p.Type == PunishmentType.Ban))
					{
						player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_REJECT_BANNED);
						return;
					}

					if (playerData.Punishments.Count > 0 && connect && connectAdminInfo)
					{
						foreach (var admin in Utilities.GetPlayers())
						{
							if (admin != null && admin.IsValid && !admin.IsBot && !admin.IsHLTV && (AdminManager.PlayerHasPermissions(admin, "@zenith/admin") || AdminManager.PlayerHasPermissions(admin, "@zenith/root")))
							{
								ShowPunishmentSummary(admin, player, playerData);
							}
						}
					}

					AdminManager.SetPlayerImmunity(player, (uint)playerData.Immunity.GetValueOrDefault());
					AdminManager.AddPlayerPermissions(player, playerData.Permissions.Select(p => p.StartsWith('@') ? p : "@" + p).Select(p => p.Replace(" ", "-")).ToArray());

					foreach (var customOverride in playerData.Overrides)
					{
						AdminManager.SetPlayerCommandOverride(player, customOverride.Key, customOverride.Value);
					}

					IPlayerServices? playerServices = GetZenithPlayer(player);
					if (playerData.Punishments.Any(p => p.Type == PunishmentType.Mute && p.ExpiresAt.HasValue && p.ExpiresAt.Value.GetDateTime() > DateTime.Now))
						playerServices?.SetMute(true, ActionPriority.High);

					if (playerData.Punishments.Any(p => p.Type == PunishmentType.Gag && p.ExpiresAt.HasValue && p.ExpiresAt.Value.GetDateTime() > DateTime.Now))
						playerServices?.SetGag(true, ActionPriority.High);

					_playerCache[steamId] = playerData;
					_disconnectedPlayers.RemoveAll(p => p.SteamId == steamId);
				});
			});
		}

		private void AddAdmin(CCSPlayerController? controller, CCSPlayerController target, string group)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			ulong targetSteamId = target.SteamID;

			_ = Task.Run(async () =>
			{
				await AddAdminAsync(targetSteamId, group);
				var (permissions, immunity) = await GetGroupDetailsAsync(group);

				Server.NextWorldUpdate(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.addadmin.success", callerName, target.PlayerName, group]);

					AdminManager.AddPlayerPermissions(target, permissions.ToArray());

					if (immunity.HasValue)
						AdminManager.SetPlayerImmunity(target, (uint)immunity.Value);

					if (_playerCache.TryGetValue(targetSteamId, out var playerData))
					{
						playerData.Groups = [group];
						playerData.Permissions = permissions;
					}

					SendDiscordWebhookAsync("k4.discord.addadmin", false, new Dictionary<string, string>
					{
						["player"] = $"[{target.PlayerName}](https://steamcommunity.com/profiles/{targetSteamId}) ({targetSteamId})",
						["group"] = group,
						["admin"] = $"{(controller != null ? $"[{callerName}](https://steamcommunity.com/profiles/{controller.SteamID})" : callerName)} {(controller != null ? $"({controller.SteamID})" : "")}"
					});
				});
			});
		}

		private List<ulong> GetOnlinePlayersSteamIds()
		{
			var players = Utilities.GetPlayers();
			List<ulong> steamIds = [];

			foreach (var player in players)
			{
				if (player != null && player.IsValid && !player.IsBot && !player.IsHLTV)
				{
					steamIds.Add(player.SteamID);
				}
			}

			return steamIds;
		}

		private void RemoveAdmin(CCSPlayerController? controller, CCSPlayerController target)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			ulong targetSteamId = target.SteamID;

			_ = Task.Run(async () =>
			{
				await RemoveAdminAsync(targetSteamId);

				Server.NextWorldUpdate(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.removeadmin.success", callerName, target.PlayerName]);

					AdminManager.RemovePlayerPermissions(target);
					AdminManager.SetPlayerImmunity(target, 0);

					if (_playerCache.TryGetValue(targetSteamId, out var playerData))
					{
						playerData.Groups = [];
						playerData.Permissions = [];
						playerData.Immunity = null;
					}

					SendDiscordWebhookAsync("k4.discord.removeadmin", false, new Dictionary<string, string>
					{
						["player"] = $"[{target.PlayerName}](https://steamcommunity.com/profiles/{targetSteamId}) ({targetSteamId})",
						["admin"] = callerName
					});
				});
			});
		}

		private void AddOfflineAdmin(CCSPlayerController? controller, SteamID steamId, string group)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			_ = Task.Run(async () =>
			{
				await AddAdminAsync(steamId.SteamId64, group);
				string targetName = await GetPlayerNameAsync(steamId.SteamId64);
				Server.NextWorldUpdate(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.addadmin.success", callerName, targetName, group]);
				});
			});
		}

		private void RemoveOfflineAdmin(CCSPlayerController? controller, SteamID steamId)
		{
			string callerName = controller?.PlayerName ?? Localizer["k4.general.console"];
			ulong targetSteamId = steamId.SteamId64;

			_ = Task.Run(async () =>
			{
				await RemoveAdminAsync(targetSteamId);
				string targetName = await GetPlayerNameAsync(targetSteamId);

				Server.NextWorldUpdate(() =>
				{
					_moduleServices?.PrintForAll(Localizer["k4.removeadmin.success", callerName, targetName]);
				});
			});
		}

		private void ShowPunishmentSummary(CCSPlayerController admin, CCSPlayerController player, PlayerData playerData)
		{
			var zenithAdmin = GetZenithPlayer(admin);
			if (zenithAdmin == null) return;

			int banCount = playerData.Punishments.Count(p => p.Type == PunishmentType.Ban);
			int commsBlockCount = playerData.Punishments.Count(p => p.Type == PunishmentType.Mute || p.Type == PunishmentType.Gag || p.Type == PunishmentType.Silence);
			int warnCount = playerData.Punishments.Count(p => p.Type == PunishmentType.Warn);

			_moduleServices?.PrintForPlayer(admin, Localizer.ForPlayer(admin, "k4.punishment_summary.header", player.PlayerName), false);

			if (banCount > 0 || commsBlockCount > 0 || warnCount > 0)
			{
				if (banCount > 0)
					_moduleServices?.PrintForPlayer(admin, Localizer.ForPlayer(admin, "k4.punishment_summary.bans", banCount), false);

				if (commsBlockCount > 0)
					_moduleServices?.PrintForPlayer(admin, Localizer.ForPlayer(admin, "k4.punishment_summary.comms_blocks", commsBlockCount), false);

				if (warnCount > 0)
					_moduleServices?.PrintForPlayer(admin, Localizer.ForPlayer(admin, "k4.punishment_summary.warns", warnCount), false);
			}
			else
			{
				_moduleServices?.PrintForPlayer(admin, Localizer.ForPlayer(admin, "k4.punishment_summary.clean"), false);
			}
		}

		private void SendDiscordWebhookAsync(string localizerKey, bool isBan, Dictionary<string, string> replacements)
		{
			string _webhookUrl = isBan ? _coreAccessor.GetValue<string>("Config", "BanDiscordWebhookUrl") : _coreAccessor.GetValue<string>("Config", "OtherDiscordWebhookUrl");
			if (string.IsNullOrEmpty(_webhookUrl))
				return;

			string jsonPayload = Localizer[localizerKey];

			foreach (var kvp in replacements)
			{
				jsonPayload = jsonPayload.Replace($"{{{kvp.Key}}}", kvp.Value);
			}

			jsonPayload = jsonPayload.Replace("{timestamp}", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

			var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

			try
			{
				_ = Task.Run(async () =>
				{
					var response = await _httpClient.PostAsync(_webhookUrl, content);
					if (!response.IsSuccessStatusCode)
					{
						Logger.LogError($"Failed to send Discord webhook. Status code: {response.StatusCode}");
					}
				});
			}
			catch (Exception ex)
			{
				Logger.LogError($"Error sending Discord webhook: {ex.Message}");
			}
		}

		public SteamID GetSteamID(string input) =>
			ulong.TryParse(input, out ulong steamId) ? new SteamID(steamId) : new SteamID(input);

		public void DisconnectPlayer(CCSPlayerController player, NetworkDisconnectionReason reason, string stringReason, bool kick)
		{
			int delay = _coreAccessor.GetValue<int>("Config", "DelayPlayerRemoval");
			if (delay <= 0)
			{
				player.Disconnect(reason);
				return;
			}

			if (player.PlayerPawn.Value?.IsValid == true)
			{
				Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_MoveType");
				Schema.GetRef<MoveType_t>(player.PlayerPawn.Value.Handle, "CBaseEntity", "m_nActualMoveType") = MoveType_t.MOVETYPE_OBSOLETE;
			}

			int countdown = delay;
			player.PrintToCenterAlert(Localizer.ForPlayer(player, kick ? "k4.alert.kick" : "k4.alert.ban", countdown, stringReason));

			_disconnectTImers[player] = AddTimer(1.0f, () =>
			{
				countdown--;

				if (player.IsValid)
				{
					player.PrintToCenterAlert(Localizer.ForPlayer(player, kick ? "k4.alert.kick" : "k4.alert.ban", countdown, stringReason));

					if (countdown <= 0)
					{
						player.Disconnect(reason);
						StopTimer();
					}
				}
				else
					StopTimer();

				void StopTimer()
				{
					if (_disconnectTImers.ContainsKey(player))
						_disconnectTImers[player]?.Kill();

					_disconnectTImers.Remove(player);
				}
			}, TimerFlags.REPEAT);
		}
	}
}