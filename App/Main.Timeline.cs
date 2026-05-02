using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Module.Render;
using MiniRPG.Module.Session;

namespace MiniRPG;

public partial class Main
{
	private const int AmbientNpcAutoAdvanceBatchLimit = 64;

	private void ProcessTimelineAutoAdvance(double delta)
	{
		if (PlayerDead || ActorModule.GetPlayer(_state) == null || (!_watchModeEnabled && !_timelineAutoAdvancePending))
			return;

		var autoAdvanceIntervalSeconds = ResolveTimelineAutoAdvanceIntervalSeconds(
			_watchModeEnabled,
			_fastTurnModeEnabled,
			_autoNav.IsExecutingStep,
			ResolveCurrentPlayerMotionTimingTier());
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

	internal static double ResolveTimelineAutoAdvanceIntervalSeconds(
		bool watchModeEnabled,
		bool fastTurnModeEnabled,
		bool autoNavigationExecutingStep,
		ActorMotionTimingTier autoNavigationTimingTier)
	{
		if (!watchModeEnabled && fastTurnModeEnabled)
			return 0.0;
		if (watchModeEnabled || !autoNavigationExecutingStep)
			return 0.2;

		var motionDuration = ActorMotionTiming.ResolveDurationSeconds(autoNavigationTimingTier);
		return Math.Max(0.04, motionDuration * 0.75f);
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
		var result = BuildTimelineAutoAdvanceResult();
		ApplyTimelineStep(result);
	}

	private TimelineStepResult BuildTimelineAutoAdvanceResult()
	{
		if (!ShouldBatchAmbientNpcAutoAdvance())
		{
			var singleStep = TimelineTurnGateway.AdvanceAuto(_state, _watchModeEnabled, _fastTurnModeEnabled);
			LogTimelineAutoAdvanceResult(
				ResolveTimelineAutoAdvanceMode(batchAmbientNpcAutoAdvance: false),
				CountMeaningfulAutoAdvanceSteps(singleStep),
				CollectActingActorLabels(singleStep),
				ResolveTimelineAutoAdvanceStopReason(
					playerTurnReady: singleStep.PlayerTurnReady,
					hasPendingAutoStep: singleStep.HasPendingAutoStep,
					blockingMotionSummary: TryResolveBlockingMotionSummary(singleStep.Events, out var blockingMotionSummary)
						? blockingMotionSummary
						: null,
					reachedBatchLimit: false),
				singleStep.Events.Count);
			return singleStep;
		}

		var aggregate = new TimelineStepResult();
		var actorLabels = new List<string>();
		string stopReason = "batch-limit";
		for (var stepIndex = 0; stepIndex < AmbientNpcAutoAdvanceBatchLimit; stepIndex++)
		{
			var step = TimelineTurnGateway.AdvanceAuto(
				_state,
				watchModeEnabled: false,
				fastTurnModeEnabled: false);
			MergeTimelineStepResult(aggregate, step);
			if (!string.IsNullOrWhiteSpace(step.ActingActorId))
				actorLabels.Add(ResolveTimelineAutoAdvanceActorLabel(step.ActingActorId));

			if (step.PlayerTurnReady
				|| !step.HasPendingAutoStep
				|| ShouldPauseAmbientNpcAutoAdvanceBatch(step.Events))
			{
				stopReason = ResolveTimelineAutoAdvanceStopReason(
					playerTurnReady: step.PlayerTurnReady,
					hasPendingAutoStep: step.HasPendingAutoStep,
					blockingMotionSummary: TryResolveBlockingMotionSummary(step.Events, out var blockingMotionSummary)
						? blockingMotionSummary
						: null,
					reachedBatchLimit: false);
				break;
			}
		}

		LogTimelineAutoAdvanceResult(
			ResolveTimelineAutoAdvanceMode(batchAmbientNpcAutoAdvance: true),
			actorLabels.Count,
			actorLabels,
			stopReason,
			aggregate.Events.Count);
		return aggregate;
	}

	private bool ShouldBatchAmbientNpcAutoAdvance() =>
		ShouldBatchAmbientNpcAutoAdvance(
			IsMultiplayerSession,
			_watchModeEnabled,
			_fastTurnModeEnabled,
			_mapRender?.HasBlockingActorMotion == true);

	internal static bool ShouldBatchAmbientNpcAutoAdvance(
		bool isMultiplayerSession,
		bool watchModeEnabled,
		bool fastTurnModeEnabled,
		bool hasBlockingActorMotion) =>
		!isMultiplayerSession
		&& !watchModeEnabled
		&& !fastTurnModeEnabled
		&& !hasBlockingActorMotion;

	private bool ShouldPauseAmbientNpcAutoAdvanceBatch(IReadOnlyList<GameEvent> events)
	{
		for (var index = 0; index < events.Count; index++)
		{
			if (!TryBuildActorMotionPresentationRequest(events[index], out var request))
				continue;
			if (!ActorMotionTracker.IsStandardStep(request))
				continue;
			if (request.Blocking)
				return true;
		}

		return false;
	}

	private static void MergeTimelineStepResult(TimelineStepResult aggregate, TimelineStepResult step)
	{
		if (step.Events.Count > 0)
			aggregate.Events.AddRange(step.Events);

		aggregate.ActingActorId = step.ActingActorId;
		aggregate.ActionConsumed |= step.ActionConsumed;
		aggregate.PlayerTurnReady = step.PlayerTurnReady;
		aggregate.HasPendingAutoStep = step.HasPendingAutoStep;
	}

	internal static string ResolveTimelineAutoAdvanceStopReason(
		bool playerTurnReady,
		bool hasPendingAutoStep,
		string? blockingMotionSummary,
		bool reachedBatchLimit)
	{
		if (!string.IsNullOrWhiteSpace(blockingMotionSummary))
			return $"blocking-motion:{blockingMotionSummary}";
		if (playerTurnReady)
			return "player-turn";
		if (!hasPendingAutoStep)
			return "no-pending-auto";
		if (reachedBatchLimit)
			return "batch-limit";
		return "step-complete";
	}

	private string ResolveTimelineAutoAdvanceMode(bool batchAmbientNpcAutoAdvance)
	{
		if (_watchModeEnabled)
			return "watch";
		if (_fastTurnModeEnabled)
			return "fast-turn";
		return batchAmbientNpcAutoAdvance ? "ambient-batch" : "single-step";
	}

	private static int CountMeaningfulAutoAdvanceSteps(TimelineStepResult result) =>
		string.IsNullOrWhiteSpace(result.ActingActorId) ? 0 : 1;

	private List<string> CollectActingActorLabels(TimelineStepResult result)
	{
		var labels = new List<string>();
		if (!string.IsNullOrWhiteSpace(result.ActingActorId))
			labels.Add(ResolveTimelineAutoAdvanceActorLabel(result.ActingActorId));
		return labels;
	}

	private bool TryResolveBlockingMotionSummary(
		IReadOnlyList<GameEvent> events,
		out string summary)
	{
		var activeActor = PartyModule.GetActiveActor(_state);
		for (var index = 0; index < events.Count; index++)
		{
			var gameEvent = events[index];
			if (!TryBuildActorMotionPresentationRequest(gameEvent, out var request))
				continue;
			if (!ActorMotionTracker.IsStandardStep(request) || !request.Blocking)
				continue;

			var actorLabel = ResolveTimelineAutoAdvanceActorLabel(gameEvent.InitiatorId);
			var reason = ResolveActorMotionPresentationReason(_state, gameEvent, activeActor, IsMultiplayerSession);
			summary = $"{actorLabel}:{reason}";
			return true;
		}

		summary = string.Empty;
		return false;
	}

	private void LogTimelineAutoAdvanceResult(
		string mode,
		int stepCount,
		IReadOnlyList<string> actorLabels,
		string stopReason,
		int eventCount)
	{
		var actors = actorLabels.Count == 0 ? "-" : string.Join(">", actorLabels);
		AddTimelineMotionDiagnosticLog(
			$"[timeline] turn={_state.Turn} mode={mode} steps={stepCount} actors={actors} " +
			$"stop={stopReason} events={eventCount}");
	}

	private string ResolveTimelineAutoAdvanceActorLabel(string? actorId)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			return "?";

		var actor = ActorModule.GetById(_state, actorId);
		return !string.IsNullOrWhiteSpace(actor?.DisplayName)
			? actor.DisplayName
			: actorId;
	}

