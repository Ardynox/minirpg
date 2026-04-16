using System;
using MiniRPG.Core.Data;
using MiniRPG.Module.Render;

namespace MiniRPG;

public partial class Main
{
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

		request = new ActorMotionPresentationRequest(
			gameEvent.InitiatorId,
			gameEvent.SourceX,
			gameEvent.SourceY,
			gameEvent.SourceZ,
			gameEvent.TargetX,
			gameEvent.TargetY,
			gameEvent.TargetZ,
			timingTier,
			Blocking: true);
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
}
