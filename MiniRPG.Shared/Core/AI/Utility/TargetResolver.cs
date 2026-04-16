using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.Job;
using MiniRPG.Core.Social;

namespace MiniRPG.Core.AI.Utility;

public static class TargetResolver
{
	public static Actor? FindBestEnemyTarget(Actor self, Perception perception, GameState? state, SimDetail detail)
	{
		if (detail == SimDetail.Simplified)
			return FindClosestEnemy(self, perception);

		return FindBestScoredEnemy(self, perception, state);
	}

	public static Actor? FindBestAllyTarget(Actor self, Perception perception, GameState? state, SimDetail detail, string purpose)
	{
		if (detail == SimDetail.Simplified)
			return FindClosestAllyWithCondition(self, perception, state, purpose);

		return FindBestScoredAlly(self, perception, state, purpose);
	}

	public static WorkTicket? FindBestWorkTicket(GameState state, Actor worker) =>
		JobScheduler.FindBestTicket(state, worker);

	public static Actor? FindFollowTarget(GameState state, Actor self)
	{
		var activeId = PartyModule.GetActiveId(state);
		if (string.IsNullOrEmpty(activeId)) return null;
		return state.Actors.TryGetValue(activeId, out var leader) ? leader : null;
	}

	public static Actor? FindBestSocialTarget(Actor self, Perception perception, GameState? state)
	{
		Actor? best = null;
		var bestDist = int.MaxValue;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other)) continue;
			if (other.Id == self.Id) continue;
			if (FactionRelation.IsHostile(self.Faction, other.Faction)) continue;
			var dist = AIUtil.Distance3D(self, other);
			if (dist > 2) continue;
			if (dist < bestDist)
			{
				bestDist = dist;
				best = other;
			}
		}
		return best;
	}

	private static Actor? FindClosestEnemy(Actor self, Perception perception)
	{
		Actor? best = null;
		var bestDist = int.MaxValue;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other)) continue;
			if (!FactionRelation.IsHostile(self.Faction, other.Faction)) continue;
			var dist = AIUtil.Distance3D(self, other);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = other;
			}
		}
		return best;
	}

	private static Actor? FindBestScoredEnemy(Actor self, Perception perception, GameState? state)
	{
		Actor? best = null;
		var bestScore = float.MinValue;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other)) continue;
			if (!FactionRelation.IsHostile(self.Faction, other.Faction)) continue;
			var dist = AIUtil.Distance3D(self, other);
			var hpRatio = GetHpRatio(other);
			var score = (1f - hpRatio) * 40f + (1f - dist / 10f) * 30f;
			if (other.Limbs.Count > 0)
			{
				foreach (var limb in other.Limbs)
				{
					if (limb.Tags.ContainsKey("vital"))
					{
						score += 20f;
						break;
					}
				}
			}
			if (score > bestScore)
			{
				bestScore = score;
				best = other;
			}
		}
		return best;
	}

	private static Actor? FindClosestAllyWithCondition(Actor self, Perception perception, GameState? state, string purpose)
	{
		Actor? best = null;
		var bestDist = int.MaxValue;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other)) continue;
			if (other.Id == self.Id) continue;
			if (FactionRelation.IsHostile(self.Faction, other.Faction)) continue;
			if (!MatchesPurpose(other, purpose, state)) continue;
			var dist = AIUtil.Distance3D(self, other);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = other;
			}
		}
		return best;
	}

	private static Actor? FindBestScoredAlly(Actor self, Perception perception, GameState? state, string purpose)
	{
		Actor? best = null;
		var bestScore = float.MinValue;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other)) continue;
			if (other.Id == self.Id) continue;
			if (FactionRelation.IsHostile(self.Faction, other.Faction)) continue;
			if (!MatchesPurpose(other, purpose, state)) continue;
			var dist = AIUtil.Distance3D(self, other);
			var urgency = GetAllyUrgency(other, purpose);
			var opinion = state != null ? SocialModule.GetOpinion(state, self.Id, other.Id) : 0;
			var score = urgency * 50f + (opinion + 100f) / 200f * 30f - dist * 2f;
			if (score > bestScore)
			{
				bestScore = score;
				best = other;
			}
		}
		return best;
	}

	private static bool MatchesPurpose(Actor other, string purpose, GameState? state)
	{
		return purpose switch
		{
			"rescue_fire" => HasCondition(other, HealthConditionIds.OnFire),
			"tend" => HealthSystem.HasTreatableCondition(other, state?.Turn ?? 0),
			_ => true,
		};
	}

	private static float GetAllyUrgency(Actor other, string purpose)
	{
		return purpose switch
		{
			"rescue_fire" => GetConditionSeverity(other, HealthConditionIds.OnFire) / 100f,
			"tend" => GetWorstTreatableSeverity(other) / 100f,
			_ => 0.5f,
		};
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

	private static bool HasCondition(Actor actor, string conditionId)
	{
		foreach (var cond in actor.HealthConditions)
		{
			if (string.Equals(cond.Id, conditionId, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static float GetConditionSeverity(Actor actor, string conditionId)
	{
		foreach (var cond in actor.HealthConditions)
		{
			if (string.Equals(cond.Id, conditionId, StringComparison.Ordinal))
				return cond.Severity;
		}
		return 0f;
	}

	private static float GetWorstTreatableSeverity(Actor actor)
	{
		var worst = 0f;
		foreach (var cond in actor.HealthConditions)
		{
			if (cond.Permanent) continue;
			if (cond.Severity > worst) worst = cond.Severity;
		}
		return worst;
	}
}
