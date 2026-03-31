using System;
using MiniRPG.Core;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// 醉汉漫步生成器：多个随机行走者从不同起点出发，
/// 在实心岩石中开辟蜿蜒的隧道。
/// 起点由噪声确定以保证跨 chunk 连续性（在 chunk 边界附近起步）。
/// </summary>
public class DrunkardWalkGenerator : IMapGenerator
{
	public string Id => "drunkard_walk";
	public string Name => "醉汉漫步";

	private const int WalkersPerChunk = 6;
	private const int StepsPerWalker = 200;
	private const double TargetOpenRatio = 0.40;

	private static readonly (int Dx, int Dy)[] Dirs = [(0, -1), (0, 1), (-1, 0), (1, 0)];

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz == 0)
		{
			SurfaceGenerator.Generate(chunk, worldSeed);
			return;
		}

		var wallId = GetWallForDepth(chunk.Coord.Cz);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		chunk.Fill(wallId);

		if (chunk.Coord.Cz < 0) { chunk.Fill(floorId); return; }

		var seed = HashSeed(worldSeed, chunk.Coord);
		var rng = new Random(seed);
		var targetCells = (int)(ChunkData.Area * TargetOpenRatio);
		var openCount = 0;

		for (var w = 0; w < WalkersPerChunk && openCount < targetCells; w++)
		{
			var x = rng.Next(2, ChunkData.Size - 2);
			var y = rng.Next(2, ChunkData.Size - 2);

			for (var step = 0; step < StepsPerWalker && openCount < targetCells; step++)
			{
				if (chunk.GetTerrainId(x, y) != floorId)
				{
					chunk.SetTerrain(x, y, floorId);
					openCount++;
				}

				var dir = Dirs[rng.Next(4)];
				var nx = x + dir.Dx;
				var ny = y + dir.Dy;
				if (nx >= 1 && nx < ChunkData.Size - 1 && ny >= 1 && ny < ChunkData.Size - 1)
				{
					x = nx;
					y = ny;
				}
			}
		}

		EnsureBorderConnections(chunk, rng, floorId);
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz <= 0) return;

		var seed = HashSeed(worldSeed, chunk.Coord) ^ unchecked((int)0xD1D2D3D4);
		var rng = new Random(seed);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		var downPlaced = false;
		var upPlaced = false;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			if (!downPlaced && rng.Next(100) < 2)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				downPlaced = true;
			}
			else if (!upPlaced && rng.Next(100) < 2)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "<", EntityId = Entities.StairUp });
				upPlaced = true;
			}
			else if (rng.Next(100) < 1)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "N", EntityId = Entities.Nest });
				chunk.Nests.Add(new NestData
				{
					X = chunk.Coord.Cx * ChunkData.Size + lx,
					Y = chunk.Coord.Cy * ChunkData.Size + ly,
					SpawnInterval = 7, MaxSpawned = 2,
				});
			}
		}
	}

	/// <summary>确保 chunk 四条边各有至少一个开口，方便跨 chunk 通行。</summary>
	private static void EnsureBorderConnections(ChunkData chunk, Random rng, ushort floorId)
	{
		EnsureEdge(chunk, rng, floorId, 0, true);
		EnsureEdge(chunk, rng, floorId, ChunkData.Size - 1, true);
		EnsureEdge(chunk, rng, floorId, 0, false);
		EnsureEdge(chunk, rng, floorId, ChunkData.Size - 1, false);
	}

	private static void EnsureEdge(ChunkData chunk, Random rng, ushort floorId, int fixedCoord, bool fixX)
	{
		var hasOpening = false;
		for (var i = 0; i < ChunkData.Size; i++)
		{
			var x = fixX ? fixedCoord : i;
			var y = fixX ? i : fixedCoord;
			if (chunk.GetTerrainId(x, y) == floorId) { hasOpening = true; break; }
		}
		if (hasOpening) return;

		var pos = rng.Next(2, ChunkData.Size - 2);
		var px = fixX ? fixedCoord : pos;
		var py = fixX ? pos : fixedCoord;
		chunk.SetTerrain(px, py, floorId);
	}

	private static ushort GetWallForDepth(int z)
	{
		if (z <= 3) return TerrainRegistry.GetId(Terrains.WallSoil);
		if (z <= 8) return TerrainRegistry.GetId(Terrains.WallStone);
		if (z <= 15) return TerrainRegistry.GetId(Terrains.WallGranite);
		return TerrainRegistry.GetId(Terrains.WallObsidian);
	}

	private static int HashSeed(int worldSeed, ChunkCoord c) =>
		unchecked(worldSeed * 73856093 ^ c.Cx * 19349663 ^ c.Cy * 83492791 ^ c.Cz * 41729387);
}
