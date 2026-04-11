using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiniRPG.Core.World;
using MiniRPG.Module.Network;

namespace MiniRPG;

public partial class Main
{
	private async void HandleMenuMultiplayer()
	{
		await OpenMultiplayerHubAsync(refreshRooms: true);
	}

	private IReadOnlyList<MultiplayerHubTemplateOption> BuildMultiplayerHubTemplates() =>
		_session.ListScenarioEntries()
			.Select(static slot => new MultiplayerHubTemplateOption
			{
				Id = slot.Id,
				DisplayName = slot.DisplayName,
				Summary = slot.Summary,
			})
			.ToArray();

	private MultiplayerHubViewState BuildMultiplayerHubState(
		bool busy = false,
		string? statusMessage = null,
		bool statusIsError = false) =>
		_multiplayerHubCoordinator.BuildHubViewState(busy, statusMessage, statusIsError);

	private MultiplayerRoomPanelViewState BuildMultiplayerRoomPanelState() =>
		_multiplayerHubCoordinator.BuildRoomPanelViewState(
			_multiplayerRuntimeCoordinator.Backend,
			_multiplayerRoomPanel != null && _multiplayerRoomPanel.Visible,
			_multiplayerRoomPanel?.HostRoomDisplayName ?? _session.DescribeCurrentSessionLabel(),
			_multiplayerRoomPanel?.HostRoomIsPublic ?? false);

	private void RefreshMultiplayerRoomPanelState()
	{
		if (_multiplayerRoomPanel == null || !_multiplayerRoomPanel.Visible)
			return;

		_multiplayerRoomPanel.ApplyState(BuildMultiplayerRoomPanelState());
	}

	private void HandleMultiplayerRoomRequested()
	{
		if (!_session.GameStarted)
			return;

		_modalStateController.Prepare(RuntimeUiResetReason.OpenMultiplayerRoomPanel);
		_multiplayerHubCoordinator.ResetRoomPanelStatus();
		_multiplayerRoomPanel.Open(BuildMultiplayerRoomPanelState());
	}

	private async void HandleMultiplayerRoomHostCurrentSessionRequested(MultiplayerRoomHostRequest request)
	{
		if (!_session.GameStarted || IsMultiplayerSession)
			return;

		_multiplayerHubCoordinator.SetRoomPanelBusy(true);
		_multiplayerHubCoordinator.SetRoomPanelStatus(
			LocalizationService.TOrFallback("ui.multiplayer.room_panel.status.hosting", "Hosting current session..."),
			false);
		RefreshMultiplayerRoomPanelState();

		var snapshot = SaveModule.BuildSnapshot(_state);
		var result = await _multiplayerFlowCoordinator.CreateSnapshotRoomAsync(new MultiplayerSnapshotRoomCreateRequest
		{
			Settings = _multiplayerFlowCoordinator.CurrentSettings,
			RoomDisplayName = request.RoomDisplayName,
			IsPublic = request.IsPublic,
			Snapshot = snapshot,
			PrimaryActorId = snapshot.Payload.PlayerId,
		});

		_multiplayerHubCoordinator.SetRoomPanelBusy(false);
		if (!result.Success)
		{
			_multiplayerHubCoordinator.SetRoomPanelStatus(
				result.FailureReason ?? LocalizationService.TOrFallback(
					"ui.multiplayer.room_panel.status.host_failed",
					"Failed to host the current session."),
				true);
			RefreshMultiplayerRoomPanelState();
			return;
		}

		CloseMultiplayerRoomPanel();
		await ActivateMultiplayerSessionAsync(result);
	}

	private void HandleMultiplayerRoomAssignPrimaryActorRequested(string targetPlayerSessionId, string targetActorId)
	{
		if (_multiplayerRuntimeCoordinator?.Backend == null)
			return;

		_multiplayerHubCoordinator.SetRoomPanelStatus(
			LocalizationService.TOrFallback("ui.multiplayer.room_panel.status.assignment_sent", "Assignment request sent."),
			false);
		RefreshMultiplayerRoomPanelState();
		TrySubmitClientCommand(new AssignPrimaryActorClientCommand
		{
			TargetPlayerSessionId = targetPlayerSessionId,
			TargetActorId = targetActorId,
		});
	}

	private void HandleMultiplayerRoomKickPlayerRequested(string targetPlayerSessionId)
	{
		if (_multiplayerRuntimeCoordinator?.Backend == null)
			return;

		_multiplayerHubCoordinator.SetRoomPanelStatus(
			LocalizationService.TOrFallback("ui.multiplayer.room_panel.status.kick_sent", "Kick request sent."),
			false);
		RefreshMultiplayerRoomPanelState();
		TrySubmitClientCommand(new KickPlayerClientCommand
		{
			TargetPlayerSessionId = targetPlayerSessionId,
		});
	}

