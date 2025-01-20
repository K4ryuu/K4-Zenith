using System.Collections.Concurrent;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Commands;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;
using Zenith.Models;

namespace Zenith
{
	public class ZenithCommand
	{
		public required string Module;
		public required string Command;
		public required string Description;
		public required CommandInfo.CommandCallback Callback;
		public CommandUsage Usage = CommandUsage.CLIENT_AND_SERVER;
		public int ArgCount = 0;
		public string? HelpText = null;
		public string? Permission = null;
	}

	public sealed partial class Plugin : BasePlugin
	{
		private readonly ConcurrentDictionary<string, List<CommandDefinition>> _pluginCommands = new();
		private readonly ConcurrentBag<ZenithCommand> _zenithCommands = [];

		public void RegisterZenithCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
		{
			command = EnsureCommandPrefix(command);
			string callingPlugin = CallerIdentifier.GetCallingPluginName();

			var existingCommand = FindExistingCommand(command);

			if (existingCommand.HasValue)
			{
				if (existingCommand.Value.Plugin != callingPlugin)
				{
					Logger.LogError($"Command '{command}' is already registered by plugin '{existingCommand.Value.Plugin}'. Registration by '{callingPlugin}' is not allowed.");
					return;
				}

				RemoveExistingCommand(existingCommand);
				Logger.LogWarning($"Command '{command}' already exists for plugin '{callingPlugin}', overwriting.");
			}

			RegisterNewCommand(command, description, handler, usage, argCount, helpText, permission, callingPlugin);
		}

		private static string EnsureCommandPrefix(string command) => command.StartsWith("css_") ? command : "css_" + command;

		private (string Plugin, CommandDefinition Command)? FindExistingCommand(string command)
		{
			var existingCommand = _pluginCommands
				.SelectMany(kvp => kvp.Value.Select(cmd => new { Plugin = kvp.Key, Command = cmd }))
				.FirstOrDefault(x => x.Command.Name == command);

			if (existingCommand != null)
			{
				return (existingCommand.Plugin, existingCommand.Command);
			}

			return null;
		}

		private void RemoveExistingCommand((string Plugin, CommandDefinition Command)? existingCommand)
		{
			if (existingCommand.HasValue)
			{
				CommandManager.RemoveCommand(existingCommand.Value.Command);
				_pluginCommands[existingCommand.Value.Plugin].Remove(existingCommand.Value.Command);
			}
		}

		private void RegisterNewCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage, int argCount, string? helpText, string? permission, string callingPlugin)
		{
			var newCommand = new CommandDefinition(command, description, (controller, info) =>
			{
				if (!CommandHelper(controller, info, usage, argCount, helpText, permission))
					return;

				handler(controller, info);
			});

			CommandManager.RegisterCommand(newCommand);
			_pluginCommands.GetOrAdd(callingPlugin, _ => []).Add(newCommand);

			_zenithCommands.Add(new ZenithCommand
			{
				Module = callingPlugin,
				Command = command,
				Description = description,
				Callback = handler,
				Usage = usage,
				ArgCount = argCount,
				HelpText = helpText,
				Permission = permission
			});
		}

		public void RemoveModuleCommands(string callingPlugin)
		{
			if (_pluginCommands.TryGetValue(callingPlugin, out var pluginCommands))
			{
				foreach (var command in pluginCommands)
				{
					CommandManager.RemoveCommand(command);
				}
				_pluginCommands.TryRemove(callingPlugin, out _);

				var commandsToRemove = _zenithCommands.Where(c => c.Module == callingPlugin).ToList();
				foreach (var cmd in commandsToRemove)
				{
					_zenithCommands.TryTake(out _);
				}
			}
		}

		public void ListAllCommands(string? pluginName = null, CCSPlayerController? player = null)
		{
			if (pluginName != null)
			{
				PrintPluginCommands(pluginName, player);
			}
			else
			{
				foreach (var pluginEntry in _pluginCommands)
				{
					PrintPluginCommands(pluginEntry.Key, player);
				}
			}

			Player.Find(player)?.Print("Command list has been printed to your console.");
		}

