using System;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

/// <summary>
/// 母亲（或父亲）抱着 carriedInfant 时专心待命：消耗一个回合做"抚育"动作。
/// 等价于 IdleExecutor 的语义但 action id / 名字独立，让玩家在 inspect 看到
/// "她在抚育婴儿"而不是 idle / wander。
/// </summary>
public sealed class TendInfantExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		_ = state;
		_ = perception;
		_ = eval;
		// 真实"喂养"行为（消耗食物 / 提升婴儿 BabyFood need）等 NeedSystem.BabyFood
		// 系统接通后再补；现在 Phase 6 之后只有 carry 状态没有 need，先以"占用回合"
		// 为信号让 UI / 玩家观察到母亲处于抚育状态。
		_ = actor;
		return new ActionExecutionResult { Consumed = true };
	}
}

/// <summary>
/// 配偶不在身边时，朝 mate 方向走一步。等价于 FollowLeader 的 mate 版本——
/// 让"两口子"在地图上看起来真的会聚到一起，给玩家"涌现关系"的视觉信号。
/// 不在同一 z 或者 mate 缺席 → no-op。
/// </summary>
public sealed class CourtMateExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		_ = perception;
		_ = eval;
		if (string.IsNullOrWhiteSpace(actor.MateActorId))
			return new ActionExecutionResult();
		if (!state.Actors.TryGetValue(actor.MateActorId!, out var mate))
			return new ActionExecutionResult();
		if (mate.Z != actor.Z)
			return new ActionExecutionResult();

		var dist = Math.Abs(actor.X - mate.X) + Math.Abs(actor.Y - mate.Y);
		if (dist <= 1)
			return new ActionExecutionResult { Consumed = true };

		var step = Pathfinding.NextStep(actor.X, actor.Y, mate.X, mate.Y,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null)
			return new ActionExecutionResult();

		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}
}