	private void HandleMultiplayerRoomReclaimPrimaryActorRequested()
	{
		var backend = _multiplayerRuntimeCoordinator?.Backend;
		if (backend == null)
			return;
		if (!TryGetCurrentPlayerPrimaryActorId(backend.PlayerSessionId ?? string.Empty, out var actorId))
			return;

		_multiplayerHubCoordinator.SetRoomPanelStatus(
			LocalizationService.TOrFallback(
				"ui.multiplayer.room_panel.status.reclaim_sent",
				"Reclaim request sent."),
			false);
		RefreshMultiplayerRoomPanelState();
		TrySubmitClientCommand(new ReclaimPrimaryActorClientCommand
		{
			ActorId = actorId,
		});
	}

	private void CloseMultiplayerRoomPanel()
	{
		if (_multiplayerRoomPanel.Visible)
			_multiplayerRoomPanel.Close();
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
			.ThenBy(actor => ResolveActorDisplayName(actor.Id), StringComparer.Ordinal)
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

		var actorLabel = ResolveActorDisplayName(player.PrimaryActorId);
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
		var ownerDisplayName = ResolvePlayerDisplayName(ownerPlayerId);
		var controllerDisplayName = ResolvePlayerDisplayName(controllerPlayerId);
		return LocalizationService.TOrFallback(
			"ui.multiplayer.room_panel.actor_row",
			"{actor} | owner: {owner} | control: {controller}",
			("actor", ResolveActorDisplayName(actor.Id)),
			("owner", ownerDisplayName),
			("controller", controllerDisplayName));
	}

	private string ResolveCurrentMultiplayerRoomDisplayName()
	{
		var ticket = _multiplayerFlowCoordinator.GetReconnectTicket();
		if (ticket != null
			&& string.Equals(ticket.RoomId, _state.Room.RoomId, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(ticket.RoomDisplayName))
		{
			return ticket.RoomDisplayName;
		}

		return string.IsNullOrWhiteSpace(_state.Room.RoomCode)
			? LocalizationService.T("ui.common.none")
			: _state.Room.RoomCode;
	}

	private string ResolveCurrentRoomOwnerDisplayName()
	{
		var owner = _state.Room.Players.Values.FirstOrDefault(static player => player.IsRoomOwner);
		return owner != null
			? owner.DisplayName
			: LocalizationService.T("ui.common.none");
	}

	private string ResolveActorDisplayName(string? actorId)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			return LocalizationService.T("ui.common.none");

		return _state.Actors.TryGetValue(actorId, out var actor)
			? string.IsNullOrWhiteSpace(actor.DisplayName) ? actor.Id : actor.DisplayName
			: actorId;
	}

	private string ResolvePlayerDisplayName(string? playerSessionId)
	{
		if (string.IsNullOrWhiteSpace(playerSessionId))
			return LocalizationService.T("ui.common.none");

		return _state.Room.Players.TryGetValue(playerSessionId, out var player)
			? player.DisplayName
			: playerSessionId;
	}

