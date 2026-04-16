using System;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

/// <summary>
/// Centralized voxel terrain shading constants and classification helpers
/// (wall-like terrain recognition, per-face edge strength, per-face side
/// darken factors). Consolidates the previously duplicated copies that
/// lived in <c>IsometricVoxelRenderer</c>, <c>TerrainAtlas</c> and
/// <c>VoxelTilePreviewTool</c>.
/// </summary>
/// <remarks>
/// The constants drive the look of voxel tiles; any tuning here affects
/// runtime rendering, atlas baking and the in-editor preview tool at the
/// same time. Keep the surface minimal so callers have one place to read
/// and one place to tweak.
/// </remarks>
public static class VoxelTerrainShading
{
	public const float LeftSideDarken = 0.65f;
	public const float RightSideDarken = 0.80f;
	public const float WallLeftSideDarken = 0.52f;
	public const float WallRightSideDarken = 0.68f;

	public const float WallTopEdgeStrength = 0.30f;
	public const float SolidTopEdgeStrength = 0.22f;
	public const float NonSolidTopEdgeStrength = 0.12f;
	public const float WallSideEdgeStrength = 0.20f;
	public const float SolidSideEdgeStrength = 0.12f;
	public const float NonSolidSideEdgeStrength = 0.06f;

	/// <summary>
	/// True when the terrain should be rendered as a "wall-like" block,
	/// which drives a stronger edge stroke and a darker side face.
	/// </summary>
	public static bool IsWall(TerrainDef terrain)
	{
		return terrain.StringId.StartsWith("wall_", StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Stone, StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Dirt, StringComparison.OrdinalIgnoreCase)
			|| terrain.StringId.Equals(Terrains.Mountain, StringComparison.OrdinalIgnoreCase);
	}

	public static float TopEdgeStrength(TerrainDef terrain)
	{
		if (IsWall(terrain))
			return WallTopEdgeStrength;
		return terrain.Solid ? SolidTopEdgeStrength : NonSolidTopEdgeStrength;
	}

	public static float SideEdgeStrength(TerrainDef terrain)
	{
		if (IsWall(terrain))
			return WallSideEdgeStrength;
		return terrain.Solid ? SolidSideEdgeStrength : NonSolidSideEdgeStrength;
	}

	public static float LeftDarken(TerrainDef terrain) =>
		IsWall(terrain) ? WallLeftSideDarken : LeftSideDarken;

	public static float RightDarken(TerrainDef terrain) =>
		IsWall(terrain) ? WallRightSideDarken : RightSideDarken;
}
