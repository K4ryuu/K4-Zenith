using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using ZenithAPI;

namespace Zenith.Models;

public sealed partial class Player
{
	// +--------------------+
	// | HELPER VARIABLES   |
	// +--------------------+

	private readonly Plugin _plugin;

	// +--------------------+
	// | PLAYER VARIABLES   |
	// +--------------------+

	public readonly CCSPlayerController? Controller;
	public readonly ulong SteamID;
	public readonly string Name;

	// +--------------------+
	// | PLAYER PROPERTIES  |
	// +--------------------+

	private readonly List<CenterMessage> _centerMessages = [];
	private CenterMessage? _cachedTopMessage;
	private float _lastUpdateTime;
	public bool Loaded = false;

	private Tuple<string, ActionPriority>? _clanTag = null;
	private Tuple<string, ActionPriority>? _nameTag = null;
	private Tuple<string, ActionPriority>? _nameColor = null;
	private Tuple<string, ActionPriority>? _chatColor = null;
	public Tuple<bool, ActionPriority>? _mute = null;
	public Tuple<bool, ActionPriority>? _gag = null;
	public Tuple<bool, ActionPriority>? _afk = null;
	public (string ShortName, string LongName) _country = ("??", "Unknown");

	// +--------------------+
	// | PLAYER CONSTRUCTOR |
	// +--------------------+

	public Player(Plugin plugin, CCSPlayerController? controller, bool noLoad = false)
	{
		_plugin = plugin;

		Controller = controller;
		SteamID = controller?.SteamID ?? 0;
		Name = controller?.PlayerName ?? "Unknown";

		if (List.Values.Any(player => player.Controller == controller))
			return;

		AddToList(this);

		_ = Task.Run(async () =>
		{
			try
			{
				if (!noLoad)
					await this.LoadPlayerData();
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error loading data for player {Name} (SteamID: {SteamID}): {ex.Message}");
			}
		});
	}

	// +--------------------+
	// | PLAYER VALIDATION  |
	// +--------------------+

	public bool IsValid
		=> Controller?.IsValid == true && Controller.PlayerPawn?.IsValid == true;

	public bool IsAlive
		=> Controller?.PlayerPawn.Value?.Health > 0;

	public bool IsMuted
		=> _mute?.Item1 ?? false;

	public bool IsGagged
		=> _gag?.Item1 ?? false;

	public bool IsAFK
		=> _afk?.Item1 ?? false;

	// +--------------------+
	// | PLAYER FUNCTIONS   |
	// +--------------------+

	public void Print(string message, bool showPrefix = true)
	{
		if (Controller == null || !Controller.IsValid)
		{
			RemoveFromList(SteamID);
			return;
		}

		string prefix = showPrefix ? $" {_plugin.Localizer.ForPlayer(Controller, "k4.general.prefix")}" : "";
		Controller?.PrintToChat($"{prefix}{message}");
	}

	public void PrintToCenter(string message, int duration = 3, ActionPriority priority = ActionPriority.Low, bool showCloseCounter = false)
	{
		_centerMessages.Add(new CenterMessage(message, Server.CurrentTime + duration, priority, showCloseCounter));
	}

	public void ShowCenterMessage()
	{
		if (_centerMessages.Count == 0)
		{
			if (_cachedTopMessage.HasValue)
				_cachedTopMessage = null;
			return;
		}

		var currentTime = Server.CurrentTime;

		if (!_cachedTopMessage.HasValue || _lastUpdateTime + 0.25f < currentTime || _centerMessages.Any(m => m.Duration <= currentTime))
		{
			UpdateCachedMessage(currentTime);
		}

		if (_cachedTopMessage.HasValue)
		{
			string finalMessage = _cachedTopMessage.Value.Message;
			if (string.IsNullOrEmpty(finalMessage))
				return;

			if (_cachedTopMessage.Value.ShowCloseCounter)
			{
				var remainingSeconds = (int)(_cachedTopMessage.Value.Duration - currentTime);
				finalMessage += $"\n\nThe message will disappear in {remainingSeconds} seconds.";
			}

			Controller?.PrintToCenterHtml(finalMessage);
		}
	}

	private void UpdateCachedMessage(float currentTime)
	{
		var orderedMessages = _centerMessages
			.Where(m => m.Duration > currentTime)
			.OrderByDescending(m => m.Priority)
			.ThenBy(m => m.Duration);

		_cachedTopMessage = orderedMessages.FirstOrDefault();
		_lastUpdateTime = currentTime;

		_centerMessages.RemoveAll(m => m.Duration <= currentTime);
	}

