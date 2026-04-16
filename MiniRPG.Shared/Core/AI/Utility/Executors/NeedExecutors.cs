using System;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

public sealed class EatFoodExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
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

		var foodCell = FindNearestGroundFood(state, actor);
		if (foodCell == null) return new ActionExecutionResult();

		if (foodCell.Value.X == actor.X && foodCell.Value.Y == actor.Y)
		{
			var eatResult = NeedActionModule.TryConsumeFood(state, actor, foodCell.Value.EntityId);
			var result = new ActionExecutionResult { Consumed = eatResult.Consumed };
			result.Events.AddRange(eatResult.Events);
			return result;
		}

		return MoveToward(state, actor, foodCell.Value.X, foodCell.Value.Y);
	}

	private static (int X, int Y, string EntityId)? FindNearestGroundFood(GameState state, Actor actor)
	{
		if (state.World == null) return null;
		(int X, int Y, string EntityId)? best = null;
		var bestDist = int.MaxValue;
		var radius = 6;
		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				var d = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (d > radius) continue;
				if (FireSystem.IsDangerousCell(state, x, y, actor.Z)) continue;
				foreach (var entity in state.World.GetEntitiesByType(x, y, actor.Z, CellEntityType.Item))
				{
					var templateId = WorldMap.ResolveGroundItemTemplateId(entity);
					if (!PresetDB.Items.TryGetValue(templateId, out var preset)) continue;
					if (!string.Equals(preset.Category, ItemCategories.Food, StringComparison.Ordinal)
						&& !preset.Tags.ContainsKey("饱腹"))
						continue;
					if (d < bestDist) { bestDist = d; best = (x, y, entity.EntityId); }
				}
			}
		}
		return best;
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

public sealed class DrinkWaterExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		for (var i = 0; i < actor.Inventory.Count; i++)
		{
			var item = actor.Inventory[i];
			if (item.Tags.ContainsKey(ItemTags.Hydration))
			{
				var drinkResult = NeedActionModule.TryConsumeDrink(state, actor, i);
				if (drinkResult.Consumed)
				{
					var result = new ActionExecutionResult { Consumed = true };
					result.Events.AddRange(drinkResult.Events);
					return result;
				}
			}
		}

		var drinkCell = FindNearestGroundDrink(state, actor);
		if (drinkCell == null) return new ActionExecutionResult();

		if (drinkCell.Value.X == actor.X && drinkCell.Value.Y == actor.Y)
		{
			var drinkResult = NeedActionModule.TryConsumeDrink(state, actor, drinkCell.Value.EntityId);
			var result = new ActionExecutionResult { Consumed = drinkResult.Consumed };
			result.Events.AddRange(drinkResult.Events);
			return result;
		}

		return MoveToward(state, actor, drinkCell.Value.X, drinkCell.Value.Y);
	}

	private static (int X, int Y, string EntityId)? FindNearestGroundDrink(GameState state, Actor actor)
	{
		if (state.World == null) return null;
		(int X, int Y, string EntityId)? best = null;
		var bestDist = int.MaxValue;
		var radius = 6;
		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				var d = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (d > radius) continue;
				if (FireSystem.IsDangerousCell(state, x, y, actor.Z)) continue;
				foreach (var entity in state.World.GetEntitiesByType(x, y, actor.Z, CellEntityType.Item))
				{
					var templateId = WorldMap.ResolveGroundItemTemplateId(entity);
					if (!PresetDB.Items.TryGetValue(templateId, out var preset)) continue;
					if (!preset.Tags.ContainsKey(ItemTags.Hydration)) continue;
					if (d < bestDist) { bestDist = d; best = (x, y, entity.EntityId); }
				}
			}
		}
		return best;
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

public sealed class RestSleepExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (actor.HasHomePosition && actor.HomeZ == actor.Z
			&& (actor.X != actor.HomeX || actor.Y != actor.HomeY))
		{
			var moveResult = MoveToward(state, actor, actor.HomeX, actor.HomeY);
			if (moveResult.Consumed) return moveResult;
		}

		if (NeedActionModule.HasBedroll(actor))
		{
			var restResult = NeedActionModule.TryRest(state, actor,
				RestContext.ForNpcBedroll(NeedActionModule.GetBedrollQuality(actor)));
			var result = new ActionExecutionResult { Consumed = restResult.Consumed };
			result.Events.AddRange(restResult.Events);
			return result;
		}

		if (actor.HasHomePosition && actor.X == actor.HomeX && actor.Y == actor.HomeY && actor.Z == actor.HomeZ)
		{
			var restResult = NeedActionModule.TryRest(state, actor, RestContext.ForHomeFloor());
			var result = new ActionExecutionResult { Consumed = restResult.Consumed };
			result.Events.AddRange(restResult.Events);
			return result;
		}

		return new ActionExecutionResult { Consumed = true };
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

public sealed class TendSelfExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		return HealthActionModule.TryTendSelf(state, actor);
	}
}

public sealed class TendOtherExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var target = eval.TargetActor;
		if (target == null || CombatModule.IsDead(target)) return new ActionExecutionResult();
		var dist = Math.Abs(target.X - actor.X) + Math.Abs(target.Y - actor.Y);
		if (dist == 1 && target.Z == actor.Z)
			return HealthActionModule.TryTendOther(state, actor, target);
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
