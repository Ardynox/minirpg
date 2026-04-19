using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.Debug;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;
using MiniRPG.Module.Render;
using MiniRPG.Module.Render.Surface;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// Wave 3.1 端到端集成测试：把 SurfaceGenerator → ChunkData.GrassCover → SaveModule
/// → GrassOverlayPass + DebugModule 4 钩子 + TerrainAtlas variant 查询整条链路串起来。
///
/// 设计原则：
/// 1. 不实例化 IsometricVoxelRenderer / TerrainAtlas.Build()，避免触发 Godot Image API。
/// 2. <see cref="GrassOverlayDrawContext"/> 是 record struct，可直接构造；EmitFace 用 lambda 当 spy。
/// 3. <see cref="TerrainAtlas"/> 用默认构造 + 反射注入 _grassOverlayRegions（每个 variant
///    region.Position.X 用唯一 stride 标识，让 spy lambda 反推命中的 variant 索引）。
/// 4. <see cref="DebugModule"/> 4 个钩子是 static state，每个测试用 try/finally 保存/恢复，
///    避免污染并行运行的其它测试。
/// </summary>
public sealed class GrassSurfaceCoverIntegrationTests
{
	static GrassSurfaceCoverIntegrationTests()
	{
		if (TerrainRegistry.Get(Terrains.Dirt) == null
			|| TerrainRegistry.Get(Terrains.GrassBlock) == null
			|| TerrainRegistry.Get(Terrains.Water) == null)
		{
			TerrainRegistry.Load("terrains.json");
		}
	}

	private const int Seed = 1234567;

	// 每个 variant region 的 X 偏移步长；spy lambda 用 region.Position.X / Stride 反推 variant 索引。
	// 取大数避免与 atlas 真实坐标混淆，方便测试可读性。
	private const float VariantOriginXStride = 1000f;
	private const int InjectedVariantCount = 6;

	// ── Case 1：端到端 ──
	// SurfaceGenerator 生成 surface chunk 后：dirt/grass_block 表面格的 GrassCover 可非 0；
	// 其它 base terrain（water/sand/mountain/tree/stone/...）必为 0；纯地下层全 0。
	[Fact]
	public void End_to_end_generated_chunk_has_grass_only_on_dirt_surface()
	{
		var dirtId = TerrainRegistry.GetId(Terrains.Dirt);
		var grassBlockId = TerrainRegistry.GetId(Terrains.GrassBlock);

		var sawNonZeroOnGrowable = false;
		for (var cz = -4; cz <= 0; cz++)
		for (var cy = 0; cy < 3; cy++)
		for (var cx = 0; cx < 3; cx++)
		{
			var chunk = GenerateChunk(cx, cy, cz, Seed);
			for (var i = 0; i < ChunkData.Area; i++)
			{
				var terrainId = chunk.TerrainIds[i];
				var cover = chunk.GrassCover[i];

				if (terrainId == dirtId || terrainId == grassBlockId)
				{
					if (cover != 0)
						sawNonZeroOnGrowable = true;
				}
				else
				{
					Assert.True(
						cover == 0,
						$"non-growable terrain id={terrainId} at chunk ({cx},{cy},{cz}) idx={i} "
						+ $"unexpectedly has GrassCover={cover}");
				}
			}
		}

		Assert.True(
			sawNonZeroOnGrowable,
			"Expected at least one dirt/grass_block surface cell with non-zero GrassCover across the surface cube.");

		var underground = GenerateChunk(0, 0, cz: 5, Seed);
		Assert.All(underground.GrassCover, value => Assert.Equal((byte)0, value));

		var deeperUnderground = GenerateChunk(1, 1, cz: 5, Seed);
		Assert.All(deeperUnderground.GrassCover, value => Assert.Equal((byte)0, value));
	}

	// ── Case 2：ForceGrassOverlay = Off → 跳过所有 emit ──
	[Fact]
	public void Force_grass_off_disables_overlay_emission()
	{
		var atlas = CreateAtlasWithRegions(InjectedVariantCount);
		var emitCount = 0;
		var ctx = CreateDrawContext(atlas, (_, _, _, _) => emitCount++);

		var saved = SaveGrassDebugState();
		try
		{
			DebugModule.ForceGrassOverlay = GrassOverlayForceMode.Off;
			DebugModule.GrassDensityThreshold = 0;
			DebugModule.GrassVariantOverride = -1;

			GrassOverlayPass.DrawTopFace(in ctx, cover: 200, wx: 0, wy: 0);
			GrassOverlayPass.DrawTopFace(in ctx, cover: 255, wx: 5, wy: 7);
			GrassOverlayPass.DrawTopFace(in ctx, cover: 1, wx: -3, wy: -9);

			Assert.Equal(0, emitCount);
		}
		finally
		{
			RestoreGrassDebugState(saved);
		}
	}

