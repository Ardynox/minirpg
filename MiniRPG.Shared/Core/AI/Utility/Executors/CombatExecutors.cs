using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

public sealed class AttackEnemyExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var target = eval.TargetActor;
		if (target == null || CombatModule.IsDead(target))
			return new ActionExecutionResult();

		var skills = CombatModule.GetAttackActions(actor);
		skills.Sort(static (a, b) =>
		{
			var ra = a.EffectType == "ranged_attack" ? 1 : 0;
			var rb = b.EffectType == "ranged_attack" ? 1 : 0;
			var cmp = rb.CompareTo(ra);
			if (cmp != 0) return cmp;
			cmp = b.Range.CompareTo(a.Range);
			return cmp != 0 ? cmp : b.Power.CompareTo(a.Power);
		});

		foreach (var skill in skills)
		{
			if (actor.IsSkillOnCooldown(skill.Id)) continue;
			if (!ActionModule.CanCastSkill(state, actor, skill.Id, SkillTargetType.Actor, targetActor: target))
				continue;
			return ActionModule.TryCastSkill(state, actor, skill.Id, SkillTargetType.Actor, targetActorId: target.Id);
		}

		return new ActionExecutionResult();
	}
}

public sealed class ChaseEnemyExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var target = eval.TargetActor;
		if (target == null || CombatModule.IsDead(target))
			return new ActionExecutionResult();

		if (state.World != null && actor.Z != target.Z)
		{
			var goDown = target.Z > actor.Z;
			if (state.World.CanTraverseVertical(actor.X, actor.Y, actor.Z, goDown))
			{
				ClimbingService.MoveActorVertical(state, actor, goDown ? 1 : -1);
				return new ActionExecutionResult { Consumed = true };
			}
		}

		return MoveToward(state, actor, target.X, target.Y);
	}

	private static ActionExecutionResult MoveToward(GameState state, Actor actor, int tx, int ty)
	{
		var step = Pathfinding.NextStep(actor.X, actor.Y, tx, ty,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null) return new ActionExecutionResult();
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}
}

public sealed class FleeCombatExecutor : IUtilityExecutor
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var threat = eval.TargetActor ?? FindClosestEnemy(actor, perception);
		if (threat == null)
			return new ActionExecutionResult();

		var bestDist = -1;
		(int X, int Y)? bestCell = null;
		foreach (var (dx, dy) in Dirs)
		{
			var nx = actor.X + dx;
			var ny = actor.Y + dy;
			if (!IsWalkable(perception, state, actor, nx, ny)) continue;
			var dist = Math.Abs(nx - threat.X) + Math.Abs(ny - threat.Y) + Math.Abs(actor.Z - threat.Z);
			if (dist > bestDist) { bestDist = dist; bestCell = (nx, ny); }
		}

		if (bestCell == null) return new ActionExecutionResult();
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, bestCell.Value.X - actor.X, bestCell.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}

	private static Actor? FindClosestEnemy(Actor self, Perception perception)
	{
		Actor? best = null;
		var bestDist = int.MaxValue;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other)) continue;
			if (!FactionRelation.IsHostile(self.Faction, other.Faction)) continue;
			var d = AIUtil.Distance3D(self, other);
			if (d < bestDist) { bestDist = d; best = other; }
		}
		return best;
	}

	private static bool IsWalkable(Perception perception, GameState state, Actor actor, int x, int y)
	{
		if (perception.NearbyWalkable.TryGetValue((x, y), out var w)) return w;
		return FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z);
	}
}

public sealed class InvestigateSuspiciousExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (string.IsNullOrWhiteSpace(actor.AlertTargetActorId))
			return new ActionExecutionResult();

		if (actor.X == actor.LastKnownTargetX && actor.Y == actor.LastKnownTargetY)
			return new ActionExecutionResult { Consumed = true };

		return MoveToward(state, actor, actor.LastKnownTargetX, actor.LastKnownTargetY);
	}

	private static ActionExecutionResult MoveToward(GameState state, Actor actor, int tx, int ty)
	{
		var step = Pathfinding.NextStep(actor.X, actor.Y, tx, ty,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null) return new ActionExecutionResult();
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}
}

public sealed class SearchAreaExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (string.IsNullOrWhiteSpace(actor.AlertTargetActorId))
			return new ActionExecutionResult();

		if (actor.X != actor.LastKnownTargetX || actor.Y != actor.LastKnownTargetY)
			return MoveToward(state, actor, actor.LastKnownTargetX, actor.LastKnownTargetY);

		var rng = new Random(unchecked(state.Turn * 17 + actor.Id.GetHashCode()));
		var candidates = new List<(int X, int Y)>();
		for (var dy = -AwarenessModule.SearchRadius; dy <= AwarenessModule.SearchRadius; dy++)
		{
			for (var dx = -AwarenessModule.SearchRadius; dx <= AwarenessModule.SearchRadius; dx++)
			{
				if (Math.Abs(dx) + Math.Abs(dy) > AwarenessModule.SearchRadius) continue;
				var x = actor.LastKnownTargetX + dx;
				var y = actor.LastKnownTargetY + dy;
				if (x == actor.X && y == actor.Y) continue;
				if (IsWalkable(perception, state, actor, x, y))
					candidates.Add((x, y));
			}
		}

		if (candidates.Count == 0) return new ActionExecutionResult { Consumed = true };
		var target = candidates[rng.Next(candidates.Count)];
		return MoveToward(state, actor, target.X, target.Y);
	}

	private static ActionExecutionResult MoveToward(GameState state, Actor actor, int tx, int ty)
	{
		var step = Pathfinding.NextStep(actor.X, actor.Y, tx, ty,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null) return new ActionExecutionResult();
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}

	private static bool IsWalkable(Perception perception, GameState state, Actor actor, int x, int y)
	{
		if (perception.NearbyWalkable.TryGetValue((x, y), out var w)) return w;
		return FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z);
	}
}
