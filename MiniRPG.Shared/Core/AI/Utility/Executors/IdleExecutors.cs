using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

public sealed class FollowLeaderExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var leader = eval.TargetActor;
		if (leader == null) return new ActionExecutionResult();
		var dist = Math.Abs(actor.X - leader.X) + Math.Abs(actor.Y - leader.Y);
		if (dist <= 2 && actor.Z == leader.Z)
			return new ActionExecutionResult { Consumed = true };
		return MoveToward(state, actor, leader.X, leader.Y);
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

public sealed class ReturnHomeExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (!actor.HasHomePosition || actor.HomeZ != actor.Z)
			return new ActionExecutionResult();
		if (actor.X == actor.HomeX && actor.Y == actor.HomeY)
			return new ActionExecutionResult { Consumed = true };
		return MoveToward(state, actor, actor.HomeX, actor.HomeY);
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

public sealed class WanderExecutor : IUtilityExecutor
{
	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var rng = new Random(unchecked(state.Turn * 17 + actor.Id.GetHashCode()));
		var candidates = new List<(int X, int Y)>();
		foreach (var (dx, dy) in Dirs)
		{
			var nx = actor.X + dx;
			var ny = actor.Y + dy;
			if (IsWalkable(perception, state, actor, nx, ny))
				candidates.Add((nx, ny));
		}
		if (candidates.Count == 0) return new ActionExecutionResult { Consumed = true };
		var target = candidates[rng.Next(candidates.Count)];
		var result = new ActionExecutionResult { Consumed = true };
		result.Events.AddRange(ActionModule.TryMove(state, actor, target.X - actor.X, target.Y - actor.Y));
		return result;
	}

	private static bool IsWalkable(Perception perception, GameState state, Actor actor, int x, int y)
	{
		if (perception.NearbyWalkable.TryGetValue((x, y), out var w)) return w;
		return FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z);
	}
}

public sealed class IdleExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval) =>
		new() { Consumed = true };
}