	// ── Case 3：ForceGrassOverlay = On → 即使 cover==0 也强制 emit 一次 ──
	[Fact]
	public void Force_grass_on_overrides_zero_cover()
	{
		var atlas = CreateAtlasWithRegions(InjectedVariantCount);
		var emitCount = 0;
		var ctx = CreateDrawContext(atlas, (_, _, _, _) => emitCount++);

		var saved = SaveGrassDebugState();
		try
		{
			DebugModule.ForceGrassOverlay = GrassOverlayForceMode.On;
			DebugModule.GrassDensityThreshold = 0;
			DebugModule.GrassVariantOverride = -1;

			GrassOverlayPass.DrawTopFace(in ctx, cover: 0, wx: 0, wy: 0);

			Assert.Equal(1, emitCount);
		}
		finally
		{
			RestoreGrassDebugState(saved);
		}
	}

	// ── Case 4：GrassDensityThreshold 阈值生效 ──
	// cover < threshold 视作裸土跳过；cover >= threshold 才 emit。
	[Fact]
	public void Density_threshold_filters_low_cover()
	{
		var atlas = CreateAtlasWithRegions(InjectedVariantCount);
		var emitCount = 0;
		var ctx = CreateDrawContext(atlas, (_, _, _, _) => emitCount++);

		var saved = SaveGrassDebugState();
		try
		{
			DebugModule.ForceGrassOverlay = GrassOverlayForceMode.Auto;
			DebugModule.GrassVariantOverride = -1;
			DebugModule.GrassDensityThreshold = 100;

			GrassOverlayPass.DrawTopFace(in ctx, cover: 50, wx: 1, wy: 2);
			Assert.Equal(0, emitCount);

			GrassOverlayPass.DrawTopFace(in ctx, cover: 99, wx: 3, wy: 4);
			Assert.Equal(0, emitCount);

			GrassOverlayPass.DrawTopFace(in ctx, cover: 100, wx: 5, wy: 6);
			Assert.Equal(1, emitCount);

			GrassOverlayPass.DrawTopFace(in ctx, cover: 150, wx: 7, wy: 8);
			Assert.Equal(2, emitCount);
		}
		finally
		{
			RestoreGrassDebugState(saved);
		}
	}

	// ── Case 5：GrassVariantOverride 强制单一 variant + 关掉后走 hash fallback ──
	[Fact]
	public void Variant_override_forces_single_variant()
	{
		var atlas = CreateAtlasWithRegions(InjectedVariantCount);
		var observedVariants = new List<int>();
		var ctx = CreateDrawContext(
			atlas,
			(rect, _, _, _) => observedVariants.Add(VariantIndexFromRect(rect)));

		var saved = SaveGrassDebugState();
		try
		{
			DebugModule.ForceGrassOverlay = GrassOverlayForceMode.Auto;
			DebugModule.GrassDensityThreshold = 0;

			DebugModule.GrassVariantOverride = 3;
			for (var i = 0; i < 10; i++)
				GrassOverlayPass.DrawTopFace(in ctx, cover: 200, wx: i * 7, wy: i * 11 - 3);

			Assert.Equal(10, observedVariants.Count);
			Assert.All(observedVariants, idx => Assert.Equal(3, idx));

			observedVariants.Clear();
			DebugModule.GrassVariantOverride = -1;
			for (var i = 0; i < 64; i++)
				GrassOverlayPass.DrawTopFace(in ctx, cover: 200, wx: i, wy: i * 31 + 5);

			Assert.Equal(64, observedVariants.Count);
			var distinctVariants = new HashSet<int>(observedVariants);
			Assert.True(
				distinctVariants.Count > 1,
				$"hash fallback should produce more than one variant; got {distinctVariants.Count}");
			Assert.All(observedVariants, idx => Assert.InRange(idx, 0, InjectedVariantCount - 1));
		}
		finally
		{
			RestoreGrassDebugState(saved);
		}
	}

