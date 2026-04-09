using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;

namespace MiniRPG.Core.AI;

public readonly record struct AwarenessTurnContext(IReadOnlyList<Actor> PlayerTargets);

public static class AwarenessModule
{
	public const int SearchDurationTurns = 6;
	public const int SearchRadius = 2;

	public static AwarenessTurnContext CreateTurnContext(GameState state)
	{
		var targets = RoomRuntimeModule.GetVisionActors(state, connectedOnly: false)
			.Where(static actor => !CombatModule.IsDead(actor))
			.DistinctBy(static actor => actor.Id)
			.ToArray();
		if (targets.Length == 0)
		{
			var player = ActorModule.GetPlayer(state);
			if (player != null && !CombatModule.IsDead(player))
				targets = [player];
		}

		return new AwarenessTurnContext(targets);
	}

	public static List<GameEvent> UpdateForTurn(GameState state, Actor actor, Perception perception) =>
		UpdateForTurn(state, actor, perception, CreateTurnContext(state));

	public static List<GameEvent> UpdateForTurn(
		GameState state,
		Actor actor,
		Perception perception,
		AwarenessTurnContext context)
	{
		var events = new List<GameEvent>();
		var playerTargets = context.PlayerTargets
			.Where(target =>
				!string.Equals(target.Id, actor.Id, StringComparison.Ordinal)
				&& !CombatModule.IsDead(target)
				&& FactionRelation.IsHostile(actor.Faction, target.Faction))
			.ToArray();
		if (playerTargets.Length == 0)
		{
			Reset(actor);
			return events;
		}

		var visibleTarget = perception.NearbyActors
			.Where(other => playerTargets.Any(target => string.Equals(target.Id, other.Id, StringComparison.Ordinal)))
			.OrderBy(other => Distance(actor, other))
			.FirstOrDefault();
		var trackedTarget = ResolveTrackedTarget(actor, playerTargets);
		var currentTarget = visibleTarget ?? trackedTarget;
		if (currentTarget == null)
		{
			Reset(actor);
			return events;
		}

		var canSeePlayer = visibleTarget != null;
		if (canSeePlayer)
			actor.RememberAlertTarget(currentTarget);

		switch (actor.AwarenessState)
		{
			case AwarenessState.Idle:
				if (canSeePlayer)
					ChangeState(state, actor, currentTarget, AwarenessState.Suspicious, events);
				else
					actor.StateTurns++;
				break;

			case AwarenessState.Suspicious:
				if (canSeePlayer)
				{
					ChangeState(state, actor, currentTarget, AwarenessState.Alerted, events);
				}
				else if (actor.StateTurns >= 1 || IsAtLastKnownPosition(actor))
				{
					ChangeState(state, actor, currentTarget, AwarenessState.Idle, events, clearTarget: true);
				}
				else
				{
					actor.StateTurns++;
				}
				break;

			case AwarenessState.Alerted:
				if (canSeePlayer)
				{
					actor.StateTurns++;
				}
				else
				{
					ChangeState(state, actor, currentTarget, AwarenessState.Searching, events);
				}
				break;

			case AwarenessState.Searching:
				if (canSeePlayer)
				{
					ChangeState(state, actor, currentTarget, AwarenessState.Alerted, events);
				}
				else if (actor.SearchTurnsRemaining <= 1)
				{
					ChangeState(state, actor, currentTarget, AwarenessState.Idle, events, clearTarget: true);
				}
				else
				{
					actor.SearchTurnsRemaining--;
					actor.StateTurns++;
				}
				break;
		}

		return events;
	}

	private static void ChangeState(
		GameState state,
		Actor actor,
		Actor player,
		AwarenessState nextState,
		List<GameEvent> events,
		bool clearTarget = false)
	{
		if (actor.AwarenessState == nextState)
			return;

		var lastKnownX = actor.LastKnownTargetX;
		var lastKnownY = actor.LastKnownTargetY;
		var lastKnownZ = actor.LastKnownTargetZ;
		actor.AwarenessState = nextState;
		actor.StateTurns = 0;

		switch (nextState)
		{
			case AwarenessState.Searching:
				actor.SearchTurnsRemaining = SearchDurationTurns;
				break;

			case AwarenessState.Alerted:
			case AwarenessState.Suspicious:
				actor.SearchTurnsRemaining = 0;
				break;

			case AwarenessState.Idle:
				actor.SearchTurnsRemaining = 0;
				if (clearTarget)
					actor.ClearAlertTarget();
				break;
		}

		var awarenessEvent = new GameEvent("awareness_state_changed")
		{
			EffectType = ToEffectType(nextState),
			TargetX = nextState == AwarenessState.Idle ? lastKnownX : actor.LastKnownTargetX,
			TargetY = nextState == AwarenessState.Idle ? lastKnownY : actor.LastKnownTargetY,
			TargetZ = nextState == AwarenessState.Idle ? lastKnownZ : actor.LastKnownTargetZ,
		};
		IdentificationModule.PopulateInitiatorIdentity(awarenessEvent, state, actor);
		IdentificationModule.PopulateTargetIdentity(awarenessEvent, state, player);
		events.Add(awarenessEvent);
	}

	private static bool IsAtLastKnownPosition(Actor actor) =>
		actor.X == actor.LastKnownTargetX
		&& actor.Y == actor.LastKnownTargetY
		&& actor.Z == actor.LastKnownTargetZ;

	private static Actor? ResolveTrackedTarget(Actor actor, IReadOnlyList<Actor> playerTargets)
	{
		var remembered = playerTargets.FirstOrDefault(target =>
			string.Equals(target.Id, actor.AlertTargetActorId, StringComparison.Ordinal));
		if (remembered != null)
			return remembered;

		return playerTargets
			.OrderBy(target => Distance(actor, target))
			.ThenBy(target => target.Id, StringComparer.Ordinal)
			.FirstOrDefault();
	}

	private static int Distance(Actor actor, Actor target) =>
		Math.Abs(actor.X - target.X) + Math.Abs(actor.Y - target.Y) + Math.Abs(actor.Z - target.Z);

	private static void Reset(Actor actor)
	{
		actor.AwarenessState = AwarenessState.Idle;
		actor.StateTurns = 0;
		actor.ClearAlertTarget();
	}

	private static string ToEffectType(AwarenessState state) => state switch
	{
		AwarenessState.Suspicious => "suspicious",
		AwarenessState.Alerted => "alerted",
		AwarenessState.Searching => "searching",
		_ => "idle",
	};
}
