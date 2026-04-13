using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Render;

/// <summary>
/// Computes per-cell light contributions from nearby point light sources
/// (campfires, fire, lava, etc.) with distance attenuation and color blending.
/// Rebuilt each render frame for the visible region.
/// </summary>
public sealed class LightMap
{
	private readonly struct PointLight
	{
		public readonly int X, Y, Z;
		public readonly float Radius;
		public readonly float Intensity;
		public readonly float R, G, B;

		public PointLight(int x, int y, int z, float radius, float intensity, float r, float g, float b)
		{
			X = x; Y = y; Z = z;
			Radius = radius;
			Intensity = intensity;
			R = r; G = g; B = b;
		}
	}

	public readonly struct CellLight
	{
		public readonly float Intensity;
		public readonly float R, G, B;

		public CellLight(float intensity, float r, float g, float b)
		{
			Intensity = intensity;
			R = r; G = g; B = b;
		}
	}

	// Known light emitters
	private static readonly (float Radius, float Intensity, float R, float G, float B) CampfireLight = (5f, 0.8f, 1.0f, 0.75f, 0.35f);
	private static readonly (float Radius, float Intensity, float R, float G, float B) FireLight = (4f, 1.0f, 1.0f, 0.60f, 0.20f);
	private static readonly (float Radius, float Intensity, float R, float G, float B) LavaLight = (2f, 0.4f, 1.0f, 0.35f, 0.05f);

	private readonly List<PointLight> _lights = new(16);
	private readonly Dictionary<long, CellLight> _cells = new(256);

	/// <summary>
	/// Rebuild the light map for the given visible region.
	/// Call once per render frame before GetFaceTint queries.
	/// </summary>
	public void Rebuild(WorldMap world, int cx, int cy, int cz, int halfW, int halfH, int zMin, int zMax)
	{
		_lights.Clear();
		_cells.Clear();

		// Pass 1: collect light sources in visible region (+ margin for lights just outside view)
		const int margin = 6;
		CollectLightsInVisibleWindow(
			world,
			cx - halfW - margin,
			cx + halfW + margin,
			cy - halfH - margin,
			cy + halfH + margin,
			zMin,
			zMax);

		if (_lights.Count == 0) return;

		// Pass 2: propagate each light to nearby cells
		for (var li = 0; li < _lights.Count; li++)
		{
			var light = _lights[li];
			var iRadius = (int)MathF.Ceiling(light.Radius);

			for (var dy = -iRadius; dy <= iRadius; dy++)
			for (var dx = -iRadius; dx <= iRadius; dx++)
			{
				var tx = light.X + dx;
				var ty = light.Y + dy;
				var tz = light.Z; // same Z-level only (simple model)

				var dist = MathF.Sqrt(dx * dx + dy * dy);
				if (dist > light.Radius) continue;

				// Quadratic falloff
				var t = dist / light.Radius;
				var contribution = light.Intensity * (1f - t) * (1f - t);
				if (contribution < 0.01f) continue;

				// Simple line-of-sight check (only for distances > 1)
				if (dist > 1.5f && !HasLineOfSight(world, light.X, light.Y, light.Z, tx, ty))
					continue;

				var key = CellKey(tx, ty, tz);
				if (_cells.TryGetValue(key, out var existing))
				{
					// Accumulate: sum intensity, weighted-average color
					var totalI = existing.Intensity + contribution;
					var wr = (existing.R * existing.Intensity + light.R * contribution) / totalI;
					var wg = (existing.G * existing.Intensity + light.G * contribution) / totalI;
					var wb = (existing.B * existing.Intensity + light.B * contribution) / totalI;
					_cells[key] = new CellLight(totalI, wr, wg, wb);
				}
				else
				{
					_cells[key] = new CellLight(contribution, light.R, light.G, light.B);
				}
			}
		}
	}

	public bool TryGetLight(int wx, int wy, int wz, out CellLight light) =>
		_cells.TryGetValue(CellKey(wx, wy, wz), out light);

