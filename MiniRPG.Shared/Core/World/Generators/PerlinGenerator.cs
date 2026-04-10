using System;
using MiniRPG.Core.World.Noise;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// Perlin 噪声生成器：
/// 地表(z=0)：高度图 → 草地/森林/河流/山/沙滩。
/// 地下(z>0)：3D 密度场 → 自然洞穴（密度低于阈值 = 空洞）。
/// 天然保证跨 chunk 连续性——噪声函数在世界坐标空间采样。
/// </summary>
public class PerlinGenerator : IMapGenerator
{
	public string Id => "perlin";
	public string Name => "自然地形";

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		var cz = chunk.Coord.Cz;

		// 地表附近：3D 体积填充
		if (cz >= -4 && cz <= 4)
		{
			SurfaceGenerator.Generate(chunk, worldSeed);
			if (cz > 0)
				GenerateUnderground(chunk, worldSeed);
			return;
		}

		// 高空：空气
		if (cz < -4)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));
			return;
		}

		// 深层地下
		GenerateUnderground(chunk, worldSeed);
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz <= 0) return;

		var config = GameConfig.Generation.Perlin;
		var seed = worldSeed ^ unchecked((int)0xDEAD0001) ^ chunk.Coord.Cz * 31337;
		var rng = new Random(seed ^ chunk.Coord.Cx * 7919 ^ chunk.Coord.Cy * 6563);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		GeneratorPopulateHelper.PlaceDungeonFixtures(
			chunk,
			rng,
			floorId,
			config.StairDownChancePercent,
			config.StairUpChancePercent,
			allowStairUp: true,
			config.NestChancePercent,
			config.NestSpawnInterval,
			config.NestMaxSpawned);
	}

	private void GenerateUnderground(ChunkData chunk, int worldSeed)
	{
		var config = GameConfig.Generation.Perlin;
		var caveNoise = new PerlinNoise(worldSeed ^ 0xCAFE);
		var oreNoise = new PerlinNoise(worldSeed ^ 0xBEEF);
		var wallId = GetWallForDepth(chunk.Coord.Cz);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		chunk.Fill(wallId);

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var wx = chunk.Coord.Cx * ChunkData.Size + lx;
			var wy = chunk.Coord.Cy * ChunkData.Size + ly;
			var wz = chunk.Coord.Cz;

			var density = caveNoise.FBM3D(
				wx * config.CaveScale, wy * config.CaveScale, wz * config.CaveScale * 0.5, 4, 0.5);

			if (density < config.CaveDensityThreshold)
				chunk.SetTerrain(lx, ly, floorId);
		}
	}

	private static ushort GetWallForDepth(int z)
	{
		if (z <= 2) return TerrainRegistry.GetId(Terrains.WallSoil);
		if (z <= 6) return TerrainRegistry.GetId(Terrains.WallStone);
		if (z <= 12) return TerrainRegistry.GetId(Terrains.WallGranite);
		return TerrainRegistry.GetId(Terrains.WallObsidian);
	}

	private static void GenerateSky(ChunkData chunk)
	{
		chunk.Fill(TerrainRegistry.GetId(Terrains.Air));
	}
}