	private void ApplyTimelineStep(TimelineStepResult result)
	{
		if (result.Events.Count > 0)
		{
			// Consequence handlers (relationships, memory, rumor, etc.) must
			// observe AI-produced events before the presentation router runs;
			// state changes have to land before UI/log/FX render them so a
			// later log line can already reflect "victim now fears attacker".
			_consequenceRouter?.DispatchConsequences(_state, result.Events);
			// 队员死亡 → 切焦点 / 全灭判定。放在 Consequence 之后，让关系/记忆先看到原始
			// actor_killed；Handler 派出的 party_member_lost / active_actor_switched / party_wiped
			// 走的是 UI / 终局通道，不属于关系层关心的范畴。
			ProcessActiveActorDeaths(result.Events);
			Dispatch(result.Events);
		}

		TickSimulationSystems();
		FinalizeTimelineStepUi();
		SyncTimelineAutoAdvanceState(emitStatusLog: true);
	}

	/// <summary>
	/// 拦截本 step 内的"队员死亡"事件，把焦点切换 / 全队灭决策派给 <see cref="ActiveActorDeathHandler"/>。
	/// Handler 产出的 party_member_lost / active_actor_switched / party_wiped 事件追加到本 step 的事件流，
	/// 由后续 <c>Dispatch</c> 一起翻译给日志 / Router；这样切焦点和"回主菜单"走的是同一个事件通道。
	/// </summary>
	private void ProcessActiveActorDeaths(List<GameEvent> events)
	{
		// 先快照：Handler 会向 events 追加新事件，避免在 foreach 时 mutate 同一个集合。
		var snapshot = events.ToArray();
		List<GameEvent>? produced = null;
		for (var i = 0; i < snapshot.Length; i++)
		{
			var ev = snapshot[i];
			if (!IsPartyDeathTrigger(ev))
				continue;
			if (string.IsNullOrWhiteSpace(ev.TargetId))
				continue;
			if (!PartyModule.IsPartyMember(_state, ev.TargetId))
				continue;

			produced ??= [];
			ActiveActorDeathHandler.Handle(_state, ev.TargetId, produced);
		}

		if (produced != null && produced.Count > 0)
			events.AddRange(produced);
	}

