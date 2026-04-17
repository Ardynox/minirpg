using System;
using System.IO;
using Godot;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;

namespace MiniRPG;

public partial class Main
{
	private const int ReadableNpcMotionDistance = 4;
	private const int ThreatNpcMotionDistance = 6;
	private const float PlayerFastBlockingGateSeconds = 0.04f;
	private const float PlayerSlowBlockingGateSeconds = 0.07f;
	private const float NearbyReadableBlockingGateSeconds = 0.05f;
	private const float NearbyThreatBlockingGateSeconds = 0.06f;
	private readonly object _timelineMotionDiagnosticLogLock = new();
	private bool _timelineMotionDiagnosticLogInitialized;
	private string? _timelineMotionDiagnosticLogPath;

	private void PresentActorMotion(GameEvent gameEvent)
	{
		if (_mapRender == null || string.IsNullOrWhiteSpace(gameEvent.InitiatorId))
			return;

		if (TryBuildActorMotionPresentationRequest(gameEvent, out var request))
		{
			_mapRender.PresentActorMotion(request);
			LogActorMotionPresentationDecision(gameEvent, request);
		}
		else
		{
			_mapRender.ClearActorMotion(gameEvent.InitiatorId);
			LogSkippedActorMotionPresentation(gameEvent);
		}
	}

	private bool TryBuildActorMotionPresentationRequest(
		GameEvent gameEvent,
		out ActorMotionPresentationRequest request)
	{
		request = default;
		if (string.IsNullOrWhiteSpace(gameEvent.InitiatorId)
			|| !TryResolveActorMotionTimingTier(gameEvent.InitiatorId, out var timingTier))
		{
			return false;
		}

		var activeActor = PartyModule.GetActiveActor(_state);
		var blocking = ResolveActorMotionBlocking(gameEvent, timingTier, activeActor);
		var asyncPresentation = timingTier == ActorMotionTimingTier.NpcFast
			&& ShouldUseAsyncNpcMotionPresentation(_state, gameEvent, activeActor, IsMultiplayerSession);
		var continuousPresentation = ShouldUseContinuousPlayerMotionPresentation(
			gameEvent.InitiatorId,
			activeActor,
			IsMultiplayerSession);
		request = new ActorMotionPresentationRequest(
			gameEvent.InitiatorId,
			gameEvent.SourceX,
			gameEvent.SourceY,
			gameEvent.SourceZ,
			gameEvent.TargetX,
			gameEvent.TargetY,
			gameEvent.TargetZ,
			timingTier,
			Blocking: blocking,
			DurationSecondsOverride: ResolveActorMotionDurationOverrideSeconds(
				gameEvent.InitiatorId,
				timingTier,
				activeActor,
				blocking,
				asyncPresentation),
			BlockingGateSecondsOverride: ResolveActorMotionBlockingGateSecondsOverride(
				gameEvent,
				timingTier,
				activeActor,
				blocking),
			AsyncPresentation: asyncPresentation,
			ContinuousPresentation: continuousPresentation);
		return true;
	}

	private ActorMotionTimingTier ResolveCurrentPlayerMotionTimingTier() =>
		_autoNav.IsExecutingStep
			? _autoNav.CurrentPlayerMotionTimingTier
			: ActorMotionTiming.ResolveManualPlayerTier();

	private bool TryResolveActorMotionTimingTier(string actorId, out ActorMotionTimingTier timingTier)
	{
		if (string.Equals(actorId, PartyModule.GetActiveId(_state), StringComparison.Ordinal))
		{
			timingTier = ResolveCurrentPlayerMotionTimingTier();
			return true;
		}

		if (_fastTurnModeEnabled && !_watchModeEnabled)
		{
			timingTier = default;
			return false;
		}

		timingTier = ActorMotionTimingTier.NpcFast;
		return true;
	}

	private bool ResolveActorMotionBlocking(
		GameEvent gameEvent,
		ActorMotionTimingTier timingTier,
		Actor? activeActor)
	{
		if (timingTier != ActorMotionTimingTier.NpcFast)
			return true;

		return !ShouldUseAsyncNpcMotionPresentation(_state, gameEvent, activeActor, IsMultiplayerSession);
	}

	private float? ResolveActorMotionDurationOverrideSeconds(
		string actorId,
		ActorMotionTimingTier timingTier,
		Actor? activeActor,
		bool blockingMotion,
		bool asyncPresentation)
	{
		if (timingTier != ActorMotionTimingTier.NpcFast || !asyncPresentation)
			return null;

		var snapshot = TimelineTurnManager.CreateDebugSnapshot(
			_state,
			PlayerDead,
			_watchModeEnabled,
			includeEntries: false);
		return ShouldUseNpcRushMotionDuration(
			_state,
			actorId,
			activeActor,
			_watchModeEnabled,
			IsMultiplayerSession,
			snapshot.HasPendingAutoAdvance && !snapshot.IsPlayerTurn,
			blockingMotion,
			ThreatDetection.HasNearbyThreat)
			? ActorMotionTiming.NpcRushSeconds
			: null;
	}

