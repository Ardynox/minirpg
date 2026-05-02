using System;
using Godot;
using MiniRPG.Core.Debug;

namespace MiniRPG.Module.Render.Surface;

/// <summary>
/// Per-cell render context the renderer hands to <see cref="GrassOverlayPass"/>.
///
/// We avoid coupling to <c>IsometricVoxelRenderer.FaceSpriteCommand</c> (which is
/// private) by going through a cached <c>EmitFace</c> delegate the renderer owns.
/// All fields are value-typed so building one of these per cell does not allocate.
/// </summary>
/// <param name="Atlas">Shared <see cref="TerrainAtlas"/> that already holds the
/// 6 grass overlay variants registered by path C.</param>
/// <param name="ScreenPos">Top face center in screen space; identical to the
/// position the dirt top face was drawn at, so the overlay aligns pixel-perfect.</param>
/// <param name="TopTint">Final tint already applied to the dirt top face
/// (lighting + vision band + editor perspective alpha + optional shadow). The
/// overlay multiplies its alpha by a cover-strength factor so sparse grass does
/// not punch above the dirt's lighting envelope.</param>
/// <param name="SortKey">Sort key of the dirt face below; the overlay is appended
/// to the same batch list right after, so painter-order naturally puts grass on top.</param>
/// <param name="EmitFace">Renderer-owned closure that appends one face sprite
/// command to the batch list. Cached once on the renderer side; no per-cell alloc.</param>
public readonly record struct GrassOverlayDrawContext(
	TerrainAtlas Atlas,
	Vector2 ScreenPos,
	Color TopTint,
	long SortKey,
	Action<Rect2, Vector2, Color, long> EmitFace);

/// <summary>
/// SurfaceCover 抽象的第一个落地实例：在已绘制的土块顶面之上叠加草地变体。
/// 仅画顶面，不动侧面（侧面继续保持土的外观，是契约的硬要求）。
///
/// Wave 2.2 接入：<see cref="MiniRPG.Module.Render.IsometricVoxelRenderer"/>
/// 在土块顶面绘制完成后立即对每个 dirt / grass_block 表面格调 <see cref="DrawTopFace"/>，
/// overlay 走同一张 <see cref="TerrainAtlas.AtlasTexture"/> 让 Godot CanvasItem 自动合批。
///
/// 接口契约见 <c>Artifacts/grass-task-handoff.md</c>（接口 3）。
/// </summary>
public static class GrassOverlayPass
{
	/// <summary>
	/// 草地变体数量。与 <see cref="TerrainAtlas.GrassOverlayVariantCount"/> 对齐。
	/// 路 C 把 procedural 6 张写进了 atlas，本常量是哈希分散的目标基数。
	/// </summary>
	internal const int VariantCount = 6;

	/// <summary>
	/// 变体哈希用的常量种子。Wave 2.2 接入时仍走纯坐标 hash，"同一坐标永远同一变体"
	/// 已经满足 patchy 视觉与同存档稳定的要求；后续 worldSeed 接入留给 Wave 3。
	/// </summary>
	private const int VariantHashSeed = unchecked((int)0x9E3779B9);

	/// <summary>
	/// cover &gt; 0 时叠加层 alpha 的下限，避免极稀疏的草直接淡到看不见。
	/// 0.40 与 atlas procedural overlay v2 的 base alpha 对齐（低饱和写实风 v2 调优 2026-04-19），
	/// 让稀疏草也能读出"绿意"，但仍透出一点底层 dirt 颜色保留"长在土里"的写实感。
	/// </summary>
	private const float MinCoverAlpha = 0.40f;

	// TODO Wave 3.1: DebugModule.ShowGrassCover == true 时叠加密度色阶/数字 debug 可视化；
	// Wave 2.2 仅做最小接入，不实现 debug 色阶。

