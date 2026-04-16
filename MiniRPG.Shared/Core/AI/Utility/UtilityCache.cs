using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;

namespace MiniRPG.Core.AI.Utility;

public sealed class UtilityDecisionCache
{
	public string ActionId { get; set; } = "";
	public int DecisionTurn { get; set; } = -1;
	public float Score { get; set; }
	public string? TargetActorId { get; set; }
	public string? TargetTicketId { get; set; }
	public (int X, int Y)? TargetPos { get; set; }
	public AwarenessState AwarenessAtDecision { get; set; }
	public float HpAtDecision { get; set; }
	public bool WasOnFire { get; set; }
	public int EnemyCountAtDecision { get; set; }
}

public static class UtilityCache
{
	private const int FullReevalInterval = 6;
	private const int SimplifiedReevalInterval = 10;
	private const int QuickReevalInterval = 3;

	public static bool NeedsFullReevaluation(
		GameState state,
		Actor actor,
		Perception perception,
		SimDetail detail,
		UtilityDecisionCache? cache)
	{
		if (cache == null || cache.DecisionTurn < 0)
			return true;

		if (HasInterrupt(state, actor, perception, cache))
			return true;

		var interval = detail switch
		{
			SimDetail.Full => FullReevalInterval,
			SimDetail.Simplified => SimplifiedReevalInterval,
			_ => QuickReevalInterval,
		};

		return state.Turn - cache.DecisionTurn >= interval;
	}

	public static bool NeedsQuickReevaluation(
		GameState state,
		Actor actor,
		Perception perception,
		SimDetail detail,
		UtilityDecisionCache? cache)
	{
		if (cache == null || cache.DecisionTurn < 0)
			return true;

		if (HasInterrupt(state, actor, perception, cache))
			return true;

		var interval = detail switch
		{
			SimDetail.Full => QuickReevalInterval,
			SimDetail.Simplified => QuickReevalInterval * 2,
			_ => QuickReevalInterval,
		};

		return state.Turn - cache.DecisionTurn >= interval;
	}

	public static bool HasInterrupt(
		GameState state,
		Actor actor,
		Perception perception,
		UtilityDecisionCache cache)
	{
		if (HasCondition(actor, HealthConditionIds.OnFire) && !cache.WasOnFire)
			return true;

		if (actor.AwarenessState != cache.AwarenessAtDecision)
			return true;

		var currentHp = GetHpRatio(actor);
		if (cache.HpAtDecision - currentHp > 0.2f)
			return true;

		var currentEnemies = CountEnemies(actor, perception);
		if (currentEnemies > 0 && cache.EnemyCountAtDecision == 0)
			return true;
		if (currentEnemies == 0 && cache.EnemyCountAtDecision > 0)
			return true;

		if (actor.MentalBreak != null && !actor.MentalBreak.IsActive(state.Turn))
		{
			actor.MentalBreak = null;
			actor.MoodValue = Math.Min(100f, actor.MoodValue + 10f);
			return true;
		}

		return false;
	}

	public static UtilityDecisionCache CreateCache(
		GameState state,
		Actor actor,
		Perception perception,
		UtilityEvalResult eval)
	{
		return new UtilityDecisionCache
		{
			ActionId = eval.Action?.Id ?? "",
			DecisionTurn = state.Turn,
			Score = eval.Score,
			TargetActorId = eval.TargetActor?.Id,
			TargetTicketId = eval.TargetTicket?.Id,
			TargetPos = eval.TargetPos,
			AwarenessAtDecision = actor.AwarenessState,
			HpAtDecision = GetHpRatio(actor),
			WasOnFire = HasCondition(actor, HealthConditionIds.OnFire),
			EnemyCountAtDecision = CountEnemies(actor, perception),
		};
	}

	public static IReadOnlyList<UtilityActionDef>? GetContextualSubset(
		Actor actor,
		Perception perception)
	{
		if (actor.MentalBreak != null)
			return UtilityActionRegistry.GetByTag("mental_break") as IReadOnlyList<UtilityActionDef>;

		if (actor.AwarenessState == AwarenessState.Alerted || actor.AwarenessState == AwarenessState.Searching)
		{
			var combatActions = new List<UtilityActionDef>();
			foreach (var a in UtilityActionRegistry.ActionList)
			{
				if (a.Tags.Contains("combat") || a.Tags.Contains("survival") || a.Tags.Contains("urgent"))
					combatActions.Add(a);
			}
			return combatActions.Count > 0 ? combatActions : null;
		}

		return null;
	}

	private static bool HasCondition(Actor actor, string conditionId)
	{
		foreach (var cond in actor.HealthConditions)
		{
			if (string.Equals(cond.Id, conditionId, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static float GetHpRatio(Actor actor)
	{
		if (actor.Limbs.Count == 0) return 0f;
		var total = 0f;
		var max = 0f;
		foreach (var limb in actor.Limbs)
		{
			total += limb.Durability;
			max += limb.MaxDurability;
		}
		return max > 0 ? total / max : 0f;
	}

	private static int CountEnemies(Actor self, Perception perception)
	{
		var count = 0;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other)) continue;
			if (FactionRelation.IsHostile(self.Faction, other.Faction))
				count++;
		}
		return count;
	}
}
