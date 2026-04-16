using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI.Utility;

public sealed class SelfExtinguishExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (state.World == null) return new ActionExecutionResult();
		return FireSystem.TryExtinguish(state, actor, actor.X, actor.Y, actor.Z);
	}
}

public sealed class FleeFireExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (state.World == null) return new ActionExecutionResult();
		var best = FindSaferCell(state, actor, 6);
		if (best == null) return new ActionExecutionResult();
		return MoveToward(state, actor, best.Value.X, best.Value.Y);
	}

	private static (int X, int Y)? FindSaferCell(GameState state, Actor actor, int radius)
	{
		(int X, int Y)? best = null;
		var bestDanger = GetDangerScore(state, actor.X, actor.Y, actor.Z);
		var bestDist = int.MaxValue;
		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				var dist = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (dist == 0 || dist > radius) continue;
				if (!FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z)) continue;
				if (Pathfinding.NextStep(actor.X, actor.Y, x, y,
					(px, py) => FireSystem.IsSafeWalkableForActor(state, actor, px, py, actor.Z)) == null)
					continue;
				var danger = GetDangerScore(state, x, y, actor.Z);
				if (danger < bestDanger || (danger == bestDanger && dist < bestDist))
				{
					bestDanger = danger;
					bestDist = dist;
					best = (x, y);
				}
			}
		}
		return best;
	}

	private static int GetDangerScore(GameState state, int x, int y, int z)
	{
		var danger = FireSystem.GetFireIntensityAt(state, x, y, z);
		danger += FireSystem.GetFireIntensityAt(state, x, y - 1, z);
		danger += FireSystem.GetFireIntensityAt(state, x, y + 1, z);
		danger += FireSystem.GetFireIntensityAt(state, x - 1, y, z);
		danger += FireSystem.GetFireIntensityAt(state, x + 1, y, z);
		return danger;
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

public sealed class ExtinguishNearbyFireExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (state.World == null) return new ActionExecutionResult();
		var fireCell = FindTargetFireCell(state, actor);
		if (fireCell == null) return new ActionExecutionResult();
		var dist = Math.Abs(fireCell.Value.X - actor.X) + Math.Abs(fireCell.Value.Y - actor.Y);
		if (dist == 1)
			return FireSystem.TryExtinguish(state, actor, fireCell.Value.X, fireCell.Value.Y, actor.Z);
		var staging = FindBestStagingCell(state, actor, fireCell.Value.X, fireCell.Value.Y);
		if (staging == null) return new ActionExecutionResult();
		return MoveToward(state, actor, staging.Value.X, staging.Value.Y);
	}

	private static (int X, int Y)? FindTargetFireCell(GameState state, Actor actor)
	{
		var radius = actor.HasHomePosition && actor.HomeZ == actor.Z
			? Math.Max(1, GameConfig.Fire.AiHomeSearchRadius)
			: Math.Max(1, GameConfig.Fire.AiSearchRadius);
		var cx = actor.HasHomePosition && actor.HomeZ == actor.Z ? actor.HomeX : actor.X;
		var cy = actor.HasHomePosition && actor.HomeZ == actor.Z ? actor.HomeY : actor.Y;
		(int X, int Y)? best = null;
		var bestDist = int.MaxValue;
		for (var y = cy - radius; y <= cy + radius; y++)
		{
			for (var x = cx - radius; x <= cx + radius; x++)
			{
				if (Math.Abs(x - cx) + Math.Abs(y - cy) > radius) continue;
				if (FireSystem.GetFireIntensityAt(state, x, y, actor.Z) <= 0) continue;
				if (FindBestStagingCell(state, actor, x, y) == null) continue;
				var d = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (d < bestDist) { bestDist = d; best = (x, y); }
			}
		}
		return best;
	}

	private static (int X, int Y)? FindBestStagingCell(GameState state, Actor actor, int fx, int fy)
	{
		(int X, int Y)? best = null;
		var bestScore = int.MaxValue;
		foreach (var (dx, dy) in Dirs)
		{
			var x = fx + dx;
			var y = fy + dy;
			if (!FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z)) continue;
			if (Pathfinding.NextStep(actor.X, actor.Y, x, y,
				(px, py) => FireSystem.IsSafeWalkableForActor(state, actor, px, py, actor.Z)) == null
				&& (x != actor.X || y != actor.Y))
				continue;
			var score = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
			if (score < bestScore) { bestScore = score; best = (x, y); }
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

	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];
}

