using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiniRPG.Core.World;
using MiniRPG.Module.Network;

namespace MiniRPG;

public partial class Main
{
	private void DoMove(int dx, int dy) => _gameplayCommandCoordinator.DoMove(dx, dy, TrySubmitPredictedMove);

	private bool TrySubmitPredictedMove(int dx, int dy) =>
		_multiplayerRuntimeCoordinator != null
		&& _multiplayerRuntimeCoordinator.TrySubmitPredictedMove(dx, dy, IsMultiplayerSession);

	private void EmitPredictionMetricsIfDue() => _multiplayerRuntimeCoordinator?.EmitPredictionMetricsIfDue();

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
