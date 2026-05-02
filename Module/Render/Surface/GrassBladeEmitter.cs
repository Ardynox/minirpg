using System;
using Godot;
using MiniRPG.Module.Render;

namespace MiniRPG.Module.Render.Surface;

/// <summary>
/// 确定性 blade 实例发射器：给定 tile (wx, wy) + cover，把 N 根 blade 塞进
/// <see cref="GrassBladeField"/> 的 pending buffer。
///
/// 两路调用方共用本发射器：
///   1. <c>App.Previews.GrassSurfacePreview</c>（F6 预览）
///   2. <c>Module.Render.IsometricVoxelRenderer.TryDrawGrassOverlay</c>（主渲染器）
///
/// 每根 blade 的位置/宽高/倾角/风相位/色板/色相扰动全部从 (worldSeed, wx, wy, i) hash 得到，
/// 保证同存档 / 同种子下布局可复现；跳过菱形蒙版外的位置。
/// </summary>
public static class GrassBladeEmitter
{
	private const float TileHalfW = IsoCoordUtil.TileHalfW;
	private const float TileHalfH = IsoCoordUtil.TileHalfH;

	/// <summary>
	/// 往 <paramref name="field"/> 里追加这一格的 blade 实例。<paramref name="cover"/>=0 时跳过。
	/// </summary>
	/// <param name="field">目标 blade field（调用方已 <see cref="GrassBladeField.BeginFill"/>）。</param>
	/// <param name="screenPos">顶面中心屏幕坐标（blade 基座位置）。</param>
	/// <param name="wx">世界 X（hash 种子）。</param>
	/// <param name="wy">世界 Y（hash 种子）。</param>
	/// <param name="worldSeed">世界种子（让换档存档 blade 不重复）。</param>
	/// <param name="cover">本格 GrassCover byte（0..255），0 直接跳过。</param>
	/// <param name="bladesPerTile">该 tile 最多 blade 数，实际会按 <paramref name="cover"/> 线性缩放。</param>
	/// <param name="variantOverride">&gt;= 0 时强制单变体，负数走坐标 hash。</param>
	public static void EmitTile(
		GrassBladeField field,
		Vector2 screenPos,
		int wx,
		int wy,
		int worldSeed,
		byte cover,
		int bladesPerTile,
		int variantOverride)
	{
		if (cover == 0 || bladesPerTile <= 0)
			return;

		var variant = variantOverride >= 0 ? variantOverride : SelectVariantHash(wx, wy);

		// 按 cover 比例缩放 blade 数
		var tileBlades = (int)(cover / 255f * bladesPerTile);
		if (tileBlades <= 0)
			return;

		// ── 多尺度高度 clumping（学 Shadertoy Xsf3zX 的 Noise(p*2)*.75+Noise(p)*.35+Noise(p*.5)*.2）──
		// 整个 tile 所有 blade 共用这两个广域噪声，让一片 blade 集体高 / 矮，不再逐根随机独立。
		// 大尺度：~12 格周期，决定"这一片"整体高低；中尺度：~3 格周期，决定小草丛。
		var broadTall = ValueNoise2D01(wx, wy, scale: 0.08f, salt: 0xA139u); // 0..1 缓变
		var midTall   = ValueNoise2D01(wx, wy, scale: 0.35f, salt: 0xB2D1u); // 0..1 中变
		var tileHeightBias = broadTall * 0.55f + midTall * 0.30f;            // 0..0.85

		// 菱形略超边界：让 blade 可以稍微跨到邻接 tile 范围内，tile 接缝模糊，整片看起来更连续。
		// 1.0 = 正好贴菱形边；1.08 ≈ 出 8% 的温和越界。
		const float DiamondBoundary = 1.08f;

		for (var i = 0; i < tileBlades; i++)
		{
			var seed = HashBlade(wx, wy, i, worldSeed);

			// ── 菱形内 rejection sampling：4 次 salt 扰动尝试，命中即用 ──
			// 原实现用 continue 直接扔掉菱形外样本 → 平均丢失 ~50% blade。
			// 现在：最多 4 次扰动找到菱形内点，4 次全失败 (概率 ~0.5⁴ ≈ 6%) 再做 L1 归一化把点"贴回边界"。
			// 结果：用户设 N 根 blade/tile，实际就产出 N 根（之前只产出 ~N/2）。
			float ux = Hash01(seed, 1) * 2f - 1f;
			float uy = Hash01(seed, 2) * 2f - 1f;
			var attempt = 1;
			while (Math.Abs(ux) + Math.Abs(uy) > DiamondBoundary && attempt < 4)
			{
				ux = Hash01(seed, 3 + attempt * 2) * 2f - 1f;
				uy = Hash01(seed, 4 + attempt * 2) * 2f - 1f;
				attempt++;
			}
			var l1 = Math.Abs(ux) + Math.Abs(uy);
			if (l1 > DiamondBoundary)
			{
				var s = DiamondBoundary / l1;
				ux *= s;
				uy *= s;
			}

			var basePos = screenPos + new Vector2(ux * TileHalfW, uy * TileHalfH);

			// ── 尺寸：单根草叶更饱满 ──
			// 高度 = 基底 5 + 广域 clumping (0..9) + 单根 jitter (0..3) → 5..17 px（原 4..13）
			// 宽度 = 1.1..2.4 px（原 0.9..1.8），让每根视觉覆盖面更大
			// 广域分量占大头 → 草丛起伏看得见；个体 jitter 保留打破齐刷刷的视觉。
			var height = 5f + tileHeightBias * 9f   + Hash01(seed, 3) * 3f;  // 5..17 px
			var width  = 1.1f + Hash01(seed, 4) * 1.3f;                      // 1.1..2.4 px
			var tilt   = (Hash01(seed, 5) - 0.5f) * 0.60f;  // ±0.3 rad ≈ ±17°
			var phase  = Hash01(seed, 6) * Mathf.Tau;       // 0..2π

			var palBase = (variant * 2) % 8;
			var palIdx  = palBase + (Hash01(seed, 7) < 0.30f ? 1 : 0);
			var hueJit  = Hash01(seed, 8) - 0.5f;

			field.AddBlade(new GrassBladeField.BladeInstance(
				ScreenPos:  basePos,
				WidthPx:    width,
				HeightPx:   height,
				TiltRad:    tilt,
				WindPhase:  phase,
				PaletteIdx: palIdx,
				HueJitter:  hueJit));
		}
	}

