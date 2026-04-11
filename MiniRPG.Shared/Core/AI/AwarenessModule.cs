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
		var visionActors = RoomRuntimeModule.GetVisionActors(state, connectedOnly: false);
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var targets = new List<Actor>();
		foreach (var actor in visionActors)
		{
			if (!CombatModule.IsDead(actor) && seen.Add(actor.Id))
				targets.Add(actor);
		}
		if (targets.Count == 0)
		{
			var player = ActorModule.GetPlayer(state);
			if (player != null && !CombatModule.IsDead(player))
				targets.Add(player);
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

		// 找到对该 actor 敌对的 player targets（替代 LINQ 减少分配）
		Actor? firstHostileTarget = null;
		var hostileCount = 0;
		foreach (var target in context.PlayerTargets)
		{
			if (string.Equals(target.Id, actor.Id, StringComparison.Ordinal))
				continue;
			if (CombatModule.IsDead(target))
				continue;
			if (!FactionRelation.IsHostile(actor.Faction, target.Faction))
				continue;
			firstHostileTarget ??= target;
			hostileCount++;
		}
		if (hostileCount == 0)
		{
			Reset(actor);
			return events;
		}

		// 找到可见的最近目标
		Actor? visibleTarget = null;
		var bestVisDist = int.MaxValue;
		foreach (var other in perception.NearbyActors)
		{
			var isPlayerTarget = false;
			foreach (var target in context.PlayerTargets)
			{
				if (string.Equals(target.Id, other.Id, StringComparison.Ordinal)
					&& !CombatModule.IsDead(target)
					&& FactionRelation.IsHostile(actor.Faction, target.Faction))
				{
					isPlayerTarget = true;
					break;
				}
			}
			if (!isPlayerTarget) continue;
			var dist = Distance(actor, other);
			if (dist < bestVisDist)
			{
				bestVisDist = dist;
				visibleTarget = other;
			}
		}
		var trackedTarget = ResolveTrackedTarget(actor, context.PlayerTargets);
		var currentTarget = visibleTarget ?? trackedTarget ?? firstHostileTarget!;
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
		Actor? bestFallback = null;
		var bestDist = int.MaxValue;
		foreach (var target in playerTargets)
		{
			if (string.Equals(target.Id, actor.AlertTargetActorId, StringComparison.Ordinal))
				return target;

			var dist = Distance(actor, target);
			if (dist < bestDist || (dist == bestDist && (bestFallback == null || string.CompareOrdinal(target.Id, bestFallback.Id) < 0)))
			{
				bestDist = dist;
				bestFallback = target;
			}
		}

		return bestFallback;
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