	private bool TryGetCurrentPlayerPrimaryActorId(string currentPlayerSessionId, out string actorId)
	{
		actorId = string.Empty;
		if (string.IsNullOrWhiteSpace(currentPlayerSessionId))
			return false;
		if (!_state.Room.Players.TryGetValue(currentPlayerSessionId, out var player))
			return false;
		if (string.IsNullOrWhiteSpace(player.PrimaryActorId))
			return false;
		if (string.Equals(
			RoomRuntimeModule.GetCurrentControllerPlayerId(_state, player.PrimaryActorId),
			currentPlayerSessionId,
			StringComparison.Ordinal))
		{
			return false;
		}

		actorId = player.PrimaryActorId;
		return true;
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

	private async Task OpenMultiplayerHubAsync(
		bool refreshRooms,
		string? statusMessage = null,
		bool statusIsError = false)
	{
		_multiplayerFlowCoordinator.OpenHub();
		_menu.ShowMultiplayerHub();
		_multiplayerHub.Open(BuildMultiplayerHubState(
			busy: refreshRooms,
			statusMessage: statusMessage,
			statusIsError: statusIsError));
		if (!refreshRooms)
			return;

		try
		{
			var rooms = await _multiplayerFlowCoordinator.RefreshRoomsAsync(
				_multiplayerFlowCoordinator.CurrentSettings);
			_multiplayerHubCoordinator.SetRooms(rooms);
			_multiplayerHub.ApplyState(BuildMultiplayerHubState(
				statusMessage: statusMessage,
				statusIsError: statusIsError));
		}
		catch (Exception ex)
		{
			_multiplayerHubCoordinator.SetRooms(Array.Empty<LobbyRoomSummary>());
			_multiplayerHub.ApplyState(BuildMultiplayerHubState(
				statusMessage: ex.Message,
				statusIsError: true));
		}
	}

	private void HandleMultiplayerHubBackRequested() =>
		_multiplayerFlowCoordinator.BackToMainMenu();

	private void HandleMultiplayerHubSaveSettingsRequested(MultiplayerSettings settings)
	{
		_multiplayerFlowCoordinator.SaveSettings(settings);
		_multiplayerHub.ApplyState(BuildMultiplayerHubState(
			statusMessage: LocalizationService.TOrFallback(
				"ui.multiplayer.status.settings_saved",
				"Multiplayer settings saved."),
			statusIsError: false));
	}

	private async void HandleMultiplayerHubRefreshRequested(MultiplayerSettings settings)
	{
		_multiplayerHub.ApplyState(BuildMultiplayerHubState(
			busy: true,
			statusMessage: LocalizationService.TOrFallback(
				"ui.multiplayer.status.refreshing_rooms",
				"Refreshing room list..."),
			statusIsError: false));
		try
		{
			var rooms = await _multiplayerFlowCoordinator.RefreshRoomsAsync(settings);
			_multiplayerHubCoordinator.SetRooms(rooms);
			_multiplayerHub.ApplyState(BuildMultiplayerHubState(
				statusMessage: LocalizationService.TOrFallback(
					"ui.multiplayer.status.rooms_ready",
					"Room list updated."),
				statusIsError: false));
		}
		catch (Exception ex)
		{
			_multiplayerHub.ApplyState(BuildMultiplayerHubState(
				statusMessage: ex.Message,
				statusIsError: true));
		}
	}

	private async void HandleMultiplayerHubJoinRoomRequested(MultiplayerHubJoinRequest request) =>
		await ExecuteMultiplayerHubConnectAsync(
			() => _multiplayerFlowCoordinator.JoinRoomAsync(request),
			LocalizationService.TOrFallback("ui.multiplayer.status.joining_room", "Joining room..."));

	private async void HandleMultiplayerHubJoinByCodeRequested(MultiplayerHubJoinCodeRequest request) =>
		await ExecuteMultiplayerHubConnectAsync(
			() => _multiplayerFlowCoordinator.JoinByCodeAsync(request),
			LocalizationService.TOrFallback("ui.multiplayer.status.joining_room", "Joining room..."));

	private async void HandleMultiplayerHubCreateRequested(MultiplayerHubCreateRequest request) =>
		await ExecuteMultiplayerHubConnectAsync(
			() => _multiplayerFlowCoordinator.CreateTemplateRoomAsync(request),
			LocalizationService.TOrFallback("ui.multiplayer.status.creating_room", "Creating room..."));

	private async void HandleMultiplayerHubReconnectRequested(MultiplayerSettings _) =>
		await ExecuteMultiplayerHubConnectAsync(
			() => _multiplayerFlowCoordinator.ReconnectAsync(),
			LocalizationService.TOrFallback("ui.multiplayer.status.reconnecting", "Reconnecting to last room..."));

	private async Task ExecuteMultiplayerHubConnectAsync(
		Func<Task<MultiplayerConnectResult>> connectAsync,
		string statusMessage)
	{
		_multiplayerHub.ApplyState(BuildMultiplayerHubState(
			busy: true,
			statusMessage: statusMessage,
			statusIsError: false));

		var result = await connectAsync();
		if (!result.Success)
		{
			await OpenMultiplayerHubAsync(
				refreshRooms: true,
				statusMessage: result.FailureReason,
				statusIsError: true);
			return;
		}

		await ActivateMultiplayerSessionAsync(result);
	}

	private async Task ActivateMultiplayerSessionAsync(MultiplayerConnectResult result)
	{
		_multiplayerHubCoordinator.ResetRoomPanelStatus();
		_mainAppFlowCoordinator.PrepareSessionTransition(clearLogs: true);
		await _multiplayerRuntimeCoordinator.ActivateMultiplayerSessionAsync(result);
		ProcessDirtyPanels();
	}

	private async void HandleMultiplayerReturnToMenu()
	{
		await CloseMultiplayerBackendAsync(suppressDisconnectHandling: true);
		CloseMultiplayerRoomPanel();
		_multiplayerFlowCoordinator.BackToMainMenu();
	}

	private async Task CloseMultiplayerBackendAsync(bool suppressDisconnectHandling)
	{
		if (_multiplayerRuntimeCoordinator == null)
			return;
		await _multiplayerRuntimeCoordinator.CloseMultiplayerBackendAsync(suppressDisconnectHandling);
	}
}
