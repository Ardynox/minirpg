using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Render;
using MiniRPG.Module.Session;

namespace MiniRPG;

internal sealed class MultiplayerRuntimeCoordinator
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly MultiplayerFlowCoordinator _flow;
	private readonly LogModule _log;
	private readonly Action<List<GameEvent>> _dispatch;
	private readonly Action _markUiDirty;
	private readonly Action _refreshVisiblePanels;
	private readonly Action _refreshPlayerCharacterVisual;
	private readonly Action _flushMap;
	private readonly Action<ActorMotionPresentationRequest> _presentPredictedActorMotion;
	private readonly Action _resetActorMotionState;
	private readonly Action<bool> _finalizeSessionPanels;
	private readonly Action _doEnterGame;
	private readonly Action<string, bool> _openHubWithStatus;
	private readonly Action _closeRoomPanel;
	private readonly Func<PredictionConfig> _predictionConfigProvider;
	private readonly Func<float> _predictionSmoothingSecondsProvider;

	private MultiplayerSessionBackend? _backend;
	private bool _suppressDisconnectHandling;
	private readonly ClientPredictionState _clientPrediction = new();
	private double _nextPredictionMetricsLogAt;

	public MultiplayerRuntimeCoordinator(
		GameState state,
		GameSessionModule session,
		MultiplayerFlowCoordinator flow,
		LogModule log,
		Action<List<GameEvent>> dispatch,
		Action markUiDirty,
		Action refreshVisiblePanels,
		Action refreshPlayerCharacterVisual,
		Action flushMap,
		Action<ActorMotionPresentationRequest> presentPredictedActorMotion,
		Action resetActorMotionState,
		Action<bool> finalizeSessionPanels,
		Action doEnterGame,
		Action<string, bool> openHubWithStatus,
		Action closeRoomPanel,
		Func<PredictionConfig> predictionConfigProvider,
		Func<float> predictionSmoothingSecondsProvider)
	{
		_state = state;
		_session = session;
		_flow = flow;
		_log = log;
		_dispatch = dispatch;
		_markUiDirty = markUiDirty;
		_refreshVisiblePanels = refreshVisiblePanels;
		_refreshPlayerCharacterVisual = refreshPlayerCharacterVisual;
		_flushMap = flushMap;
		_presentPredictedActorMotion = presentPredictedActorMotion;
		_resetActorMotionState = resetActorMotionState;
		_finalizeSessionPanels = finalizeSessionPanels;
		_doEnterGame = doEnterGame;
		_openHubWithStatus = openHubWithStatus;
		_closeRoomPanel = closeRoomPanel;
		_predictionConfigProvider = predictionConfigProvider;
		_predictionSmoothingSecondsProvider = predictionSmoothingSecondsProvider;
	}

	public MultiplayerSessionBackend? Backend => _backend;
	public bool HasBackend => _backend != null;
	public long TotalRollbackCount => _clientPrediction.TotalRollbackCount;
	public int PendingPredictionCount => _clientPrediction.PendingCount;
	public long ActivityVersion { get; private set; }

	public void Poll() => _backend?.Poll();

	public async Task ActivateMultiplayerSessionAsync(MultiplayerConnectResult result)
	{
		if (result.Backend == null || result.JoinTicket == null || result.InitialSnapshot == null)
			return;

		await CloseMultiplayerBackendAsync(suppressDisconnectHandling: true);
		_backend = result.Backend;
		_backend.SnapshotReceived += HandleMultiplayerSnapshotReceived;
		_backend.DeltaReceived += HandleMultiplayerDeltaReceived;
		_backend.Disconnected += HandleMultiplayerDisconnected;
		_backend.CommandRejected += HandleMultiplayerCommandRejected;
		_backend.RosterChanged += HandleMultiplayerRosterChanged;
		_backend.ReconnectClaimed += HandleMultiplayerReconnectClaimed;
		_backend.Trace += HandleMultiplayerTrace;
		ActivityVersion = 0;
		_clientPrediction.Configure(_predictionConfigProvider());

		var status = _session.ApplyMultiplayerRoomSnapshot(result.JoinTicket, result.InitialSnapshot.Snapshot);
		if (status != SaveLoadStatus.Success)
		{
			await CloseMultiplayerBackendAsync(suppressDisconnectHandling: true);
			_openHubWithStatus(
				LocalizationService.TOrFallback("ui.multiplayer.status.load_failed", "Failed to apply the multiplayer room snapshot."),
				true);
			return;
		}

		_flow.EnterMultiplayerGame();
		_finalizeSessionPanels(true);
		_log.Clear();
		_log.Add(LocalizationService.TOrFallback(
			"ui.multiplayer.status.connected",
			"Connected to room {room}.",
			("room", string.IsNullOrWhiteSpace(result.JoinTicket.RoomDisplayName)
				? result.JoinTicket.RoomCode
				: result.JoinTicket.RoomDisplayName)));
		_markUiDirty();
		_refreshVisiblePanels();
		_doEnterGame();
	}

	public bool TrySubmitClientCommand(ClientCommand command, bool isMultiplayerSession)
	{
		if (!isMultiplayerSession)
			return false;

		if (_backend == null)
		{
			_log.Add(LocalizationService.TOrFallback("ui.multiplayer.status.backend_missing", "Multiplayer backend is not available."));
			return true;
		}

		_ = SubmitMultiplayerCommandAsync(command);
		return true;
	}

	public bool TrySubmitPredictedMove(int dx, int dy, bool isMultiplayerSession, ActorMotionTimingTier timingTier)
	{
		if (!isMultiplayerSession || _backend == null)
			return false;

		var actorId = ActorModule.GetPlayer(_state)?.Id;
		if (string.IsNullOrWhiteSpace(actorId))
			return false;

		var command = new MoveClientCommand
		{
			ActorId = actorId,
			Dx = dx,
			Dy = dy,
			ClientTick = _clientPrediction.NextClientTick,
		};
		ApplyPredictedMove(actorId, command.RequestId, dx, dy, timingTier);
		_ = SubmitMultiplayerCommandAsync(command);
		return true;
	}

	public void EmitPredictionMetricsIfDue()
	{
		var nowSec = Godot.Time.GetTicksMsec() / 1000.0;
		if (nowSec < _nextPredictionMetricsLogAt)
			return;

		_nextPredictionMetricsLogAt = nowSec + 5.0;
		var breakdown = _clientPrediction.GetRollbackBreakdown();
		var movementRollback = breakdown.TryGetValue("movement", out var count) ? count : 0;
		_log.Add($"[Prediction] metrics rollbackTotal={_clientPrediction.TotalRollbackCount} movement={movementRollback} lastDistance={_clientPrediction.LastRollbackDistanceManhattan}");
	}

	public async Task CloseMultiplayerBackendAsync(bool suppressDisconnectHandling)
	{
		if (_backend == null)
			return;

		var backend = _backend;
		_backend = null;
		_suppressDisconnectHandling = suppressDisconnectHandling;
		backend.SnapshotReceived -= HandleMultiplayerSnapshotReceived;
		backend.DeltaReceived -= HandleMultiplayerDeltaReceived;
		backend.Disconnected -= HandleMultiplayerDisconnected;
		backend.CommandRejected -= HandleMultiplayerCommandRejected;
		backend.RosterChanged -= HandleMultiplayerRosterChanged;
		backend.ReconnectClaimed -= HandleMultiplayerReconnectClaimed;
		backend.Trace -= HandleMultiplayerTrace;
		try
		{
			await backend.DisposeAsync();
		}
		finally
		{
			ActivityVersion = 0;
			_suppressDisconnectHandling = false;
		}
	}

	private async Task SubmitMultiplayerCommandAsync(ClientCommand command)
	{
		if (_backend == null)
			return;

		var result = await _backend.SubmitCommandAsync(command);
		if (!result.Accepted && !string.IsNullOrWhiteSpace(result.FailureReason))
			_log.Add(result.FailureReason);
	}

	private void ApplyPredictedMove(string actorId, string requestId, int dx, int dy, ActorMotionTimingTier timingTier)
	{
		var sourceX = _state.PlayerX;
		var sourceY = _state.PlayerY;
		var sourceZ = _state.PlayerZ;
		var predictedX = _state.PlayerX + dx;
		var predictedY = _state.PlayerY + dy;
		var predictedZ = _state.PlayerZ;
		_clientPrediction.CreateMovePrediction(requestId, dx, dy, predictedX, predictedY, predictedZ);
		_state.PlayerX = predictedX;
		_state.PlayerY = predictedY;
		_presentPredictedActorMotion(new ActorMotionPresentationRequest(
			actorId,
			sourceX,
			sourceY,
			sourceZ,
			predictedX,
			predictedY,
			predictedZ,
			timingTier,
			Blocking: true));
		_markUiDirty();
		_flushMap();
	}

	private void ApplyPredictionReconciliation(string? authoritativeRequestId)
	{
		var decision = _clientPrediction.Reconcile(authoritativeRequestId, _state.PlayerX, _state.PlayerY, _state.PlayerZ);
		if (!decision.HasPending)
			return;

		if (!decision.RollbackNeeded)
		{
			_state.PlayerX = decision.ReplayedX;
			_state.PlayerY = decision.ReplayedY;
			_state.PlayerZ = decision.ReplayedZ;
			return;
		}

		var correctionDx = decision.AuthoritativeX - decision.ReplayedX;
		var correctionDy = decision.AuthoritativeY - decision.ReplayedY;
		_state.PlayerX = decision.ReplayedX;
		_state.PlayerY = decision.ReplayedY;
		_state.PlayerZ = decision.ReplayedZ;
		_resetActorMotionState();
		// correction smoothing handled by render module owner.
		_log.Add($"[Prediction] rollback req={authoritativeRequestId ?? ""} distance={decision.ManhattanDistance} total={_clientPrediction.TotalRollbackCount} dx={correctionDx} dy={correctionDy}");
	}

	private void HandleMultiplayerSnapshotReceived(GameSessionSnapshotEnvelope envelope)
	{
		if (_backend == null || envelope.Room == null)
			return;

		var playerSessionId = envelope.PlayerSessionId ?? _backend.PlayerSessionId;
		if (string.IsNullOrWhiteSpace(playerSessionId))
			return;

		_session.ApplyMultiplayerRoomSnapshot(
			envelope.Room,
			playerSessionId,
			_flow.CurrentSettings.DisplayName,
			primaryActorId: null,
			envelope.Snapshot);
		ActivityVersion++;
		_resetActorMotionState();
		ApplyPredictionReconciliation(envelope.RequestId);
		_refreshVisiblePanels();
		_refreshPlayerCharacterVisual();
		_flushMap();
		_log.Add($"[MP] inbound snapshot requestId={envelope.RequestId ?? ""} serverTick={envelope.ServerTick} seq={envelope.SnapshotSequence}");
		_markUiDirty();
	}

	private void HandleMultiplayerDeltaReceived(GameSessionDeltaEnvelope envelope)
	{
		if (envelope.Events.Count == 0)
			return;
		ActivityVersion++;
		_dispatch([.. envelope.Events]);
		_log.Add($"[MP] inbound events requestId={envelope.RequestId ?? ""} serverTick={envelope.ServerTick} seq={envelope.SnapshotSequence} count={envelope.Events.Count}");
		_markUiDirty();
	}

	private void HandleMultiplayerTrace(string trace)
	{
		if (!string.IsNullOrWhiteSpace(trace))
			_log.Add(trace);
	}

	private void HandleMultiplayerRosterChanged(RoomRuntimeState room)
	{
		ActivityVersion++;
		_state.Room = room.Clone();
		RoomRuntimeModule.SyncLegacyPlayerAlias(_state);
		_refreshVisiblePanels();
		_refreshPlayerCharacterVisual();
		_markUiDirty();
	}

	private void HandleMultiplayerCommandRejected(string reason)
	{
		ActivityVersion++;
		if (!string.IsNullOrWhiteSpace(reason))
			_log.Add(reason);
	}

	private void HandleMultiplayerReconnectClaimed(string playerSessionId, string actorId)
	{
		if (_backend == null
			|| !string.Equals(_backend.PlayerSessionId, playerSessionId, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(actorId)
			|| !_state.Actors.TryGetValue(actorId, out var actor))
		{
			return;
		}

		_state.PlayerId = actor.Id;
		_state.PlayerX = actor.X;
		_state.PlayerY = actor.Y;
		_state.PlayerZ = actor.Z;
		ActivityVersion++;
		_clientPrediction.Configure(_predictionConfigProvider());
		_resetActorMotionState();
		RoomRuntimeModule.SyncLegacyPlayerAlias(_state);
		_refreshPlayerCharacterVisual();
		_markUiDirty();
		_flushMap();
	}

	private async void HandleMultiplayerDisconnected(string reason)
	{
		if (_suppressDisconnectHandling)
			return;

		await CloseMultiplayerBackendAsync(suppressDisconnectHandling: true);
		_closeRoomPanel();
		_flow.MarkDisconnectedRecoverable(reason);
		_openHubWithStatus(reason, true);
	}
}
