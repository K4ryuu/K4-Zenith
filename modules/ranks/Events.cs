using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_Ranks
{
	public sealed partial class Plugin : BasePlugin
	{
		private EventManager? _eventManager;
		private readonly Dictionary<string, (string targetProperty, int points)> _experienceEvents = new(StringComparer.OrdinalIgnoreCase);

		private void Initialize_Events()
		{
			_eventManager = new EventManager(this);

			RegisterListener<Listeners.OnMapStart>(OnMapStart);

			if (CoreConfig.FollowCS2ServerGuidelines)
			{
				Logger.LogWarning("CS2 server guidelines are enabled, disabling fake ranks functionality.");
				Logger.LogInformation("To enable fake ranks, set 'FollowCS2ServerGuidelines' to false in the Core configuration (configs/core.cfg).");
			}
			else
			{
				RegisterListener<Listeners.OnTick>(UpdateScoreboards);

				if (ConVar.Find("mp_halftime")?.GetPrimitiveValue<bool>() == false)
					Logger.LogWarning("Halftime is disabled, this may lead to scoreboard render issues.");
			}

			RegisterEventHandler<EventRoundEnd>(OnRoundEnd, HookMode.Post);
			RegisterEventHandler<EventRoundPrestart>(OnRoundPrestart, HookMode.Post);
			RegisterEventHandler<EventCsWinPanelMatch>(OnCsWinPanelMatch, HookMode.Post);
			RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn, HookMode.Post);

			InitializeExperienceEvents();
		}

		private void OnMapStart(string mapName)
		{
			_isGameEnd = false;
			AddTimer(1.0f, () =>
			{
				GameRules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
			});
		}

		private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;

			bool resetKillStreaks = GetCachedConfigValue<bool>("Points", "RoundEndKillStreakReset");
			bool pointSummary = GetCachedConfigValue<bool>("Settings", "PointSummaries");

			foreach (var player in GetValidPlayers())
			{
				if (resetKillStreaks)
					_eventManager?.ResetKillStreak(player);

				if (_playerSpawned.Contains(player.Controller) && player.Controller.Team > CsTeam.Spectator)
				{
					if (player.Controller.TeamNum == @event.Winner)
					{
						ModifyPlayerPoints(player, _configAccessor.GetValue<int>("Points", "RoundWin"), "k4.events.roundwin");
					}
					else
					{
						ModifyPlayerPoints(player, _configAccessor.GetValue<int>("Points", "RoundLose"), "k4.events.roundlose");
					}

					if (pointSummary)
					{
						if (player.GetSetting<bool>("ShowRankChanges") && _roundPoints.TryGetValue(player.Controller, out int points))
						{
							string message = points > 0 ? Localizer.ForPlayer(player.Controller, "k4.phrases.round-summary-earn", points) : Localizer.ForPlayer(player.Controller, "k4.phrases.round-summary-lose", points);
							player.Print(message);
						}
					}
				}
			}

			_roundPoints.Clear();
			return HookResult.Continue;
		}

		private HookResult OnRoundPrestart(EventRoundPrestart @event, GameEventInfo info)
		{
			_isGameEnd = false;
			_playerSpawned.Clear();
			return HookResult.Continue;
		}

		private HookResult OnCsWinPanelMatch(EventCsWinPanelMatch @event, GameEventInfo info)
		{
			_isGameEnd = true;
			return HookResult.Continue;
		}

		private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
		{
			HandlePlayerSpawn(@event.Userid);
			return HookResult.Continue;
		}

		private void InitializeExperienceEvents()
		{
			_experienceEvents.Clear();

			RegisterEventHandler<EventRoundMvp>(OnRoundMvp, HookMode.Post);
			RegisterEventHandler<EventHostageRescued>(OnHostageRescued, HookMode.Post);
			RegisterEventHandler<EventBombDefused>(OnBombDefused, HookMode.Post);
			RegisterEventHandler<EventBombPlanted>(OnBombPlanted, HookMode.Post);
			RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath, HookMode.Post);
			RegisterEventHandler<EventHostageKilled>(OnHostageKilled, HookMode.Post);
			RegisterEventHandler<EventHostageHurt>(OnHostageHurt, HookMode.Post);
			RegisterEventHandler<EventBombPickup>(OnBombPickup, HookMode.Post);
			RegisterEventHandler<EventBombDropped>(OnBombDropped, HookMode.Post);
			RegisterEventHandler<EventBombExploded>(OnBombExploded, HookMode.Post);
			RegisterEventHandler<EventHostageRescuedAll>(OnHostageRescuedAll, HookMode.Post);
		}

		private HookResult OnRoundMvp(EventRoundMvp @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "MVP", "k4.events.roundmvp");
			return HookResult.Continue;
		}

		private HookResult OnHostageRescued(EventHostageRescued @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "HostageRescue", "k4.events.hostagerescued");
			return HookResult.Continue;
		}

		private HookResult OnBombDefused(EventBombDefused @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "BombDefused", "k4.events.bombdefused");
			return HookResult.Continue;
		}

		private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "BombPlant", "k4.events.bombplanted");
			return HookResult.Continue;
		}

		private HookResult OnHostageKilled(EventHostageKilled @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "HostageKill", "k4.events.hostagekilled");
			return HookResult.Continue;
		}

		private HookResult OnHostageHurt(EventHostageHurt @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "HostageHurt", "k4.events.hostagehurt");
			return HookResult.Continue;
		}

		private HookResult OnBombPickup(EventBombPickup @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "BombPickup", "k4.events.bombpickup");
			return HookResult.Continue;
		}

		private HookResult OnBombDropped(EventBombDropped @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyPlayerPointsForEvent(@event.Userid, "BombDrop", "k4.events.bombdropped");
			return HookResult.Continue;
		}

		private HookResult OnBombExploded(EventBombExploded @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyTeamPointsForEvent(CsTeam.Terrorist, "BombExploded", "k4.events.bombexploded");
			return HookResult.Continue;
		}

		private HookResult OnHostageRescuedAll(EventHostageRescuedAll @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			ModifyTeamPointsForEvent(CsTeam.CounterTerrorist, "HostageRescueAll", "k4.events.hostagerescuedall");
			return HookResult.Continue;
		}

		private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
		{
			if (!ShouldProcessEvent()) return HookResult.Continue;
			_eventManager?.HandlePlayerDeathEvent(@event);
			return HookResult.Continue;
		}

		private DateTime _lastCheckTime = DateTime.MinValue;
		private bool _lastProcessResult;
		private const int CACHE_UPDATE_INTERVAL_SECONDS = 10;

		private bool ShouldProcessEvent()
		{
			var currentTime = DateTime.UtcNow;
			if ((currentTime - _lastCheckTime).TotalSeconds < CACHE_UPDATE_INTERVAL_SECONDS)
			{
				return _lastProcessResult;
			}

			_lastProcessResult = GetCachedConfigValue<int>("Settings", "MinPlayers") <= _playerCache.Count &&
								(GetCachedConfigValue<bool>("Settings", "WarmupPoints") || GameRules?.WarmupPeriod != true);
			_lastCheckTime = currentTime;

			return _lastProcessResult;
		}

		private void ModifyPlayerPointsForEvent(CCSPlayerController? player, string pointsKey, string eventKey)
		{
			if (player == null)
				return;

			if (_playerCache.TryGetValue(player, out var playerServices))
			{
				int points = _configAccessor.GetValue<int>("Points", pointsKey);
				ModifyPlayerPoints(playerServices, points, eventKey);
			}
		}

		private void ModifyTeamPointsForEvent(CsTeam team, string pointsKey, string eventKey)
		{
			int points = _configAccessor.GetValue<int>("Points", pointsKey);
			foreach (var player in GetValidPlayers())
			{
				if (player.Controller.Team == team)
				{
					ModifyPlayerPoints(player, points, eventKey);
				}
			}
		}

		private static void SetCompetitiveRank(IPlayerServices player, int mode, int rankId, long currentPoints, int rankMax, int rankBase, int rankMargin)
		{
			player.Controller.CompetitiveWins = 10;

			switch (mode)
			{
				case 1:
					player.Controller.CompetitiveRanking = (int)currentPoints;
					player.Controller.CompetitiveRankType = 11;
					break;
				case 2:
				case 3:
					player.Controller.CompetitiveRanking = Math.Min(rankId, 18);
					player.Controller.CompetitiveRankType = (sbyte)(mode == 2 ? 12 : 7);
					break;
				case 4:
					player.Controller.CompetitiveRanking = Math.Min(rankId, 15);
					player.Controller.CompetitiveRankType = 10;
					break;
				default:
					int rank = rankId > rankMax ? rankBase + rankMax - rankMargin : rankBase + (rankId - rankMargin - 1);
					player.Controller.CompetitiveRanking = rank;
					player.Controller.CompetitiveRankType = 12;
					break;
			}
		}

		private void HandlePlayerSpawn(CCSPlayerController? player)
		{
			if (player == null || player.IsBot || player.IsHLTV)
				return;

			if (_configAccessor.GetValue<bool>("Settings", "EnableRequirementMessages"))
			{
				int requiredPlayers = _configAccessor.GetValue<int>("Settings", "MinPlayers");
				if (requiredPlayers > Utilities.GetPlayers().Count(p => p.IsValid && !p.IsBot && !p.IsHLTV) && !_playerSpawned.Contains(player))
				{
					_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, "k4.phrases.points_disabled", requiredPlayers));
				}
			}

			_playerSpawned.Add(player);
		}

		public class EventManager
		{
			private readonly Plugin _plugin;
			private readonly Dictionary<int, int> _killStreakPoints;

			public EventManager(Plugin plugin)
			{
				_plugin = plugin;
				_killStreakPoints = InitializeKillStreakPoints();
			}

			private Dictionary<int, int> InitializeKillStreakPoints()
			{
				return new Dictionary<int, int>
				{
					{ 2, _plugin._configAccessor.GetValue<int>("Points", "DoubleKill") },
					{ 3, _plugin._configAccessor.GetValue<int>("Points", "TripleKill") },
					{ 4, _plugin._configAccessor.GetValue<int>("Points", "Domination") },
					{ 5, _plugin._configAccessor.GetValue<int>("Points", "Rampage") },
					{ 6, _plugin._configAccessor.GetValue<int>("Points", "MegaKill") },
					{ 7, _plugin._configAccessor.GetValue<int>("Points", "Ownage") },
					{ 8, _plugin._configAccessor.GetValue<int>("Points", "UltraKill") },
					{ 9, _plugin._configAccessor.GetValue<int>("Points", "KillingSpree") },
					{ 10, _plugin._configAccessor.GetValue<int>("Points", "MonsterKill") },
					{ 11, _plugin._configAccessor.GetValue<int>("Points", "Unstoppable") },
					{ 12, _plugin._configAccessor.GetValue<int>("Points", "GodLike") }
				};
			}

			public void HandlePlayerDeathEvent(EventPlayerDeath? deathEvent)
			{
				if (deathEvent?.Userid == null) return;

				var victim = deathEvent.Userid != null ? _plugin._playerCache.TryGetValue(deathEvent.Userid, out var victimPlayer) ? victimPlayer : null : null;
				var attacker = deathEvent.Attacker != null ? _plugin._playerCache.TryGetValue(deathEvent.Attacker, out var attackerPlayer) ? attackerPlayer : null : null;
				var assister = deathEvent.Assister != null ? _plugin._playerCache.TryGetValue(deathEvent.Assister, out var assisterPlayer) ? assisterPlayer : null : null;

				if (victim != null)
				{
					HandleVictimDeath(victim, attacker, deathEvent);
				}

				if (attacker != null && attacker.Controller.SteamID != victim?.Controller.SteamID)
				{
					HandleAttackerKill(attacker, victim, deathEvent);
				}

				if (assister != null && assister.Controller.SteamID != deathEvent.Userid?.SteamID)
				{
					HandleAssisterEvent(assister, attacker, victim, deathEvent);
				}
			}

			private void HandleVictimDeath(IPlayerServices victim, IPlayerServices? attacker, EventPlayerDeath deathEvent)
			{
				if (deathEvent.Attacker == null || deathEvent.Attacker.SteamID == victim.Controller.SteamID)
				{
					if (!_plugin._isGameEnd)
						_plugin.ModifyPlayerPoints(victim, _plugin._configAccessor.GetValue<int>("Points", "Suicide"), "k4.events.suicide");
				}
				else
				{
					if (!_plugin._configAccessor.GetValue<bool>("Settings", "PointsForBots") && deathEvent.Attacker?.IsBot == true)
						return;

					string? eventInfo = attacker != null && _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
						? (_plugin.Localizer.ForPlayer(victim.Controller, "k4.phrases.death-extended", attacker.Name, $"{attacker.GetStorage<long>("Points"):N0}") ?? string.Empty)
						: null;

					int points = attacker != null && _plugin._configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints")
						? _plugin.CalculateDynamicPoints(attacker, victim, _plugin._configAccessor.GetValue<int>("Points", "Death"))
						: _plugin._configAccessor.GetValue<int>("Points", "Death");

					_plugin.ModifyPlayerPoints(victim, points, "k4.events.playerdeath", eventInfo);
				}

				ResetKillStreak(victim);
			}

			private void HandleAttackerKill(IPlayerServices attacker, IPlayerServices? victim, EventPlayerDeath deathEvent)
			{
				if (deathEvent.Userid == null || (!_plugin._configAccessor.GetValue<bool>("Settings", "PointsForBots") && deathEvent.Userid.IsBot))
					return;

				if (!_plugin._configAccessor.GetValue<bool>("Settings", "FFAMode") && attacker.Controller.Team == deathEvent.Userid?.Team)
				{
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "TeamKill"), "k4.events.teamkill");
				}
				else
				{
					HandleKillEvent(attacker, victim, deathEvent);
				}
			}

			private void HandleKillEvent(IPlayerServices attacker, IPlayerServices? victim, EventPlayerDeath deathEvent)
			{
				string? eventInfo = victim != null && _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
					? (_plugin.Localizer.ForPlayer(attacker.Controller, "k4.phrases.kill-extended", victim.Name, $"{victim.GetStorage<long>("Points"):N0}") ?? string.Empty)
					: null;

				int points = _plugin._configAccessor.GetValue<bool>("Settings", "DynamicDeathPoints") && victim != null
					? _plugin.CalculateDynamicPoints(attacker, victim, _plugin._configAccessor.GetValue<int>("Points", "Kill"))
					: _plugin._configAccessor.GetValue<int>("Points", "Kill");

				_plugin.ModifyPlayerPoints(attacker, points, "k4.events.kill", eventInfo);

				HandleSpecialKillEvents(attacker, deathEvent);
				HandleKillStreak(attacker);
			}

			private void HandleSpecialKillEvents(IPlayerServices attacker, EventPlayerDeath deathEvent)
			{
				if (deathEvent.Headshot)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "Headshot"), "k4.events.headshot");

				if (deathEvent.Penetrated > 0)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "Penetrated") * deathEvent.Penetrated, "k4.events.penetrated");

				if (deathEvent.Noscope)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "NoScope"), "k4.events.noscope");

				if (deathEvent.Thrusmoke)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "Thrusmoke"), "k4.events.thrusmoke");

				if (deathEvent.Attackerblind)
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "BlindKill"), "k4.events.blindkill");

				if (deathEvent.Distance >= _plugin._configAccessor.GetValue<int>("Points", "LongDistance"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "LongDistanceKill"), "k4.events.longdistance");

				HandleSpecialWeaponKills(attacker, deathEvent.Weapon);
			}

			private void HandleSpecialWeaponKills(IPlayerServices attacker, string weapon)
			{
				string lowerCaseWeaponName = weapon.ToLower();

				if (lowerCaseWeaponName.Contains("hegrenade"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "GrenadeKill"), "k4.events.grenadekill");
				else if (lowerCaseWeaponName.Contains("inferno"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "InfernoKill"), "k4.events.infernokill");
				else if (lowerCaseWeaponName.Contains("grenade") || lowerCaseWeaponName.Contains("molotov") || lowerCaseWeaponName.Contains("flashbang") || lowerCaseWeaponName.Contains("bumpmine"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "ImpactKill"), "k4.events.impactkill");
				else if (lowerCaseWeaponName.Contains("knife") || lowerCaseWeaponName.Contains("bayonet"))
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "KnifeKill"), "k4.events.knifekill");
				else if (lowerCaseWeaponName == "taser")
					_plugin.ModifyPlayerPoints(attacker, _plugin._configAccessor.GetValue<int>("Points", "TaserKill"), "k4.events.taserkill");
			}

			public void HandleKillStreak(IPlayerServices attacker)
			{
				var playerData = _plugin.GetOrUpdatePlayerRankInfo(attacker);
				long currentTime = DateTimeOffset.Now.ToUnixTimeSeconds();

				int timeBetweenKills = _plugin.GetCachedConfigValue<int>("Points", "SecondsBetweenKills");
				bool isValidStreak = timeBetweenKills <= 0 || (currentTime - playerData.KillStreak.LastKillTime <= timeBetweenKills);

				if (isValidStreak)
				{
					playerData.KillStreak.KillCount++;
					playerData.KillStreak.LastKillTime = currentTime;

					if (_killStreakPoints.TryGetValue(playerData.KillStreak.KillCount, out var streakPoints) && streakPoints != 0)
					{
						_plugin.ModifyPlayerPoints(attacker, streakPoints, $"k4.events.killstreak{playerData.KillStreak.KillCount}");
					}
				}
				else
				{
					playerData.KillStreak.KillCount = 1;
					playerData.KillStreak.LastKillTime = currentTime;
				}
			}

			public void ResetKillStreak(IPlayerServices player)
			{
				if (_plugin._playerRankCache.TryGetValue(player.SteamID, out var playerData))
				{
					playerData.KillStreak = new KillStreakInfo();
				}
			}

			private void HandleAssisterEvent(IPlayerServices assister, IPlayerServices? attacker, IPlayerServices? victim, EventPlayerDeath deathEvent)
			{
				if (!_plugin._configAccessor.GetValue<bool>("Settings", "PointsForBots") && deathEvent.Userid?.IsBot == true)
					return;

				if (!_plugin._configAccessor.GetValue<bool>("Settings", "FFAMode") && attacker?.Controller.Team == deathEvent.Userid?.Team && assister.Controller.Team == deathEvent.Userid?.Team)
				{
					_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "TeamKillAssist"), "k4.events.teamkillassist");
					if (deathEvent.Assistedflash)
					{
						_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "TeamKillAssistFlash"), "k4.events.teamkillassistflash");
					}
				}
				else
				{
					string? eventInfo = victim != null && _plugin._configAccessor.GetValue<bool>("Settings", "ExtendedDeathMessages")
						? (_plugin.Localizer.ForPlayer(assister.Controller, "k4.phrases.assist-extended", victim.Name, $"{victim.GetStorage<long>("Points"):N0}") ?? string.Empty)
						: null;

					_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "Assist"), "k4.events.assist", eventInfo);
					if (deathEvent.Assistedflash)
					{
						_plugin.ModifyPlayerPoints(assister, _plugin._configAccessor.GetValue<int>("Points", "AssistFlash"), "k4.events.assistflash");
					}
				}
			}
		}
	}
}
