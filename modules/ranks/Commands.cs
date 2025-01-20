
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Utils;
using Menu;
using Menu.Enums;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	public void OnRanksCommand(CCSPlayerController? player, CommandInfo info)
	{
		if (player == null) return;
		if (!_playerCache.TryGetValue(player!, out var playerServices))
		{
			info.ReplyToCommand($" {Localizer.ForPlayer(player, "k4.general.prefix")} {Localizer.ForPlayer(player, "k4.general.loading")}");
			return;
		}
		if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
		{
			ShowCenterRanksList(playerServices);
		}
		else
		{
			ShowChatRanksList(playerServices);
		}
	}

	private void ShowCenterRanksList(IPlayerServices player)
	{
		List<MenuItem> items = [];
		foreach (var rank in Ranks)
		{
			string formattedPoints = FormatPoints(rank.Point);
			string rankInfo = $"<font color='{rank.HexColor}'>{rank.Name}</font>: {formattedPoints} {Localizer.ForPlayer(player.Controller, "k4.ranks.points")}";
			items.Add(new MenuItem(MenuItemType.Button, [new MenuValue(rankInfo)]));
		}
		Menu.ShowScrollableMenu(player.Controller, Localizer.ForPlayer(player.Controller, "k4.ranks.list.title"), items, (buttons, menu, selected) =>
		{
			// No action needed when an item is selected, as we're just displaying information
			// Can be extended later if needed
		}, false, _coreAccessor.GetValue<bool>("Core", "FreezeInMenu") && player.GetSetting<bool>("FreezeInMenu", "K4-Zenith"), 5, disableDeveloper: !_coreAccessor.GetValue<bool>("Core", "ShowDevelopers"));
	}

	private void ShowChatRanksList(IPlayerServices player)
	{
		ChatMenu menu = new ChatMenu(Localizer.ForPlayer(player.Controller, "k4.ranks.list.title"));
		foreach (var rank in Ranks)
		{
			string formattedPoints = FormatPoints(rank.Point);
			string rankInfo = $"{rank.ChatColor}{rank.Name}{ChatColors.Default}: {formattedPoints} {Localizer.ForPlayer(player.Controller, "k4.ranks.points")}";
			menu.AddMenuOption(rankInfo, (p, o) => { });
		}
		MenuManager.OpenChatMenu(player.Controller, menu);
	}

	public void OnRankCommand(CCSPlayerController? player, CommandInfo info)
	{
		if (!_playerCache.TryGetValue(player!, out var playerServices))
		{
			info.ReplyToCommand($" {Localizer.ForPlayer(player, "k4.general.prefix")} {Localizer.ForPlayer(player, "k4.general.loading")}");
			return;
		}

		var playerData = GetOrUpdatePlayerRankInfo(playerServices);

		long pointsToNextRank = playerData.NextRank != null ? playerData.NextRank.Point - playerData.Points : 0;

		if (_coreAccessor.GetValue<bool>("Core", "CenterMenuMode"))
		{
			string htmlMessage = $@"
				<font color='#ff3333' class='fontSize-m'>{Localizer.ForPlayer(player, "k4.ranks.info.title")}</font><br>
				<font color='#FF6666' class='fontSize-sm'>{Localizer.ForPlayer(player, "k4.ranks.info.current")}</font> <font color='{playerData.Rank?.HexColor ?? "#FFFFFF"}' class='fontSize-s'>{playerData.Rank?.Name ?? Localizer.ForPlayer(player, "k4.phrases.rank.none")}</font><br>
				<font color='#FF6666' class='fontSize-sm'>{Localizer.ForPlayer(player, "k4.ranks.info.points")}</font> <font color='#FFFFFF' class='fontSize-s'>{playerData.Points:N0}</font>";

			if (playerData.NextRank != null)
			{
				htmlMessage += $@"
					<br><font color='#FF6666' class='fontSize-sm'>{Localizer.ForPlayer(player, "k4.ranks.info.next")}</font> <font color='{playerData.NextRank.HexColor}' class='fontSize-s'>{playerData.NextRank.Name}</font><br>
					<font color='#FF6666' class='fontSize-sm'>{Localizer.ForPlayer(player, "k4.ranks.info.pointstonext")}</font> <font color='#FFFFFF' class='fontSize-s'>{pointsToNextRank:N0}</font>";
			}

			playerServices.PrintToCenter(htmlMessage, _configAccessor.GetValue<int>("Core", "CenterMessageTime"), ActionPriority.Low);
		}
		else
		{
			playerServices.Print(Localizer.ForPlayer(player, "k4.phrases.rank.title", player?.PlayerName ?? "Unknown"));
			playerServices.Print(Localizer.ForPlayer(player, "k4.phrases.rank.line1", playerData.Rank?.ChatColor ?? ChatColors.Grey.ToString(), playerData.Rank?.Name ?? Localizer.ForPlayer(player, "k4.phrases.rank.none"), $"{playerData.Points:N0}"));
			if (playerData.NextRank != null)
				playerServices.Print(Localizer.ForPlayer(player, "k4.phrases.rank.line2", playerData.NextRank.ChatColor ?? ChatColors.Grey.ToString(), playerData.NextRank.Name, $"{pointsToNextRank:N0}"));
		}
	}

	private void ProcessTargetAction(CCSPlayerController? player, CommandInfo info, Func<IPlayerServices, long?, (string message, string logMessage)> action, bool requireAmount = true)
	{
		TargetResult targets = info.GetArgTargetResult(1);
		if (!targets.Any())
		{
			_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "k4.phrases.no-target"));
			return;
		}

		long? amount = null;
		if (requireAmount)
		{
			if (!int.TryParse(info.GetArg(2), out int parsedAmount) || parsedAmount <= 0)
			{
				_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "k4.phrases.invalid-amount"));
				return;
			}
			amount = parsedAmount;
		}

		foreach (var target in targets)
		{
			if (_playerCache.TryGetValue(target, out var zenithPlayer))
			{
				var (message, logMessage) = action(zenithPlayer, amount);
				if (player != null)
					_moduleServices?.PrintForPlayer(target, message);

				Logger.LogWarning(logMessage,
					player?.PlayerName ?? "CONSOLE", player?.SteamID ?? 0,
					target.PlayerName, target.SteamID, amount ?? 0);
			}
			else
			{
				_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "k4.phrases.cant-target", target.PlayerName));
			}
		}
	}

	public void OnGivePoints(CCSPlayerController? player, CommandInfo info)
	{
		ProcessTargetAction(player, info,
			(zenithPlayer, amount) =>
			{
				long newAmount = zenithPlayer.GetStorage<long>("Points") + amount!.Value;
				zenithPlayer.SetStorage("Points", newAmount);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = newAmount;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, newAmount);

				return (
					Localizer.ForPlayer(player, "k4.phrases.points-given", player?.PlayerName ?? "CONSOLE", amount),
					"{0} ({1}) gave {2} ({3}) {4} rank points."
				);
			}
		);
	}

	public void OnTakePoints(CCSPlayerController? player, CommandInfo info)
	{
		ProcessTargetAction(player, info,
			(zenithPlayer, amount) =>
			{
				long newAmount = zenithPlayer.GetStorage<long>("Points") - amount!.Value;
				zenithPlayer.SetStorage("Points", newAmount, true);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = newAmount;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, newAmount);

				return (
					Localizer.ForPlayer(player, "k4.phrases.points-taken", player?.PlayerName ?? "CONSOLE", amount),
					"{0} ({1}) taken {4} rank points from {2} ({3})."
				);
			}
		);
	}

	public void OnSetPoints(CCSPlayerController? player, CommandInfo info)
	{
		ProcessTargetAction(player, info,
			(zenithPlayer, amount) =>
			{
				zenithPlayer.SetStorage("Points", amount!.Value, true);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = amount!.Value;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, amount!.Value);

				return (
					Localizer.ForPlayer(player, "k4.phrases.points-set", player?.PlayerName ?? "CONSOLE", amount),
					"{0} ({1}) set {2} ({3}) rank points to {4}."
				);
			}
		);
	}

	public void OnResetPoints(CCSPlayerController? player, CommandInfo info)
	{
		if (ulong.TryParse(info.GetArg(1), out ulong steamId))
		{
			_moduleServices?.ResetModuleStorage(steamId);

			var onlinePlayer = Utilities.GetPlayerFromSteamId(steamId);
			if (onlinePlayer != null)
			{
				var zenithPlayer = GetZenithPlayer(onlinePlayer);
				if (zenithPlayer != null)
				{
					var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
					playerData.Points = _configAccessor.GetValue<long>("Settings", "StartPoints");
					playerData.LastUpdate = DateTime.Now;
					UpdatePlayerRank(zenithPlayer, playerData, playerData.Points);
				}

				Logger.LogWarning("{0} ({1}) reset {2} ({3}) rank points.", player?.PlayerName ?? "CONSOLE", player?.SteamID ?? 0, onlinePlayer.PlayerName, steamId);
			}
			else
				Logger.LogWarning("{0} ({1}) reset {2} rank points.", player?.PlayerName ?? "CONSOLE", player?.SteamID ?? 0, steamId);

			return;
		}

		ProcessTargetAction(player, info,
			(zenithPlayer, _) =>
			{
				long startPoints = _configAccessor.GetValue<long>("Settings", "StartPoints");
				zenithPlayer.SetStorage("Points", startPoints, true);

				var playerData = GetOrUpdatePlayerRankInfo(zenithPlayer);
				playerData.Points = startPoints;
				playerData.LastUpdate = DateTime.Now;
				UpdatePlayerRank(zenithPlayer, playerData, startPoints);

				return (
					Localizer.ForPlayer(player, "k4.phrases.points-reset", player?.PlayerName ?? "CONSOLE"),
					"{0} ({1}) reset {2} ({3}) rank points to {4}."
				);
			},
			requireAmount: false
		);
	}
}