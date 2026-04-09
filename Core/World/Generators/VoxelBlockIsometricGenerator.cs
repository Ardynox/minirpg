using MiniRPG.Core.Data;
using MiniRPG.Core.World.Noise;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// Minecraft-like block world generator for isometric 2.5D rendering.
/// - Quantized column height for clear voxel steps
/// - Solid body fill (grass/dirt/stone), air above, optional shallow water
/// - Deterministic by worldSeed + world coordinates
/// </summary>
public sealed class VoxelBlockIsometricGenerator : IMapGenerator
{
	private const double HeightScale = 0.020;
	private const int MaxElevation = 10;
	private const int WaterPlaneZ = 1;
	private const int DirtLayerDepth = 3;

	public string Id => "voxel_block_iso";
	public string Name => "体素方块世界(等距2.5D)";

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		var heightNoise = new PerlinNoise(worldSeed ^ 0x5A17A11);
		var moistureNoise = new PerlinNoise(worldSeed ^ 0x27B91E3);
		var cz = chunk.Coord.Cz;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var wx = chunk.Coord.Cx * ChunkData.Size + lx;
			var wy = chunk.Coord.Cy * ChunkData.Size + ly;

			var baseHeight = heightNoise.FBM2D(wx * HeightScale, wy * HeightScale, 5, 0.5);
			var ridge = System.Math.Abs(heightNoise.FBM2D(wx * HeightScale * 0.5, wy * HeightScale * 0.5, 3, 0.55));
			var moisture = moistureNoise.FBM2D(wx * HeightScale * 0.8, wy * HeightScale * 0.8, 3, 0.6);

			var mixedHeight = baseHeight * 0.78 + ridge * 0.22;
			var surfaceZ = -(int)System.Math.Round(mixedHeight * MaxElevation);

			var terrainId = ClassifyVoxel(cz, surfaceZ, moisture);
			chunk.SetTerrain(lx, ly, terrainId);
		}
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz != 0)
			return;

		var treeChancePercent = 4;
		var seed = worldSeed ^ unchecked((int)0x6E5A1234) ^ chunk.Coord.Cx * 7919 ^ chunk.Coord.Cy * 6563;
		var rng = new System.Random(seed);

		var grassId = TerrainRegistry.GetId(Terrains.GrassBlock);
		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != grassId)
				continue;
			if (rng.Next(100) >= treeChancePercent)
				continue;

			chunk.SetTerrain(lx, ly, TerrainRegistry.GetId(Terrains.Tree));
		}
	}

	private static ushort ClassifyVoxel(int currentZ, int surfaceZ, double moisture)
	{
		if (currentZ < surfaceZ)
		{
			if (currentZ >= WaterPlaneZ)
				return TerrainRegistry.GetId(Terrains.Water);
			return TerrainRegistry.GetId(Terrains.Air);
		}

		if (currentZ == surfaceZ)
		{
			if (surfaceZ >= WaterPlaneZ)
				return TerrainRegistry.GetId(Terrains.Water);
			if (surfaceZ <= -MaxElevation + 2)
				return TerrainRegistry.GetId(Terrains.Mountain);
			if (surfaceZ >= WaterPlaneZ - 1)
				return TerrainRegistry.GetId(Terrains.Sand);
			if (moisture > -0.25)
				return TerrainRegistry.GetId(Terrains.GrassBlock);
			return TerrainRegistry.GetId(Terrains.Sand);
		}

		var depth = currentZ - surfaceZ;
		if (depth <= DirtLayerDepth)
			return TerrainRegistry.GetId(Terrains.Dirt);
		return TerrainRegistry.GetId(Terrains.Stone);
	}
}
