using System;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.Social;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

public sealed class RescueAllyFireExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var target = eval.TargetActor;
		if (target == null || CombatModule.IsDead(target)) return new ActionExecutionResult();
		var dist = Math.Abs(target.X - actor.X) + Math.Abs(target.Y - actor.Y);
		if (dist == 1 && target.Z == actor.Z)
			return FireSystem.TryExtinguish(state, actor, target.X, target.Y, actor.Z);
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

public sealed class SocialInteractExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var target = eval.TargetActor;
		if (target == null || CombatModule.IsDead(target)) return new ActionExecutionResult();
		var dist = Math.Abs(target.X - actor.X) + Math.Abs(target.Y - actor.Y);
		if (dist <= 1 && target.Z == actor.Z)
		{
			var events = SocialModule.TryInteract(state, actor.Id, target.Id);
			var result = new ActionExecutionResult { Consumed = events.Count > 0 };
			result.Events.AddRange(events);
			return result;
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
