using System;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Health;

public static class TemperatureBehaviorModule
{
	private const int CampfireSearchRadius = 8;
	private const int CoolSearchRadius = 6;

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
		if (state.World == null)
			return result;
		if (actor.AwarenessState != AwarenessState.Idle || NeedBehaviorModule.HasNearbyThreat(state, actor, behaviorContext))
			return result;

		var exposure = behaviorContext?.GetCurrentExposure(actor) ?? DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);
		if (IsCold(actor, exposure))
		{
			if (TryEquipPortableHeat(actor, state))
			{
				result.Consumed = true;
				Finalize(actor, result, tickBuffs);
				return result;
			}

			var nearbyCampfire = FindNearestCampfire(state, actor, CampfireSearchRadius);
			if (nearbyCampfire is { } campfireTarget)
			{
				var distance = Math.Abs(campfireTarget.X - actor.X) + Math.Abs(campfireTarget.Y - actor.Y);
				if (distance <= 1)
				{
					result.Consumed = true;
					Finalize(actor, result, tickBuffs);
					return result;
				}

				var step = Pathfinding.NextStep(
					actor.X,
					actor.Y,
					campfireTarget.X,
					campfireTarget.Y,
					(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
				if (step != null)
				{
					result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
					result.Consumed = result.Events.Count > 0;
					Finalize(actor, result, tickBuffs);
					return result;
				}
			}

			if (HeatActionModule.FindFirstLightableAdjacentCell(state, actor) is { } fireCell)
			{
				var fireResult = ActionModule.TryCastSkill(
					state,
					actor,
					"light_fire",
					SkillTargetType.Cell,
					targetActor: null,
					targetLimb: null,
					targetX: fireCell.X,
					targetY: fireCell.Y,
					targetZ: actor.Z);
				if (fireResult.Consumed)
				{
					Finalize(actor, fireResult, tickBuffs);
					return fireResult;
				}
			}
		}

		if (IsHot(actor, exposure) && FindCoolestCell(state, actor, CoolSearchRadius) is { } coolerCell)
		{
			if (coolerCell.X != actor.X || coolerCell.Y != actor.Y)
			{
				var step = Pathfinding.NextStep(
					actor.X,
					actor.Y,
					coolerCell.X,
					coolerCell.Y,
					(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
				if (step != null)
				{
					result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
					result.Consumed = result.Events.Count > 0;
					Finalize(actor, result, tickBuffs);
				}
			}
		}

		return result;
	}

	private static bool TryEquipPortableHeat(Actor actor, GameState state)
	{
		var best = actor.Inventory
			.Where(item => !item.Equipped && item.PortableWarmthC > 0f)
			.OrderByDescending(item => item.PortableWarmthC)
			.ThenByDescending(item => item.PortableDryingBonus)
			.ThenBy(item => item.Id, StringComparer.Ordinal)
			.FirstOrDefault();
		if (best == null)
			return false;

		var equipResult = InventoryModule.Equip(actor, best, state);
		return equipResult.Ok;
	}

	private static (int X, int Y)? FindNearestCampfire(GameState state, Actor actor, int radius)
	{
		if (state.World == null)
			return null;

		(int X, int Y)? best = null;
		var bestDistance = int.MaxValue;
		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				var distance = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (distance > radius)
					continue;
				if (!state.World.HasFixture(x, y, actor.Z, Entities.Campfire))
					continue;
				if (Pathfinding.NextStep(actor.X, actor.Y, x, y, (px, py) => FireSystem.IsSafeWalkableForActor(state, actor, px, py, actor.Z)) == null
					&& (x != actor.X || y != actor.Y))
				{
					continue;
				}

				if (distance < bestDistance)
				{
					bestDistance = distance;
					best = (x, y);
				}
			}
		}

		return best;
	}

	private static (int X, int Y)? FindCoolestCell(GameState state, Actor actor, int radius)
	{
		if (state.World == null)
			return null;

		(int X, int Y)? best = (actor.X, actor.Y);
		var bestAmbient = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor).AmbientTemperature;
		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				var distance = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (distance > radius)
					continue;
				if (!FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z))
					continue;
				if (Pathfinding.NextStep(actor.X, actor.Y, x, y, (px, py) => FireSystem.IsSafeWalkableForActor(state, actor, px, py, actor.Z)) == null
					&& (x != actor.X || y != actor.Y))
				{
					continue;
				}

				var ambient = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor, x, y, actor.Z).AmbientTemperature;
				if (ambient < bestAmbient - 0.1f)
				{
					bestAmbient = ambient;
					best = (x, y);
				}
			}
		}

		return best;
	}

	private static bool IsCold(Actor actor, EnvironmentExposureSnapshot exposure)
	{
		return exposure.AmbientTemperature <= 2f
			|| actor.HealthConditions.Any(condition => string.Equals(condition.Id, HealthConditionIds.Hypothermia, StringComparison.Ordinal))
			|| (actor.WetnessValue >= 60f && exposure.AmbientTemperature <= 8f);
	}

	private static bool IsHot(Actor actor, EnvironmentExposureSnapshot exposure)
	{
		return exposure.AmbientTemperature >= 34f
			|| actor.HealthConditions.Any(condition => string.Equals(condition.Id, HealthConditionIds.Heatstroke, StringComparison.Ordinal));
	}

	private static void Finalize(Actor actor, ActionExecutionResult result, bool tickBuffs)
	{
		if (tickBuffs && result.Consumed)
			actor.TickBuffs();
	}
}