	/// <summary>
	/// 在已绘制的土块顶面之上叠加草地。仅画顶面，不动侧面。
	/// 调用方负责确保该格的 base terrain 是 dirt / grass_block；本方法只关心 cover 与 debug 钩子。
	/// </summary>
	/// <param name="ctx">渲染上下文。</param>
	/// <param name="cover">本格 GrassCover byte（0..255），0 = 无草。</param>
	/// <param name="wx">世界 X，用于哈希选变体。</param>
	/// <param name="wy">世界 Y，用于哈希选变体。</param>
	public static void DrawTopFace(in GrassOverlayDrawContext ctx, byte cover, int wx, int wy)
	{
		// ── DebugModule 钩子 1：ForceGrassOverlay ──
		// Off 直接退出（用于一键关闭 overlay 验证回归）；
		// On 把 cover 视为 255（用于强制铺满 + 检视贴图整体效果）；
		// Auto 走真实数据。
		var force = DebugModule.ForceGrassOverlay;
		if (force == GrassOverlayForceMode.Off)
			return;
		if (force == GrassOverlayForceMode.On)
			cover = 255;

		if (cover == 0)
			return;

		// ── DebugModule 钩子 2：GrassDensityThreshold ──
		// 渲染端密度阈值；低于阈值视作裸土。默认 0 不裁剪。
		if (cover < DebugModule.GrassDensityThreshold)
			return;

		// ── DebugModule 钩子 3：GrassVariantOverride ──
		// >= 0 且在 atlas 实际注册数范围内 → 强制单一变体（用于检视单张贴图）；
		// 否则走 (wx, wy) hash 选变体，保证同一坐标永远同一变体、视觉无 tile-repeat。
		var variantOverride = DebugModule.GrassVariantOverride;
		var atlasVariantCount = ctx.Atlas.GrassOverlayVariantCount;
		int variant;
		if (variantOverride >= 0 && variantOverride < atlasVariantCount)
			variant = variantOverride;
		else
			variant = SelectVariant(wx, wy, atlasVariantCount);

		EmitVariantSprite(in ctx, variant, cover);
	}

	/// <summary>
	/// 根据世界坐标确定性地选择变体索引（0..variantCount-1）。
	/// 同一 (wx, wy) 永远返回同一变体；分布近似均匀，避免肉眼可见的 tile-repeat。
	/// 暴露为 <c>internal</c> 以便单测做分布手测。
	/// </summary>
	internal static int SelectVariant(int wx, int wy) => SelectVariant(wx, wy, VariantCount);

	private static int SelectVariant(int wx, int wy, int variantCount)
	{
		if (variantCount <= 0)
			return 0;

		unchecked
		{
			uint h = (uint)VariantHashSeed;
			h = (h ^ (uint)wx) * 0x01000193u;
			h = (h ^ (uint)wy) * 0x01000193u;
			h ^= h >> 13;
			h *= 0x5BD1E995u;
			h ^= h >> 15;
			return (int)(h % (uint)variantCount);
		}
	}

	/// <summary>
	/// 取出 variant 对应的图集 region，按 cover 强度调整 alpha 后塞进渲染队列。
	/// SortKey 用 dirt 顶面同值并紧跟其后入队 → painter 顺序自然让草盖在土上；
	/// 走同一张 atlas → DrawTextureRectRegion 自动合批，draw call 不增。
	/// </summary>
	private static void EmitVariantSprite(in GrassOverlayDrawContext ctx, int variant, byte cover)
	{
		if (!ctx.Atlas.TryGetGrassOverlayVariant(variant, out var region))
			return;

		// cover ∈ [1, 255] → alphaScale ∈ [MinCoverAlpha, 1.0]，保留密度差的可感知性。
		var coverNorm = cover / 255f;
		var alphaScale = MinCoverAlpha + (1f - MinCoverAlpha) * coverNorm;

		var top = ctx.TopTint;
		var grassTint = new Color(top.R, top.G, top.B, top.A * alphaScale);

		ctx.EmitFace(region, ctx.ScreenPos, grassTint, ctx.SortKey);
	}
}
