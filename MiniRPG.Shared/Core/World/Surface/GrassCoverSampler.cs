using System;
using MiniRPG.Core.Data;
using MiniRPG.Core.World.Noise;

namespace MiniRPG.Core.World.Surface;

/// <summary>
/// 草地密度采样器（Wave 1 路 B）。
///
/// 仅根据 (worldSeed, wx, wy, baseTerrainId, exposedToSky, underwater) 计算单格草地密度，
/// 不读 / 不写任何运行时状态；同一坐标 + 同一种子在任何调用顺序下都返回相同的字节。
///
/// 接入点（Wave 2.1）：<c>SurfaceGenerator</c> 在确定 surfaceZ 后为该格调一次。
/// </summary>
public static class GrassCoverSampler
{
	/// <summary>噪声采样的世界单位缩放：约 1/14 ≈ 14 格一个 patch 周期。</summary>
	private const double NoiseScale = 0.073;

	/// <summary>低于此阈值（归一化后 [0,1] 空间内）视为裸土，返回 0。</summary>
	private const double BareThreshold = 0.42;

	/// <summary>区分草地通道的种子偏移，避免与高度图 / 湿度图共用 PerlinNoise。</summary>
	private const int GrassSeedSalt = unchecked((int)0xA17C0DE5);

	private static readonly object CacheLock = new();
	private static int _cachedSeedKey;
	private static PerlinNoise? _cachedNoise;

	/// <summary>
	/// 在生成阶段为单个格子采样草地密度。
	/// 必须是确定性的：同一 (worldSeed, wx, wy) 永远返回同样结果。
	/// </summary>
	/// <param name="worldSeed">世界种子（来自 WorldConfig 等）。</param>
	/// <param name="wx">世界 X。</param>
	/// <param name="wy">世界 Y。</param>
	/// <param name="baseTerrainId">该格的 base terrain（来自 TerrainIds）。</param>
	/// <param name="exposedToSky">该格上方是否畅通（不被实心格挡住）。</param>
	/// <param name="underwater">该格是否在水下。</param>
	/// <returns>0 = 无草；1..255 = 密度。</returns>
	public static byte Sample(
		int worldSeed,
		int wx,
		int wy,
		ushort baseTerrainId,
		bool exposedToSky,
		bool underwater)
	{
		if (!exposedToSky || underwater)
			return 0;

		if (!IsGrassGrowable(baseTerrainId))
			return 0;

		var noise = GetOrCreateNoise(worldSeed);

		// 2 频叠加：低频决定 patch 形状，高频给同一 patch 内细变化。
		var raw = noise.FBM2D(wx * NoiseScale, wy * NoiseScale, octaves: 2, persistence: 0.5);

		// raw ∈ ~[-1, 1]，映射到 [0, 1]。
		var t = (raw + 1.0) * 0.5;
		if (t <= BareThreshold)
			return 0;

		// 阈值之上线性铺到 [1, 255]，保留 0 专门表示"无草"。
		var normalized = (t - BareThreshold) / (1.0 - BareThreshold);
		var v = (int)Math.Round(normalized * 254.0) + 1;
		if (v < 1) v = 1;
		if (v > 255) v = 255;
		return (byte)v;
	}

	private static bool IsGrassGrowable(ushort baseTerrainId)
	{
		var def = TerrainRegistry.Get(baseTerrainId);
		if (def == null) return false;
		return def.StringId == Terrains.Dirt || def.StringId == Terrains.GrassBlock;
	}

	private static PerlinNoise GetOrCreateNoise(int worldSeed)
	{
		var key = worldSeed ^ GrassSeedSalt;
		// 单条目缓存：一个 chunk 内所有调用都命中，跨 chunk 切换 seed 概率极低。
		lock (CacheLock)
		{
			if (_cachedNoise == null || _cachedSeedKey != key)
			{
				_cachedSeedKey = key;
				_cachedNoise = new PerlinNoise(key);
			}
			return _cachedNoise;
		}
	}
}
