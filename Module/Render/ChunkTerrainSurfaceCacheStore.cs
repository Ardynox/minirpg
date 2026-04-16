using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

/// <summary>One surface cell harvested for a chunk: world-space position, screen-space position, sort key and face-draw flags.</summary>
internal readonly record struct TerrainSurfaceEntry(
	int WorldX,
	int WorldY,
	int WorldZ,
	Vector2 ScreenPos,
	long SortKey,
	TerrainDef Terrain,
	bool DrawTop,
	bool DrawLeftSide,
	bool DrawRightSide,
	bool ShadowTop);

/// <summary>Per-chunk render data keyed by <c>TerrainGeometryRevision</c>.</summary>
internal readonly record struct ChunkTerrainSurfaceCache(
	int TerrainGeometryRevision,
	TerrainSurfaceEntry[] Entries,
	int EmptyCellCount,
	int OccludedCellCount,
	int HiddenFaceCellCount);

/// <summary>
/// Owns the per-chunk surface-cell caches consumed by the terrain draw
/// pass. <see cref="IsometricVoxelRenderer"/> only asks for the entries
/// that belong to the currently visible chunks and prunes stale entries
/// on frame end; the actual geometry analysis (opacity / face-visibility
/// / hidden-cell accounting) lives here.
/// </summary>
internal sealed class ChunkTerrainSurfaceCacheStore
{
	private readonly Dictionary<ChunkCoord, ChunkTerrainSurfaceCache> _cache = new();
	private readonly List<ChunkCoord> _pruneBuffer = [];

	public ChunkTerrainSurfaceCache GetOrBuild(WorldMap world, ChunkData chunk, out bool rebuiltThisFrame)
	{
		if (_cache.TryGetValue(chunk.Coord, out var existing)
			&& existing.TerrainGeometryRevision == chunk.TerrainGeometryRevision)
		{
			rebuiltThisFrame = false;
			return existing;
		}

		var rebuilt = BuildCache(world, chunk);
		_cache[chunk.Coord] = rebuilt;
		rebuiltThisFrame = true;
		return rebuilt;
	}

	public void Prune(IReadOnlyDictionary<ChunkCoord, ChunkData> loadedChunks)
	{
		_pruneBuffer.Clear();
		foreach (var coord in _cache.Keys)
		{
			if (!loadedChunks.ContainsKey(coord))
				_pruneBuffer.Add(coord);
		}

		for (var i = 0; i < _pruneBuffer.Count; i++)
			_cache.Remove(_pruneBuffer[i]);
	}

	private static ChunkTerrainSurfaceCache BuildCache(WorldMap world, ChunkData chunk)
	{
		var entries = BuildEntries(
			world,
			chunk,
			out var emptyCellCount,
			out var occludedCellCount,
			out var hiddenFaceCellCount);
		return new ChunkTerrainSurfaceCache(
			chunk.TerrainGeometryRevision,
			entries,
			emptyCellCount,
			occludedCellCount,
			hiddenFaceCellCount);
	}

	internal static TerrainSurfaceEntry[] BuildEntries(
		WorldMap world,
		ChunkData chunk,
		out int emptyCellCount,
		out int occludedCellCount,
		out int hiddenFaceCellCount)
	{
		var airId = TerrainRegistry.GetId(Terrains.Air);
		var voidId = TerrainRegistry.GetId(Terrains.Void);
		var baseX = chunk.Coord.Cx * ChunkData.Size;
		var baseY = chunk.Coord.Cy * ChunkData.Size;
		var entries = new List<TerrainSurfaceEntry>(ChunkData.Area);
		ChunkData? aboveChunk = null;
		ChunkData? eastChunk = null;
		ChunkData? southChunk = null;
		var neighborsResolved = false;

		emptyCellCount = 0;
		occludedCellCount = 0;
		hiddenFaceCellCount = 0;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var index = CoordUtil.LocalIndex(lx, ly);
			var terrainId = chunk.TerrainIds[index];
			if (terrainId == airId || terrainId == voidId)
			{
				emptyCellCount++;
				continue;
			}

			if (!neighborsResolved)
			{
				aboveChunk = world.Chunks.GetOrLoad(new ChunkCoord(chunk.Coord.Cx, chunk.Coord.Cy, chunk.Coord.Cz - 1));
				eastChunk = world.Chunks.GetOrLoad(new ChunkCoord(chunk.Coord.Cx + 1, chunk.Coord.Cy, chunk.Coord.Cz));
				southChunk = world.Chunks.GetOrLoad(new ChunkCoord(chunk.Coord.Cx, chunk.Coord.Cy + 1, chunk.Coord.Cz));
				neighborsResolved = true;
			}

			var topOpaque = IsOpaque(aboveChunk!, lx, ly);
			var southOpaque = ly + 1 < ChunkData.Size
				? TerrainRegistry.Get(chunk.TerrainIds[CoordUtil.LocalIndex(lx, ly + 1)]).IsOpaque
				: IsOpaque(southChunk!, lx, 0);
			var eastOpaque = lx + 1 < ChunkData.Size
				? TerrainRegistry.Get(chunk.TerrainIds[CoordUtil.LocalIndex(lx + 1, ly)]).IsOpaque
				: IsOpaque(eastChunk!, 0, ly);
			if (topOpaque && southOpaque && eastOpaque)
			{
				occludedCellCount++;
				continue;
			}

			var drawTop = !topOpaque;
			var drawLeft = !southOpaque;
			var drawRight = !eastOpaque;
			var shadowTop = false;
			if (topOpaque && (drawLeft || drawRight))
			{
				drawTop = true;
				shadowTop = true;
			}

			if (!drawTop && !drawLeft && !drawRight)
			{
				hiddenFaceCellCount++;
				continue;
			}

			var worldX = baseX + lx;
			var worldY = baseY + ly;
			var worldZ = chunk.Coord.Cz;
			entries.Add(new TerrainSurfaceEntry(
				worldX,
				worldY,
				worldZ,
				IsoCoordUtil.WorldToScreen(worldX, worldY, worldZ),
				IsoCoordUtil.SortKey(worldX, worldY, worldZ),
				TerrainRegistry.Get(terrainId),
				drawTop,
				drawLeft,
				drawRight,
				shadowTop));
		}

		return [.. entries];
	}

	private static bool IsOpaque(ChunkData chunk, int lx, int ly) =>
		TerrainRegistry.Get(chunk.TerrainIds[CoordUtil.LocalIndex(lx, ly)]).IsOpaque;
}
