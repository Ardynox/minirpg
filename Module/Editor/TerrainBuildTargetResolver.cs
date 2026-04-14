using Godot;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Editor;

internal static class TerrainBuildTargetResolver
{
	public const int DefaultColumnScanDepth = 16;

	public static Vector3I? ResolveBuildTargetCell(
		WorldMap? world,
		Vector3I hoverCell,
		bool reverseStack,
		int scanDepth = DefaultColumnScanDepth)
	{
		if (world == null)
			return null;

		var pickedTerrain = world.GetTerrain(hoverCell.X, hoverCell.Y, hoverCell.Z).StringId;
		if (pickedTerrain is Terrains.Air or Terrains.Void)
			return hoverCell;

		var zStep = reverseStack ? 1 : -1;
		var placeZ = hoverCell.Z + zStep;
		for (var scanned = 0; scanned < scanDepth; scanned++, placeZ += zStep)
		{
			var terrain = world.GetTerrain(hoverCell.X, hoverCell.Y, placeZ).StringId;
			if (terrain is Terrains.Air or Terrains.Void)
				return new Vector3I(hoverCell.X, hoverCell.Y, placeZ);
		}

		return null;
	}
}
