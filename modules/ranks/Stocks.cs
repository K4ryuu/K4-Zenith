using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using ZenithAPI;

namespace Zenith_Ranks;

public sealed partial class Plugin : BasePlugin
{
	private readonly Dictionary<ulong, PlayerRankInfo> _playerRankCache = [];
	private readonly TimeSpan _cacheCleanupInterval = TimeSpan.FromSeconds(3);
	private readonly Dictionary<CCSPlayerController, int> _roundPoints = [];

	public IEnumerable<IPlayerServices> GetValidPlayers()
	{
		foreach (var player in Utilities.GetPlayers())
		{
			if (player != null)
			{
				if (_playerCache.TryGetValue(player, out var zenithPlayer))
				{
					yield return zenithPlayer;
				}
			}
		}
	}

	public void ModifyPlayerPoints(IPlayerServices player, int points, string eventKey, string? extraInfo = null)
	{
		if (points == 0) return;

		var vipFlags = GetCachedConfigValue<List<string>>("Settings", "VIPFlags");
		var vipMultiplier = (decimal)GetCachedConfigValue<double>("Settings", "VipMultiplier");
		bool scoreboardSync = GetCachedConfigValue<bool>("Settings", "ScoreboardScoreSync");
		bool showSummaries = GetCachedConfigValue<bool>("Settings", "PointSummaries");

		var playerData = GetOrUpdatePlayerRankInfo(player);

		if (points > 0 && vipFlags.Any(f => AdminManager.PlayerHasPermissions(player.Controller, f)))
		{
			points = (int)(points * vipMultiplier);
		}

		long newPoints = Math.Max(0, playerData.Points + points);
		if (playerData.Points == newPoints)
			return;

		playerData.Points = newPoints;
		playerData.LastUpdate = DateTime.Now;

		if (scoreboardSync && player.Controller.Score != (int)newPoints)
		{
			player.Controller.Score = (int)newPoints;
		}

		UpdatePlayerRank(player, playerData, newPoints);

		if (showSummaries || !player.GetSetting<bool>("ShowRankChanges"))
		{
			_roundPoints[player.Controller] = _roundPoints.TryGetValue(player.Controller, out int existingPoints)
				? existingPoints + points
				: points;
		}
		else
		{
			string message = Localizer.ForPlayer(player.Controller, points >= 0 ? "k4.phrases.gain" : "k4.phrases.loss",
				$"{newPoints:N0}", Math.Abs(points), extraInfo ?? Localizer.ForPlayer(player.Controller, eventKey));
			Server.NextFrame(() => player.Print(message));
		}
	}

	private void UpdatePlayerRank(IPlayerServices player, PlayerRankInfo playerData, long points)
	{
		var (determinedRank, _) = DetermineRanks(points);

		if (determinedRank?.Id != playerData.Rank?.Id)
		{
			string newRankName = determinedRank?.Name ?? Localizer.ForPlayer(player.Controller, "k4.phrases.rank.none");
			player.SetStorage("Rank", newRankName);

			if (!GetCachedConfigValue<bool>("Settings", "ShowRankChanges"))
				return;

			bool isRankUp = playerData.Rank is null || CompareRanks(determinedRank, playerData.Rank) > 0;

			string htmlMessage = $@"
            <font color='{(isRankUp ? "#00FF00" : "#FF0000")}' class='fontSize-m'>{Localizer.ForPlayer(player.Controller, isRankUp ? "k4.phrases.rankup" : "k4.phrases.rankdown")}</font><br>
            <font color='{determinedRank?.HexColor}' class='fontSize-m'>{Localizer.ForPlayer(player.Controller, "k4.phrases.newrank", newRankName)}</font>";

			player.PrintToCenter(htmlMessage, _configAccessor.GetValue<int>("Core", "CenterAlertTime"), ActionPriority.Normal);
		}
	}

	private static int CompareRanks(Rank? rank1, Rank? rank2)
	{
		if (rank1 == rank2) return 0;

		if (rank1 == null) return rank2 == null ? 0 : -1;
		if (rank2 == null) return 1;

		return rank1.Point.CompareTo(rank2.Point);
	}

	private PlayerRankInfo GetOrUpdatePlayerRankInfo(IPlayerServices player)
	{
		if (_playerRankCache.TryGetValue(player.SteamID, out var rankInfo) && (DateTime.Now - rankInfo.LastUpdate) < _cacheCleanupInterval)
			return rankInfo;

		long currentPoints;
		if (rankInfo != null && (DateTime.Now - rankInfo.LastUpdate) >= _cacheCleanupInterval)
		{
			currentPoints = rankInfo.Points;
			player.SetStorage("Points", currentPoints);
		}
		else
		{
			currentPoints = player.GetStorage<long>("Points");
		}

		var (determinedRank, nextRank) = DetermineRanks(currentPoints);
		rankInfo = new PlayerRankInfo
		{
			Rank = determinedRank,
			NextRank = nextRank,
			Points = currentPoints,
			LastUpdate = DateTime.Now
		};
		_playerRankCache[player.SteamID] = rankInfo;

		return rankInfo;
	}

	private (Rank? CurrentRank, Rank? NextRank) DetermineRanks(long points)
	{
		Rank? currentRank = null;
		Rank? nextRank = null;

		foreach (var rank in Ranks)
		{
			if (points >= rank.Point)
			{
				currentRank = rank;
			}
			else
			{
				nextRank = rank;
				break;
			}
		}

		return (currentRank, nextRank);
	}

	public int CalculateDynamicPoints(IPlayerServices attacker, IPlayerServices victim, int basePoints)
	{
		if (!_configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints"))
			return basePoints;

		long attackerPoints = attacker.GetStorage<long>("Points");
		long victimPoints = victim.GetStorage<long>("Points");

		if (attackerPoints <= 0 || victimPoints <= 0)
			return basePoints;

		double minMultiplier = _configAccessor.GetValue<double>("Settings", "DynamicDeathPointsMinMultiplier");
		double maxMultiplier = _configAccessor.GetValue<double>("Settings", "DynamicDeathPointsMaxMultiplier");

		double pointsRatio = Math.Clamp(victimPoints / (double)attackerPoints, minMultiplier, maxMultiplier);
		return (int)Math.Round(pointsRatio * basePoints);
	}

	private void UpdateScoreboards()
	{
		if (!GetCachedConfigValue<bool>("Settings", "UseScoreboardRanks"))
			return;

		int mode = GetCachedConfigValue<int>("Settings", "ScoreboardMode");
		int rankMax = GetCachedConfigValue<int>("Settings", "RankMax");
		int rankBase = GetCachedConfigValue<int>("Settings", "RankBase");
		int rankMargin = GetCachedConfigValue<int>("Settings", "RankMargin");

		foreach (var player in GetValidPlayers())
		{
			var playerData = GetOrUpdatePlayerRankInfo(player);
			SetCompetitiveRank(player, mode, playerData.Rank?.Id ?? 0, playerData.Points, rankMax, rankBase, rankMargin);
		}
	}

	private static string FormatPoints(int points)
	{
		if (points >= 1000000)
		{
			double millions = points / 1000000.0;
			return $"{millions:F1}M";
		}
		else if (points >= 1000)
		{
			double thousands = points / 1000.0;
			return $"{thousands:F1}k";
		}
		else
		{
			return points.ToString();
		}
	}
}
