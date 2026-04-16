using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG;

public partial class Main
{
	private void ProcessTimelineAutoAdvance(double delta)
	{
		if (PlayerDead || ActorModule.GetPlayer(_state) == null || (!_watchModeEnabled && !_timelineAutoAdvancePending))
			return;

		var autoAdvanceIntervalSeconds = (!_watchModeEnabled && _fastTurnModeEnabled) ? 0.0 : 0.2;
		_watchTimer += delta;
		if (_watchTimer < autoAdvanceIntervalSeconds)
			return;
		if (ShouldPauseTimelineAutoAdvanceForBlockingMotion(
			_watchModeEnabled,
			_fastTurnModeEnabled,
			_mapRender?.HasBlockingActorMotion == true))
			return;

		_watchTimer = 0;
		WatchModeTick();
	}

	internal static bool ShouldPauseTimelineAutoAdvanceForBlockingMotion(
		bool watchModeEnabled,
		bool fastTurnModeEnabled,
		bool hasBlockingActorMotion)
	{
		if (!hasBlockingActorMotion)
			return false;

		if (watchModeEnabled || !fastTurnModeEnabled)
			return true;

		// Render-side blocking is already filtered down to player / nearby / threat
		// motions, so fast-turn only waits for those key actions to stay readable.
		return true;
	}

	private void ProcessPlayerRestMode()
	{
		if (!_playerRestModeActive || PlayerDead || _watchModeEnabled)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			_playerRestModeActive = false;
			return;
		}

