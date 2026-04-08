using System;
using System.Linq;
using MiniRPG.Core.Combat;

namespace MiniRPG.Core.Health;

public static class HeatActionModule
{
	private static readonly (int Dx, int Dy)[] AdjacentDirs =
	[
		(0, -1),
		(0, 1),
		(-1, 0),
		(1, 0),
	];

	public static bool CanLightFireAt(
		GameState state,
		Actor actor,
		int targetX,
		int targetY,
		int targetZ,
		out SkillCastFailureReason failureReason)
	{
		failureReason = SkillCastFailureReason.InvalidTarget;
		if (state.World == null || actor.Z != targetZ)
			return false;

		if (Math.Abs(actor.X - targetX) + Math.Abs(actor.Y - targetY) != 1)
			return false;

		if (FindWoodIndex(actor) < 0)
			return false;

		var terrain = state.World.GetTerrain(targetX, targetY, targetZ);
		if (!state.World.IsWalkable(targetX, targetY, targetZ))
		{
			failureReason = SkillCastFailureReason.InvalidTerrain;
			return false;
		}

		if (terrain.StringId is Terrains.Water or Terrains.Swamp or Terrains.Marsh or Terrains.Lava)
		{
			failureReason = SkillCastFailureReason.InvalidTerrain;
			return false;
		}

		if (state.Actors.Values.Any(other => other.X == targetX && other.Y == targetY && other.Z == targetZ))
			return false;

		if (state.World.GetEntitiesByType(targetX, targetY, targetZ, CellEntityType.Fixture).Count > 0)
			return false;

		return true;
	}

	public static ActionExecutionResult TryLightFire(GameState state, Actor actor, int targetX, int targetY, int targetZ)
	{
		var result = new ActionExecutionResult();
		if (!CanLightFireAt(state, actor, targetX, targetY, targetZ, out _))
			return result;

		var woodIndex = FindWoodIndex(actor);
		if (woodIndex < 0 || state.World == null)
			return result;

		InventoryModule.RemoveAt(actor, woodIndex);
		state.World.SetFixture(targetX, targetY, targetZ, "*", Entities.Campfire);

		var litEvent = new GameEvent("campfire_lit")
		{
			TargetX = targetX,
			TargetY = targetY,
			TargetZ = targetZ,
			ActionName = Entities.Campfire,
		};
		IdentificationModule.PopulateInitiatorIdentity(litEvent, state, actor);
		result.Events.Add(litEvent);
		result.Consumed = true;
		return result;
	}

	public static (int X, int Y)? FindFirstLightableAdjacentCell(GameState state, Actor actor)
	{
		foreach (var (dx, dy) in AdjacentDirs)
		{
			var x = actor.X + dx;
			var y = actor.Y + dy;
			if (CanLightFireAt(state, actor, x, y, actor.Z, out _))
				return (x, y);
		}

		return null;
	}

	private static int FindWoodIndex(Actor actor)
	{
		for (var i = 0; i < actor.Inventory.Count; i++)
		{
			if (string.Equals(actor.Inventory[i].Id, "mat_wood", StringComparison.Ordinal))
				return i;
		}

		return -1;
	}
}
