using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

/// <summary>
/// 母亲（或父亲）抱着 carriedInfant 时的"喂养 + 安抚"组合动作：
/// 1. 占用一个回合做抚育（消耗 turn）。
/// 2. 如果背包里有食物，消耗 1 份 → 提升 carriedInfant 的 baby_food need
///    （nutrition×20 算法，与 NeedActionModule.ConsumeFood 对齐）。
/// 3. 婴儿不在 state（已死 / 已脱离）→ no-op，让 utility 重新选别的 action。
///
/// 没食物时仍 Consumed=true 占用回合（"安抚但没奶"），让动作分数曲线不
/// 来回切换；UI 看到的状态行（status text）能稳定显示 "(carrying infant)"。
/// </summary>
public sealed class TendInfantExecutor : IUtilityExecutor
{
	public const int DefaultBabyFoodPerFood = 30;

	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		_ = perception;
		_ = eval;

		if (string.IsNullOrWhiteSpace(actor.CarriedInfantId))
			return new ActionExecutionResult();
		if (!state.Actors.TryGetValue(actor.CarriedInfantId!, out var infant))
			return new ActionExecutionResult();

		var result = new ActionExecutionResult { Consumed = true };

		var foodIndex = FindFoodInInventory(actor);
		if (foodIndex < 0)
			return result; // 没食物：仍占用回合做"安抚"，但不喂养。

		var food = actor.Inventory[foodIndex];
		var nutritionPerFood = Math.Max(1, food.Tags.GetValueOrDefault(ItemTags.Nutrition, 1));
		actor.Inventory.RemoveAt(foodIndex);

		NeedSystem.EnsureInitialized(infant, state.Turn);
		var current = NeedSystem.GetNeedValue(infant, NeedIds.BabyFood);
		var nutrition = Math.Min(NeedSystem.NeedMax, nutritionPerFood * 20f);
		NeedSystem.SetNeedValue(infant, NeedIds.BabyFood, current + nutrition);
		NeedSystem.Sync(infant, state.Turn, result.Events, state);

		result.Events.Add(new GameEvent("infant_nursed")
		{
			InitiatorId = actor.Id,
			InitiatorActorName = actor.DisplayName,
			TargetId = infant.Id,
			TargetActorName = infant.DisplayName,
			TargetX = infant.X,
			TargetY = infant.Y,
			TargetZ = infant.Z,
			ItemTypeId = food.Id,
			ItemName = food.Name,
		});
		return result;
	}

	private static int FindFoodInInventory(Actor actor)
	{
		for (var i = 0; i < actor.Inventory.Count; i++)
		{
			var item = actor.Inventory[i];
			if (item.Equipped)
				continue;
			if (string.Equals(item.Category, ItemCategories.Food, StringComparison.Ordinal)
				|| item.Tags.ContainsKey(ItemTags.Nutrition))
				return i;
		}

		return -1;
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
