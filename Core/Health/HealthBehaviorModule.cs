using System;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;

namespace MiniRPG.Core.Health;

public static class HealthBehaviorModule
{
	public static ActionExecutionResult TryExecute(GameState state, Actor actor, Perception perception, bool tickBuffs) =>
		TryExecute(state, actor, perception, tickBuffs, behaviorContext: null);

	public static ActionExecutionResult TryExecute(
		GameState state,
		Actor actor,
		Perception perception,
		bool tickBuffs,
		AIBehaviorContext? behaviorContext)
	{
		var result = new ActionExecutionResult();
		var exposure = behaviorContext?.GetCurrentExposure(actor) ?? DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);
		HealthSystem.Sync(actor, state.Turn, exposure, result.Events, state);

		if (actor.AwarenessState != AwarenessState.Idle || Needs.NeedBehaviorModule.HasNearbyThreat(state, actor, behaviorContext))
			return result;

		var selfCare = HealthActionModule.TryTendSelf(state, actor);
		Finalize(result, actor, selfCare, tickBuffs);
		if (result.Consumed)
			return result;

		var patient = FindAdjacentPatient(state, actor);
		if (patient == null)
			return result;

		var otherCare = HealthActionModule.TryTendOther(state, actor, patient);
		Finalize(result, actor, otherCare, tickBuffs);
		return result;
	}

	private static Actor? FindAdjacentPatient(GameState state, Actor actor)
	{
		return CardinalDirs
			.SelectMany(dir => ActorModule.GetAllAt(state, actor.X + dir.Dx, actor.Y + dir.Dy, actor.Z))
			.Where(other =>
				other.Id != actor.Id
				&& !CombatModule.IsDead(other)
				&& !FactionRelation.IsHostile(actor.Faction, other.Faction))
			.Select(other => new
			{
				Actor = other,
				Condition = HealthSystem.GetMostSevereTreatableCondition(other, state.Turn),
			})
			.Where(entry => entry.Condition != null)
			.OrderByDescending(entry => entry.Condition!.Severity)
			.ThenBy(entry => entry.Actor.Id, StringComparer.Ordinal)
			.Select(entry => entry.Actor)
			.FirstOrDefault();
	}

	private static void Finalize(ActionExecutionResult result, Actor actor, ActionExecutionResult healthResult, bool tickBuffs)
	{
		if (healthResult.Events.Count > 0)
			result.Events.AddRange(healthResult.Events);
		if (!healthResult.Consumed)
			return;

		result.Consumed = true;
		if (tickBuffs)
			actor.TickBuffs();
	}

	private static readonly (int Dx, int Dy)[] CardinalDirs =
	[
		(0, -1),
		(0, 1),
		(-1, 0),
		(1, 0),
	];
}