		private void PrintPluginCommands(string pluginName, CCSPlayerController? player)
		{
			var pluginCommands = _zenithCommands.Where(c => c.Module == pluginName).ToList();
			if (pluginCommands.Count != 0)
			{
				PrintToConsole($"Commands for plugin '{pluginName}':", player);
				foreach (var command in pluginCommands)
				{
					string permission = command.Permission ?? string.Empty;
					PrintToConsole($"  - {command.Command}{(command.HelpText != null ? " " + command.HelpText : "")}: {command.Description} {(string.IsNullOrEmpty(permission) ? "" : $"({permission})")}", player);
				}
			}
			else
			{
				PrintToConsole($"No commands found for plugin '{pluginName}'.", player);
			}
		}

		public void RemoveAllCommands()
		{
			foreach (var pluginEntry in _pluginCommands)
			{
				foreach (var command in pluginEntry.Value)
				{
					CommandManager.RemoveCommand(command);
				}
			}

			_pluginCommands.Clear();
			_zenithCommands.Clear();
		}

		public void RegisterZenithCommand(List<string> commands, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null)
		{
			foreach (var command in commands)
			{
				RegisterZenithCommand(command, description, handler, usage, argCount, helpText, permission);
			}
		}

		public bool CommandHelper(CCSPlayerController? controller, CommandInfo info, CommandUsage usage, int argCount = 0, string? helpText = null, string? permission = null)
		{
			Player? player = Player.Find(controller);

			if (!IsCommandUsageValid(player, info, usage))
				return false;

			if (!HasPermission(player, controller, info, permission))
				return false;

			if (IsArgumentCountInvalid(controller, info, argCount, helpText))
				return false;

			return true;
		}

		private bool IsCommandUsageValid(Player? player, CommandInfo info, CommandUsage usage)
		{
			switch (usage)
			{
				case CommandUsage.CLIENT_ONLY when player == null || !player.IsValid:
					info.ReplyToCommand($" {Localizer.ForPlayer(player?.Controller, "k4.general.prefix")} {Localizer.ForPlayer(player?.Controller, "k4.command.client-only")}");
					return false;
				case CommandUsage.SERVER_ONLY when player != null:
					info.ReplyToCommand($" {Localizer.ForPlayer(player?.Controller, "k4.general.prefix")} {Localizer.ForPlayer(player?.Controller, "k4.command.server-only")}");
					return false;
				default:
					return true;
			}
		}

		private bool HasPermission(Player? player, CCSPlayerController? controller, CommandInfo info, string? permission)
		{
			if (string.IsNullOrEmpty(permission))
				return true;

			if (player != null && !AdminManager.PlayerHasPermissions(controller, permission) &&
				!AdminManager.PlayerHasPermissions(controller, "@zenith/root") &&
				!AdminManager.PlayerHasPermissions(controller, "@css/root") &&
				(!AdminManager.PlayerHasCommandOverride(controller, info.GetArg(0)) || AdminManager.GetPlayerCommandOverrideState(controller, info.GetArg(0)) == false))
			{
				info.ReplyToCommand($" {Localizer.ForPlayer(controller, "k4.general.prefix")} {Localizer.ForPlayer(controller, "k4.command.no-permission")}");
				return false;
			}

			return true;
		}

		private bool IsArgumentCountInvalid(CCSPlayerController? controller, CommandInfo info, int argCount, string? helpText)
		{
			if (argCount > 0 && info.ArgCount < argCount + 1 && helpText != null)
			{
				info.ReplyToCommand($" {Localizer.ForPlayer(controller, "k4.general.prefix")} {Localizer.ForPlayer(controller, "k4.command.help", info.ArgByIndex(0), helpText)}");
				return true;
			}
			return false;
		}
	}
}