	// ── Case 6：老存档无 GrassCover 字段，加载后默认全 0 数组；调 OverlayPass 不抛 ──
	[Fact]
	public void Old_save_loads_without_grass_cover_field()
	{
		var coord = new ChunkCoord(2, 3, 0);
		var hadPrevious = SaveModule.DirtyChunkCache.TryGetValue(coord, out var previous);
		SaveModule.DirtyChunkCache[coord] = new ChunkSnapshot
		{
			Cx = coord.Cx,
			Cy = coord.Cy,
			Cz = coord.Cz,
			TerrainIds = new ushort[ChunkData.Area],
			Hardness = new byte[ChunkData.Area],
			Stacks = [],
			Nests = [],
			GrassCover = null,
		};

		try
		{
			var restored = SaveModule.LoadChunkFromCache(coord);

			Assert.NotNull(restored);
			Assert.NotNull(restored!.GrassCover);
			Assert.Equal(ChunkData.Area, restored.GrassCover.Length);
			Assert.True(restored.GrassCover.All(v => v == 0));

			var atlas = CreateAtlasWithRegions(InjectedVariantCount);
			var emitCount = 0;
			var ctx = CreateDrawContext(atlas, (_, _, _, _) => emitCount++);

			var saved = SaveGrassDebugState();
			try
			{
				DebugModule.ForceGrassOverlay = GrassOverlayForceMode.Auto;
				DebugModule.GrassDensityThreshold = 0;
				DebugModule.GrassVariantOverride = -1;

				for (var ly = 0; ly < ChunkData.Size; ly++)
				for (var lx = 0; lx < ChunkData.Size; lx++)
				{
					var idx = ly * ChunkData.Size + lx;
					GrassOverlayPass.DrawTopFace(in ctx, restored.GrassCover[idx], lx, ly);
				}

				// cover==0 → DrawTopFace zero-cost return；无 emit 也无异常。
				Assert.Equal(0, emitCount);
			}
			finally
			{
				RestoreGrassDebugState(saved);
			}
		}
		finally
		{
			if (hadPrevious && previous != null)
				SaveModule.DirtyChunkCache[coord] = previous;
			else
				SaveModule.DirtyChunkCache.Remove(coord);
		}
	}

	// ══════════════════════════════════════════════════════════════════
	//  Helpers
	// ══════════════════════════════════════════════════════════════════

	private static ChunkData GenerateChunk(int cx, int cy, int cz, int seed)
	{
		var chunk = new ChunkData { Coord = new ChunkCoord(cx, cy, cz) };
		SurfaceGenerator.Generate(chunk, seed);
		return chunk;
	}

	private static int VariantIndexFromRect(Rect2 rect) =>
		(int)Math.Round(rect.Position.X / VariantOriginXStride);

	/// <summary>
	/// 默认构造 <see cref="TerrainAtlas"/>（不调 Build，避免 Godot Image API 依赖），
	/// 反射往 _grassOverlayRegions 列表注入 <paramref name="count"/> 个唯一标识 region。
	/// 每个 region 的 Position.X = idx * <see cref="VariantOriginXStride"/>，
	/// 让 spy 拦截到的 EmitFace rect 可以反推 variant 索引。
	/// </summary>
	private static TerrainAtlas CreateAtlasWithRegions(int count)
	{
		var atlas = new TerrainAtlas();
		var listField = typeof(TerrainAtlas).GetField(
			"_grassOverlayRegions",
			BindingFlags.NonPublic | BindingFlags.Instance);
		Assert.NotNull(listField);
		var list = (List<Rect2>)listField!.GetValue(atlas)!;
		list.Clear();
		for (var i = 0; i < count; i++)
			list.Add(new Rect2(i * VariantOriginXStride, 0f, 128f, 64f));
		return atlas;
	}

	private static GrassOverlayDrawContext CreateDrawContext(
		TerrainAtlas atlas,
		Action<Rect2, Vector2, Color, long> emit) =>
		new(
			atlas,
			ScreenPos: Vector2.Zero,
			TopTint: Colors.White,
			SortKey: 0L,
			EmitFace: emit);

	private readonly record struct GrassDebugSnapshot(
		bool ShowGrassCover,
		GrassOverlayForceMode ForceGrassOverlay,
		byte GrassDensityThreshold,
		int GrassVariantOverride);

	private static GrassDebugSnapshot SaveGrassDebugState() => new(
		DebugModule.ShowGrassCover,
		DebugModule.ForceGrassOverlay,
		DebugModule.GrassDensityThreshold,
		DebugModule.GrassVariantOverride);

	private static void RestoreGrassDebugState(GrassDebugSnapshot snapshot)
	{
		DebugModule.ShowGrassCover = snapshot.ShowGrassCover;
		DebugModule.ForceGrassOverlay = snapshot.ForceGrassOverlay;
		DebugModule.GrassDensityThreshold = snapshot.GrassDensityThreshold;
		DebugModule.GrassVariantOverride = snapshot.GrassVariantOverride;
	}
}
