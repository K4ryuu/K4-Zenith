using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using ZenithAPI;
using Menu;
using Menu.Enums;
using MySqlConnector;
using Dapper;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Modules.Menu;
using System.Reflection;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
namespace Zenith_AFKManager;

[MinimumApiVersion(260)]
public class Plugin : BasePlugin
{
	public CCSGameRules? GameRules = null;
	public IModuleConfigAccessor _coreAccessor = null!;
	private const string MODULE_ID = "AFK Manager";

	public override string ModuleName => $"Kewaii-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "Kewaii";
	public override string ModuleVersion => "1.0.";

	private PlayerCapability<IPlayerServices>? _playerServicesCapability;
	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;
	private readonly HashSet<CCSPlayerController> playerSpawned = [];

    private CCSGameRules? _gGameRulesProxy;
    public Dictionary<uint, PlayerInfo> _gPlayerInfo = new();


    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            Server.NextFrame(() =>
            {
                _gGameRulesProxy =
                    Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules ??
                    throw new Exception("Failed to find game rules proxy entity.");
            });
            
        });
        
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _gPlayerInfo.Clear();
        });
        
        #region OnClientConnected
        RegisterListener<Listeners.OnClientConnected>(playerSlot =>
        {
            var finalSlot = (uint)playerSlot + 1;
            
            if (_gPlayerInfo.ContainsKey(finalSlot))
                return;
            
            _gPlayerInfo.Add(finalSlot, new PlayerInfo {
                Angles = new QAngle(),
                Origin = new Vector()
            });
        });
        
        RegisterListener<Listeners.OnClientDisconnectPost>(playerSlot =>
        {
            _gPlayerInfo.Remove((uint)playerSlot + 1);
        });
        #endregion
        #region hotReload
        if (hotReload)
        {
            AddTimer(1.0f, () =>
            {
                var players = Utilities.GetPlayers().Where(x => x is { IsBot: false, Connected: PlayerConnectedState.PlayerConnected });

                foreach (var player in players)
                {
                    var i = player.Index;
                    
                    if (!_gPlayerInfo.ContainsKey(i))
                    {
                        _gPlayerInfo.Add(i, new PlayerInfo
                        {
                            Angles = new QAngle(),
                            Origin = new Vector()
                        });
                    }
                }
                
                _gGameRulesProxy =
                    Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").First().GameRules ??
                        throw new Exception("Failed to find game rules proxy entity on hotReload.");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        #endregion
        AddCommandListener("spec_mode", OnCommandListener);
        AddCommandListener("spec_next", OnCommandListener);
    }
	public override void OnAllPluginsLoaded(bool hotReload)
	{
		try
		{
			_playerServicesCapability = new("zenith:player-services");
			_moduleServicesCapability = new("zenith:module-services");
		}
		catch (Exception ex)
		{
			Logger.LogError($"Failed to initialize Zenith API: {ex.Message}");
			Logger.LogInformation("Please check if Zenith is installed, configured and loaded correctly.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_moduleServices = _moduleServicesCapability.Get();
		if (_moduleServices == null)
		{
			Logger.LogError("Failed to get Module-Services API for Zenith.");
			Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
			return;
		}

		_coreAccessor = _moduleServices.GetModuleConfigAccessor();

		RegisterModuleConfigs();

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}
        
		RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
		RegisterEventHandler<EventPlayerTeam>(OnPlayerTeam);

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
        
	}

	private void RegisterModuleConfigs()
	{
		_moduleServices!.RegisterModuleConfig("Config", "AfkPunishAfterWarnings", "AFK Punish after Warnings", 3);
		_moduleServices.RegisterModuleConfig("Config", "AfkPunishment", "AFK Punishment", 1);
		_moduleServices.RegisterModuleConfig("Config", "AfkWarnInterval", "AfkWarnInterval", 5.0f);
		_moduleServices.RegisterModuleConfig("Config", "SpecWarnInterval", "SpecWarnInterval", 20.0f);
		_moduleServices.RegisterModuleConfig("Config", "SpecKickAfterWarnings", "SpecKickAfterWarnings", 5);
		_moduleServices.RegisterModuleConfig("Config", "SpecKickMinPlayers", "SpecKickMinPlayers", 5);
		_moduleServices.RegisterModuleConfig("Config", "SpecKickOnlyMovedByPlugin", "SpecKickOnlyMovedByPlugin", false);
		_moduleServices.RegisterModuleConfig("Config", "SpecSkipFlag", "SpecSkipFlag", new List<string>{"@css/root", "@css/ban"});
		_moduleServices.RegisterModuleConfig("Config", "AfkSkipFlag", "AfkSkipFlag", new List<string>{"@css/root", "@css/ban"});
		_moduleServices.RegisterModuleConfig("Config", "AntiCampSkipFlag", "AntiCampSkipFlag", new List<string>{"@css/root", "@css/ban"});
		_moduleServices.RegisterModuleConfig("Config", "PlaySoundName", "PlaySoundName", "ui/panorama/popup_reveal_01");
        _moduleServices.RegisterModuleConfig("Config", "SkipWarmup", "SkipWarmup", false);
        _moduleServices.RegisterModuleConfig("Config", "AntiCampRadius", "AntiCampRadius", 130.0f);
        _moduleServices.RegisterModuleConfig("Config", "AntiCampPunishment", "AntiCampPunishment", 1);
        _moduleServices.RegisterModuleConfig("Config", "AntiCampSlapDamage", "AntiCampSlapDamage", 0);
        _moduleServices.RegisterModuleConfig("Config", "AntiCampWarnInterval", "AntiCampWarnInterval", 10.0f);
        _moduleServices.RegisterModuleConfig("Config", "AntiCampPunishAfterWarnings", "AntiCampPunishAfterWarnings", 3);
        _moduleServices.RegisterModuleConfig("Config", "AntiCampSkipBombPlanted", "AntiCampSkipBombPlanted", true);
        _moduleServices.RegisterModuleConfig("Config", "AntiCampSkipTeam", "AntiCampSkipTeam", 3);
        _moduleServices.RegisterModuleConfig("Config", "Timer", "Timer", 5.0f);
        
        var timer = _coreAccessor.GetValue<float>("Config", "Timer");
        AddTimer(timer, AfkTimer_Callback, TimerFlags.REPEAT);
	}



	private void OnZenithCoreUnload(bool hotReload)
	{
		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(ModulePath, DateTime.Now); }
				catch (Exception ex) { Logger.LogError($"Failed to update file: {ex.Message}"); }
			});
		}
	}

	public override void Unload(bool hotReload)
	{
		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}


	public IPlayerServices? GetZenithPlayer(CCSPlayerController? player)
	{
		if (player == null) return null;
		try { return _playerServicesCapability?.Get(player); }
		catch { return null; }
	}



    private HookResult OnCommandListener(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (player == null || !player.IsValid)
            return HookResult.Continue;
        
        if (!_gPlayerInfo.TryGetValue(player.Index, out var data))
            return HookResult.Continue;
        
        data.SpecAfkTime = 0;
        data.SpecWarningCount = 0;
        data.AfkWarningCount = 0;
        
        return HookResult.Continue;
    }

    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        if (!_gPlayerInfo.TryGetValue(player.Index, out var value))
            return HookResult.Continue;
        
        value.SpecAfkTime = 0;
        value.SpecWarningCount = 0;
        value.AfkWarningCount = 0;
                
        if(@event.Team != 1)
            value.MovedByPlugin = false;
        
        return HookResult.Continue;
    }
    
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsBot)
            return HookResult.Continue;

        AddTimer(0.2f, () =>
        {
            if (player == null || !player.IsValid || player.LifeState != (byte)LifeState_t.LIFE_ALIVE)
                return;
                
            if(!_gPlayerInfo.TryGetValue(player.Index, out var data))
                return;
                
            var angles = player.PlayerPawn.Value?.EyeAngles;
            var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                
            data.Angles = new QAngle(
                x: angles?.X,
                y: angles?.Y,
                z: angles?.Z
            );
                
            data.Origin = new Vector(
                x: origin?.X,
                y: origin?.Y,
                z: origin?.Z
            );
            
            data.SpecAfkTime = 0;
            data.SpecWarningCount = 0;
            data.MovedByPlugin = false;
            data.AntiCampWarningCount = 0;
            data.AntiCampTime = 0;
        }, TimerFlags.STOP_ON_MAPCHANGE);
            
        return HookResult.Continue;
    }
    
    private void AfkTimer_Callback()
    {
        if (_gGameRulesProxy == null || _gGameRulesProxy.FreezePeriod || (_coreAccessor.GetValue<bool>("Config", "SkipWarmup") && _gGameRulesProxy.WarmupPeriod))
            return;

        var players = Utilities.GetPlayers().Where(x => x is { IsBot: false, Connected: PlayerConnectedState.PlayerConnected }).ToList();
        var playersCount = players.Count;
        
        foreach (var player in players)
        {
            if (player.ControllingBot || !_gPlayerInfo.TryGetValue(player.Index, out var data))
                continue;
            
            #region AFK Time
            if (player is { LifeState: (byte)LifeState_t.LIFE_ALIVE, Team: CsTeam.Terrorist or CsTeam.CounterTerrorist })
            {
                var playerPawn = player.PlayerPawn.Value;
                var playerFlags = player.Pawn.Value!.Flags;

                if ((playerFlags & ((uint)PlayerFlags.FL_ONGROUND | (uint)PlayerFlags.FL_FROZEN)) != (uint)PlayerFlags.FL_ONGROUND)
                    continue;
                
                var angles = playerPawn?.EyeAngles;
                var origin = player.PlayerPawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
                
                /*  ------------------------------------------->  <-------------------------------------------  */
                if (_coreAccessor.GetValue<int>("Config", "AfkPunishAfterWarnings") != 0
                    && data.Angles.X == angles.X && data.Angles.Y == angles.Y
                    && data.Origin.X == origin.X && data.Origin.Y == origin.Y)
                {
                    data.AfkTime += _coreAccessor.GetValue<float>("Config", "Timer");
                    
                    if (data.AfkTime < _coreAccessor.GetValue<float>("Config", "AfkWarnInterval"))
                        continue;

                    if (_coreAccessor.GetValue<List<string>>("Config", "AfkSkipFlag").Count() >= 1 && AdminManager.PlayerHasPermissions(player, _coreAccessor.GetValue<List<string>>("Config", "AfkSkipFlag").ToArray())) {
                        
                        if (data.AfkWarningCount == _coreAccessor.GetValue<int>("Config", "AfkPunishAfterWarnings"))
                        {
                            var zenithPlayer = GetZenithPlayer(player);
                            zenithPlayer.SetAFK(true);
                        }
                        data.AfkTime = 0;
                        data.AfkWarningCount++;
                        continue;
                    }

                    
                    if (data.AfkWarningCount == _coreAccessor.GetValue<int>("Config", "AfkPunishAfterWarnings"))
                    {
			            var zenithPlayer = GetZenithPlayer(player);
                        zenithPlayer.SetAFK(true);
                        
                        switch (_coreAccessor.GetValue<int>("Config", "AfkPunishment"))
                        {
                            case 0:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKillMessage"].Value));
                                playerPawn?.CommitSuicide(false, true);
                                
                                break;
                            case 1:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatMoveMessage"].Value));
                                playerPawn?.CommitSuicide(false, true);
                                player.ChangeTeam(CsTeam.Spectator);
                                data.MovedByPlugin = true;
                                
                                break;
                            case 2:
                                Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                                Server.ExecuteCommand($"kickid {player.UserId}");
                                
                                break;
                        }
                        
                        data.AfkWarningCount = 0;
                        data.AfkTime = 0;
                        
                        continue;
                    }
                    
                    switch (_coreAccessor.GetValue<int>("Config", "AfkPunishment"))
                    {
                        case 0:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKillMessage"].Value, _coreAccessor.GetValue<int>("Config", "AfkPunishAfterWarnings") * _coreAccessor.GetValue<float>("Config", "AfkWarnInterval") - data.AfkWarningCount * _coreAccessor.GetValue<float>("Config", "AfkWarnInterval")));
                        break;

                        case 1:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningMoveMessage"].Value, _coreAccessor.GetValue<int>("Config", "AfkPunishAfterWarnings") * _coreAccessor.GetValue<float>("Config", "AfkWarnInterval") - data.AfkWarningCount * _coreAccessor.GetValue<float>("Config", "AfkWarnInterval")));
                            break;

                        case 2:
                            player.PrintToChat(ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, _coreAccessor.GetValue<int>("Config", "AfkPunishAfterWarnings") * _coreAccessor.GetValue<float>("Config", "AfkWarnInterval") - data.AfkWarningCount * _coreAccessor.GetValue<float>("Config", "AfkWarnInterval")));
                            break;
                    }

                    if (!string.IsNullOrEmpty(_coreAccessor.GetValue<string>("Config", "PlaySoundName"))) {
                        string playSoundName = _coreAccessor.GetValue<string>("Config", "PlaySoundName");
                        player.ExecuteClientCommand($"play {playSoundName}");
                    }
                    
                    data.AfkTime = 0;
                    data.AfkWarningCount++;
                }
                else
                {
                    data.AfkTime = 0;
                    data.AfkWarningCount = 0;
                }
                /*  ------------------------------------------->  <-------------------------------------------  */
                if (data.AfkWarningCount == 0 && _coreAccessor.GetValue<int>("Config", "AntiCampPunishAfterWarnings") != 0
                                           && !(_coreAccessor.GetValue<bool>("Config", "AntiCampSkipBombPlanted") && _gGameRulesProxy.BombPlanted)
                                           && !(_coreAccessor.GetValue<List<string>>("Config", "AntiCampSkipFlag").Count() >= 1 && AdminManager.PlayerHasPermissions(player, _coreAccessor.GetValue<List<string>>("Config", "AntiCampSkipFlag").ToArray()))
                                           && player.TeamNum != _coreAccessor.GetValue<int>("Config", "AntiCampSkipTeam"))
                {
                    if (CalculateDistance(data.Origin, origin) < _coreAccessor.GetValue<float>("Config", "AntiCampRadius"))
                    {
                        data.AntiCampTime += _coreAccessor.GetValue<float>("Config", "Timer");

                        if (data.AntiCampTime < _coreAccessor.GetValue<float>("Config", "AntiCampWarnInterval"))
                            continue;

                        if (data.AntiCampWarningCount == _coreAccessor.GetValue<int>("Config", "AntiCampPunishAfterWarnings"))
                        {
                            switch (_coreAccessor.GetValue<int>("Config", "AntiCampPunishment"))
                            {
                                case 0:
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlayMessage"].Value));

                                    playerPawn?.CommitSuicide(false, true);
                                    break;
                                case 1:
                                    Server.PrintToChatAll(ReplaceVars(player, Localizer["AntiCampSlapMessage"].Value));

                                    Slap(playerPawn, _coreAccessor.GetValue<int>("Config", "AntiCampSlapDamage"));
                                    break;
                            }
                            
                            data.AntiCampWarningCount = 0;
                            data.AntiCampTime = 0;

                            continue;
                        }

                        switch (_coreAccessor.GetValue<int>("Config", "AntiCampPunishment"))
                        {
                            case 0:
                                player.PrintToChat(ReplaceVars(player, Localizer["AntiCampSlayWarningMessage"].Value, _coreAccessor.GetValue<int>("Config", "AntiCampPunishAfterWarnings") * _coreAccessor.GetValue<float>("Config", "AntiCampWarnInterval") - data.AntiCampWarningCount * _coreAccessor.GetValue<float>("Config", "AntiCampWarnInterval")));
                                break;
                            case 1:
                                player.PrintToChat(ReplaceVars(player, Localizer["AntiCampSlapWarningMessage"].Value, _coreAccessor.GetValue<int>("Config", "AntiCampPunishAfterWarnings") * _coreAccessor.GetValue<float>("Config", "AntiCampWarnInterval") - data.AntiCampWarningCount * _coreAccessor.GetValue<float>("Config", "AntiCampWarnInterval")));
                                break;
                        }
                            
                        if (!string.IsNullOrEmpty(_coreAccessor.GetValue<string>("Config", "PlaySoundName"))) {
                            string playSoundName = _coreAccessor.GetValue<string>("Config", "PlaySoundName");
                            player.ExecuteClientCommand($"play {playSoundName}");
                        }
                            
                        data.AntiCampWarningCount++;
                        data.AntiCampTime = 0;
                    }
                    else
                    {
                        data.AntiCampWarningCount = 0;
                        data.AntiCampTime = 0;
                    }
                }
                
                data.Angles = new QAngle(angles?.X, angles?.Y, angles?.Z);
                data.Origin = new Vector(origin?.X, origin?.Y, origin?.Z);
                
                continue;
            }
            
            #endregion
            #region SPEC Time

            if (_coreAccessor.GetValue<int>("Config", "SpecKickAfterWarnings") != 0
                && player.TeamNum == 1
                && playersCount >= _coreAccessor.GetValue<int>("Config", "SpecKickMinPlayers"))
            {
                if((_coreAccessor.GetValue<bool>("Config", "SpecKickOnlyMovedByPlugin") && !data.MovedByPlugin) || (_coreAccessor.GetValue<List<string>>("Config", "SpecSkipFlag").Count() >= 1 && AdminManager.PlayerHasPermissions(player, _coreAccessor.GetValue<List<string>>("Config", "SpecSkipFlag").ToArray())))
                    continue;
                
                data.SpecAfkTime += _coreAccessor.GetValue<float>("Config", "Timer");

                if (!(data.SpecAfkTime >= _coreAccessor.GetValue<float>("Config", "SpecWarnInterval")))
                    continue;
                
                if (data.SpecWarningCount == _coreAccessor.GetValue<int>("Config", "SpecKickAfterWarnings"))
                {
                    Server.PrintToChatAll(ReplaceVars(player, Localizer["ChatKickMessage"].Value));
                    Server.ExecuteCommand($"kickid {player.UserId}");

                    data.SpecWarningCount = 0;
                    data.SpecAfkTime = 0;
                    
                    continue;
                }

                player.PrintToChat( ReplaceVars(player, Localizer["ChatWarningKickMessage"].Value, _coreAccessor.GetValue<int>("Config", "SpecKickAfterWarnings") * _coreAccessor.GetValue<float>("Config", "SpecWarnInterval") - data.SpecWarningCount * _coreAccessor.GetValue<float>("Config", "SpecWarnInterval")));
                data.SpecWarningCount++;
                data.SpecAfkTime = 0;
            }
            #endregion
        }
    }
    
    private static float CalculateDistance(Vector point1, Vector point2)
    {
        var dx = point2.X - point1.X;
        var dy = point2.Y - point1.Y;
        var dz = point2.Z - point1.Z;

        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
    
    private static string GetTeamColor(CsTeam team)
    {
        return team switch
        {
            CsTeam.Spectator => ChatColors.Grey.ToString(),
            CsTeam.Terrorist => ChatColors.Red.ToString(),
            CsTeam.CounterTerrorist => ChatColors.Blue.ToString(),
            _ => ChatColors.Default.ToString()
        };
    }
    
    private string ReplaceVars(CCSPlayerController player, string message, float timeAmount = 0.0f)
    {
        return Localizer["ChatPrefix"] + message.Replace("{playerName}", player.PlayerName)
                      .Replace("{teamColor}", GetTeamColor(player.Team))
                      .Replace("{weaponName}", player.PlayerPawn?.Value?.WeaponServices?.ActiveWeapon?.Value?.DesignerName ?? "Unknown")
                      .Replace("{timeAmount}", $"{timeAmount:F1}")
                      .Replace("{slapAmount}", _coreAccessor.GetValue<int>("Config", "AntiCampSlapDamage").ToString())
                      .Replace("{zoneName}", player.PlayerPawn?.Value?.LastPlaceName ?? "Unknown");
    }
    
    private static void Slap(CBasePlayerPawn? pawn, int damage = 0)
    {
        if (pawn == null || pawn.Health <= 0)
            return;

        pawn.Health -= damage;

        if (pawn.Health <= 0)
        {
            pawn.CommitSuicide(true, true);
            return;
        }
        
        Random random = new();
        Vector vel = new(pawn.AbsVelocity.X, pawn.AbsVelocity.Y, pawn.AbsVelocity.Z);

        vel.X += (random.Next(180) + 50) * (random.Next(2) == 1 ? -1 : 1);
        vel.Y += (random.Next(180) + 50) * (random.Next(2) == 1 ? -1 : 1);
        vel.Z += random.Next(200) + 100;

        pawn.Teleport(pawn.AbsOrigin, pawn.AbsRotation, vel);
    }
 
    public class PlayerInfo
    {
        public QAngle? Angles { get; set; }
        public Vector? Origin { get; set; }
        public float AfkTime { get; set; }
        public int AfkWarningCount { get; set; }
        public int SpecWarningCount { get; set; }
        public float SpecAfkTime { get; set; }
        public bool MovedByPlugin { get; set; }
        public float AntiCampTime { get; set; }
        public int AntiCampWarningCount { get; set; }
    };

}