	public void SetMute(bool mute, ActionPriority priority)
	{
		if (priority < _mute?.Item2)
			return;

		if (mute)
		{
			if (!Controller!.VoiceFlags.HasFlag(VoiceFlags.Muted))
				Controller!.VoiceFlags |= VoiceFlags.Muted;

			_mute = new Tuple<bool, ActionPriority>(mute, priority);
		}
		else
		{
			if (Controller!.VoiceFlags.HasFlag(VoiceFlags.Muted))
				Controller!.VoiceFlags &= ~VoiceFlags.Muted;

			_mute = null;
		}
	}

	public void SetGag(bool gag, ActionPriority priority)
	{
		if (priority < _gag?.Item2)
			return;

		if (!gag)
			_gag = null;

		_gag = new Tuple<bool, ActionPriority>(gag, priority);
	}

	public void SetAFK(bool afk, ActionPriority priority)
	{
		if (priority < _afk?.Item2)
			return;

		if (!afk)
			_afk = null;

		_afk = new Tuple<bool, ActionPriority>(afk, priority);
	}

	public void SetClanTag(string? tag, ActionPriority priority)
	{
		if (tag == null)
		{
			_nameTag = null;
			return;
		}

		if (priority < _clanTag?.Item2)
			return;

		_clanTag = new Tuple<string, ActionPriority>(tag, priority);

		Controller!.Clan = tag;
		Utilities.SetStateChanged(Controller, "CCSPlayerController", "m_szClan");
	}

	public string GetClanTag()
		=> _clanTag?.Item1 ?? Controller?.Clan ?? string.Empty;

	public void SetNameTag(string? tag, ActionPriority priority)
	{
		if (tag == null)
		{
			_nameTag = null;
			return;
		}

		if (priority < _nameTag?.Item2)
			return;

		_nameTag = new Tuple<string, ActionPriority>(tag, priority);
	}

	public string GetNameTag()
	{
		string? nameTag = _nameTag?.Item1;
		if (nameTag != null)
			return nameTag;

		return _plugin.ReplacePlayerPlaceholders(Controller!, _plugin.GetCoreConfig<string>("Modular", "PlayerChatRankFormat"));
	}

	public void SetNameColor(string? color, ActionPriority priority)
	{
		if (color == null)
		{
			_nameColor = null;
			return;
		}

		if (priority < _nameColor?.Item2)
			return;

		_nameColor = new Tuple<string, ActionPriority>(color, priority);
	}

	public char GetNameColor()
			=> _nameColor != null ? ChatColorUtility.GetChatColorValue(_nameColor.Item1, Controller) : ChatColors.ForPlayer(Controller!);

	public void SetChatColor(string? color, ActionPriority priority)
	{
		if (color == null)
		{
			_chatColor = null;
			return;
		}

		if (priority < _chatColor?.Item2)
			return;

		_chatColor = new Tuple<string, ActionPriority>(color, priority);
	}

	public char GetChatColor()
		=> _chatColor != null ? ChatColorUtility.GetChatColorValue(_chatColor.Item1, Controller) : ChatColors.Default;

	public void EnforcePluginValues(string coreFormat)
	{
		if (IsMuted && Controller?.VoiceFlags.HasFlag(VoiceFlags.Muted) == false)
		{
			Controller!.VoiceFlags |= VoiceFlags.Muted;
		}

		if (GetSetting<bool>("ShowClanTags"))
		{
			string clanTag = (_clanTag?.Item1) ?? coreFormat;

			if (!string.IsNullOrEmpty(clanTag))
			{
				Controller!.Clan = _plugin.ReplacePlayerPlaceholders(Controller, clanTag);
				Utilities.SetStateChanged(Controller, "CCSPlayerController", "m_szClan");
			}
		}
	}

	// +--------------------+
	// | PLAYER DISPOSER    |
	// +--------------------+

	public void Dispose()
	{
		_plugin._moduleServices?.InvokeZenithPlayerUnloaded(Controller!);

		_ = Task.Run(async () =>
		{
			try
			{
				await SaveDataAsync(Settings, TABLE_PLAYER_SETTINGS);
				await SaveDataAsync(Storage, TABLE_PLAYER_STORAGE);
			}
			catch (Exception ex)
			{
				_plugin.Logger.LogError($"Error saving data for player {Name} (SteamID: {SteamID}) during disposal: {ex.Message}");
			}
			finally
			{
				RemoveFromList(SteamID);
			}
		});
	}
}

public struct CenterMessage(string message, float duration, ActionPriority priority, bool showCloseCounter = false)
{
	public string Message { get; set; } = message;
	public float Duration { get; set; } = duration;
	public ActionPriority Priority { get; set; } = priority;
	public bool ShowCloseCounter { get; set; } = showCloseCounter;
}