	private static bool IsPartyDeathTrigger(GameEvent e) =>
		e.Type is "actor_killed" or "actor_incapacitated"
			or "death_blood_loss" or "death_infection";

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
	/// 进入"全队灭"终局流程：暂停玩家输入、清掉战斗 / 寻路态、写一段死亡日志，
	/// 让玩家点回主菜单。
	/// </summary>
	/// <remarks>
	/// 设计意图（见 <c>Docs/产品愿景.md</c> 死亡与复活）：
	/// 焦点角色死亡 ≠ 终局——只要 Party 还有活人就应该自动切焦点继续玩。
	/// 真正进入终局的入口是 <see cref="ProcessActiveActorDeaths"/> →
	/// <see cref="ActiveActorDeathHandler"/> 返回 AllMembersDead → 派 <c>party_wiped</c> 事件 →
	/// <see cref="GameEventPresentationRouter"/> 调本方法（reason="party_wiped"）。
	/// 这里再做一道防御：万一旧路径（某些 Router 分支仍按 PlayerId 匹配）误触，
	/// 队伍如果还有活人就拒绝进终局，让玩家继续操作下一焦点。
	/// </remarks>
	public void HandlePlayerDeath(string reason)
	{
		if (PlayerDead) return;
		if (_state.Party.MemberIds.Count > 0 && PartyModule.AnyMemberAlive(_state))
			return;

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

		// Heading（"你死了" / "你失去了意识" / "全队覆灭"）和死因摘要由 Presenter 统一兜底，
		// 避免 Main 与 Presenter 双写 heading、且 party_wiped 分支落回 "death.killed" fallback（Δ-8 修复）。
		// 全队灭显示焦点角色身上的金币（焦点 ActiveId 在 Handler 里被清空时回退到 PlayerId 队长，符合"队长身家"的玩家直觉）。
		var fallenActive = ActiveActorAccess.GetActive(_state);
		var fallenActorId = fallenActive?.Id ?? _state.PlayerId;

		_log.Add("");
		_playerDeathPresenter?.Present(reason, fallenActorId, _state.Turn);
		_log.Add(LocalizationService.T("death.turn", ("turn", _state.Turn)));
		_log.Add(LocalizationService.T("death.floor", ("floor", _state.PlayerZ)));
		_log.Add(LocalizationService.T("death.kills", ("kills", _state.KillCount)));
		if (fallenActive != null)
			_log.Add(LocalizationService.T("death.gold", ("gold", fallenActive.Gold)));
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
