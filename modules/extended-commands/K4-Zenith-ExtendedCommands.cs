using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Capabilities;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith_ExtendedCommands;

[MinimumApiVersion(250)]
public sealed partial class Plugin : BasePlugin
{
	private const string MODULE_ID = "ExtendedCommands";

	public override string ModuleName => $"K4-Zenith | {MODULE_ID}";
	public override string ModuleAuthor => "K4ryuu @ KitsuneLab";
	public override string ModuleVersion => "1.0.6";

	private IModuleConfigAccessor _coreAccessor = null!;

	private PluginCapability<IModuleServices>? _moduleServicesCapability;

	private IZenithEvents? _zenithEvents;
	private IModuleServices? _moduleServices;

	public override void OnAllPluginsLoaded(bool hotReload)
	{
		string pluginDirectory = Path.GetDirectoryName(ModuleDirectory)!;
		List<string> blockPlugins = ["CS2-SimpleAdmin"];
		foreach (var p in blockPlugins)
		{
			if (Directory.GetDirectories(pluginDirectory, p).Any())
			{
				Logger.LogCritical($"This module is not compatible with {p}. You can use only one of them. Unloading...");
				Server.ExecuteCommand($"css_plugins unload {Path.GetFileNameWithoutExtension(ModulePath)}");
				return;
			}
		}

		try
		{
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

		_zenithEvents = _moduleServices.GetEventHandler();
		if (_zenithEvents != null)
		{
			_zenithEvents.OnZenithCoreUnload += OnZenithCoreUnload;
		}
		else
		{
			Logger.LogError("Failed to get Zenith event handler.");
		}

		Initialize_Commands();
		Initialize_Events();

		Logger.LogInformation("Zenith {0} module successfully registered.", MODULE_ID);
	}

	private void OnZenithCoreUnload(bool hotReload)
	{
		if (hotReload)
		{
			AddTimer(3.0f, () =>
			{
				try { File.SetLastWriteTime(Path.Combine(ModulePath), DateTime.Now); }
				catch (Exception ex) { Logger.LogError($"Failed to update file: {ex.Message}"); }
			});
		}
	}

	public override void Unload(bool hotReload)
	{
		_moduleServicesCapability?.Get()?.DisposeModule(this.GetType().Assembly);
	}
}