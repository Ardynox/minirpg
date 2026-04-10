using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI;

public static class FireBehaviorModule
{
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

		var selfExtinguish = TrySelfExtinguish(state, actor);
		Finalize(result, actor, selfExtinguish, tickBuffs);
		if (result.Consumed)
			return result;

		var fleeFire = TryFleeFire(state, actor);
		Finalize(result, actor, fleeFire, tickBuffs);
		if (result.Consumed)
			return result;

		if (actor.AwarenessState != AwarenessState.Idle || NeedBehaviorModule.HasNearbyThreat(state, actor, behaviorContext))
			return result;

		var extinguishNearby = TryHandleNearbyFire(state, actor);
		Finalize(result, actor, extinguishNearby, tickBuffs);
		return result;
	}

	private static ActionExecutionResult TrySelfExtinguish(GameState state, Actor actor)
	{
		if (!actor.HealthConditions.Any(condition => string.Equals(condition.Id, HealthConditionIds.OnFire, StringComparison.Ordinal)))
			return new ActionExecutionResult();

		return FireSystem.TryExtinguish(state, actor, actor.X, actor.Y, actor.Z);
	}

	private static ActionExecutionResult TryFleeFire(GameState state, Actor actor)
	{
		var currentDanger = GetDangerScore(state, actor.X, actor.Y, actor.Z);
		if (currentDanger <= 0)
			return new ActionExecutionResult();

		var bestSafeCell = FindSaferCell(state, actor, searchRadius: 6);
		if (bestSafeCell == null)
			return new ActionExecutionResult();

		var step = Pathfinding.NextStep(
			actor.X,
			actor.Y,
			bestSafeCell.Value.X,
			bestSafeCell.Value.Y,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null)
			return new ActionExecutionResult();

		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}

	private static ActionExecutionResult TryHandleNearbyFire(GameState state, Actor actor)
	{
		var fireCell = FindTargetFireCell(state, actor);
		if (fireCell == null)
			return new ActionExecutionResult();

		var distance = Math.Abs(fireCell.Value.X - actor.X) + Math.Abs(fireCell.Value.Y - actor.Y);
		if (distance == 1)
			return FireSystem.TryExtinguish(state, actor, fireCell.Value.X, fireCell.Value.Y, actor.Z);

		var stagingCell = FindBestExtinguishStagingCell(state, actor, fireCell.Value.X, fireCell.Value.Y);
		if (stagingCell == null)
			return new ActionExecutionResult();

		var step = Pathfinding.NextStep(
			actor.X,
			actor.Y,
			stagingCell.Value.X,
			stagingCell.Value.Y,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null)
			return new ActionExecutionResult();

		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}

	private static (int X, int Y)? FindTargetFireCell(GameState state, Actor actor)
	{
		var searchRadius = actor.HasHomePosition && actor.HomeZ == actor.Z
			? Math.Max(1, GameConfig.Fire.AiHomeSearchRadius)
			: Math.Max(1, GameConfig.Fire.AiSearchRadius);
		var centerX = actor.HasHomePosition && actor.HomeZ == actor.Z ? actor.HomeX : actor.X;
		var centerY = actor.HasHomePosition && actor.HomeZ == actor.Z ? actor.HomeY : actor.Y;

		(int X, int Y)? best = null;
		var bestDistance = int.MaxValue;
		for (var y = centerY - searchRadius; y <= centerY + searchRadius; y++)
		{
			for (var x = centerX - searchRadius; x <= centerX + searchRadius; x++)
			{
				var distanceFromCenter = Math.Abs(x - centerX) + Math.Abs(y - centerY);
				if (distanceFromCenter > searchRadius || FireSystem.GetFireIntensityAt(state, x, y, actor.Z) <= 0)
					continue;

				var distanceToActor = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				var staging = FindBestExtinguishStagingCell(state, actor, x, y);
				if (staging == null)
					continue;

				if (distanceToActor < bestDistance)
				{
					bestDistance = distanceToActor;
					best = (x, y);
				}
			}
		}

		return best;
	}

	private static (int X, int Y)? FindBestExtinguishStagingCell(GameState state, Actor actor, int fireX, int fireY)
	{
		(int X, int Y)? best = null;
		var bestScore = int.MaxValue;
		foreach (var (dx, dy) in CardinalDirs)
		{
			var x = fireX + dx;
			var y = fireY + dy;
			if (!FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z))
				continue;

			var step = Pathfinding.NextStep(
				actor.X,
				actor.Y,
				x,
				y,
				(px, py) => FireSystem.IsSafeWalkableForActor(state, actor, px, py, actor.Z));
			if (step == null && (x != actor.X || y != actor.Y))
				continue;

			var score = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
			if (score < bestScore)
			{
				bestScore = score;
				best = (x, y);
			}
		}

		return best;
	}

	private static (int X, int Y)? FindSaferCell(GameState state, Actor actor, int searchRadius)
	{
		(int X, int Y)? best = null;
		var bestDanger = GetDangerScore(state, actor.X, actor.Y, actor.Z);
		var bestDistance = int.MaxValue;
		for (var y = actor.Y - searchRadius; y <= actor.Y + searchRadius; y++)
		{
			for (var x = actor.X - searchRadius; x <= actor.X + searchRadius; x++)
			{
				var distance = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (distance == 0 || distance > searchRadius)
					continue;
				if (!FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z))
					continue;

				var step = Pathfinding.NextStep(
					actor.X,
					actor.Y,
					x,
					y,
					(px, py) => FireSystem.IsSafeWalkableForActor(state, actor, px, py, actor.Z));
				if (step == null)
					continue;

				var danger = GetDangerScore(state, x, y, actor.Z);
				if (danger < bestDanger || (danger == bestDanger && distance < bestDistance))
				{
					bestDanger = danger;
					bestDistance = distance;
					best = (x, y);
				}
			}
		}

		return best;
	}

	private static int GetDangerScore(GameState state, int x, int y, int z)
	{
		var danger = FireSystem.GetFireIntensityAt(state, x, y, z);
		foreach (var (dx, dy) in CardinalDirs)
			danger += FireSystem.GetFireIntensityAt(state, x + dx, y + dy, z);
		return danger;
	}

	private static void Finalize(ActionExecutionResult result, Actor actor, ActionExecutionResult fireResult, bool tickBuffs)
	{
		if (fireResult.Events.Count > 0)
			result.Events.AddRange(fireResult.Events);
		if (!fireResult.Consumed)
			return;

		result.Consumed = true;
		if (tickBuffs)
			actor.TickBuffs();
	}

	private static readonly (int Dx, int Dy)[] CardinalDirs =
	[
		(0, -1),
		(0, 1),
		(-1, 0),
		(1, 0),
	];
}
