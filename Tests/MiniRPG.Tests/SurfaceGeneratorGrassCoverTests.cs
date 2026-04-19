using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// Wave 2.1：验证 SurfaceGenerator.Generate 在 surface 层把 GrassCoverSampler 的输出
/// 写入 chunk.GrassCover；地下/空气层保持默认 0；非 dirt/grass_block 表面保持 0；同种子同坐标可复现。
/// </summary>
public sealed class SurfaceGeneratorGrassCoverTests
{
	static SurfaceGeneratorGrassCoverTests()
	{
		if (TerrainRegistry.Get(Terrains.Dirt) == null
			|| TerrainRegistry.Get(Terrains.GrassBlock) == null
			|| TerrainRegistry.Get(Terrains.Water) == null)
		{
			TerrainRegistry.Load("terrains.json");
		}
	}

	private const int Seed = 1234567;

	private static ChunkData GenerateChunk(int cx, int cy, int cz, int seed = Seed)
	{
		var chunk = new ChunkData { Coord = new ChunkCoord(cx, cy, cz) };
		SurfaceGenerator.Generate(chunk, seed);
		return chunk;
	}

	// 1) 扫一个立方体范围的 chunk，至少有 1 格 GrassCover != 0。
	//    地表起伏 surfaceZ ∈ [-4,4]；遍历 Cz ∈ [-4,0] 配合 3x3 水平区域，
	//    保证我们覆盖到典型的 grass_block / dirt 表面层。
	[Fact]
	public void Generate_SurfaceChunks_ProduceNonZeroGrassCoverSomewhere()
	{
		var sawNonZero = false;
		for (var cz = -4; cz <= 0 && !sawNonZero; cz++)
		for (var cy = 0; cy < 3 && !sawNonZero; cy++)
		for (var cx = 0; cx < 3 && !sawNonZero; cx++)
		{
			var chunk = GenerateChunk(cx, cy, cz);
			for (var i = 0; i < chunk.GrassCover.Length; i++)
			{
				if (chunk.GrassCover[i] != 0)
				{
					sawNonZero = true;
					break;
				}
			}
		}

		Assert.True(
			sawNonZero,
			"Expected at least one non-zero GrassCover cell across the 3x3x5 surface chunk cube.");
	}

	// 2) 非 dirt/grass_block 表面（water/sand/mountain/tree/stone/...）GrassCover 必须为 0。
	//    我们扫整个立方体范围的 chunk，对每个 (lx,ly) 检查 baseTerrainId。
	[Fact]
	public void Generate_SurfaceChunk_NonGrowableSurfacesStayZero()
	{
		var dirtId = TerrainRegistry.GetId(Terrains.Dirt);
		var grassBlockId = TerrainRegistry.GetId(Terrains.GrassBlock);

		for (var cz = -4; cz <= 0; cz++)
		for (var cy = 0; cy < 3; cy++)
		for (var cx = 0; cx < 3; cx++)
		{
			var chunk = GenerateChunk(cx, cy, cz);
			for (var ly = 0; ly < ChunkData.Size; ly++)
			for (var lx = 0; lx < ChunkData.Size; lx++)
			{
				var idx = ly * ChunkData.Size + lx;
				var terrainId = chunk.TerrainIds[idx];
				if (terrainId == dirtId || terrainId == grassBlockId)
					continue;

				Assert.True(
					chunk.GrassCover[idx] == 0,
					$"Non-growable terrain id={terrainId} at chunk ({cx},{cy},{cz}) cell ({lx},{ly}) "
					+ $"unexpectedly has GrassCover={chunk.GrassCover[idx]}.");
			}
		}
	}

	// 3) 纯地下层（cz=5 远超最大 surfaceZ=4）所有 GrassCover 必须为 0。
	[Fact]
	public void Generate_UndergroundChunk_AllGrassCoverIsZero()
	{
		for (var cy = 0; cy < 2; cy++)
		for (var cx = 0; cx < 2; cx++)
		{
			var chunk = GenerateChunk(cx, cy, cz: 5);
			Assert.All(chunk.GrassCover, value => Assert.Equal((byte)0, value));
		}
	}

	// 4) 同种子 + 同 chunk 坐标，两次生成 GrassCover 完全一致。
	[Fact]
	public void Generate_SameSeedAndCoord_ProducesIdenticalGrassCover()
	{
		var coords = new[]
		{
			new ChunkCoord(0, 0, 0),
			new ChunkCoord(1, -2, -1),
			new ChunkCoord(-3, 4, -2),
		};

		foreach (var coord in coords)
		{
			var first = GenerateChunk(coord.Cx, coord.Cy, coord.Cz);
			var second = GenerateChunk(coord.Cx, coord.Cy, coord.Cz);

			Assert.Equal(first.GrassCover, second.GrassCover);
		}
	}
}