	private void CollectLightsInVisibleWindow(WorldMap world, int minX, int maxX, int minY, int maxY, int zMin, int zMax)
	{
		var lavaTerrainId = TerrainRegistry.GetId(Terrains.Lava);

		for (var wz = zMin; wz <= zMax; wz++)
		{
			var minChunk = CoordUtil.WorldToChunk(minX, minY, wz);
			var maxChunk = CoordUtil.WorldToChunk(maxX, maxY, wz);
			for (var cy = minChunk.Cy; cy <= maxChunk.Cy; cy++)
			for (var cx = minChunk.Cx; cx <= maxChunk.Cx; cx++)
			{
				var chunk = world.Chunks.GetOrLoad(new ChunkCoord(cx, cy, wz));
				CollectTerrainLightsFromChunk(chunk, minX, maxX, minY, maxY, lavaTerrainId);
				CollectEntityLightsFromChunk(chunk, minX, maxX, minY, maxY);
			}
		}
	}

	private void CollectTerrainLightsFromChunk(ChunkData chunk, int minX, int maxX, int minY, int maxY, ushort lavaTerrainId)
	{
		var baseX = chunk.Coord.Cx * ChunkData.Size;
		var baseY = chunk.Coord.Cy * ChunkData.Size;
		var localMinX = Math.Max(0, minX - baseX);
		var localMaxX = Math.Min(ChunkData.Size - 1, maxX - baseX);
		var localMinY = Math.Max(0, minY - baseY);
		var localMaxY = Math.Min(ChunkData.Size - 1, maxY - baseY);
		if (localMinX > localMaxX || localMinY > localMaxY)
			return;

		for (var ly = localMinY; ly <= localMaxY; ly++)
		for (var lx = localMinX; lx <= localMaxX; lx++)
		{
			var index = CoordUtil.LocalIndex(lx, ly);
			if (chunk.TerrainIds[index] != lavaTerrainId)
				continue;

			_lights.Add(new PointLight(
				baseX + lx,
				baseY + ly,
				chunk.Coord.Cz,
				LavaLight.Radius,
				LavaLight.Intensity,
				LavaLight.R,
				LavaLight.G,
				LavaLight.B));
		}
	}

	private void CollectEntityLightsFromChunk(ChunkData chunk, int minX, int maxX, int minY, int maxY)
	{
		if (chunk.Entities.Count == 0)
			return;

		foreach (var (index, entities) in chunk.Entities)
		{
			var (lx, ly) = CoordUtil.IndexToLocal(index);
			var worldX = chunk.Coord.Cx * ChunkData.Size + lx;
			var worldY = chunk.Coord.Cy * ChunkData.Size + ly;
			if (worldX < minX || worldX > maxX || worldY < minY || worldY > maxY)
				continue;

			for (var i = 0; i < entities.Count; i++)
			{
				var entity = entities[i];
				if (entity.Type != CellEntityType.Fixture)
					continue;

				if (entity.EntityId == Entities.Campfire)
				{
					_lights.Add(new PointLight(
						worldX,
						worldY,
						chunk.Coord.Cz,
						CampfireLight.Radius,
						CampfireLight.Intensity,
						CampfireLight.R,
						CampfireLight.G,
						CampfireLight.B));
				}
				else if (entity.EntityId == Entities.Fire)
				{
					_lights.Add(new PointLight(
						worldX,
						worldY,
						chunk.Coord.Cz,
						FireLight.Radius,
						FireLight.Intensity,
						FireLight.R,
						FireLight.G,
						FireLight.B));
				}
			}
		}
	}

	private static long CellKey(int x, int y, int z) =>
		((long)(x + 32768) << 32) | ((long)(y + 32768) << 16) | (long)(z + 32768);

	/// <summary>Bresenham-style LOS check on the XY plane (same Z).</summary>
	private static bool HasLineOfSight(WorldMap world, int x0, int y0, int z, int x1, int y1)
	{
		var dx = Math.Abs(x1 - x0);
		var dy = Math.Abs(y1 - y0);
		var sx = x0 < x1 ? 1 : -1;
		var sy = y0 < y1 ? 1 : -1;
		var err = dx - dy;
		var cx = x0;
		var cy = y0;

		while (cx != x1 || cy != y1)
		{
			var e2 = 2 * err;
			if (e2 > -dy) { err -= dy; cx += sx; }
			if (e2 < dx) { err += dx; cy += sy; }

			// Don't check the destination cell itself
			if (cx == x1 && cy == y1) break;

			if (world.GetTerrain(cx, cy, z).IsOpaque)
				return false;
		}

		return true;
	}
}
