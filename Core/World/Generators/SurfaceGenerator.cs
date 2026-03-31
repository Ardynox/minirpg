using MiniRPG.Core;
using MiniRPG.Core.World.Noise;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// 共享地表生成逻辑：所有生成器在 z=0 时调用此方法。
/// 使用双层 Perlin 噪声（高度 + 湿度）生成自然的森林/草原/水域地形。
/// </summary>
public static class SurfaceGenerator
{
	private const double HeightScale = 0.025;
	private const double MoistureScale = 0.02;

	public static void Generate(ChunkData chunk, int worldSeed)
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

			chunk.SetTerrain(lx, ly, Classify(height, moisture));
		}
	}

	/// <summary>
	/// 地表地形分类：
	///   低地 → 水/沙滩
	///   平原 → 干燥则草地，湿润则森林
	///   高地 → 山（实心可挖，不是墙）
	/// </summary>
	private static ushort Classify(double height, double moisture)
	{
		if (height < -0.35) return TerrainRegistry.GetId(Terrains.Water);
		if (height < -0.2) return TerrainRegistry.GetId(Terrains.Sand);
		if (height > 0.55) return TerrainRegistry.GetId(Terrains.Mountain);

		if (moisture > 0.25) return TerrainRegistry.GetId(Terrains.Tree);
		if (moisture > -0.05) return TerrainRegistry.GetId(Terrains.Grass);
		return TerrainRegistry.GetId(Terrains.Sand);
	}
}
