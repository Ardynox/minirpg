namespace MiniRPG.Core.World;

public static class BlockPlacementRules
{
	private static readonly (int X, int Y, int Z)[] FaceNeighborOffsets =
	[
		(1, 0, 0),
		(-1, 0, 0),
		(0, 1, 0),
		(0, -1, 0),
		(0, 0, 1),
		(0, 0, -1),
	];

	public static bool HasFaceConnectedTerrain(WorldMap world, int x, int y, int z)
	{
		foreach (var (offsetX, offsetY, offsetZ) in FaceNeighborOffsets)
		{
			if (IsSupportingTerrain(world.GetTerrain(x + offsetX, y + offsetY, z + offsetZ)))
				return true;
		}

		return false;
	}

	public static bool IsSupportingTerrain(TerrainDef terrain) =>
		terrain.StringId is not (Terrains.Air or Terrains.Void);
}
