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

	private const double CaveScale = 0.08;
	private const double CaveDensityThreshold = 0.1;

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz == 0)
			SurfaceGenerator.Generate(chunk, worldSeed);
		else if (chunk.Coord.Cz > 0)
			GenerateUnderground(chunk, worldSeed);
		else
			GenerateSky(chunk);
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz <= 0) return;

		var seed = worldSeed ^ unchecked((int)0xDEAD0001) ^ chunk.Coord.Cz * 31337;
		var rng = new Random(seed ^ chunk.Coord.Cx * 7919 ^ chunk.Coord.Cy * 6563);
		var floorId = TerrainRegistry.GetId("floor");
		var downPlaced = false;
		var upPlaced = false;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			if (!downPlaced && rng.Next(100) < 1)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				downPlaced = true;
			}
			else if (!upPlaced && rng.Next(100) < 1)
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
					SpawnInterval = 8, MaxSpawned = 2,
				});
			}
		}
	}

	private void GenerateUnderground(ChunkData chunk, int worldSeed)
	{
		var caveNoise = new PerlinNoise(worldSeed ^ 0xCAFE);
		var oreNoise = new PerlinNoise(worldSeed ^ 0xBEEF);
		var wallId = GetWallForDepth(chunk.Coord.Cz);
		var floorId = TerrainRegistry.GetId("floor");

		chunk.Fill(wallId);

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var wx = chunk.Coord.Cx * ChunkData.Size + lx;
			var wy = chunk.Coord.Cy * ChunkData.Size + ly;
			var wz = chunk.Coord.Cz;

			var density = caveNoise.FBM3D(
				wx * CaveScale, wy * CaveScale, wz * CaveScale * 0.5, 4, 0.5);

			if (density < CaveDensityThreshold)
				chunk.SetTerrain(lx, ly, floorId);
		}
	}

	private static ushort GetWallForDepth(int z)
	{
		if (z <= 2) return TerrainRegistry.GetId("wall_soil");
		if (z <= 6) return TerrainRegistry.GetId("wall_stone");
		if (z <= 12) return TerrainRegistry.GetId("wall_granite");
		return TerrainRegistry.GetId("wall_obsidian");
	}

	private static void GenerateSky(ChunkData chunk)
	{
		var floorId = TerrainRegistry.GetId("floor");
		chunk.Fill(floorId);
	}
}
