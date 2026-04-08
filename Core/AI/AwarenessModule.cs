using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.AI;

public readonly record struct AwarenessTurnContext(Actor? Player, bool PlayerIsDead);

public static class AwarenessModule
{
	public const int SearchDurationTurns = 6;
	public const int SearchRadius = 2;

	public static AwarenessTurnContext CreateTurnContext(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		return new AwarenessTurnContext(player, player == null || CombatModule.IsDead(player));
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
		var player = context.Player;
		if (player == null
			|| string.Equals(player.Id, actor.Id, StringComparison.Ordinal)
			|| context.PlayerIsDead
			|| !FactionRelation.IsHostile(actor.Faction, player.Faction))
		{
			Reset(actor);
			return events;
		}

		var canSeePlayer = perception.NearbyActors.Any(other => string.Equals(other.Id, player.Id, StringComparison.Ordinal));
		if (canSeePlayer)
			actor.RememberAlertTarget(player);

		switch (actor.AwarenessState)
		{
			case AwarenessState.Idle:
				if (canSeePlayer)
					ChangeState(state, actor, player, AwarenessState.Suspicious, events);
				else
					actor.StateTurns++;
				break;

			case AwarenessState.Suspicious:
				if (canSeePlayer)
				{
					ChangeState(state, actor, player, AwarenessState.Alerted, events);
				}
				else if (actor.StateTurns >= 1 || IsAtLastKnownPosition(actor))
				{
					ChangeState(state, actor, player, AwarenessState.Idle, events, clearTarget: true);
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
					ChangeState(state, actor, player, AwarenessState.Searching, events);
				}
				break;

			case AwarenessState.Searching:
				if (canSeePlayer)
				{
					ChangeState(state, actor, player, AwarenessState.Alerted, events);
				}
				else if (actor.SearchTurnsRemaining <= 1)
				{
					ChangeState(state, actor, player, AwarenessState.Idle, events, clearTarget: true);
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
