
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands.Targeting;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;

namespace Zenith_ExtendedCommands;

public sealed partial class Plugin : BasePlugin
{
	private void ProcessTargetAction(CCSPlayerController? caller, CCSPlayerController target, Action<CCSPlayerController> action, bool? aliveState = null)
	{
		if (!AdminManager.CanPlayerTarget(caller, target))
		{
			_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "commands.error.invalid_immunity", target.PlayerName));
			return;
		}

		if (aliveState == true && (target.PlayerPawn.Value == null || target.PlayerPawn.Value.LifeState != (byte)LifeState_t.LIFE_ALIVE))
		{
			_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "commands.error.invalid_dead", target.PlayerName));
			return;
		}

		if (aliveState == false && (target.PlayerPawn.Value == null || target.PlayerPawn.Value.LifeState == (byte)LifeState_t.LIFE_ALIVE))
		{
			_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "commands.error.invalid_alive", target.PlayerName));
			return;
		}

		action.Invoke(target);
	}

	private void ProcessTargetAction(CCSPlayerController? caller, TargetResult targetResult, Action<CCSPlayerController> action, bool? aliveState = null)
	{
		if (!targetResult.Any())
		{
			_moduleServices?.PrintForPlayer(caller, Localizer.ForPlayer(caller, "commands.error.invalid_target"));
			return;
		}

		foreach (CCSPlayerController target in targetResult.Players)
		{
			ProcessTargetAction(caller, target, action, aliveState);
		}
	}

	private static void RemoveWeapon(CCSPlayerController player, gear_slot_t? slot = null, string? className = null)
	{
		if (player.PlayerPawn.Value?.WeaponServices is null)
			return;

		List<CHandle<CBasePlayerWeapon>> weaponList = [.. player.PlayerPawn.Value.WeaponServices.MyWeapons];
		foreach (CHandle<CBasePlayerWeapon> weapon in weaponList)
		{
			if (weapon.IsValid && weapon.Value != null)
			{
				CCSWeaponBase ccsWeaponBase = weapon.Value.As<CCSWeaponBase>();
				if (ccsWeaponBase?.IsValid == true)
				{
					CCSWeaponBaseVData? weaponData = ccsWeaponBase.VData;

					if (weaponData == null || (slot != null && weaponData.GearSlot != slot) || (className != null && !ccsWeaponBase.DesignerName.Contains(className)))
						continue;

					player.PlayerPawn.Value.WeaponServices.ActiveWeapon.Raw = weapon.Raw;
					player.DropActiveWeapon();

					Server.NextFrame(() =>
					{
						if (ccsWeaponBase != null && ccsWeaponBase.IsValid)
						{
							ccsWeaponBase.AcceptInput("Kill");
						}
					});
				}
			}
		}
	}

	private List<string> GetItems(CCSPlayerController player, gear_slot_t? slot = null, string? className = null)
	{
		List<string> items = new();
		if (player.PlayerPawn.Value?.WeaponServices is null)
			return items;

		List<CHandle<CBasePlayerWeapon>> weaponList = [.. player.PlayerPawn.Value.WeaponServices.MyWeapons];
		foreach (CHandle<CBasePlayerWeapon> weapon in weaponList)
		{
			if (weapon.IsValid && weapon.Value != null)
			{
				CCSWeaponBase ccsWeaponBase = weapon.Value.As<CCSWeaponBase>();
				if (ccsWeaponBase?.IsValid == true)
				{
					CCSWeaponBaseVData? weaponData = ccsWeaponBase.VData;

					if (weaponData == null || (slot != null && weaponData.GearSlot != slot) || (className != null && !ccsWeaponBase.DesignerName.Contains(className)))
						continue;

					if (ccsWeaponBase.DesignerName == "weapon_healthshot")
					{
						for (int i = 0; i < player.PlayerPawn.Value.WeaponServices.Ammo[20]; i++)
							items.Add(ccsWeaponBase.DesignerName);
					}
					else
						items.Add(ccsWeaponBase.DesignerName);
				}
			}
		}

		return items;
	}

	static float RandomVelocityComponent()
	{
		return (Random.Shared.Next(180) + 50) * (Random.Shared.Next(2) == 1 ? -1 : 1);
	}

	private void ShowActivityToPlayers(ulong? callerSteamId, string localizerKey, params object[] args)
	{
		var players = Utilities.GetPlayers();
		for (int i = 0; i < players.Count; i++)
		{
			var player = players[i];
			if (player == null || !player.IsValid || player.IsBot || player.IsHLTV)
				continue;

			if (ShouldShowActivity(callerSteamId, player, true))
			{
				_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, localizerKey, args));
			}
			else if (ShouldShowActivity(callerSteamId, player, false))
			{
				var anonymousArgs = new object[args.Length];
				Array.Copy(args, anonymousArgs, args.Length);
				anonymousArgs[0] = Localizer.ForPlayer(player, "k4.general.admin");
				_moduleServices?.PrintForPlayer(player, Localizer.ForPlayer(player, localizerKey, anonymousArgs));
			}
		}
	}

	public void SetConvarValue(ConVar? cvar, string? value)
	{
		if (cvar == null) return;
		if (string.IsNullOrEmpty(value)) return;

		var flag = cvar.Flags;

		if ((flag & ConVarFlags.FCVAR_CHEAT) > 0)
			cvar.Flags &= ~ConVarFlags.FCVAR_CHEAT;

		Server.ExecuteCommand($"{cvar.Name} {value}");

		if (flag != cvar.Flags)
		{
			AddTimer(0.1f, () =>
			{
				cvar.Flags = flag;
			});
		}
	}

	private bool ShouldShowActivity(ulong? adminSteamId, CCSPlayerController player, bool showName)
	{
		if (!adminSteamId.HasValue) return true; // Always show console activity
		if (!_coreAccessor.HasValue("Core", "ShowActivity")) return true; // Show activity if no ZenithBans installed

		int _showActivity = _coreAccessor.GetValue<int>("Core", "ShowActivity");

		bool isRoot = AdminManager.PlayerHasPermissions(player, "@zenith/root");
		bool isPlayerAdmin = AdminManager.PlayerHasPermissions(player, "@zenith/admin");

		if (isRoot && (_showActivity & 16) != 0) return true; // Always show to root

		if (isPlayerAdmin)
		{
			if ((_showActivity & 4) == 0) return false; // Don't show to admins
			if (showName && (_showActivity & 8) == 0) return false; // Don't show names to admins
		}
		else
		{
			if ((_showActivity & 1) == 0) return false; // Don't show to non-admins
			if (showName && (_showActivity & 2) == 0) return false; // Don't show names to non-admins
		}

		return true;
	}

	private static CsTeam GetTeamFromName(string teamName)
	{
		return teamName.ToLower() switch
		{
			"t" => CsTeam.Terrorist,
			"terrorist" => CsTeam.Terrorist,
			"tt" => CsTeam.Terrorist,
			"ct" => CsTeam.CounterTerrorist,
			"counterterrorist" => CsTeam.CounterTerrorist,
			"spec" => CsTeam.Spectator,
			"spectator" => CsTeam.Spectator,
			_ => CsTeam.None
		};
	}
}