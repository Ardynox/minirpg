using System;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Needs;

public static class NeedBehaviorModule
{
	private const int SearchRadius = 6;

	public static bool HasNearbyThreat(GameState state, Actor actor, int radius = SearchRadius) =>
		HasNearbyThreat(state, actor, behaviorContext: null, radius);

	public static bool HasNearbyThreat(GameState state, Actor actor, AIBehaviorContext? behaviorContext, int radius = SearchRadius)
	{
		if (behaviorContext != null)
			return behaviorContext.HasNearbyThreat(actor, radius);

		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				if (Math.Abs(x - actor.X) + Math.Abs(y - actor.Y) > radius)
					continue;

				foreach (var other in ActorModule.GetAllAt(state, x, y, actor.Z))
				{
					if (other.Id == actor.Id || CombatModule.IsDead(other))
						continue;
					if (!FactionRelation.IsHostile(actor.Faction, other.Faction))
						continue;

					return true;
				}
			}
		}

		return false;
	}

	public static ActionExecutionResult TryExecute(GameState state, Actor actor, Perception perception, bool tickBuffs) =>
		TryExecute(state, actor, perception, tickBuffs, behaviorContext: null);

	public static ActionExecutionResult TryExecute(
		GameState state,
		Actor actor,
		Perception perception,
		bool tickBuffs,
		AIBehaviorContext? behaviorContext)
	{
		var result = new ActionExecutionResult();
		NeedSystem.Sync(actor, state.Turn, result.Events, state);

		var profile = NeedCatalog.GetProfileForActor(actor);
		if (actor.AwarenessState != AwarenessState.Idle || HasNearbyThreat(state, actor, behaviorContext))
			return result;

		if (actor.Needs.TryGetValue(NeedIds.Hunger, out var hunger)
			&& hunger.Current < profile.NpcHungerThreshold)
		{
			var eatResult = TrySatisfyHunger(state, actor);
			Finalize(result, actor, eatResult, tickBuffs);
			if (result.Consumed)
				return result;
		}

		if (actor.Needs.TryGetValue(NeedIds.Rest, out var rest)
			&& rest.Current < profile.NpcRestThreshold)
		{
			var restResult = TrySatisfyRest(state, actor);
			Finalize(result, actor, restResult, tickBuffs);
		}

		return result;
	}

	private static NeedActionResult TrySatisfyHunger(GameState state, Actor actor)
	{
		for (var i = 0; i < actor.Inventory.Count; i++)
		{
			var item = actor.Inventory[i];
			if (item.Category == ItemCategories.Food || item.Tags.ContainsKey("饱腹"))
				return NeedActionModule.TryConsumeFood(state, actor, i);
		}

		var foodCell = FindNearestGroundFood(state, actor);
		if (foodCell == null)
			return new NeedActionResult();

		if (foodCell.Value.X == actor.X && foodCell.Value.Y == actor.Y)
			return NeedActionModule.TryConsumeFood(state, actor, foodCell.Value.EntityId);

		var step = Pathfinding.NextStep(
			actor.X,
			actor.Y,
			foodCell.Value.X,
			foodCell.Value.Y,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null)
			return new NeedActionResult();

		var move = new NeedActionResult();
		move.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		move.Consumed = move.Events.Count > 0;
		return move;
	}

	private static NeedActionResult TrySatisfyRest(GameState state, Actor actor)
	{
		var homeIsReachable = actor.HasHomePosition && actor.HomeZ == actor.Z;
		if (homeIsReachable && (actor.X != actor.HomeX || actor.Y != actor.HomeY))
		{
			var step = Pathfinding.NextStep(
				actor.X,
				actor.Y,
				actor.HomeX,
				actor.HomeY,
				(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
			if (step != null)
			{
				var move = new NeedActionResult();
				move.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
				move.Consumed = move.Events.Count > 0;
				return move;
			}
		}

		if (NeedActionModule.HasBedroll(actor))
			return NeedActionModule.TryRest(state, actor, RestContext.ForNpcBedroll(NeedActionModule.GetBedrollQuality(actor)));

		if (homeIsReachable && actor.X == actor.HomeX && actor.Y == actor.HomeY)
			return NeedActionModule.TryRest(state, actor, RestContext.ForHomeFloor());

		return new NeedActionResult();
	}

	private static (int X, int Y, string EntityId)? FindNearestGroundFood(GameState state, Actor actor)
	{
		if (state.World == null)
			return null;

		(int X, int Y, string EntityId)? best = null;
		var bestDistance = int.MaxValue;
		for (var y = actor.Y - SearchRadius; y <= actor.Y + SearchRadius; y++)
		{
			for (var x = actor.X - SearchRadius; x <= actor.X + SearchRadius; x++)
			{
				var distance = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (distance > SearchRadius)
					continue;
				if (FireSystem.IsDangerousCell(state, x, y, actor.Z))
					continue;

				foreach (var entity in state.World.GetEntitiesByType(x, y, actor.Z, CellEntityType.Item))
				{
					var templateId = WorldMap.ResolveGroundItemTemplateId(entity);
					if (!PresetDB.Items.TryGetValue(templateId, out var preset))
						continue;
					if (!string.Equals(preset.Category, ItemCategories.Food, StringComparison.Ordinal)
						&& !preset.Tags.ContainsKey("饱腹"))
					{
						continue;
					}

					if (distance < bestDistance)
					{
						bestDistance = distance;
						best = (x, y, entity.EntityId);
					}
				}
			}
		}

		return best;
	}

	private static void Finalize(ActionExecutionResult result, Actor actor, NeedActionResult needResult, bool tickBuffs)
	{
		if (needResult.Events.Count > 0)
			result.Events.AddRange(needResult.Events);
		if (!needResult.Consumed)
			return;

		result.Consumed = true;
		if (tickBuffs)
			actor.TickBuffs();
	}
}