		ActorDerivedStateUpdater.SyncPlayerUiState(_state);
		var restValue = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Rest);
		if (restValue >= 85f)
		{
			_playerRestModeActive = false;
			return;
		}

		if (ThreatDetection.HasNearbyThreat(_state, player))
		{
			_playerRestModeActive = false;
			return;
		}

		if (TimelineTurnManager.IsPlayerTurn(_state))
			SubmitPlayerAction(TimelinePlayerAction.Rest());
	}

	private void TogglePlayerRestMode()
	{
		if (_playerRestModeActive)
		{
			_playerRestModeActive = false;
			return;
		}

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;
		if (!NeedActionModule.HasBedroll(player))
		{
			_log.Add(LocalizationService.T("log.rest.needs_bedroll"));
			return;
		}
		if (ThreatDetection.HasNearbyThreat(_state, player))
		{
			_log.Add(LocalizationService.T("log.rest.unsafe"));
			return;
		}

		_playerRestModeActive = true;
		Dispatch(
		[
			new GameEvent("rest_started")
			{
				InitiatorId = player.Id,
				TargetId = player.Id,
				TargetActorName = IdentificationModule.GetActorDisplayName(_state, player),
			},
		]);
	}

	private bool IsTimelineInputLocked() =>
		!PlayerDead && (_timelineAutoAdvancePending || _watchModeEnabled || _mapRender?.HasBlockingActorMotion == true);

	private void SubmitPlayerAction(TimelinePlayerAction action)
	{
		if (!_autoNav.IsExecutingStep)
			InterruptAutoNavigationForManualInput();

		if (TrySubmitMultiplayerTimelineAction(action))
			return;

		_ = SubmitPlayerActionWithResult(action);
	}

	private TimelineStepResult SubmitPlayerActionWithResult(TimelinePlayerAction action)
	{
		if (!_autoNav.IsExecutingStep)
			InterruptAutoNavigationForManualInput();

		if (TrySubmitMultiplayerTimelineAction(action))
			return new TimelineStepResult();

		var result = TimelineTurnGateway.SubmitPlayerAction(_state, action);
		ApplyTimelineStep(result);
		return result;
	}

	private bool TrySubmitClientCommand(ClientCommand command)
	{
		if (_multiplayerRuntimeCoordinator != null)
			return _multiplayerRuntimeCoordinator.TrySubmitClientCommand(command, IsMultiplayerSession);
		return false;
	}

	/// <summary>
	/// Unified command submission: multiplayer routes via backend transport,
	/// single-player routes through <see cref="IGameSessionBackend.SubmitCommandAsync"/>.
	/// </summary>
	private void SubmitClientCommand(ClientCommand command)
	{
		if (TrySubmitClientCommand(command))
			return;

		var result = _sessionBackend.SubmitCommandAsync(command).GetAwaiter().GetResult();
		if (!result.Accepted && !string.IsNullOrEmpty(result.FailureReason))
			_log.Add(result.FailureReason);
	}

	private bool TrySubmitMultiplayerTimelineAction(TimelinePlayerAction action)
	{
		if (!IsMultiplayerSession)
			return false;

		var actorId = ActorModule.GetPlayer(_state)?.Id;
		if (string.IsNullOrWhiteSpace(actorId))
		{
			_log.Add(LocalizationService.TOrFallback(
				"ui.multiplayer.status.no_actor",
				"No controlled actor is available for multiplayer input."));
			return true;
		}

		if (!TimelineActionClientCommandFactory.TryBuild(action, actorId, out var command))
		{
			_log.Add(LocalizationService.TOrFallback(
				"ui.multiplayer.status.unsupported_action",
				"This action is not available in multiplayer yet."));
			return true;
		}

		return TrySubmitClientCommand(command);
	}

	private void AdvanceTimelineAutoStep()
	{
		var result = TimelineTurnGateway.AdvanceAuto(_state, _watchModeEnabled, _fastTurnModeEnabled);
		ApplyTimelineStep(result);
	}

	private void ApplyTimelineStep(TimelineStepResult result)
	{
		if (result.Events.Count > 0)
			Dispatch(result.Events);

		TickSimulationSystems();
		FinalizeTimelineStepUi();
		SyncTimelineAutoAdvanceState(emitStatusLog: true);
	}

	// Drive per-turn decay for the second-layer simulation modules.
	// Called once per timeline step after the event batch has been
	// dispatched; keeps relationships / memories / rumors from piling
	// up forever in long sessions.
	private void TickSimulationSystems()
	{
		_relationships?.Tick();
		_actorMemories?.Tick();
		_rumorBus?.Tick();
	}

	private void SyncTimelineAutoAdvanceState(bool emitStatusLog = false)
	{
		var includeEntries = _turnPanelModule.PanelNode.Visible;
		var snapshot = TimelineTurnManager.CreateDebugSnapshot(
			_state,
			PlayerDead,
			_watchModeEnabled,
			includeEntries: includeEntries);
		_timelineAutoAdvancePending = snapshot.HasPendingAutoAdvance;
		if (!_timelineAutoAdvancePending)
			_watchTimer = 0;

		if (includeEntries)
			_turnPanelModule.Refresh(snapshot, _mapRender?.IsIsometricMode ?? true);
		else
			_turnPanelModule.Dirty = true;

		if (emitStatusLog)
			UpdateTimelineStatusLog(snapshot);
		else
			PrimeTimelineStatusLog(snapshot);
	}

	private void FinalizeTimelineStepUi()
	{
		var anchors = RoomRuntimeModule.GetWorldAnchors(_state).Select(static anchor => anchor.Position).ToArray();
		_state.World?.Chunks.UpdateLoadedChunks(anchors, _state.Turn);
		FlushMap();
		CheckChestRange();
	}

	/// <summary>
	/// </summary>
	public void HandlePlayerDeath(string reason)
	{
		if (PlayerDead) return;
		PlayerDead = true;
		CancelLayoutEditMode();

		SetWatchModeEnabled(false, emitLog: false);

		_inputModule.CancelSelection();
		ClearArmedSkill(restoreFocus: false);
		EndSkillTargetCursorMode(restoreFocus: false);
		ClearPlayerTargeting();
		CancelAutoNavigation(AutoNavigationStopReason.SessionReset, emitLog: false);
		_playerRestModeActive = false;
		ResetThreatHud();
		_timelineAutoAdvancePending = false;
		_watchTimer = 0;
		ResetTimelineStatusLog();

		_log.Add("");
		_log.Add(reason == "incapacitated"
			? LocalizationService.T("death.incapacitated")
			: LocalizationService.T("death.killed"));
		_log.Add(LocalizationService.T("death.turn", ("turn", _state.Turn)));
		_log.Add(LocalizationService.T("death.floor", ("floor", _state.PlayerZ)));
		_log.Add(LocalizationService.T("death.kills", ("kills", _state.KillCount)));
		var player2 = ActorModule.GetPlayer(_state);
		if (player2 != null)
			_log.Add(LocalizationService.T("death.gold", ("gold", player2.Gold)));
		_log.Add(LocalizationService.T("death.separator"));
		_log.Add(LocalizationService.T("death.back_to_menu"));
	}

	private void ToggleWatchMode()
	{
		SetWatchModeEnabled(!_watchModeEnabled);
	}

	private void SetWatchModeEnabled(bool enabled, bool emitLog = true)
	{
		_watchModeEnabled = enabled;
		_watchTimer = 0;

		var player = ActorModule.GetPlayer(_state);
		if (player != null && !IsMultiplayerSession)
			player.BrainId = enabled ? "simple" : null;

		SyncSettingsUiState();
		SyncTimelineAutoAdvanceState(emitStatusLog: emitLog);
		if (!emitLog)
			return;

		_log.Add(enabled
			? LocalizationService.T("log.watch_mode.on")
			: LocalizationService.T("log.watch_mode.off"));
	}

	private void WatchModeTick()
	{
		AdvanceTimelineAutoStep();
	}

	private void PrimeTimelineStatusLog(TimelineDebugSnapshot snapshot)
	{
		_timelineStatusLogPrimed = true;
		_lastTimelineLockReason = snapshot.InputLockedReason;
	}

	private void ResetTimelineStatusLog()
	{
		_timelineStatusLogPrimed = false;
		_lastTimelineLockReason = TimelineInputLockReason.None;
	}

	private void UpdateTimelineStatusLog(TimelineDebugSnapshot snapshot)
	{
		if (!_timelineStatusLogPrimed)
		{
			PrimeTimelineStatusLog(snapshot);
			return;
		}

		var nextReason = snapshot.InputLockedReason;
		if (nextReason == _lastTimelineLockReason)
			return;

		if (nextReason == TimelineInputLockReason.None)
		{
			if (_lastTimelineLockReason is TimelineInputLockReason.OtherActorsActing or TimelineInputLockReason.WatchMode)
				_log.Add(LocalizationService.T("log.timeline.player_turn"));
			_lastTimelineLockReason = nextReason;
			return;
		}

		var logKey = nextReason switch
		{
			TimelineInputLockReason.OtherActorsActing => "log.timeline.locked.auto",
			TimelineInputLockReason.WatchMode => "log.timeline.locked.watch",
			_ => null,
		};
		if (logKey != null)
			_log.Add(LocalizationService.T(logKey));

		_lastTimelineLockReason = nextReason;
	}
}
