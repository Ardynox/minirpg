using System;
using MiniRPG.Core.World.Noise;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// 共享地表生成逻辑：3D 体积填充版本。
/// 使用双层 Perlin 噪声（高度 + 湿度）生成高度图，
/// 然后按列填充 stone/dirt/grass_block/air 等体素方块。
///
/// Z 约定：Z=0 为基准地表，Z>0 为地下（Z 越大越深），Z&lt;0 为地上（Z 越小越高）。
/// 高度图值映射到 Z 偏移：height 0 → Z=0，正 height → 负 Z（更高的山）。
/// </summary>
public static class SurfaceGenerator
{
	private const double HeightScale = 0.025;
	private const double MoistureScale = 0.02;

	/// <summary>地表起伏的最大振幅（单位：Z 层数）。</summary>
	private const int MaxElevation = 4;

	/// <summary>水面所在的 Z 层（Z=1 表示水面在地表下一层）。</summary>
	private const int WaterZ = 1;

	/// <summary>
	/// 为指定 chunk 生成体素地形。
	/// 根据 chunk 的 Cz 决定该层填充什么方块。
	/// </summary>
	public static void Generate(ChunkData chunk, int worldSeed)
	{
		var heightNoise = new PerlinNoise(worldSeed);
		var moistureNoise = new PerlinNoise(worldSeed ^ 0x12345678);
		var cz = chunk.Coord.Cz;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var wx = chunk.Coord.Cx * ChunkData.Size + lx;
			var wy = chunk.Coord.Cy * ChunkData.Size + ly;

			var heightVal = heightNoise.FBM2D(wx * HeightScale, wy * HeightScale, 5, 0.5);
			var moisture = moistureNoise.FBM2D(wx * MoistureScale, wy * MoistureScale, 3, 0.6);

			// 高度图 → 地表 Z 层（surfaceZ）。
			// heightVal ∈ [-1,1]，映射到 Z 偏移。
			// 正 heightVal = 高地 → 负 Z（更高），负 heightVal = 低地 → 正 Z（更低）。
			var surfaceZ = -(int)Math.Round(heightVal * MaxElevation);

			chunk.SetTerrain(lx, ly, ClassifyVoxel(cz, surfaceZ, moisture));
		}
	}

	/// <summary>
	/// 旧版兼容：为 Z=0 单层生成 2D 地表（供旧渲染路径使用）。
	/// </summary>
	public static void GenerateFlat(ChunkData chunk, int worldSeed)
	{
		var heightNoise = new PerlinNoise(worldSeed);
		var moistureNoise = new PerlinNoise(worldSeed ^ 0x12345678);

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var wx = chunk.Coord.Cx * ChunkData.Size + lx;
			var wy = chunk.Coord.Cy * ChunkData.Size + ly;

			var height = heightNoise.FBM2D(wx * HeightScale, wy * HeightScale, 5, 0.5);
			var moisture = moistureNoise.FBM2D(wx * MoistureScale, wy * MoistureScale, 3, 0.6);

			chunk.SetTerrain(lx, ly, ClassifyFlat(height, moisture));
		}
	}

	/// <summary>
	/// 3D 体素分类：根据当前 Z 层与地表 Z 的关系决定方块类型。
	/// </summary>
	private static ushort ClassifyVoxel(int currentZ, int surfaceZ, double moisture)
	{
		// currentZ > surfaceZ → 地下（更深处）
		// currentZ == surfaceZ → 地表面
		// currentZ < surfaceZ → 地上（空气）

		if (currentZ < surfaceZ)
		{
			// 地上：空气。但如果在水面以下，填水。
			if (currentZ >= WaterZ)
				return TerrainRegistry.GetId(Terrains.Water);
			return TerrainRegistry.GetId(Terrains.Air);
		}

		if (currentZ == surfaceZ)
		{
			// 地表面：根据湿度决定表面类型
			if (surfaceZ >= WaterZ)
				return TerrainRegistry.GetId(Terrains.Water);

			// 高山
			if (surfaceZ <= -MaxElevation + 1)
				return TerrainRegistry.GetId(Terrains.Mountain);

			// 沙滩（靠近水面）
			if (surfaceZ >= WaterZ - 1)
				return TerrainRegistry.GetId(Terrains.Sand);

			// 森林/草地
			if (moisture > 0.25)
				return TerrainRegistry.GetId(Terrains.Tree);
			if (moisture > -0.05)
				return TerrainRegistry.GetId(Terrains.GrassBlock);
			return TerrainRegistry.GetId(Terrains.Sand);
		}

		// 地下
		var depth = currentZ - surfaceZ;
		if (depth <= 3)
			return TerrainRegistry.GetId(Terrains.Dirt);

		return TerrainRegistry.GetId(Terrains.Stone);
	}

	/// <summary>
	/// 旧版 2D 地表分类（保持向后兼容）。
	/// </summary>
	private static ushort ClassifyFlat(double height, double moisture)
	{
		if (height < -0.35) return TerrainRegistry.GetId(Terrains.Water);
		if (height < -0.2) return TerrainRegistry.GetId(Terrains.Sand);
		if (height > 0.55) return TerrainRegistry.GetId(Terrains.Mountain);

		if (moisture > 0.25) return TerrainRegistry.GetId(Terrains.Tree);
		if (moisture > -0.05) return TerrainRegistry.GetId(Terrains.Grass);
		return TerrainRegistry.GetId(Terrains.Sand);
	}
}
