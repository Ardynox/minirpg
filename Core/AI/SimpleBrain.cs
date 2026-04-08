using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI;

public class SimpleBrain : IBrainModule
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	public Decision Decide(Perception perception, Random rng)
	{
		var self = perception.Self;
		var statefulDecision = self.AwarenessState switch
		{
			AwarenessState.Suspicious => BuildSuspiciousDecision(self, perception),
			AwarenessState.Alerted => BuildAlertedDecision(self, perception),
			AwarenessState.Searching => BuildSearchingDecision(self, perception, rng),
			_ => null,
		};
		if (statefulDecision != null)
			return statefulDecision;

		return BuildIdleDecision(self, perception, rng);
	}

	private static Decision BuildIdleDecision(Actor self, Perception perception, Random rng)
	{
		var enemies = FindEnemies(self, perception.NearbyActors);
		var attackDecision = BuildAttackDecision(self, enemies, perception);
		if (attackDecision != null)
			return attackDecision;

		if (enemies.Count > 0)
		{
			var closest = FindClosest(self, enemies);
			if (closest != null)
				return BuildMoveTo(self, closest.X, closest.Y, perception);
		}

		return BuildReturnHome(self, perception) ?? BuildWander(self, perception, rng);
	}

	private static Decision? BuildSuspiciousDecision(Actor self, Perception perception)
	{
		if (!HasLastKnownTarget(self))
			return BuildReturnHome(self, perception);

		if (IsAtPosition(self, self.LastKnownTargetX, self.LastKnownTargetY))
			return new Decision { Type = DecisionType.Idle };

		return BuildMoveTo(self, self.LastKnownTargetX, self.LastKnownTargetY, perception);
	}

	private static Decision? BuildAlertedDecision(Actor self, Perception perception)
	{
		var trackedTarget = ResolveTrackedTarget(self, perception);
		if (trackedTarget == null)
			return HasLastKnownTarget(self)
				? BuildMoveTo(self, self.LastKnownTargetX, self.LastKnownTargetY, perception)
				: BuildReturnHome(self, perception);

		var attackDecision = BuildAttackDecision(self, [trackedTarget], perception);
		if (attackDecision != null)
			return attackDecision;

		return BuildMoveTo(self, trackedTarget.X, trackedTarget.Y, perception);
	}

	private static Decision? BuildSearchingDecision(Actor self, Perception perception, Random rng)
	{
		if (!HasLastKnownTarget(self))
			return BuildReturnHome(self, perception);

		if (!IsAtPosition(self, self.LastKnownTargetX, self.LastKnownTargetY))
			return BuildMoveTo(self, self.LastKnownTargetX, self.LastKnownTargetY, perception);

		var sweepTarget = PickSearchSweepTarget(self, perception, rng);
		if (sweepTarget != null)
			return BuildMoveTo(self, sweepTarget.Value.X, sweepTarget.Value.Y, perception);

		return new Decision { Type = DecisionType.Idle };
	}

	private static List<Actor> FindEnemies(Actor self, List<Actor> nearby)
	{
		var result = new List<Actor>();
		foreach (var other in nearby)
		{
			if (FactionRelation.IsHostile(self.Faction, other.Faction))
				result.Add(other);
		}

		return result;
	}

	private static Actor? FindClosest(Actor self, List<Actor> enemies)
	{
		Actor? best = null;
		var bestDist = int.MaxValue;
		foreach (var enemy in enemies)
		{
			var distance = Math.Abs(enemy.X - self.X) + Math.Abs(enemy.Y - self.Y);
			if (distance < bestDist)
			{
				bestDist = distance;
				best = enemy;
			}
		}

		return best;
	}

	private static Decision? BuildAttackDecision(Actor self, List<Actor> enemies, Perception perception)
	{
		if (enemies.Count == 0)
			return null;

		var skills = CombatModule.GetAttackActions(self)
			.Where(skill => !self.IsSkillOnCooldown(skill.Id))
			.OrderByDescending(skill => skill.EffectType == "ranged_attack")
			.ThenByDescending(skill => skill.Range)
			.ThenByDescending(skill => skill.Power)
			.ToList();
		if (skills.Count == 0)
			return null;

		var orderedEnemies = enemies
			.Where(enemy => enemy.Limbs.Count > 0)
			.OrderBy(enemy => Math.Abs(enemy.X - self.X) + Math.Abs(enemy.Y - self.Y))
			.ThenBy(enemy => enemy.Id, StringComparer.Ordinal)
			.ToList();
		if (orderedEnemies.Count == 0)
			return null;

		if (perception.State != null)
		{
			foreach (var skill in skills)
			{
				foreach (var enemy in orderedEnemies)
				{
					if (!ActionModule.CanCastSkill(perception.State, self, skill.Id, SkillTargetType.Actor, targetActor: enemy))
						continue;

					return BuildAttack(skill, enemy);
				}
			}
		}

		var adjacent = orderedEnemies.FirstOrDefault(enemy => Math.Abs(enemy.X - self.X) + Math.Abs(enemy.Y - self.Y) == 1);
		if (adjacent == null)
			return null;

		var adjacentSkill = skills.FirstOrDefault(skill => skill.Range == 1);
		return adjacentSkill != null ? BuildAttack(adjacentSkill, adjacent) : null;
	}

	private static Decision BuildAttack(InteractionDef skill, Actor target) => new()
	{
		Type = DecisionType.Attack,
		TargetActorId = target.Id,
		ActionDefId = skill.Id,
	};

	private static Decision BuildMoveTo(Actor self, int targetX, int targetY, Perception perception)
	{
		var pos = StepToward(self.X, self.Y, targetX, targetY, perception);
		return pos == null
			? new Decision { Type = DecisionType.Idle }
			: new Decision { Type = DecisionType.MoveTo, TargetPos = pos };
	}

	private static Decision? BuildReturnHome(Actor self, Perception perception)
	{
		if (!self.HasHomePosition || self.HomeZ != self.Z || IsAtPosition(self, self.HomeX, self.HomeY))
			return null;

		return BuildMoveTo(self, self.HomeX, self.HomeY, perception);
	}

	private static Decision BuildWander(Actor self, Perception perception, Random rng)
	{
		var candidates = new List<(int X, int Y)>();
		foreach (var (dx, dy) in Dirs)
		{
			var nx = self.X + dx;
			var ny = self.Y + dy;
			if (IsWalkable(perception, nx, ny))
				candidates.Add((nx, ny));
		}

		return candidates.Count == 0
			? new Decision { Type = DecisionType.Idle }
			: new Decision { Type = DecisionType.Wander, TargetPos = candidates[rng.Next(candidates.Count)] };
	}

	private static (int X, int Y)? PickSearchSweepTarget(Actor self, Perception perception, Random rng)
	{
		var candidates = new List<(int X, int Y)>();
		for (var dy = -AwarenessModule.SearchRadius; dy <= AwarenessModule.SearchRadius; dy++)
		{
			for (var dx = -AwarenessModule.SearchRadius; dx <= AwarenessModule.SearchRadius; dx++)
			{
				if (Math.Abs(dx) + Math.Abs(dy) > AwarenessModule.SearchRadius)
					continue;

				var x = self.LastKnownTargetX + dx;
				var y = self.LastKnownTargetY + dy;
				if (IsAtPosition(self, x, y) || !IsWalkable(perception, x, y))
					continue;

				candidates.Add((x, y));
			}
		}

		if (candidates.Count == 0)
			return null;

		candidates.Sort(static (left, right) =>
		{
			var leftCompare = left.X.CompareTo(right.X);
			return leftCompare != 0 ? leftCompare : left.Y.CompareTo(right.Y);
		});
		return candidates[rng.Next(candidates.Count)];
	}

	private static (int, int)? StepToward(int sx, int sy, int tx, int ty, Perception perception)
	{
		if (perception.State != null)
		{
			var z = perception.Floor;
			var state = perception.State;
			var step = Pathfinding.NextStep(
				sx,
				sy,
				tx,
				ty,
				(x, y) => FireSystem.IsSafeWalkableForActor(state, perception.Self, x, y, z));
			if (step != null)
				return step;
		}

		(int, int)? best = null;
		var bestDist = Math.Abs(tx - sx) + Math.Abs(ty - sy);
		foreach (var (dx, dy) in Dirs)
		{
			var nx = sx + dx;
			var ny = sy + dy;
			if (!IsWalkable(perception, nx, ny))
				continue;

			var distance = Math.Abs(tx - nx) + Math.Abs(ty - ny);
			if (distance < bestDist)
			{
				bestDist = distance;
				best = (nx, ny);
			}
		}

		return best;
	}

	private static Actor? ResolveTrackedTarget(Actor self, Perception perception)
	{
		if (perception.State == null || string.IsNullOrWhiteSpace(self.AlertTargetActorId))
			return null;

		var visibleTrackedTarget = perception.NearbyActors
			.FirstOrDefault(other => string.Equals(other.Id, self.AlertTargetActorId, StringComparison.Ordinal));
		if (visibleTrackedTarget != null)
			return visibleTrackedTarget;

		var target = ActorModule.GetById(perception.State, self.AlertTargetActorId);
		return target != null && !CombatModule.IsDead(target) ? target : null;
	}

	private static bool HasLastKnownTarget(Actor self) =>
		!string.IsNullOrWhiteSpace(self.AlertTargetActorId);

	private static bool IsAtPosition(Actor self, int x, int y) =>
		self.X == x && self.Y == y;

	private static bool IsWalkable(Perception perception, int x, int y)
	{
		if (perception.NearbyWalkable.TryGetValue((x, y), out var walkable))
			return walkable;

		return perception.State != null && FireSystem.IsSafeWalkableForActor(perception.State, perception.Self, x, y, perception.Floor);
	}
}