public sealed class EquipWarmClothingExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		Item? best = null;
		var bestWarmth = 0f;
		foreach (var item in actor.Inventory)
		{
			if (item.Equipped || item.PortableWarmthC <= 0f) continue;
			if (item.PortableWarmthC > bestWarmth)
			{
				bestWarmth = item.PortableWarmthC;
				best = item;
			}
		}
		if (best == null) return new ActionExecutionResult();
		var equipResult = InventoryModule.Equip(actor, best, state);
		return new ActionExecutionResult { Consumed = equipResult.Ok };
	}
}

public sealed class SeekCampfireExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (state.World == null) return new ActionExecutionResult();
		var campfire = FindNearestCampfire(state, actor, 8);
		if (campfire == null) return new ActionExecutionResult();
		var dist = Math.Abs(campfire.Value.X - actor.X) + Math.Abs(campfire.Value.Y - actor.Y);
		if (dist <= 1) return new ActionExecutionResult { Consumed = true };
		var step = Pathfinding.NextStep(actor.X, actor.Y, campfire.Value.X, campfire.Value.Y,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null) return new ActionExecutionResult();
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}

	private static (int X, int Y)? FindNearestCampfire(GameState state, Actor actor, int radius)
	{
		if (state.World == null) return null;
		(int X, int Y)? best = null;
		var bestDist = int.MaxValue;
		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				var d = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (d > radius) continue;
				if (!state.World.HasFixture(x, y, actor.Z, Entities.Campfire)) continue;
				if (d < bestDist) { bestDist = d; best = (x, y); }
			}
		}
		return best;
	}
}

public sealed class LightFireExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		var cell = HeatActionModule.FindFirstLightableAdjacentCell(state, actor);
		if (cell == null) return new ActionExecutionResult();
		return ActionModule.TryCastSkill(state, actor, "light_fire", SkillTargetType.Cell,
			targetActor: null, targetLimb: null, targetX: cell.Value.X, targetY: cell.Value.Y, targetZ: actor.Z);
	}
}

public sealed class SeekCoolAreaExecutor : IUtilityExecutor
{
	public ActionExecutionResult Execute(GameState state, Actor actor, Perception perception, UtilityEvalResult eval)
	{
		if (state.World == null) return new ActionExecutionResult();
		var cooler = FindCoolestCell(state, actor, 6);
		if (cooler == null || (cooler.Value.X == actor.X && cooler.Value.Y == actor.Y))
			return new ActionExecutionResult();
		var step = Pathfinding.NextStep(actor.X, actor.Y, cooler.Value.X, cooler.Value.Y,
			(x, y) => FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z));
		if (step == null) return new ActionExecutionResult();
		var result = new ActionExecutionResult();
		result.Events.AddRange(ActionModule.TryMove(state, actor, step.Value.X - actor.X, step.Value.Y - actor.Y));
		result.Consumed = result.Events.Count > 0;
		return result;
	}

	private static (int X, int Y)? FindCoolestCell(GameState state, Actor actor, int radius)
	{
		(int X, int Y)? best = null;
		var bestTemp = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor).AmbientTemperature;
		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				var d = Math.Abs(x - actor.X) + Math.Abs(y - actor.Y);
				if (d > radius) continue;
				if (!FireSystem.IsSafeWalkableForActor(state, actor, x, y, actor.Z)) continue;
				var temp = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor, x, y, actor.Z).AmbientTemperature;
				if (temp < bestTemp - 0.1f) { bestTemp = temp; best = (x, y); }
			}
		}
		return best;
	}
}
