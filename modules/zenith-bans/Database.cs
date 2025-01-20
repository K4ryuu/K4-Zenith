using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Entities;
using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Zenith_Bans
{
	public sealed partial class Plugin : BasePlugin
	{
		private readonly Dictionary<ulong, PlayerData> _playerCache = [];

		private async Task<PlayerData> LoadOrUpdatePlayerDataAsync(ulong steamId, string playerName, string ipAddress)
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var checkPlayerQuery = $"SELECT `id` FROM `zenith_bans_players` WHERE `steam_id` = @SteamId";
				var existingPlayerId = await connection.ExecuteScalarAsync<int?>(checkPlayerQuery, new { SteamId = steamId });

				int playerId;
				bool isNewPlayer = !existingPlayerId.HasValue;
				ipAddress = ipAddress.Split(':')[0];

				if (isNewPlayer)
				{
					var insertQuery = $@"
                        INSERT INTO `zenith_bans_players` (`steam_id`, `name`, `last_online`, `current_server`)
                        VALUES (@SteamId, @PlayerName, NOW(), @ServerIp);
                        SELECT LAST_INSERT_ID();";
					playerId = await connection.ExecuteScalarAsync<int>(insertQuery, new { SteamId = steamId, PlayerName = playerName, ServerIp = _serverIp });
				}
				else
				{
					playerId = existingPlayerId.Value;
					var updateQuery = $@"
                        UPDATE `zenith_bans_players`
                        SET `name` = @PlayerName, `last_online` = NOW(), `current_server` = @ServerIp
                        WHERE `steam_id` = @SteamId";
					await connection.ExecuteAsync(updateQuery, new { SteamId = steamId, PlayerName = playerName, ServerIp = _serverIp });
				}

				var ipCheckQuery = $@"
					SELECT 1
					FROM `zenith_bans_ip_addresses`
					WHERE `player_id` = @PlayerId AND `ip_address` = @IpAddress";

				var exists = await connection.ExecuteScalarAsync<int?>(ipCheckQuery, new { PlayerId = playerId, IpAddress = ipAddress });

				if (!exists.HasValue)
				{
					var ipInsertQuery = $@"
						INSERT INTO `zenith_bans_ip_addresses` (`player_id`, `ip_address`)
						VALUES (@PlayerId, @IpAddress)";

					await connection.ExecuteAsync(ipInsertQuery, new { PlayerId = playerId, IpAddress = ipAddress });
				}

				if (isNewPlayer)
				{
					return new PlayerData
					{
						SteamId = steamId,
						Name = playerName,
						IpAddress = ipAddress,
						Groups = [],
						Permissions = [],
						Punishments = []
					};
				}
				else
				{
					var selectPlayerQuery = $@"
						WITH PlayerRanks AS (
							SELECT
								pr.*,
								ROW_NUMBER() OVER (
									PARTITION BY pr.player_id
									ORDER BY
										CASE
											WHEN pr.server_ip = @ServerIp THEN 0
											WHEN pr.server_ip = 'all' THEN 1
											ELSE 2
										END
								) as rank_priority
							FROM zenith_bans_player_ranks pr
							WHERE (pr.server_ip = @ServerIp OR pr.server_ip = 'all')
						),
						GroupImmunity AS (
							SELECT
								pg.player_rank_id,
								MAX(ag.immunity) as max_group_immunity
							FROM zenith_bans_player_groups pg
							LEFT JOIN zenith_bans_admin_groups ag ON pg.group_name = ag.name
							GROUP BY pg.player_rank_id
						)
						SELECT
							p.id,
							p.steam_id,
							p.name,
							p.last_online,
							p.current_server,
							GROUP_CONCAT(DISTINCT ipa.ip_address) AS IpAddresses,
							COALESCE(
								CASE
									WHEN pr.immunity >= COALESCE(gi.max_group_immunity, 0) THEN pr.immunity
									ELSE gi.max_group_immunity
								END,
								0
							) AS Immunity,
							pr.rank_expiry AS RankExpiry,
							GROUP_CONCAT(DISTINCT pg.group_name) AS GroupsString,
							GROUP_CONCAT(DISTINCT pp.permission) AS PermissionsString,
							GROUP_CONCAT(DISTINCT agp.permission) AS GroupPermissionsString,
							GROUP_CONCAT(DISTINCT CONCAT(po.command, ':', po.value)) AS OverridesString
						FROM zenith_bans_players p
						LEFT JOIN zenith_bans_ip_addresses ipa ON p.id = ipa.player_id
						LEFT JOIN PlayerRanks pr ON p.id = pr.player_id AND pr.rank_priority = 1
						LEFT JOIN zenith_bans_player_groups pg ON pr.id = pg.player_rank_id
						LEFT JOIN zenith_bans_player_permissions pp ON pr.id = pp.player_rank_id
						LEFT JOIN GroupImmunity gi ON pr.id = gi.player_rank_id
						LEFT JOIN zenith_bans_admin_groups ag ON pg.group_name = ag.name
						LEFT JOIN zenith_bans_admin_group_permissions agp ON ag.id = agp.group_id
						LEFT JOIN zenith_bans_player_overrides po ON pr.id = po.player_rank_id
						WHERE p.steam_id = @SteamId
						GROUP BY
							p.id,
							p.steam_id,
							p.name,
							p.last_online,
							p.current_server,
							pr.id,
							pr.immunity,
							pr.rank_expiry
						LIMIT 1";

					var playerDataRaw = await connection.QuerySingleOrDefaultAsync<PlayerDataRaw>(selectPlayerQuery, new { SteamId = steamId, ServerIp = _serverIp });

					var punishments = await GetActivePunishmentsAsync(steamId);

					var playerData = new PlayerData
					{
						SteamId = steamId,
						Name = playerName,
						IpAddress = ipAddress,
						Immunity = playerDataRaw?.Immunity,
						RankExpiry = playerDataRaw?.RankExpiry,
						Punishments = punishments
					};

					if (playerDataRaw != null)
					{
						playerData.Groups = playerDataRaw.GroupsString?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [];
						playerData.Permissions = (playerDataRaw.PermissionsString?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? [])
							.Concat(playerDataRaw.GroupPermissionsString?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>())
							.Distinct()
							.ToList();
						playerData.Overrides = playerDataRaw.OverridesString?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(overrideStr =>
						{
							var parts = overrideStr.Split(':');
							return parts.Length == 2 ? (parts[0], bool.Parse(parts[1])) : (null, false);
						}).Where(x => x.Item1 != null).ToDictionary(x => x.Item1!, x => x.Item2) ?? [];

						DateTime? rankExpiry = playerData.RankExpiry?.IsValidDateTime == true ? (DateTime?)playerData.RankExpiry : null;

						if (rankExpiry <= DateTime.Now)
						{
							playerData.Groups = [];
							playerData.Permissions = [];
							playerData.Overrides = [];
							playerData.Immunity = null;
							await UpdatePlayerRankAsync(steamId, null, null, null, null, null, _serverIp);
						}
					}
					else
					{
						playerData.Groups = [];
						playerData.Permissions = [];
						playerData.Overrides = [];
					}

					return playerData;
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Failed to load or update player data for SteamID: {SteamId}", steamId);
				return new PlayerData
				{
					SteamId = steamId,
					Name = playerName,
					IpAddress = ipAddress,
					Groups = [],
					Permissions = [],
					Punishments = []
				};
			}
		}

		private async Task HandlePlayerDisconnectAsync(ulong steamId)
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
                    UPDATE `zenith_bans_players`
                    SET `current_server` = NULL
                    WHERE `steam_id` = @SteamId;";

				await connection.ExecuteAsync(query, new { SteamId = steamId });

				_playerCache.Remove(steamId);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error handling player disconnect for SteamID: {SteamId}", steamId);
			}
		}

		private async Task<bool> IsIpBannedAsync(string ipAddress)
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
                    SELECT COUNT(DISTINCT p.player_id)
                    FROM `zenith_bans_punishments` p
                    JOIN `zenith_bans_players` pl ON p.`player_id` = pl.`id`
                    JOIN `zenith_bans_ip_addresses` ipa ON pl.`id` = ipa.`player_id`
                    WHERE ipa.`ip_address` = @IpAddress
                    AND p.`type` = 'ban'
                    AND (p.`expires_at` > NOW() OR p.`expires_at` IS NULL)
                    AND p.`status` = 'active'";

				int count = await connection.ExecuteScalarAsync<int>(query, new { IpAddress = ipAddress });
				return count > 0;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Failed to check if IP address is banned: {IpAddress}", ipAddress);
				return false;
			}
		}

		private async Task UpdatePlayerRankAsync(ulong steamId, List<string>? groups, List<string>? permissions, Dictionary<string, bool>? overrides, int? immunity, MySqlDateTime? expiry, string serverIp = "all")
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var playerIdQuery = $"SELECT `id` FROM `zenith_bans_players` WHERE `steam_id` = @SteamId";
				int playerId = await connection.ExecuteScalarAsync<int>(playerIdQuery, new { SteamId = steamId });

				if (playerId == 0)
				{
					Logger.LogError("Player ID not found for SteamID: {SteamId}", steamId);
					return;
				}

				var rankQuery = $@"
					INSERT INTO `zenith_bans_player_ranks`
					(`player_id`, `server_ip`, `immunity`, `rank_expiry`)
					VALUES (@PlayerId, @ServerIp, @Immunity, @Expiry)
					ON DUPLICATE KEY UPDATE
					`immunity` = @Immunity,
					`rank_expiry` = @Expiry;
					SELECT `id` FROM `zenith_bans_player_ranks`
					WHERE `player_id` = @PlayerId AND `server_ip` = @ServerIp;";

				int rankId = await connection.ExecuteScalarAsync<int>(rankQuery, new
				{
					PlayerId = playerId,
					ServerIp = serverIp,
					Immunity = immunity,
					Expiry = expiry?.GetDateTime()
				});

				await connection.ExecuteAsync($"DELETE FROM `zenith_bans_player_groups` WHERE `player_rank_id` = (SELECT `id` FROM `zenith_bans_player_ranks` WHERE `player_id` = @PlayerId AND `server_ip` = @ServerIp)", new { PlayerId = playerId, ServerIp = serverIp });
				await connection.ExecuteAsync($"DELETE FROM `zenith_bans_player_permissions` WHERE `player_rank_id` = (SELECT `id` FROM `zenith_bans_player_ranks` WHERE `player_id` = @PlayerId AND `server_ip` = @ServerIp)", new { PlayerId = playerId, ServerIp = serverIp });
				await connection.ExecuteAsync($"DELETE FROM `zenith_bans_player_overrides` WHERE `player_rank_id` = (SELECT `id` FROM `zenith_bans_player_ranks` WHERE `player_id` = @PlayerId AND `server_ip` = @ServerIp)", new { PlayerId = playerId, ServerIp = serverIp });

				bool changes = false;

				if (groups != null && groups.Count != 0)
				{
					changes = true;
					var groupInsertQuery = $@"
						INSERT INTO `zenith_bans_player_groups` (`player_rank_id`, `group_name`)
						VALUES {string.Join(", ", groups.Select((_, index) => $"(@RankId, @GroupName{index})"))};";
					var groupParams = new DynamicParameters();
					groupParams.Add("RankId", rankId);
					for (int i = 0; i < groups.Count; i++)
					{
						groupParams.Add($"GroupName{i}", groups[i]);
					}
					await connection.ExecuteAsync(groupInsertQuery, groupParams);
				}

				if (permissions != null && permissions.Count != 0)
				{
					changes = true;
					var permissionInsertQuery = $@"
						INSERT INTO `zenith_bans_player_permissions` (`player_rank_id`, `permission`)
						VALUES {string.Join(", ", permissions.Select((_, index) => $"(@RankId, @Permission{index})"))};";
					var permissionParams = new DynamicParameters();
					permissionParams.Add("RankId", rankId);
					for (int i = 0; i < permissions.Count; i++)
					{
						permissionParams.Add($"Permission{i}", permissions[i]);
					}
					await connection.ExecuteAsync(permissionInsertQuery, permissionParams);
				}

				if (overrides != null && overrides.Count != 0)
				{
					changes = true;
					var overrideInsertQuery = $@"
						INSERT INTO `zenith_bans_player_overrides` (`player_rank_id`, `command`, `value`)
						VALUES {string.Join(", ", overrides.Select((pair, index) => $"(@RankId, @Command{index}, @Value{index})"))};";
					var overrideParams = new DynamicParameters();
					overrideParams.Add("RankId", rankId);
					for (int i = 0; i < overrides.Count; i++)
					{
						var (command, value) = overrides.ElementAt(i);
						overrideParams.Add($"Command{i}", command);
						overrideParams.Add($"Value{i}", value);
					}
					await connection.ExecuteAsync(overrideInsertQuery, overrideParams);
				}

				if (changes)
				{
					Server.NextWorldUpdate(() =>
					{
						var player = Utilities.GetPlayerFromSteamId(steamId);
						if (player != null)
							ProcessPlayerData(player, false);
					});
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Failed to update player rank for SteamID: {SteamId}", steamId);
			}
		}

		private async Task<int> AddPunishmentAsync(ulong targetSteamId, PunishmentType type, int? duration, string reason, ulong? adminSteamId)
		{
			if (type == PunishmentType.SilentKick)
				return -1;

			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var playerIdQuery = $"SELECT `id` FROM `zenith_bans_players` WHERE `steam_id` = @SteamId";
			int playerId = await connection.ExecuteScalarAsync<int>(playerIdQuery, new { SteamId = targetSteamId });

			int? adminId = null;
			if (adminSteamId.HasValue)
			{
				adminId = await connection.ExecuteScalarAsync<int?>(playerIdQuery, new { SteamId = adminSteamId.Value });
			}

			var query = $@"
                INSERT INTO `zenith_bans_punishments`
                (`player_id`, `type`, `status`, `duration`, `created_at`, `expires_at`, `admin_id`, `reason`, `server_ip`)
                VALUES
                (@PlayerId, @Type, @Status, @Duration, NOW(),
                    CASE
                        WHEN @Type = 'ban' AND (@Duration IS NULL OR @Duration = 0) THEN NULL
                        WHEN @Duration IS NULL THEN NULL
                        ELSE DATE_ADD(NOW(), INTERVAL @Duration MINUTE)
                    END,
                @AdminId, @Reason, @ServerIp);
                SELECT LAST_INSERT_ID();";

			return await connection.ExecuteScalarAsync<int>(query, new
			{
				PlayerId = playerId,
				Type = type.ToString().ToLower(),
				Status = type == PunishmentType.Kick ? "removed" : "active",
				Duration = duration,
				AdminId = adminId,
				Reason = reason,
				ServerIp = _coreAccessor.GetValue<bool>("Config", "GlobalPunishments") ? "all" : _serverIp
			});
		}

		private async Task<bool> RemovePunishmentAsync(ulong targetSteamId, PunishmentType type, ulong? removerSteamId, string? removeReason)
		{
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var playerIdQuery = $"SELECT `id` FROM `zenith_bans_players` WHERE `steam_id` = @SteamId";
			int playerId = await connection.ExecuteScalarAsync<int>(playerIdQuery, new { SteamId = targetSteamId });

			int? removerId = null;
			if (removerSteamId.HasValue)
			{
				removerId = await connection.ExecuteScalarAsync<int?>(playerIdQuery, new { SteamId = removerSteamId.Value });
			}

			var query = $@"
                UPDATE `zenith_bans_punishments`
                SET `status` = CASE WHEN @RemoverId IS NULL THEN 'removed_console' ELSE 'removed' END,
                    `removed_at` = NOW(),
                    `remove_admin_id` = @RemoverId,
                    `remove_reason` = @RemoveReason
                WHERE `player_id` = @PlayerId AND `type` = @Type
                AND (`server_ip` = 'all' OR `server_ip` = @ServerIp)
                AND `status` = 'active'";

			int affectedRows = await connection.ExecuteAsync(query, new
			{
				PlayerId = playerId,
				Type = type.ToString().ToLower(),
				RemoverId = removerId,
				RemoveReason = removeReason,
				ServerIp = _serverIp
			});
			return affectedRows > 0;
		}

		private async Task<List<Punishment>> GetActivePunishmentsAsync(ulong steamId)
		{
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $@"
                SELECT p.id, p.type, p.duration, p.expires_at AS ExpiresAt,
                    COALESCE(admin.name, 'Console') AS PunisherName, admin.steam_id AS AdminSteamId, p.reason
                FROM `zenith_bans_punishments` p
                LEFT JOIN `zenith_bans_players` pl ON p.player_id = pl.id
                LEFT JOIN `zenith_bans_players` admin ON p.admin_id = admin.id
                WHERE pl.steam_id = @SteamId
                AND (p.server_ip = 'all' OR p.server_ip = @ServerIp)
                AND p.status = 'active'
                AND (p.type = 'warn' OR (p.expires_at > NOW() OR p.expires_at IS NULL))";

			var punishments = await connection.QueryAsync<Punishment>(query, new { SteamId = steamId, ServerIp = _serverIp });

			return punishments.ToList();
		}

		private async Task<string> GetPlayerNameAsync(ulong steamId)
		{
			using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
			await connection.OpenAsync();

			var query = $"SELECT `name` FROM `zenith_bans_players` WHERE `steam_id` = @SteamId";
			return await connection.ExecuteScalarAsync<string>(query, new { SteamId = steamId }) ?? "Unknown";
		}

		private async Task RemoveOfflinePlayersFromServerAsync(IEnumerable<ulong> onlineSteamIds)
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				string query;
				var parameters = new DynamicParameters();
				parameters.Add("ServerIp", _serverIp);

				if (onlineSteamIds.Any())
				{
					// If the list is not empty, construct the IN clause dynamically
					var steamIdList = string.Join(", ", onlineSteamIds.Select((id, index) => $"@steamId{index}"));

					query = $@"
						UPDATE `zenith_bans_players`
						SET `current_server` = NULL
						WHERE `current_server` = @ServerIp
						AND `steam_id` NOT IN ({steamIdList});";

					// Create parameters for each Steam ID
					int i = 0;
					foreach (var steamId in onlineSteamIds)
					{
						parameters.Add($"steamId{i}", steamId);
						i++;
					}
				}
				else
				{
					// If the list is empty, update all players with current_server = @ServerIp
					query = $@"
						UPDATE `zenith_bans_players`
						SET `current_server` = NULL
						WHERE `current_server` = @ServerIp;";
				}

				await connection.ExecuteAsync(query, parameters);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error removing offline players from server.");
			}
		}

		private async Task RemoveExpiredPunishmentsAsync(IEnumerable<ulong> onlineSteamIds)
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
                    UPDATE `zenith_bans_punishments` p
                    JOIN `zenith_bans_players` pl ON p.`player_id` = pl.`id`
                    SET p.`status` = 'expired',
                        p.`removed_at` = NOW(),
                        p.`remove_admin_id` = NULL
                    WHERE p.`expires_at` <= NOW()
                    AND p.`expires_at` IS NOT NULL
                    AND p.`status` = 'active'
                    AND p.`type` IN ('mute', 'gag', 'silence', 'ban');

                    SELECT p.`player_id`, p.`type`, pl.`name` AS `player_name`, pl.`current_server`, pl.`steam_id`
                    FROM `zenith_bans_punishments` p
                    JOIN `zenith_bans_players` pl ON p.`player_id` = pl.`id`
                    WHERE p.`expires_at` <= NOW()
                    AND p.`expires_at` IS NOT NULL
                    AND p.`status` = 'expired'
                    AND p.`removed_at` = NOW()
                    AND p.`type` IN ('mute', 'gag', 'silence', 'ban');";

				using var multi = await connection.QueryMultipleAsync(query);
				var removedPunishments = await multi.ReadAsync<(int PlayerId, string Type, string PlayerName, string CurrentServer, ulong SteamId)>();

				bool notifyAdmins = _coreAccessor.GetValue<bool>("Config", "NotifyAdminsOnBanExpire");

				Server.NextWorldUpdate(() =>
				{
					foreach (var (playerId, type, playerName, currentServer, steamId) in removedPunishments)
					{
						var player = Utilities.GetPlayerFromSteamId(steamId);
						if (player != null && _playerCache.TryGetValue(steamId, out var playerData))
						{
							playerData.Punishments.RemoveAll(p => p.Type.ToString().Equals(type, StringComparison.OrdinalIgnoreCase) && p.ExpiresAt?.GetDateTime() <= DateTime.Now);
							RemovePunishmentEffect(player, Enum.Parse<PunishmentType>(type, true));

							_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, $"k4.punishment.expired.{type.ToLower()}"));
						}

						if (notifyAdmins && type.Equals("ban", StringComparison.OrdinalIgnoreCase))
						{
							NotifyAdminsAboutExpiredBan(playerName, steamId);
						}
					}
				});
			}
			catch (Exception ex)
			{
				Logger.LogError($"Failed to remove expired punishments: {ex.Message}");
			}
		}

		private async Task<(List<string> Permissions, int? Immunity)> GetGroupDetailsAsync(string groupName)
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
					SELECT ag.immunity, agp.permission
					FROM `zenith_bans_admin_groups` ag
					LEFT JOIN `zenith_bans_admin_group_permissions` agp ON ag.id = agp.group_id
					WHERE ag.name = @GroupName";
				var results = await connection.QueryAsync<dynamic>(query, new { GroupName = groupName });

				if (!results.Any())
					return ([], null);

				var permissions = new List<string>();
				int? immunity = null;

				foreach (var row in results)
				{
					if (immunity == null)
						immunity = (int?)row.immunity;

					if (row.permission != null)
						permissions.Add(row.permission.ToString());
				}

				return (permissions, immunity);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error getting group details for group: {GroupName}", groupName);
				return ([], null);
			}
		}

		private async Task<List<string>> GetAdminGroupsAsync()
		{
			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $"SELECT `name` FROM `zenith_bans_admin_groups`";
				var groups = await connection.QueryAsync<string>(query);
				return groups.ToList();
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error getting admin groups");
				return [];
			}
		}

		private async Task AddAdminAsync(ulong steamId, string groupName, string serverIp = "all")
		{
			await UpdatePlayerRankAsync(steamId, [groupName], null, null, null, null, serverIp);
		}

		private async Task RemoveAdminAsync(ulong steamId, string serverIp = "all")
		{
			await UpdatePlayerRankAsync(steamId, [], null, null, null, null, serverIp);
		}

		private async Task ImportAdminGroupsFromJsonAsync(string directory)
		{
			string adminGroupsPath = Path.Combine(directory, "csgo", "addons", "counterstrikesharp", "configs", "admin_groups.json");

			if (!File.Exists(adminGroupsPath))
				return;

			try
			{
				string jsonContent = await File.ReadAllTextAsync(adminGroupsPath);
				var adminGroups = JsonSerializer.Deserialize<Dictionary<string, AdminGroupInfo>>(jsonContent);

				if (adminGroups == null || adminGroups.Count == 0)
					return;

				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				foreach (var group in adminGroups)
				{
					string groupName = group.Key;
					var groupInfo = group.Value;

					var query = $@"
						INSERT INTO `zenith_bans_admin_groups` (`name`, `immunity`)
						VALUES (@Name, @Immunity)
						ON DUPLICATE KEY UPDATE
						`immunity` = @Immunity";

					await connection.ExecuteAsync(query, new
					{
						Name = groupName,
						Immunity = groupInfo.Immunity
					});

					var permissionQuery = $@"
						INSERT INTO `zenith_bans_admin_group_permissions` (`group_id`, `permission`)
						SELECT ag.id, @Permission
						FROM `zenith_bans_admin_groups` ag
						WHERE ag.name = @GroupName
						AND NOT EXISTS (
							SELECT 1
							FROM `zenith_bans_admin_group_permissions` agp
							WHERE agp.group_id = ag.id AND agp.permission = @Permission
						)";

					var uniquePermissions = groupInfo.Flags.Distinct();
					foreach (var permission in uniquePermissions)
					{
						await connection.ExecuteAsync(permissionQuery, new
						{
							GroupName = groupName,
							Permission = permission
						});
					}
				}

				Logger.LogInformation($"Successfully imported {adminGroups.Count} admin groups from local JSON. You can disable this feature in the config.");
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error importing admin groups from JSON.");
			}
		}

		private async Task ImportAdminsFromJsonAsync(string directory)
		{
			try
			{
				string adminsPath = Path.Combine(directory, "csgo", "addons", "counterstrikesharp", "configs", "admins.json");
				if (!File.Exists(adminsPath))
					return;

				var admins = JsonSerializer.Deserialize<Dictionary<string, AdminInfo>>(await File.ReadAllTextAsync(adminsPath));
				if (admins == null)
					return;

				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				int importedCount = 0;
				foreach (var admin in admins)
				{
					var adminInfo = admin.Value;

					SteamID? convertedSteamID = null;
					if (!ulong.TryParse(adminInfo.identity, out ulong steamId) && !SteamID.TryParse(adminInfo.identity, out convertedSteamID))
					{
						Logger.LogError($"Invalid identifier for {admin.Key}: {adminInfo.identity}");
						continue;
					}

					if (convertedSteamID != null)
					{
						steamId = convertedSteamID.SteamId64;
					}

					// Először hozzuk létre/ellenőrizzük a játékost
					await connection.ExecuteAsync(
						"INSERT IGNORE INTO zenith_bans_players (steam_id, name) VALUES (@SteamId, @Name)",
						new { SteamId = steamId, Name = admin.Key }
					);

					await UpdatePlayerRankAsync(
						steamId,
						adminInfo.groups,
						adminInfo.flags,
						adminInfo.command_overrides,
						adminInfo.immunity,
						null,
						_serverIp
					);

					importedCount++;
				}

				Logger.LogInformation($"Successfully imported {importedCount} admins from local JSON. You can disable this feature in the config.");
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error importing admins from JSON");
			}
		}

		private class AdminInfo
		{
			public required string identity { get; set; }
			public int? immunity { get; set; }
			public List<string>? flags { get; set; }
			public List<string>? groups { get; set; }
			public Dictionary<string, bool>? command_overrides { get; set; }
		}

		private async Task<List<PlayerInfo>> FindPlayersByNameOrPartialNameAsync(string name)
		{
			var players = new List<PlayerInfo>();

			try
			{
				using var connection = new MySqlConnection(_moduleServices?.GetConnectionString());
				await connection.OpenAsync();

				var query = $@"
                    SELECT steam_id, name
                    FROM `zenith_bans_players`
                    WHERE name LIKE @Name";
				var results = await connection.QueryAsync<dynamic>(query, new { Name = $"%{name}%" });

				players = results.Select(r => new PlayerInfo
				{
					SteamID = (ulong)r.steam_id,
					PlayerName = r.name
				}).ToList();
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Error querying players by name: {Name}", name);
			}

			return players;
		}

		private void NotifyAdminsAboutExpiredBan(string playerName, ulong steamId)
		{
			var players = Utilities.GetPlayers();
			foreach (var admin in players)
			{
				if (admin.IsValid && !admin.IsBot && !admin.IsHLTV && (AdminManager.PlayerHasPermissions(admin, "@zenith/admin") || AdminManager.PlayerHasPermissions(admin, "@zenith/root") || AdminManager.PlayerHasPermissions(admin, "@css/root")))
				{
					_moduleServices?.PrintForPlayer(admin, Localizer.ForPlayer(admin, "k4.admin.ban_expired", playerName, steamId));
				}
			}
		}
	}
}