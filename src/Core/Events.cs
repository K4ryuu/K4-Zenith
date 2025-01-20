namespace Zenith
{
	using CounterStrikeSharp.API;
	using CounterStrikeSharp.API.Core;
	using CounterStrikeSharp.API.Core.Attributes.Registration;
	using CounterStrikeSharp.API.Core.Translations;
	using CounterStrikeSharp.API.Modules.UserMessages;
	using CounterStrikeSharp.API.Modules.Utils;
	using Zenith.Models;

	public sealed partial class Plugin : BasePlugin
	{
		public void Initialize_Events()
		{
			HookUserMessage(118, OnMessage, HookMode.Pre);
		}

		public HookResult OnMessage(UserMessage um)
		{
			int entity = um.ReadInt("entityindex");
			Player? player = Player.Find(Utilities.GetPlayerFromIndex(entity));
			if (player == null || !player.IsValid || player.Controller is null)
				return HookResult.Continue;

			if (player.IsGagged)
				return HookResult.Stop;

			if (!GetCoreConfig<bool>("Core", "HookChatMessages"))
				return HookResult.Continue;

			bool enabledChatModifier = player.GetSetting<bool>("ShowChatTags");

			string dead = player.IsAlive ? string.Empty : Localizer.ForPlayer(player.Controller, "k4.tag.dead");
			string team = um.ReadString("messagename").Contains("All") ? Localizer.ForPlayer(player.Controller, "k4.tag.all") : TeamLocalizer(player.Controller);
			string tag = enabledChatModifier ? player.GetNameTag() : string.Empty;

			char namecolor = enabledChatModifier ? player.GetNameColor() : ChatColors.ForTeam(player.Controller!.Team);
			char chatcolor = enabledChatModifier ? player.GetChatColor() : ChatColors.Default;

			string message = um.ReadString("param2");

			string formattedMessage = FormatMessage(player.Controller!, $" {dead}{team}{tag}{namecolor}{um.ReadString("param1")}{RemoveLeadingSpaceBeforeColorCode(Localizer.ForPlayer(player.Controller, "k4.tag.separator"))}{chatcolor}{message}");

			um.SetString("messagename", formattedMessage);

			_moduleServices?.InvokteZenithChatMessage(player.Controller!, message, formattedMessage);

			return HookResult.Changed;
		}

		private static string FormatMessage(CCSPlayerController player, string message)
		{
			return StringExtensions.ReplaceColorTags(message)
				.Replace("{team}", ChatColors.ForPlayer(player).ToString());
		}

		private string TeamLocalizer(CCSPlayerController player)
		{
			return player.Team switch
			{
				CsTeam.Spectator => Localizer.ForPlayer(player, "k4.tag.team.spectator"),
				CsTeam.Terrorist => Localizer.ForPlayer(player, "k4.tag.team.t"),
				CsTeam.CounterTerrorist => Localizer.ForPlayer(player, "k4.tag.team.ct"),
				_ => Localizer.ForPlayer(player, "k4.tag.team.unassigned"),
			};
		}

		[GameEventHandler]
		public HookResult OnPlayerActivate(EventPlayerActivate @event, GameEventInfo info)
		{
			CCSPlayerController? player = @event.Userid;
			if (player is null || !player.IsValid || player.IsHLTV || player.IsBot)
				return HookResult.Continue;

			_ = new Player(this, player);
			return HookResult.Continue;
		}

		[GameEventHandler(HookMode.Post)]
		public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
		{
			var player = Player.Find(@event.Userid);
			if (player == null)
				return HookResult.Continue;

			if (player.Loaded)
			{
				string joinFormat = GetCoreConfig<string>("Modular", "LeaveMessage");
				if (!string.IsNullOrEmpty(joinFormat))
					_moduleServices?.PrintForAll(StringExtensions.ReplaceColorTags(ReplacePlaceholders(player.Controller, joinFormat)), false);

				player.Dispose();
			}

			return HookResult.Continue;
		}

		[GameEventHandler]
		public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
		{
			if (GetCoreConfig<bool>("Database", "SaveOnRoundEnd"))
				_ = Task.Run(async () => await Player.SaveAllOnlinePlayerDataWithTransaction(this));
			return HookResult.Continue;
		}
	}
}
