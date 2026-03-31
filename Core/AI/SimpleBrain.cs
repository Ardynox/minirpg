using System;
using System.Collections.Generic;

namespace MiniRPG.Core.AI;

/// <summary>
/// MVP 内核：最简优先级状态机。
/// 优先级：相邻敌人 → Attack，感知范围有敌人 → MoveTo，否则 → Wander。
/// 纯决策，不涉及任何副作用。未来可替换为行为树 / GOAP 等。
/// </summary>
public class SimpleBrain : IBrainModule
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	public Decision Decide(Perception perception, Random rng)
	{
		var self = perception.Self;
		var enemies = FindEnemies(self, perception.NearbyActors);

		var adjacent = FindAdjacentEnemy(self, enemies);
		if (adjacent != null)
			return BuildAttack(self, adjacent, rng);

		if (enemies.Count > 0)
		{
			var closest = FindClosest(self, enemies);
			if (closest != null)
				return BuildMoveTo(self, closest, perception);
		}

		return BuildWander(self, perception, rng);
	}

	private static List<Actor> FindEnemies(Actor self, List<Actor> nearby)
	{
		var result = new List<Actor>();
		foreach (var other in nearby)
		{
			if (CombatModule.IsDead(other)) continue;
			if (FactionRelation.IsHostile(self.Faction, other.Faction))
				result.Add(other);
		}
		return result;
	}

	private static Actor? FindAdjacentEnemy(Actor self, List<Actor> enemies)
	{
		foreach (var enemy in enemies)
			if (Math.Abs(enemy.X - self.X) + Math.Abs(enemy.Y - self.Y) == 1)
				return enemy;
		return null;
	}

	private static Actor? FindClosest(Actor self, List<Actor> enemies)
	{
		Actor? best = null;
		var bestDist = int.MaxValue;
		foreach (var enemy in enemies)
		{
			var d = Math.Abs(enemy.X - self.X) + Math.Abs(enemy.Y - self.Y);
			if (d < bestDist) { bestDist = d; best = enemy; }
		}
		return best;
	}

	private static Decision BuildAttack(Actor self, Actor target, Random rng)
	{
		var actions = CombatModule.GetAttackActions(self);
		if (actions.Count == 0 || target.Limbs.Count == 0)
			return new Decision { Type = DecisionType.Idle };

		return new Decision
		{
			Type = DecisionType.Attack,
			TargetActorId = target.Id,
			ActionDefId = actions[rng.Next(actions.Count)].Id,
			TargetLimbId = target.Limbs[rng.Next(target.Limbs.Count)].Id,
		};
	}

	private static Decision BuildMoveTo(Actor self, Actor target, Perception perception)
	{
		var pos = StepToward(self.X, self.Y, target.X, target.Y, perception);
		return pos == null
			? new Decision { Type = DecisionType.Idle }
			: new Decision { Type = DecisionType.MoveTo, TargetPos = pos };
	}

	private static Decision BuildWander(Actor self, Perception perception, Random rng)
	{
		var candidates = new List<(int, int)>();
		foreach (var (dx, dy) in Dirs)
		{
			var nx = self.X + dx;
			var ny = self.Y + dy;
			if (perception.NearbyWalkable.TryGetValue((nx, ny), out var w) && w)
				candidates.Add((nx, ny));
		}
		return candidates.Count == 0
			? new Decision { Type = DecisionType.Idle }
			: new Decision { Type = DecisionType.Wander, TargetPos = candidates[rng.Next(candidates.Count)] };
	}

	/// <summary>贪心寻路：在四邻中选曼哈顿距离最小的可行走格。</summary>
	private static (int, int)? StepToward(int sx, int sy, int tx, int ty, Perception p)
	{
		(int, int)? best = null;
		var bestDist = Math.Abs(tx - sx) + Math.Abs(ty - sy);
		foreach (var (dx, dy) in Dirs)
		{
			var nx = sx + dx;
			var ny = sy + dy;
			if (!p.NearbyWalkable.TryGetValue((nx, ny), out var w) || !w) continue;
			var d = Math.Abs(tx - nx) + Math.Abs(ty - ny);
			if (d < bestDist) { bestDist = d; best = (nx, ny); }
		}
		return best;
	}
}
