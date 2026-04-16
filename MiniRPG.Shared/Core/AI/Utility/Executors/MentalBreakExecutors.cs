using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

public sealed class MentalBreakBerserkExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		EnsureMentalBreak(actor, state, MentalBreakType.Berserk, 8, 15);

		Actor? nearest = null;
		var bestDist = int.MaxValue;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other) || other.Id == actor.Id) continue;
			var d = AIUtil.Distance3D(actor, other);
			if (d < bestDist) { bestDist = d; nearest = other; }
		}

		if (nearest == null) return new ActionExecutionResult { Consumed = true };

		var skills = CombatModule.GetAttackActions(actor);
		foreach (var skill in skills)
		{
			if (actor.IsSkillOnCooldown(skill.Id)) continue;
			if (ActionModule.CanCastSkill(state, actor, skill.Id, SkillTargetType.Actor, targetActor: nearest))
				return ActionModule.TryCastSkill(state, actor, skill.Id, SkillTargetType.Actor, targetActorId: nearest.Id);
		}

		return MoveToward(state, actor, nearest.X, nearest.Y);
	}

	private static ActionExecutionResult MoveToward(GameState state, Actor actor, int tx, int ty)
	{
		var step = Pathfinding.NextStep(actor.X, actor.Y, tx, ty,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null) return new ActionExecutionResult { Consumed = true };
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = true;
		return result;
	}

	private static void EnsureMentalBreak(Actor actor, GameState state, MentalBreakType type, int minDur, int maxDur)
	{
		if (actor.MentalBreak != null) return;
		var rng = new Random(unchecked(state.Turn * 31 + actor.Id.GetHashCode()));
		actor.MentalBreak = new MentalBreakState
		{
			Type = type,
			StartTurn = state.Turn,
			Duration = rng.Next(minDur, maxDur + 1),
		};
	}
}

public sealed class MentalBreakCatatonicExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (actor.MentalBreak == null)
		{
			var rng = new Random(unchecked(state.Turn * 31 + actor.Id.GetHashCode()));
			actor.MentalBreak = new MentalBreakState
			{
				Type = MentalBreakType.Catatonic,
				StartTurn = state.Turn,
				Duration = rng.Next(15, 31),
			};
		}
		return new ActionExecutionResult { Consumed = true };
	}
}

public sealed class MentalBreakBingeEatExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (actor.MentalBreak == null)
		{
			var rng = new Random(unchecked(state.Turn * 31 + actor.Id.GetHashCode()));
			actor.MentalBreak = new MentalBreakState
			{
				Type = MentalBreakType.BingeEating,
				StartTurn = state.Turn,
				Duration = rng.Next(5, 11),
			};
		}

		for (var i = 0; i < actor.Inventory.Count; i++)
		{
			var item = actor.Inventory[i];
			if (item.Category == ItemCategories.Food || item.Tags.ContainsKey("饱腹"))
			{
				var eatResult = NeedActionModule.TryConsumeFood(state, actor, i);
				if (eatResult.Consumed)
				{
					var result = new ActionExecutionResult { Consumed = true };
					result.Events.AddRange(eatResult.Events);
					return result;
				}
			}
		}

		return new ActionExecutionResult { Consumed = true };
	}
}

public sealed class MentalBreakFleeExecutor : IUtilityExecutor
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (actor.MentalBreak == null)
		{
			var rng = new Random(unchecked(state.Turn * 31 + actor.Id.GetHashCode()));
			actor.MentalBreak = new MentalBreakState
			{
				Type = MentalBreakType.Flee,
				StartTurn = state.Turn,
				Duration = rng.Next(10, 21),
			};
		}

		var avgX = 0f;
		var avgY = 0f;
		var count = 0;
		foreach (var other in perception.NearbyActors)
		{
			if (CombatModule.IsDead(other) || other.Id == actor.Id) continue;
			avgX += other.X;
			avgY += other.Y;
			count++;
		}

		if (count == 0) return new ActionExecutionResult { Consumed = true };

		avgX /= count;
		avgY /= count;

		var bestDist = -1f;
		(int X, int Y)? bestCell = null;
		foreach (var (dx, dy) in Dirs)
		{
			var nx = actor.X + dx;
			var ny = actor.Y + dy;
			if (perception.NearbyWalkable.TryGetValue((nx, ny), out var w) && !w) continue;
			var dist = MathF.Sqrt((nx - avgX) * (nx - avgX) + (ny - avgY) * (ny - avgY));
			if (dist > bestDist) { bestDist = dist; bestCell = (nx, ny); }
		}

		if (bestCell == null) return new ActionExecutionResult { Consumed = true };
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, bestCell.Value.X - actor.X, bestCell.Value.Y - actor.Y));
		result.Consumed = true;
		return result;
	}
}