	/// <summary>
	/// 坐标 hash → variant 索引（0..5），与 <see cref="GrassOverlayPass.SelectVariant(int,int)"/> 同族。
	/// </summary>
	public static int SelectVariantHash(int wx, int wy)
	{
		unchecked
		{
			uint h = 0x9E3779B9u;
			h = (h ^ (uint)wx) * 0x01000193u;
			h = (h ^ (uint)wy) * 0x01000193u;
			h ^= h >> 13;
			h *= 0x5BD1E995u;
			h ^= h >> 15;
			return (int)(h % 6u);
		}
	}

	private static uint HashBlade(int wx, int wy, int i, int seed)
	{
		unchecked
		{
			uint h = 2166136261u ^ (uint)seed;
			h = (h ^ (uint)wx) * 16777619u;
			h = (h ^ (uint)wy) * 16777619u;
			h = (h ^ (uint)i)  * 16777619u;
			h ^= h >> 13;
			h *= 0x5BD1E995u;
			h ^= h >> 15;
			return h;
		}
	}

	private static float Hash01(uint seed, int saltK)
	{
		unchecked
		{
			var h = seed;
			h = (h ^ (uint)saltK) * 0x9E3779B9u;
			h ^= h >> 13;
			h *= 0x85EBCA6Bu;
			h ^= h >> 16;
			return (h & 0x00FFFFFFu) / (float)0x01000000;
		}
	}

	/// <summary>
	/// 整数坐标下的双线性 value noise，smoothstep 插值（参考 Shadertoy f*f*(3-2f) 版）。
	/// 专为 tile 尺度广域变化使用：scale=0.08 ≈ 12 格周期，scale=0.35 ≈ 3 格周期。
	/// 输出 0..1。同 (wx, wy, scale, salt) 恒定，保证存档确定性。
	/// </summary>
	private static float ValueNoise2D01(int wx, int wy, float scale, uint salt)
	{
		var px = wx * scale;
		var py = wy * scale;
		var ix = (int)Math.Floor(px);
		var iy = (int)Math.Floor(py);
		var fx = px - ix;
		var fy = py - iy;
		fx = fx * fx * (3f - 2f * fx);
		fy = fy * fy * (3f - 2f * fy);

		var h00 = Hash2D01(ix,     iy,     salt);
		var h10 = Hash2D01(ix + 1, iy,     salt);
		var h01 = Hash2D01(ix,     iy + 1, salt);
		var h11 = Hash2D01(ix + 1, iy + 1, salt);

		var hx0 = h00 + (h10 - h00) * fx;
		var hx1 = h01 + (h11 - h01) * fx;
		return hx0 + (hx1 - hx0) * fy;
	}

	private static float Hash2D01(int wx, int wy, uint salt)
	{
		unchecked
		{
			uint h = 2166136261u ^ salt;
			h = (h ^ (uint)wx) * 16777619u;
			h = (h ^ (uint)wy) * 16777619u;
			h ^= h >> 13;
			h *= 0x85EBCA6Bu;
			h ^= h >> 16;
			return (h & 0x00FFFFFFu) / (float)0x01000000;
		}
	}
}
