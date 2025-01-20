using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Localization;

namespace ZenithAPI
{
	/// <summary>
	/// Provides services for managing player-specific data.
	/// </summary>
	public interface IPlayerServices // ! zenith:player-services
	{
		/// <summary>
		/// The player's controller.
		/// </summary>
		CCSPlayerController Controller { get; }

		/// <summary>
		/// The SteamID of the player.
		/// </summary>
		ulong SteamID { get; }

		/// <summary>
		/// The name of the player.
		/// </summary>
		string Name { get; }

		/// <summary>
		/// Checks if the player is valid and connected.
		/// </summary>
		bool IsValid { get; }

		/// <summary>
		/// Checks if the player is alive.
		/// </summary>
		bool IsAlive { get; }

		/// <summary>
		/// Checks if the player is muted in voice chat.
		/// </summary>
		bool IsMuted { get; }

		/// <summary>
		/// Checks if the player is gagged in chat.
		/// </summary>
		bool IsGagged { get; }

		/// <summary>
		/// Checks if the player is afk.
		/// </summary>
		bool IsAFK { get; }

		/// <summary>
		/// Sets the player's mute status.
		/// </summary>
		void SetMute(bool mute, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's gag status.
		/// </summary>
		void SetGag(bool gag, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's afk status.
		/// </summary>
		void SetAFK(bool afk, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Prints a message to the player's chat.
		/// </summary>
		/// <param name="message">The message to print.</param>
		void Print(string message);

		/// <summary>
		/// Prints a message to the center of the player's screen.
		/// </summary>
		/// <param name="message">The message to print.</param>
		/// <param name="duration">The duration to display the message, in seconds.</param>
		/// <remarks>Duration defaults to 3 seconds if not specified.</remarks>
		void PrintToCenter(string message, int duration = 3, ActionPriority priority = ActionPriority.Low, bool showCloseCounter = false);

		/// <summary>
		/// Sets the player's clan tag.
		/// </summary>
		/// <param name="tag">The tag to set, or null to clear the tag.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetClanTag(string? tag, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's name tag.
		/// </summary>
		/// <param name="tag">The tag to set, or null to clear the tag.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetNameTag(string? tag, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's name color.
		/// </summary>
		/// <param name="color">The color to set, or null to clear the color.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetNameColor(string? color, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Sets the player's chat color.
		/// </summary>
		/// <param name="color">The color to set, or null to clear the color.</param>
		/// <param name="priority">The priority of the action.</param>
		void SetChatColor(string? color, ActionPriority priority = ActionPriority.Low);

		/// <summary>
		/// Retrieves a setting value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the setting.</param>
		/// <returns>The value of the setting, or null if not found.</returns>
		T? GetSetting<T>(string key, string? moduleID = null);

		/// <summary>
		/// Sets a setting value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the setting.</param>
		/// <param name="value">The value to set.</param>
		/// <param name="saveImmediately">If true, saves the setting to the database immediately.</param>
		void SetSetting(string key, object? value, bool saveImmediately = false, string? moduleID = null);

		/// <summary>
		/// Retrieves a storage value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the storage item.</param>
		/// <returns>The value of the storage item, or null if not found.</returns>
		T? GetStorage<T>(string key, string? moduleID = null);

		/// <summary>
		/// Sets a storage value for a specific module and key.
		/// </summary>
		/// <param name="key">The key of the storage item.</param>
		/// <param name="value">The value to set.</param>
		/// <param name="saveImmediately">If true, saves the storage item to the database immediately.</param>
		void SetStorage(string key, object? value, bool saveImmediately = false, string? moduleID = null);

		/// <summary>
		/// Saves all settings and storage items, or those for a specific module.
		/// </summary>
		void Save();

		/// <summary>
		/// Loads all player data from the database.
		/// </summary>
		void LoadPlayerData();

		/// <summary>
		/// Resets the settings for a specific module to their default values.
		/// </summary>
		void ResetModuleSettings();

		/// <summary>
		/// Resets the storage items for a specific module to their default values.
		/// </summary>
		void ResetModuleStorage();

		/// <summary>
		/// Replaces the placeholders that are registered by modules or the Zenith core such as {name}, {ip}, etc
		/// </summary>
		/// <param name="player"></param>
		/// <param name="text"></param>
		/// <returns>Returns the replaced text</returns>
		string ReplacePlaceholders(string text);
	}

	/// <summary>
	/// Provides services for managing module-specific data.
	/// </summary>
	public interface IModuleServices : IZenithEvents // ! zenith:module-services
	{
		/// <summary>
		/// Prints a message to all players' chat.
		/// </summary>
		void PrintForAll(string message, bool showPrefix = true);

		/// <summary>
		/// Prints a message to all players on a specific team.
		/// </summary>
		void PrintForTeam(CsTeam team, string message, bool showPrefix = true);

		/// <summary>
		/// Prints a message to all players on a specific team.
		/// </summary>
		void PrintForPlayer(CCSPlayerController? player, string message, bool showPrefix = true);

		/// <summary>
		/// Return true if a module is enabled. False otherwise.
		/// </summary>
		bool IsModuleEnabled(string module);

		/// <summary>
		/// Retrieves the connection string for the database.
		/// </summary>
		string GetConnectionString();

		/// <summary>
		/// Registers default settings for a module.
		/// </summary>
		/// <param name="defaultSettings">A dictionary of default settings.</param>
		void RegisterModuleSettings(Dictionary<string, object?> defaultSettings, IStringLocalizer? localizer = null);

		/// <summary>
		/// Registers default storage items for a module.
		/// </summary>
		/// <param name="defaultStorage">A dictionary of default storage items.</param>
		void RegisterModuleStorage(Dictionary<string, object?> defaultStorage);

		/// <summary>
		/// Registers a command for a module.
		/// </summary>
		/// <param name="command">The command to register.</param>
		/// <param name="description">The description of the command.</param>
		/// <param name="handler">The callback function to execute when the command is invoked.</param>
		/// <param name="usage">The usage type of the command.</param>
		/// <param name="argCount">The number of arguments required for the command.</param>
		/// <param name="helpText">The help text to display when the command is used incorrectly.</param>
		/// <param name="permission">The permission required to use the command.</param>
		void RegisterModuleCommand(string command, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null);

		/// <summary>
		/// Registers multiple commands for a module.
		/// </summary>
		/// <param name="commands">The commands to register.</param>
		/// <param name="description">The description of the commands.</param>
		/// <param name="handler">The callback function to execute when the commands are invoked.</param>
		/// <param name="usage">The usage type of the commands.</param>
		/// <param name="argCount">The number of arguments required for the commands.</param>
		/// <param name="helpText">The help text to display when the commands are used incorrectly.</param>
		/// <param name="permission">The permission required to use the commands.</param>
		void RegisterModuleCommands(List<string> commands, string description, CommandInfo.CommandCallback handler, CommandUsage usage = CommandUsage.CLIENT_AND_SERVER, int argCount = 0, string? helpText = null, string? permission = null);

		/// <summary>
		/// Registers a placeholder for a player-specific value.
		/// </summary>
		/// <param name="key">The key of the placeholder.</param>
		/// <param name="valueFunc">The function to retrieve the value.</param>
		void RegisterModulePlayerPlaceholder(string key, Func<CCSPlayerController, string> valueFunc);

		/// <summary>
		/// Registers a placeholder for a server-specific value.
		/// </summary>
		/// <param name="key">The key of the placeholder.</param>
		/// <param name="valueFunc">The function to retrieve the value.</param>
		void RegisterModuleServerPlaceholder(string key, Func<string> valueFunc);

		/// <summary>
		/// Registers a module configuration setting.
		/// </summary>
		/// <typeparam name="T">The type of the setting.</typeparam>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		/// <param name="description">The description of the setting.</param>
		/// <param name="defaultValue">The default value of the setting.</param>
		/// <param name="flags">The flags of the setting.</param>
		void RegisterModuleConfig<T>(string groupName, string configName, string description, T defaultValue, ConfigFlag flags = ConfigFlag.None) where T : notnull;

		/// <summary>
		/// Checks if a module configuration setting exists.
		/// </summary>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		bool HasModuleConfigValue(string groupName, string configName);

		/// <summary>
		/// Retrieves a module configuration setting.
		/// </summary>
		/// <typeparam name="T">The type of the setting.</typeparam>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		T GetModuleConfigValue<T>(string groupName, string configName) where T : notnull;

		/// <summary>
		/// Sets a module configuration setting.
		/// </summary>
		/// <typeparam name="T">The type of the setting.</typeparam>
		/// <param name="groupName">The group name of the setting.</param>
		/// <param name="configName">The name of the setting.</param>
		/// <param name="value">The value to set.</param>
		void SetModuleConfigValue<T>(string groupName, string configName, T value) where T : notnull;

		/// <summary>
		/// Retrieves a module configuration setting.
		/// </summary>
		IModuleConfigAccessor GetModuleConfigAccessor();

		/// <summary>
		/// Retrieves the event handler for the module.
		/// </summary>
		IZenithEvents GetEventHandler();

		/// <summary>
		/// Loads all player data from the database.
		/// </summary>
		void LoadAllOnlinePlayerData();

		/// <summary>
		/// Saves all player data to the database.
		/// </summary>
		void SaveAllOnlinePlayerData();

		/// <summary>
		/// Dispose the module's Zenith based resources such as commands, configs, and player datas.
		/// </summary>
		void DisposeModule(Assembly assembly);

		void ResetModuleStorage(ulong steamId);

		void ResetModuleSettings(ulong steamId);

		void ResetModuleSettings(CCSPlayerController player);

		void ResetModuleStorage(CCSPlayerController player);
	}

	public interface IModuleConfigAccessor
	{
		/// <summary>
		///  Retrieves a configuration value.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="groupName"></param>
		/// <param name="configName"></param>
		/// <returns></returns>
		T GetValue<T>(string groupName, string configName) where T : notnull;

		/// <summary>
		/// Sets a configuration value.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="groupName"></param>
		/// <param name="configName"></param>
		/// <param name="value"></param>
		void SetValue<T>(string groupName, string configName, T value) where T : notnull;

		/// <summary>
		/// Checks if a configuration value exists.
		/// </summary>
		/// <param name="groupName"></param>
		/// <param name="configName"></param>
		bool HasValue(string groupName, string configName);
	}

	public interface IZenithEvents
	{
		/// <summary>
		/// Occurs when a player is loaded and their data is downloaded.
		/// </summary>
		/// <param name="player">The player who was loaded.</param>
		event Action<CCSPlayerController> OnZenithPlayerLoaded;

		/// <summary>
		/// Occurs when a player is unloaded and their data is saved.
		/// </summary>
		/// <param name="player">The player who was unloaded.</param>
		event Action<CCSPlayerController> OnZenithPlayerUnloaded;

		/// <summary>
		/// Occurs when the Zenith storage is reset.
		/// </summary>
		/// <param name="moduleID">The ID of the module that reset the storage.</param>
		event Action<string> OnZenithStorageReset;

		/// <summary>
		/// Invokes the OnZenithCoreUnload event.
		/// </summary>
		/// <param name="hotReload">True if the core is being reloaded.</param>
		event Action<bool> OnZenithCoreUnload;

		/// <summary>
		/// Occurs when a player sends a chat message.
		/// </summary>
		/// <param name="player">The player who sent the message.</param>
		/// <param name="message">The message itself that was sent.</param>
		/// <param name="formattedMessage">The formatted message that was sent. (full line with prefix, colors, etc)</param>
		event Action<CCSPlayerController, string, string> OnZenithChatMessage;
	}

	public class SettingChangedEventArgs : EventArgs
	{
		public CCSPlayerController Controller { get; }
		public string Key { get; }
		public object? OldValue { get; }
		public object? NewValue { get; }

		public SettingChangedEventArgs(CCSPlayerController controller, string key, object? oldValue, object? newValue)
		{
			Controller = controller;
			Key = key;
			OldValue = oldValue;
			NewValue = newValue;
		}
	}

	public enum ActionPriority
	{
		Low = 0,
		Normal = 1,
		High = 2
	}

	[Flags]
	public enum ConfigFlag
	{
		None = 0,
		Global = 1, // Allow all other modules to access this config value
		Protected = 2, // Prevent this config value from retrieving the value (hidden)
		Locked = 4 // Prevent this config value from being changed (read-only)
	}

	public static class PerformanceProfiler
	{
		private static readonly ConsoleColor TitleColor = ConsoleColor.Cyan;
		private static readonly ConsoleColor ErrorColor = ConsoleColor.Red;
		private static readonly ConsoleColor ResultColor = ConsoleColor.Green;
		private static readonly ConsoleColor DetailColor = ConsoleColor.Yellow;
		private static readonly ConsoleColor ValueColor = ConsoleColor.White;

		public static void ProfileGenericFunction<T, TResult>(T instance, string methodName, object[] args, int iterations)
		{
			var method = typeof(T).GetMethod(methodName);
			if (method == null)
			{
				WriteColorLine(ErrorColor, $"Method '{methodName}' not found in type {typeof(T).Name}");
				return;
			}

			var genericMethod = method.MakeGenericMethod(typeof(TResult));

			WriteColorLine(TitleColor, $"\n{"=",-20}[ Profiling Generic Method ]{"=",-20}");
			WriteColorLine(TitleColor, $"Method: {genericMethod.DeclaringType?.FullName}.{genericMethod.Name}<{typeof(TResult).Name}>");
			WriteColorLine(TitleColor, $"Parameter Types: {string.Join(", ", genericMethod.GetParameters().Select(p => p.ParameterType.Name))}");
			WriteColorLine(TitleColor, $"Return Type: {genericMethod.ReturnType.Name}");

			ProfileMethodExecution(genericMethod, instance, args, iterations);
		}

		public static void ProfileNonGenericFunction<T>(T instance, string methodName, object[] args, int iterations)
		{
			var method = typeof(T).GetMethod(methodName);
			if (method == null)
			{
				WriteColorLine(ErrorColor, $"Method '{methodName}' not found in type {typeof(T).Name}");
				return;
			}

			WriteColorLine(TitleColor, $"\n{"=",-20}[ Profiling Non-Generic Method ]{"=",-20}");
			WriteColorLine(TitleColor, $"Method: {method.DeclaringType?.FullName}.{method.Name}");
			WriteColorLine(TitleColor, $"Parameter Types: {string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}");
			WriteColorLine(TitleColor, $"Return Type: {method.ReturnType.Name}");

			ProfileMethodExecution(method, instance, args, iterations);
		}

		private static void ProfileMethodExecution(MethodInfo method, object? instance, object[] args, int iterations)
		{
			var parameters = method.GetParameters();
			if (args.Length < parameters.Length)
			{
				var newArgs = new object[parameters.Length];
				Array.Copy(args, newArgs, args.Length);
				for (int i = args.Length; i < parameters.Length; i++)
				{
					newArgs[i] = Type.Missing;
				}
				args = newArgs;
			}

			var stopwatch = new Stopwatch();
			var executionTimes = new List<long>();
			object? result = null;

			// Garbage Collection monitoring
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			var gcBefore = GC.CollectionCount(0);

			for (int i = 0; i < iterations; i++)
			{
				// Stopwatch for method execution
				stopwatch.Restart();
				result = method.Invoke(instance, args);
				stopwatch.Stop();
				executionTimes.Add(stopwatch.ElapsedTicks);
			}

			var gcAfter = GC.CollectionCount(0);

			// Memory usage profiling
			var memoryBefore = GC.GetTotalMemory(false);
			result = method.Invoke(instance, args); // Another call to measure memory impact
			var memoryAfter = GC.GetTotalMemory(false);
			var memoryUsedBytes = memoryAfter - memoryBefore;
			var memoryUsedMB = (double)memoryUsedBytes / (1024 * 1024);

			// Profiling results
			long totalTicks = executionTimes.Sum();
			double avgMs = totalTicks / (double)iterations / Stopwatch.Frequency * 1000;
			double minMs = executionTimes.Min() / (double)Stopwatch.Frequency * 1000;
			double maxMs = executionTimes.Max() / (double)Stopwatch.Frequency * 1000;
			double medianMs = executionTimes.OrderBy(t => t).ElementAt(iterations / 2) / (double)Stopwatch.Frequency * 1000;
			double stdDev = Math.Sqrt(executionTimes.Select(t => Math.Pow(t / (double)Stopwatch.Frequency * 1000 - avgMs, 2)).Sum() / iterations);

			WriteColorLine(ResultColor, $"\n{"=",-20}[ Performance Results ]{"=",-20}");
			WriteColorLine(ResultColor, $"Iterations: {iterations}");
			WriteDetailValue("Total time", $"{totalTicks / (double)Stopwatch.Frequency * 1000:F6} ms");
			WriteDetailValue("Average time", $"{avgMs:F6} ms");
			WriteDetailValue("Minimum time", $"{minMs:F6} ms");
			WriteDetailValue("Maximum time", $"{maxMs:F6} ms");
			WriteDetailValue("Median time", $"{medianMs:F6} ms");
			WriteDetailValue("Standard deviation", $"{stdDev:F6} ms");
			WriteDetailValue("GC collections", $"{gcAfter - gcBefore}");
			WriteDetailValue("Memory usage", $"{memoryUsedMB:F6} MB ({memoryUsedBytes:N0} bytes)");
			WriteDetailValue("Return value", $"{result}");

			WriteColorLine(DetailColor, $"\n{"=",-20}[ Method Attributes ]{"=",-20}");
			foreach (var attribute in method.GetCustomAttributes(true))
			{
				Console.WriteLine($"- {attribute.GetType().Name}");
			}

			WriteColorLine(DetailColor, $"\n{"=",-20}[ Parameter Details ]{"=",-20}");
			foreach (var param in method.GetParameters())
			{
				Console.WriteLine($"- {param.Name}: {param.ParameterType.Name} (In: {param.IsIn}, Out: {param.IsOut}, Optional: {param.IsOptional})");
			}
		}

		private static void WriteColorLine(ConsoleColor color, string message)
		{
			Console.ForegroundColor = color;
			Console.WriteLine(message);
			Console.ResetColor();
		}

		private static void WriteDetailValue(string detail, string value)
		{
			Console.ForegroundColor = DetailColor;
			Console.Write($"{detail,-20} ");
			Console.ForegroundColor = ValueColor;
			Console.WriteLine(value);
			Console.ResetColor();
		}
	}

	public static class ChatColorUtility
	{
		private static readonly Dictionary<string, char> _chatColors;
		private static readonly Regex _colorPattern;

		static ChatColorUtility()
		{
			_chatColors = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
			var chatColorType = typeof(ChatColors);
			var fields = chatColorType.GetFields(BindingFlags.Public | BindingFlags.Static);

			foreach (var field in fields)
			{
				if (field.FieldType == typeof(char) && !field.IsObsolete())
				{
					string colorName = field.Name.ToLowerInvariant();
					char colorValue = (char)field.GetValue(null)!;
					_chatColors[colorName] = colorValue;
				}
			}

			string pattern = string.Join("|", _chatColors.Keys.Select(k => $@"\{{{k}\}}|{k}"));
			_colorPattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
		}

		public static string ApplyPrefixColors(string msg)
		{
			if (string.IsNullOrEmpty(msg))
				return msg;

			return _colorPattern.Replace(msg, match =>
			{
				string key = match.Value.Trim('{', '}');
				return _chatColors.TryGetValue(key, out char color) ? color.ToString() : match.Value;
			});
		}

		public static char GetChatColorValue(string colorName, CCSPlayerController? player = null)
		{
			if (colorName.Equals("team", StringComparison.OrdinalIgnoreCase) && player != null)
			{
				return ChatColors.ForPlayer(player);
			}

			if (_chatColors.TryGetValue(colorName, out char color))
			{
				return color;
			}

			return ChatColors.Default;
		}
	}

	public static class ReflectionExtensions
	{
		public static bool IsObsolete(this FieldInfo field)
		{
			return field.GetCustomAttribute<ObsoleteAttribute>() != null;
		}
	}
}
