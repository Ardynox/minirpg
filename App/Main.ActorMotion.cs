using System;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;

namespace MiniRPG;

public partial class Main
{
	private const int ReadableNpcMotionDistance = 4;
	private const int ThreatNpcMotionDistance = 6;

	private void PresentActorMotion(GameEvent gameEvent)
	{
		if (_mapRender == null || string.IsNullOrWhiteSpace(gameEvent.InitiatorId))
			return;

		if (TryBuildActorMotionPresentationRequest(gameEvent, out var request))
			_mapRender.PresentActorMotion(request);
		else
			_mapRender.ClearActorMotion(gameEvent.InitiatorId);
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
			AsyncPresentation: asyncPresentation);
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

	internal static bool ShouldUseAsyncNpcMotionPresentation(
		GameState state,
		GameEvent gameEvent,
		Actor? activeActor,
		bool isMultiplayerSession) =>
		!ShouldBlockNpcMotion(state, gameEvent, activeActor, isMultiplayerSession);

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
}
