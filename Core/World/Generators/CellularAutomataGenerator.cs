using System;
using MiniRPG.Core.World.Noise;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// 细胞自动机生成器：
/// 1. 用 Perlin 噪声初始化（保证跨 chunk 连续性）
/// 2. 多次 CA 迭代平滑 → 自然的有机洞穴
/// 地表(z=0)用噪声生成开阔平原+零星洞穴入口。
/// </summary>
public class CellularAutomataGenerator : IMapGenerator
{
	public string Id => "cellular_automata";
	public string Name => "细胞自动机洞穴";

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		var cz = chunk.Coord.Cz;

		// 地表附近：3D 体积填充
		if (cz >= -4 && cz <= 4)
		{
			SurfaceGenerator.Generate(chunk, worldSeed);
			if (cz > 0)
				GenerateCaves(chunk, worldSeed);
			return;
		}

		// 高空：空气
		if (cz < -4)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));
			return;
		}

		// 深层地下
		GenerateCaves(chunk, worldSeed);
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz <= 0) return;
		PlaceFixtures(chunk, worldSeed);
	}

	private void GenerateCaves(ChunkData chunk, int worldSeed)
	{
		var config = GameConfig.Generation.Cellular;
		var wallId = GetWallForDepth(chunk.Coord.Cz);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		var noise = new PerlinNoise(worldSeed ^ chunk.Coord.Cz * 99991);

		var grid = new bool[ChunkData.Size, ChunkData.Size];

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var wx = chunk.Coord.Cx * ChunkData.Size + lx;
			var wy = chunk.Coord.Cy * ChunkData.Size + ly;
			var n = noise.Noise2D(wx * config.NoiseScale, wy * config.NoiseScale);
			grid[lx, ly] = (n + 1.0) / 2.0 < config.InitialFillChance;
		}

		for (var iter = 0; iter < config.Iterations; iter++)
		{
			var next = new bool[ChunkData.Size, ChunkData.Size];
			for (var ly = 0; ly < ChunkData.Size; ly++)
			for (var lx = 0; lx < ChunkData.Size; lx++)
			{
				var neighbors = CountNeighbors(grid, lx, ly);
				next[lx, ly] = grid[lx, ly]
					? neighbors >= config.SurviveThreshold
					: neighbors >= config.BirthThreshold;
			}
			grid = next;
		}

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
			chunk.SetTerrain(lx, ly, grid[lx, ly] ? wallId : floorId);
	}

	private static int CountNeighbors(bool[,] grid, int x, int y)
	{
		var count = 0;
		for (var dy = -1; dy <= 1; dy++)
		for (var dx = -1; dx <= 1; dx++)
		{
			if (dx == 0 && dy == 0) continue;
			var nx = x + dx;
			var ny = y + dy;
			if (nx < 0 || ny < 0 || nx >= ChunkData.Size || ny >= ChunkData.Size)
				count++;
			else if (grid[nx, ny])
				count++;
		}
		return count;
	}

	private static ushort GetWallForDepth(int z)
	{
		if (z <= 3) return TerrainRegistry.GetId(Terrains.WallSoil);
		if (z <= 8) return TerrainRegistry.GetId(Terrains.WallStone);
		if (z <= 15) return TerrainRegistry.GetId(Terrains.WallGranite);
		return TerrainRegistry.GetId(Terrains.WallObsidian);
	}

	private static void PlaceFixtures(ChunkData chunk, int worldSeed)
	{
		var config = GameConfig.Generation.Cellular;
		var seed = worldSeed ^ 0xCA1234 ^ chunk.Coord.Cx * 7193 ^ chunk.Coord.Cy * 5437 ^ chunk.Coord.Cz * 3119;
		var rng = new Random(seed);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		var downPlaced = false;
		var upPlaced = false;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			if (!downPlaced && rng.Next(100) < Math.Clamp(config.StairDownChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				downPlaced = true;
			}
			else if (!upPlaced && rng.Next(100) < Math.Clamp(config.StairUpChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "<", EntityId = Entities.StairUp });
				upPlaced = true;
			}
			else if (rng.Next(100) < Math.Clamp(config.NestChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "N", EntityId = Entities.Nest });
				chunk.Nests.Add(new NestData
				{
					X = chunk.Coord.Cx * ChunkData.Size + lx,
					Y = chunk.Coord.Cy * ChunkData.Size + ly,
					SpawnInterval = config.NestSpawnInterval,
					MaxSpawned = config.NestMaxSpawned,
				});
			}
		}
	}
}
