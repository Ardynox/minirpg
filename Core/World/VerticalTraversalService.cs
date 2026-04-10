using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

/// <summary>
/// Shared vertical traversal rules for player, AI, and gravity.
/// Keeps continuous-space semantics consistent across systems.
/// </summary>
public static class VerticalTraversalService
{
	public static bool CanClimb(WorldMap world, int x, int y, int z, bool goDown) =>
		world.CanTraverseVertical(x, y, z, goDown);

	public static bool TryMoveActorVertical(GameState state, Actor actor, bool goDown)
	{
		if (state.World == null)
			return false;

		if (!CanClimb(state.World, actor.X, actor.Y, actor.Z, goDown))
			return false;

		var oldZ = actor.Z;
		actor.Z += goDown ? 1 : -1;
		if (string.Equals(actor.Id, state.PlayerId, System.StringComparison.Ordinal))
			state.PlayerZ = actor.Z;
		state.World.UpdateActorChunk(actor, actor.X, actor.Y, oldZ);
		return true;
	}

	public static int ApplyGravity(GameState state, Actor actor, int maxFallPerStep)
	{
		if (state.World == null)
			return 0;

		var world = state.World;
		var x = actor.X;
		var y = actor.Y;
		var z = actor.Z;
		var fellLayers = 0;
		var cappedMax = System.Math.Clamp(maxFallPerStep, 1, 128);

		while (fellLayers < cappedMax)
		{
			var belowZ = z + 1;
			if (world.HasVerticalAnchor(x, y, z, goDown: true)
				|| world.HasVerticalAnchor(x, y, belowZ, goDown: false))
			{
				break;
			}

			if (!world.IsWalkable(x, y, belowZ))
				break;

			z = belowZ;
			fellLayers++;
		}

		if (fellLayers <= 0)
			return 0;

		var oldZ = actor.Z;
		actor.Z = z;
		if (string.Equals(actor.Id, state.PlayerId, System.StringComparison.Ordinal))
			state.PlayerZ = z;
		world.UpdateActorChunk(actor, x, y, oldZ);
		return fellLayers;
	}
}