	private float? ResolveActorMotionBlockingGateSecondsOverride(
		GameEvent gameEvent,
		ActorMotionTimingTier timingTier,
		Actor? activeActor,
		bool blockingMotion)
	{
		if (!blockingMotion || IsMultiplayerSession)
			return null;

		var reason = ResolveActorMotionPresentationReason(_state, gameEvent, activeActor, IsMultiplayerSession);
		return ResolveSinglePlayerBlockingGateSeconds(reason, timingTier);
	}

	internal static bool ShouldUseAsyncNpcMotionPresentation(
		GameState state,
		GameEvent gameEvent,
		Actor? activeActor,
		bool isMultiplayerSession) =>
		!ShouldBlockNpcMotion(state, gameEvent, activeActor, isMultiplayerSession);

	internal static bool ShouldUseContinuousPlayerMotionPresentation(
		string actorId,
		Actor? activeActor,
		bool isMultiplayerSession) =>
		!isMultiplayerSession
		&& activeActor != null
		&& !string.IsNullOrWhiteSpace(actorId)
		&& string.Equals(actorId, activeActor.Id, StringComparison.Ordinal);

	internal static float? ResolveSinglePlayerBlockingGateSeconds(
		string reason,
		ActorMotionTimingTier timingTier) => reason switch
	{
		"active-player" when timingTier == ActorMotionTimingTier.PlayerFast => PlayerFastBlockingGateSeconds,
		"active-player" => PlayerSlowBlockingGateSeconds,
		"nearby-threat" => NearbyThreatBlockingGateSeconds,
		"nearby-readable" => NearbyReadableBlockingGateSeconds,
		_ => null,
	};

	internal static string ResolveActorMotionPresentationReason(
		GameState state,
		GameEvent gameEvent,
		Actor? activeActor,
		bool isMultiplayerSession)
	{
		if (isMultiplayerSession)
			return "multiplayer";
		if (string.IsNullOrWhiteSpace(gameEvent.InitiatorId))
			return "missing-initiator";
		if (activeActor == null)
			return "no-active-actor";
		if (string.Equals(gameEvent.InitiatorId, activeActor.Id, StringComparison.Ordinal))
			return "active-player";

		var distance = ResolveActorMotionDistance(state, gameEvent, activeActor);
		if (distance <= ReadableNpcMotionDistance)
			return "nearby-readable";

		return FactionRelation.IsHostile(
				activeActor.Faction,
				ResolveMotionActorFaction(state, gameEvent))
			&& distance <= ThreatNpcMotionDistance
				? "nearby-threat"
				: "ambient-background";
	}

