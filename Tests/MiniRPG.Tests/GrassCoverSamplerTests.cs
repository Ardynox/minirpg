using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Surface;
using Xunit;

namespace MiniRPG.Tests;

public sealed class GrassCoverSamplerTests
{
	static GrassCoverSamplerTests()
	{
		if (TerrainRegistry.Get(Terrains.Dirt) == null
			|| TerrainRegistry.Get(Terrains.GrassBlock) == null
			|| TerrainRegistry.Get(Terrains.Stone) == null)
		{
			TerrainRegistry.Load("terrains.json");
		}
	}

	private const int Seed = 1234567;

	private static ushort DirtId => TerrainRegistry.GetId(Terrains.Dirt);
	private static ushort GrassBlockId => TerrainRegistry.GetId(Terrains.GrassBlock);
	private static ushort StoneId => TerrainRegistry.GetId(Terrains.Stone);

	// ── Determinism ──────────────────────────────────────────────────────

	[Fact]
	public void Sample_SameInput_RepeatableAcrossManyCalls()
	{
		var first = GrassCoverSampler.Sample(Seed, 17, 31, DirtId, exposedToSky: true, underwater: false);
		for (var i = 0; i < 50; i++)
		{
			var again = GrassCoverSampler.Sample(Seed, 17, 31, DirtId, exposedToSky: true, underwater: false);
			Assert.Equal(first, again);
		}
	}

	[Fact]
	public void Sample_SameInput_DeterministicAcrossCoordinates()
	{
		for (var y = -10; y <= 10; y++)
		for (var x = -10; x <= 10; x++)
		{
			var a = GrassCoverSampler.Sample(Seed, x, y, DirtId, exposedToSky: true, underwater: false);
			var b = GrassCoverSampler.Sample(Seed, x, y, DirtId, exposedToSky: true, underwater: false);
			Assert.Equal(a, b);
		}
	}

	// ── Hard gates: 0 必须返回 ───────────────────────────────────────────

	[Fact]
	public void Sample_NotExposedToSky_ReturnsZero()
	{
		for (var y = 0; y < 20; y++)
		for (var x = 0; x < 20; x++)
		{
			var v = GrassCoverSampler.Sample(Seed, x, y, DirtId, exposedToSky: false, underwater: false);
			Assert.Equal((byte)0, v);
		}
	}

	[Fact]
	public void Sample_Underwater_ReturnsZero()
	{
		for (var y = 0; y < 20; y++)
		for (var x = 0; x < 20; x++)
		{
			var v = GrassCoverSampler.Sample(Seed, x, y, DirtId, exposedToSky: true, underwater: true);
			Assert.Equal((byte)0, v);
		}
	}

	[Fact]
	public void Sample_NonDirtTerrain_ReturnsZero()
	{
		for (var y = 0; y < 20; y++)
		for (var x = 0; x < 20; x++)
		{
			var stone = GrassCoverSampler.Sample(Seed, x, y, StoneId, exposedToSky: true, underwater: false);
			Assert.Equal((byte)0, stone);
		}
	}

	[Fact]
	public void Sample_VoidTerrain_ReturnsZero()
	{
		// Id 0 = void (FallbackVoid)，不是 dirt / grass_block，必须 0。
		for (var y = 0; y < 20; y++)
		for (var x = 0; x < 20; x++)
		{
			var v = GrassCoverSampler.Sample(Seed, x, y, baseTerrainId: 0, exposedToSky: true, underwater: false);
			Assert.Equal((byte)0, v);
		}
	}

	// ── 正向：dirt / grass_block + 暴露 + 不在水下 → 大概率非 0 ──────────

	[Fact]
	public void Sample_DirtExposedDry_MostlyNonZeroOverManyPoints()
	{
		var (nonZero, total) = CountNonZero(DirtId);
		// 阈值 0.42 + Perlin 平均值 0 → 期望约 55%~60% 非零。
		// 用宽松下限避免噪声运气导致 flake。
		Assert.True(
			nonZero * 100 / total >= 35,
			$"Expected at least 35% non-zero grass cover on dirt, got {nonZero}/{total}");
	}

	[Fact]
	public void Sample_GrassBlockExposedDry_MostlyNonZero()
	{
		var (nonZero, total) = CountNonZero(GrassBlockId);
		Assert.True(
			nonZero * 100 / total >= 35,
			$"Expected at least 35% non-zero grass cover on grass_block, got {nonZero}/{total}");
	}

	[Fact]
	public void Sample_DirtExposedDry_NotAllSameValue()
	{
		// 草地必须是 patchy 的：同一片区域内既有 0 也有非 0，不是常数。
		var sawZero = false;
		var sawNonZero = false;
		for (var y = 0; y < 32; y++)
		for (var x = 0; x < 32; x++)
		{
			var v = GrassCoverSampler.Sample(Seed, x, y, DirtId, exposedToSky: true, underwater: false);
			if (v == 0) sawZero = true;
			else sawNonZero = true;
			if (sawZero && sawNonZero) return;
		}
		Assert.True(sawZero, "Expected at least one bare-dirt (0) cell within 32x32 patch.");
		Assert.True(sawNonZero, "Expected at least one grass-cover (>0) cell within 32x32 patch.");
	}

	// ── 不同种子 → 至少在某些点取值不同（确保 seed 真的进了哈希） ───────

	[Fact]
	public void Sample_DifferentSeed_ProducesDifferentDistribution()
	{
		var diffCount = 0;
		for (var y = 0; y < 16; y++)
		for (var x = 0; x < 16; x++)
		{
			var a = SampleDirt(seed: 111, x, y);
			var b = SampleDirt(seed: 222, x, y);
			if (a != b) diffCount++;
		}
		Assert.True(diffCount > 16, $"Expected different seeds to yield meaningfully different results, only {diffCount} diffs in 256.");
	}

	private static (int nonZero, int total) CountNonZero(ushort terrainId)
	{
		var nonZero = 0;
		var total = 0;
		// 32x32 = 1024 个采样点，足够压平噪声方差。
		for (var y = 0; y < 32; y++)
		for (var x = 0; x < 32; x++)
		{
			var v = GrassCoverSampler.Sample(Seed, x, y, terrainId, exposedToSky: true, underwater: false);
			if (v != 0) nonZero++;
			total++;
		}
		return (nonZero, total);
	}

	// 命名参数包装：让"不同种子"那条测试读起来一目了然。
	private static byte SampleDirt(int seed, int x, int y) =>
		GrassCoverSampler.Sample(seed, x, y, DirtId, exposedToSky: true, underwater: false);
}
