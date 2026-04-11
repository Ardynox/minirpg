using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Session;

namespace MiniRPG;

internal sealed class MultiplayerHubCoordinator
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly MultiplayerFlowCoordinator _flow;
	private readonly Func<bool> _isMultiplayerSession;
	private readonly Func<bool> _isMapEditorActive;
	private readonly Func<string, string> _resolveActorDisplayName;
	private readonly Func<string?, string> _resolvePlayerDisplayName;
	private readonly Func<string, bool> _tryGetCurrentPlayerPrimaryActor;
	private readonly Func<string> _resolveCurrentRoomDisplayName;
	private readonly Func<string> _resolveCurrentRoomOwnerDisplayName;

	private IReadOnlyList<LobbyRoomSummary> _rooms = Array.Empty<LobbyRoomSummary>();
	private IReadOnlyList<MultiplayerHubTemplateOption> _templates = Array.Empty<MultiplayerHubTemplateOption>();
	private bool _roomPanelBusy;
	private bool _roomPanelStatusIsError;
	private string _roomPanelStatusMessage = string.Empty;

	public MultiplayerHubCoordinator(
		GameState state,
		GameSessionModule session,
		MultiplayerFlowCoordinator flow,
		Func<bool> isMultiplayerSession,
		Func<bool> isMapEditorActive,
		Func<string, string> resolveActorDisplayName,
		Func<string?, string> resolvePlayerDisplayName,
		Func<string, bool> tryGetCurrentPlayerPrimaryActor,
		Func<string> resolveCurrentRoomDisplayName,
		Func<string> resolveCurrentRoomOwnerDisplayName)
	{
		_state = state;
		_session = session;
		_flow = flow;
		_isMultiplayerSession = isMultiplayerSession;
		_isMapEditorActive = isMapEditorActive;
		_resolveActorDisplayName = resolveActorDisplayName;
		_resolvePlayerDisplayName = resolvePlayerDisplayName;
		_tryGetCurrentPlayerPrimaryActor = tryGetCurrentPlayerPrimaryActor;
		_resolveCurrentRoomDisplayName = resolveCurrentRoomDisplayName;
		_resolveCurrentRoomOwnerDisplayName = resolveCurrentRoomOwnerDisplayName;
	}

	public void SetTemplates(IReadOnlyList<MultiplayerHubTemplateOption> templates) => _templates = templates;
	public void SetRooms(IReadOnlyList<LobbyRoomSummary> rooms) => _rooms = rooms;
	public IReadOnlyList<LobbyRoomSummary> Rooms => _rooms;

	public void SetRoomPanelBusy(bool busy) => _roomPanelBusy = busy;
	public void SetRoomPanelStatus(string message, bool isError)
	{
		_roomPanelStatusMessage = message;
		_roomPanelStatusIsError = isError;
	}

	public void ResetRoomPanelStatus()
	{
		_roomPanelBusy = false;
		_roomPanelStatusIsError = false;
		_roomPanelStatusMessage = string.Empty;
	}

	public MultiplayerHubViewState BuildHubViewState(bool busy = false, string? statusMessage = null, bool statusIsError = false) =>
		_flow.BuildHubViewState(_templates, _rooms, busy, statusMessage, statusIsError);

	public MultiplayerRoomPanelViewState BuildRoomPanelViewState(
		MultiplayerSessionBackend? backend,
		bool roomPanelVisible,
		string currentHostRoomName,
		bool currentHostPublic)
	{
		var settings = _flow.CurrentSettings;
		if (!_isMultiplayerSession() || backend == null)
		{
			return new MultiplayerRoomPanelViewState
			{
				Mode = MultiplayerRoomPanelMode.LocalSession,
				Subtitle = LocalizationService.TOrFallback(
					"ui.multiplayer.room_panel.subtitle.local",
					"Host the current run as a multiplayer room without going back to the main menu."),
				Summary = LocalizationService.TOrFallback(
					"ui.multiplayer.room_panel.summary.local",
					"Current session: {session}\nMultiplayer profile: {displayName}\nHost mode: {hostMode}",
					("session", _session.DescribeCurrentSessionLabel()),
					("displayName", settings.DisplayName),
					("hostMode", DescribeMultiplayerHostMode(settings.PreferredHostMode))),
				StatusMessage = _roomPanelStatusMessage,
				StatusIsError = _roomPanelStatusIsError,
				Busy = _roomPanelBusy,
				CanHostCurrentSession = _session.GameStarted && !_isMapEditorActive(),
				HostRoomDisplayName = roomPanelVisible ? currentHostRoomName : _session.DescribeCurrentSessionLabel(),
				HostRoomIsPublic = roomPanelVisible && currentHostPublic,
			};
		}

		var currentPlayerSessionId = backend.PlayerSessionId ?? string.Empty;
		return new MultiplayerRoomPanelViewState
		{
			Mode = MultiplayerRoomPanelMode.MultiplayerRoom,
			Subtitle = LocalizationService.TOrFallback(
				"ui.multiplayer.room_panel.subtitle.room",
				"Inspect the live room roster and manage actor ownership from inside the run."),
			Summary = LocalizationService.TOrFallback(
				"ui.multiplayer.room_panel.summary.room",
				"Room: {room} [{code}]\nOwner: {owner}\nYou are controlling: {actor}",
				("room", _resolveCurrentRoomDisplayName()),
				("code", _state.Room.RoomCode),
				("owner", _resolveCurrentRoomOwnerDisplayName()),
				("actor", _resolveActorDisplayName(_state.PlayerId))),
			StatusMessage = _roomPanelStatusMessage,
			StatusIsError = _roomPanelStatusIsError,
			Busy = _roomPanelBusy,
			Players = BuildMultiplayerRoomPlayerOptions(currentPlayerSessionId),
			Actors = BuildMultiplayerRoomActorOptions(),
			CurrentPlayerSessionId = currentPlayerSessionId,
			IsCurrentPlayerRoomOwner = _state.Room.Players.TryGetValue(currentPlayerSessionId, out var currentPlayer) && currentPlayer.IsRoomOwner,
			CanReclaimPrimaryActor = _tryGetCurrentPlayerPrimaryActor(currentPlayerSessionId),
		};
	}

	private IReadOnlyList<MultiplayerRoomPlayerOption> BuildMultiplayerRoomPlayerOptions(string currentPlayerSessionId) =>
		_state.Room.Players.Values
			.OrderByDescending(static player => player.IsRoomOwner)
			.ThenByDescending(player => string.Equals(player.PlayerSessionId, currentPlayerSessionId, StringComparison.Ordinal))
			.ThenBy(player => string.IsNullOrWhiteSpace(player.DisplayName) ? player.PlayerSessionId : player.DisplayName, StringComparer.Ordinal)
			.Select(player => new MultiplayerRoomPlayerOption
			{
				PlayerSessionId = player.PlayerSessionId,
				DisplayLabel = BuildMultiplayerRoomPlayerLabel(player, currentPlayerSessionId),
			})
			.ToArray();

	private IReadOnlyList<MultiplayerRoomActorOption> BuildMultiplayerRoomActorOptions() =>
		_state.Actors.Values
			.Where(IsAssignableMultiplayerRoomActor)
			.OrderBy(GetAssignableMultiplayerActorPriority)
			.ThenBy(actor => _resolveActorDisplayName(actor.Id), StringComparer.Ordinal)
			.ThenBy(static actor => actor.Id, StringComparer.Ordinal)
			.Select(actor => new MultiplayerRoomActorOption
			{
				ActorId = actor.Id,
				DisplayLabel = BuildMultiplayerRoomActorLabel(actor),
			})
			.ToArray();

	private string BuildMultiplayerRoomPlayerLabel(RoomPlayerState player, string currentPlayerSessionId)
	{
		var flags = new List<string>();
		if (player.IsRoomOwner)
			flags.Add(LocalizationService.TOrFallback("ui.multiplayer.room_panel.flag.owner", "owner"));
		if (string.Equals(player.PlayerSessionId, currentPlayerSessionId, StringComparison.Ordinal))
			flags.Add(LocalizationService.TOrFallback("ui.multiplayer.room_panel.flag.you", "you"));
		flags.Add(player.Connected
			? LocalizationService.TOrFallback("ui.multiplayer.room_panel.flag.online", "online")
			: LocalizationService.TOrFallback("ui.multiplayer.room_panel.flag.offline", "offline"));

		var actorLabel = _resolveActorDisplayName(player.PrimaryActorId);
		return string.IsNullOrWhiteSpace(actorLabel)
			? $"{player.DisplayName} ({string.Join(", ", flags)})"
			: $"{player.DisplayName} ({string.Join(", ", flags)}) | {actorLabel}";
	}

	private string BuildMultiplayerRoomActorLabel(Actor actor)
	{
		var ownerPlayerId = _state.Room.ActorControlBindings.TryGetValue(actor.Id, out var binding)
			? binding.PrimaryOwnerPlayerId
			: _state.Room.Players.Values.FirstOrDefault(player =>
				string.Equals(player.PrimaryActorId, actor.Id, StringComparison.Ordinal))?.PlayerSessionId;
		var controllerPlayerId = RoomRuntimeModule.GetCurrentControllerPlayerId(_state, actor.Id);
		var ownerDisplayName = _resolvePlayerDisplayName(ownerPlayerId);
		var controllerDisplayName = _resolvePlayerDisplayName(controllerPlayerId);
		return LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.actor_row",
			"{actor} | owner: {owner} | control: {controller}",
			("actor", _resolveActorDisplayName(actor.Id)),
			("owner", ownerDisplayName),
			("controller", controllerDisplayName));
	}

	private string DescribeMultiplayerHostMode(MultiplayerHostMode hostMode) => hostMode == MultiplayerHostMode.Local
		? LocalizationService.T("ui.multiplayer.config.host_mode.local")
		: LocalizationService.T("ui.multiplayer.config.host_mode.remote");

	private bool IsAssignableMultiplayerRoomActor(Actor actor)
	{
		if (string.Equals(actor.Id, _state.PlayerId, StringComparison.Ordinal))
			return true;
		if (string.Equals(actor.Faction, Factions.Player, StringComparison.Ordinal))
			return true;
		return string.Equals(actor.Faction, Factions.Friendly, StringComparison.Ordinal);
	}

	private int GetAssignableMultiplayerActorPriority(Actor actor)
	{
		if (string.Equals(actor.Id, _state.PlayerId, StringComparison.Ordinal))
			return 0;
		if (string.Equals(actor.Faction, Factions.Player, StringComparison.Ordinal))
			return 1;
		if (string.Equals(actor.Faction, Factions.Friendly, StringComparison.Ordinal))
			return 2;
		return 3;
	}
}