	internal static string ResolveSkippedActorMotionReason(
		string actorId,
		string activeActorId,
		bool watchModeEnabled,
		bool fastTurnModeEnabled)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			return "missing-initiator";
		if (string.Equals(actorId, activeActorId, StringComparison.Ordinal))
			return "active-player";
		if (fastTurnModeEnabled && !watchModeEnabled)
			return "fast-turn-skip-npc";
		return "timing-tier-unresolved";
	}

	internal static bool ShouldBlockNpcMotion(
		GameState state,
		GameEvent gameEvent,
		Actor? activeActor,
		bool isMultiplayerSession)
	{
		if (isMultiplayerSession)
			return true;
		if (string.IsNullOrWhiteSpace(gameEvent.InitiatorId) || activeActor == null)
			return true;
		if (string.Equals(gameEvent.InitiatorId, activeActor.Id, StringComparison.Ordinal))
			return true;

		var distance = ResolveActorMotionDistance(state, gameEvent, activeActor);
		if (distance <= ReadableNpcMotionDistance)
			return true;

		return FactionRelation.IsHostile(
				activeActor.Faction,
				ResolveMotionActorFaction(state, gameEvent))
			&& distance <= ThreatNpcMotionDistance;
	}

	private static int ResolveActorMotionDistance(
		GameState state,
		GameEvent gameEvent,
		Actor activeActor)
	{
		var distance = Math.Min(
			AIUtil.Distance3D(activeActor, gameEvent.SourceX, gameEvent.SourceY, gameEvent.SourceZ),
			AIUtil.Distance3D(activeActor, gameEvent.TargetX, gameEvent.TargetY, gameEvent.TargetZ));
		if (string.IsNullOrWhiteSpace(gameEvent.InitiatorId))
			return distance;

		var actor = ActorModule.GetById(state, gameEvent.InitiatorId);
		return actor == null
			? distance
			: Math.Min(distance, AIUtil.Distance3D(activeActor, actor));
	}

	private static string ResolveMotionActorFaction(GameState state, GameEvent gameEvent)
	{
		if (!string.IsNullOrWhiteSpace(gameEvent.InitiatorId))
		{
			var actor = ActorModule.GetById(state, gameEvent.InitiatorId);
			if (actor != null)
				return actor.Faction;
		}

		return gameEvent.InitiatorFaction ?? string.Empty;
	}

	internal static bool ShouldUseNpcRushMotionDuration(
		GameState state,
		string actorId,
		Actor? activeActor,
		bool watchModeEnabled,
		bool isMultiplayerSession,
		bool hasAdditionalAutoAdvance,
		bool blockingMotion,
		Func<GameState, Actor, int, bool> hasNearbyThreat)
	{
		if (string.IsNullOrWhiteSpace(actorId)
			|| activeActor == null
			|| watchModeEnabled
			|| isMultiplayerSession
			|| blockingMotion
			|| !hasAdditionalAutoAdvance)
		{
			return false;
		}

		if (string.Equals(actorId, activeActor.Id, StringComparison.Ordinal))
			return false;

		return !hasNearbyThreat(state, activeActor, ThreatNpcMotionDistance);
	}

	private void LogActorMotionPresentationDecision(
		GameEvent gameEvent,
		ActorMotionPresentationRequest request)
	{
		var activeActor = PartyModule.GetActiveActor(_state);
		var distance = activeActor == null
			? (int?)null
			: ResolveActorMotionDistance(_state, gameEvent, activeActor);
		var hostileToActive = activeActor != null
			&& FactionRelation.IsHostile(activeActor.Faction, ResolveMotionActorFaction(_state, gameEvent));
		var durationSeconds = request.DurationSecondsOverride ?? ActorMotionTiming.ResolveDurationSeconds(
			request.TimingTier,
			request.UsesAsyncPresentation);
		var lane = request.Blocking
			? "blocking"
			: request.UsesAsyncPresentation
				? "ambient-async"
				: "non-blocking";
		var reason = ResolveActorMotionPresentationReason(_state, gameEvent, activeActor, IsMultiplayerSession);
		var actorLabel = ResolveActorMotionDebugActorLabel(gameEvent.InitiatorId, gameEvent.InitiatorActorName);
		var distanceLabel = distance?.ToString() ?? "NA";
		AddTimelineMotionDiagnosticLog(
			$"[motion] turn={_state.Turn} actor={actorLabel} evt={gameEvent.Type} lane={lane} reason={reason} " +
			$"tier={request.TimingTier} dist={distanceLabel} hostile={(hostileToActive ? "Y" : "N")} " +
			$"continuous={(request.UsesContinuousPresentation ? "Y" : "N")} " +
			$"gate={(request.BlockingGateSecondsOverride?.ToString("0.00") ?? "-")}s " +
			$"step=({request.SourceX},{request.SourceY},{request.SourceZ})->({request.TargetX},{request.TargetY},{request.TargetZ}) " +
			$"duration={durationSeconds:0.00}s");
	}

	private void LogSkippedActorMotionPresentation(GameEvent gameEvent)
	{
		var actorLabel = ResolveActorMotionDebugActorLabel(gameEvent.InitiatorId, gameEvent.InitiatorActorName);
		var skipReason = ResolveSkippedActorMotionReason(
			gameEvent.InitiatorId ?? string.Empty,
			PartyModule.GetActiveId(_state),
			_watchModeEnabled,
			_fastTurnModeEnabled);
		AddTimelineMotionDiagnosticLog(
			$"[motion] turn={_state.Turn} actor={actorLabel} evt={gameEvent.Type} skipped={skipReason} " +
			$"step=({gameEvent.SourceX},{gameEvent.SourceY},{gameEvent.SourceZ})->({gameEvent.TargetX},{gameEvent.TargetY},{gameEvent.TargetZ})");
	}

	private string ResolveActorMotionDebugActorLabel(string? actorId, string? actorName)
	{
		if (!string.IsNullOrWhiteSpace(actorName))
			return actorName!;
		if (!string.IsNullOrWhiteSpace(actorId))
		{
			var actor = ActorModule.GetById(_state, actorId);
			if (!string.IsNullOrWhiteSpace(actor?.DisplayName))
				return actor.DisplayName;
			return actorId;
		}

		return "?";
	}

	private void AddTimelineMotionDiagnosticLog(string message)
	{
		try
		{
			lock (_timelineMotionDiagnosticLogLock)
			{
				var path = _timelineMotionDiagnosticLogPath ??= ResolveTimelineMotionDiagnosticLogPath();
				var directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrWhiteSpace(directory))
					Directory.CreateDirectory(directory);

				if (!_timelineMotionDiagnosticLogInitialized)
				{
					File.WriteAllText(path, string.Empty);
					_timelineMotionDiagnosticLogInitialized = true;
				}

				File.AppendAllText(path, message + System.Environment.NewLine);
			}
		}
		catch
		{
			// Diagnostics are best-effort and should never disturb runtime presentation.
		}
	}

	private static string ResolveTimelineMotionDiagnosticLogPath()
	{
		try
		{
			var projectPath = ProjectSettings.GlobalizePath("res://Artifacts/turn/log.txt");
			if (!string.IsNullOrWhiteSpace(projectPath))
				return projectPath;
		}
		catch
		{
		}

		return Path.GetFullPath(Path.Combine(
			Directory.GetCurrentDirectory(),
			"Artifacts",
			"turn",
			"log.txt"));
	}
}
