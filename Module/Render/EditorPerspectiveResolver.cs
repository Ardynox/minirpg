using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

internal static class EditorPerspectiveResolver
{
	internal const float SoftFadeAlpha = 0.30f;
	private const int GeometryFallbackRadius = 2;

	public static EditorPerspectiveResult Resolve(
		GameState state,
		int viewCenterX,
		int viewCenterY,
		int targetZ,
		int zMin,
		int zMax,
		WorldCoord? hoverCell = null)
	{
		var focusCell = hoverCell is { } hover
			? new WorldCoord(hover.X, hover.Y, targetZ)
			: new WorldCoord(viewCenterX, viewCenterY, targetZ);

		if (state.World == null || zMin > zMax)
			return new EditorPerspectiveResult(focusCell, false, new HashSet<WorldCoord>());

		var room = RoomContextAnalyzer.AnalyzeRoom(state, focusCell.X, focusCell.Y, focusCell.Z);
		var fadedCells = room.IsIndoors
			? CollectIndoorFadeCells(state.World, room, targetZ, zMin)
			: CollectGeometryFallbackCells(state.World, focusCell, targetZ, zMin);

		if (hoverCell is { } hc)
			AddSameLayerForegroundOccluders(state.World, hc.X, hc.Y, targetZ, fadedCells);

		return new EditorPerspectiveResult(focusCell, room.IsIndoors, fadedCells);
	}

	private static HashSet<WorldCoord> CollectIndoorFadeCells(WorldMap world, RoomSnapshot room, int targetZ, int zMin)
	{
		var fadedCells = new HashSet<WorldCoord>();
		for (var index = 0; index < room.Cells.Count; index++)
		{
			var cell = room.Cells[index];
			// RoomContextAnalyzer includes blocking boundary tiles in Cells; perspective anchors should only use interior walkable cells.
			if (world.IsSolid(cell.X, cell.Y, targetZ))
				continue;

			AddOverheadOccluders(world, cell.X, cell.Y, targetZ, zMin, fadedCells);
			AddBoundaryWallColumn(world, cell.X + 1, cell.Y, targetZ, zMin, fadedCells);
			AddBoundaryWallColumn(world, cell.X, cell.Y + 1, targetZ, zMin, fadedCells);
		}

		return fadedCells;
	}

	private static HashSet<WorldCoord> CollectGeometryFallbackCells(WorldMap world, WorldCoord focusCell, int targetZ, int zMin)
	{
		var fadedCells = new HashSet<WorldCoord>();
		for (var dx = 0; dx <= GeometryFallbackRadius; dx++)
		for (var dy = 0; dy <= GeometryFallbackRadius; dy++)
		{
			if (Math.Max(dx, dy) > GeometryFallbackRadius)
				continue;

			var wx = focusCell.X + dx;
			var wy = focusCell.Y + dy;
			for (var wz = targetZ; wz >= zMin; wz--)
			{
				var terrain = world.GetTerrain(wx, wy, wz);
				if (!terrain.IsOpaque)
					continue;

				if (!HasForegroundExposure(world, wx, wy, wz))
					continue;

				fadedCells.Add(new WorldCoord(wx, wy, wz));
			}
		}

		return fadedCells;
	}

	private static void AddOverheadOccluders(
		WorldMap world,
		int worldX,
		int worldY,
		int targetZ,
		int zMin,
		HashSet<WorldCoord> fadedCells)
	{
		for (var wz = targetZ - 1; wz >= zMin; wz--)
		{
			if (!world.GetTerrain(worldX, worldY, wz).IsOpaque)
				continue;

			fadedCells.Add(new WorldCoord(worldX, worldY, wz));
		}
	}

	private static void AddBoundaryWallColumn(
		WorldMap world,
		int worldX,
		int worldY,
		int targetZ,
		int zMin,
		HashSet<WorldCoord> fadedCells)
	{
		if (!world.IsSolid(worldX, worldY, targetZ))
			return;

		for (var wz = targetZ; wz >= zMin; wz--)
		{
			if (!world.GetTerrain(worldX, worldY, wz).IsOpaque)
				break;

			fadedCells.Add(new WorldCoord(worldX, worldY, wz));
		}
	}

	private static bool HasForegroundExposure(WorldMap world, int worldX, int worldY, int worldZ)
	{
		return !world.GetTerrain(worldX + 1, worldY, worldZ).IsOpaque
			|| !world.GetTerrain(worldX, worldY + 1, worldZ).IsOpaque;
	}

	private static void AddSameLayerForegroundOccluders(
		WorldMap world,
		int hoverX,
		int hoverY,
		int targetZ,
		HashSet<WorldCoord> fadedCells)
	{
		const int radius = 2;
		for (var dx = 0; dx <= radius; dx++)
		for (var dy = 0; dy <= radius; dy++)
		{
			if (dx == 0 && dy == 0)
				continue;

			var wx = hoverX + dx;
			var wy = hoverY + dy;
			if (world.GetTerrain(wx, wy, targetZ).IsOpaque)
				fadedCells.Add(new WorldCoord(wx, wy, targetZ));
		}
	}
}

internal readonly record struct EditorPerspectiveResult(
	WorldCoord FocusCell,
	bool IsIndoors,
	IReadOnlySet<WorldCoord> FadedCells)
{
	public static EditorPerspectiveResult Empty =>
		new(default, false, new HashSet<WorldCoord>());

	public bool ShouldFade(int worldX, int worldY, int worldZ) =>
		FadedCells.Contains(new WorldCoord(worldX, worldY, worldZ));
}
