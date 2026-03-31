using System;
using System.Collections.Generic;

namespace MiniRPG.Core.World;

/// <summary>
/// 递归对称 Shadowcasting FOV。
/// 使用 Albert Ford 的对称 shadowcasting 变体，保证 A 看到 B ⟺ B 看到 A。
/// </summary>
public static class ShadowcastFOV
{
	public delegate bool IsOpaqueFunc(int x, int y);

	/// <summary>全向 FOV。</summary>
	public static HashSet<(int X, int Y)> Compute(int ox, int oy, int radius, IsOpaqueFunc isOpaque)
	{
		var visible = new HashSet<(int, int)> { (ox, oy) };
		for (int i = 0; i < 4; i++)
			ScanQuadrant(visible, ox, oy, radius, i, isOpaque);
		return visible;
	}

	/// <summary>
	/// 朝向 FOV：从预计算的完整视野集合中按朝向裁剪距离。
	/// 传入 fullVisible 避免重复 shadowcast。
	/// </summary>
	public static HashSet<(int X, int Y)> ComputeDirectional(
		int ox, int oy, int frontRadius, int rearRadius,
		int facingX, int facingY, HashSet<(int X, int Y)> fullVisible)
	{
		if (frontRadius == rearRadius)
			return new HashSet<(int X, int Y)>(fullVisible);

		long frontSq = (long)frontRadius * frontRadius;
		long rearSq = (long)rearRadius * rearRadius;

		var result = new HashSet<(int X, int Y)>(fullVisible.Count);
		foreach (var (cx, cy) in fullVisible)
		{
			if (cx == ox && cy == oy) { result.Add((cx, cy)); continue; }
			int dx = cx - ox, dy = cy - oy;
			long distSq = (long)dx * dx + (long)dy * dy;
			int dot = dx * facingX + dy * facingY;
			if (distSq <= (dot >= 0 ? frontSq : rearSq))
				result.Add((cx, cy));
		}
		return result;
	}

	/// <summary>
	/// 四象限扫描。每个象限 (cardinal) 对应两个八分区（row 方向 + col 方向）。
	/// cardinal: 0=North, 1=East, 2=South, 3=West
	/// </summary>
	private static void ScanQuadrant(
		HashSet<(int, int)> visible, int ox, int oy, int radius,
		int cardinal, IsOpaqueFunc isOpaque)
	{
		ScanRow(visible, ox, oy, radius, cardinal, 1, -1.0f, 1.0f, isOpaque);
	}

	/// <summary>
	/// 递归扫描一行（row depth）。
	/// slope 定义为 col/row，范围 [-1, 1]。
	/// startSlope 是扫描区间的左边界（较小值），endSlope 是右边界（较大值）。
	/// </summary>
	private static void ScanRow(
		HashSet<(int, int)> visible, int ox, int oy, int radius,
		int cardinal, int depth, float startSlope, float endSlope,
		IsOpaqueFunc isOpaque)
	{
		if (depth > radius) return;
		if (startSlope > endSlope) return;

		int minCol = RoundTiesUp(depth * startSlope);
		int maxCol = RoundTiesDown(depth * endSlope);

		bool prevOpaque = false;
		bool prevSet = false;

		for (int col = minCol; col <= maxCol; col++)
		{
			int wx, wy;
			switch (cardinal)
			{
				case 0: wx = ox + col; wy = oy - depth; break; // North
				case 1: wx = ox + depth; wy = oy + col; break; // East
				case 2: wx = ox + col; wy = oy + depth; break; // South
				default: wx = ox - depth; wy = oy + col; break; // West
			}

			long distSq = (long)(wx - ox) * (wx - ox) + (long)(wy - oy) * (wy - oy);
			if (distSq > (long)radius * radius) continue;

			bool isSymmetric = col >= (int)Math.Ceiling(depth * startSlope)
			                && col <= (int)Math.Floor(depth * endSlope);

			bool opaque = isOpaque(wx, wy);

			if (opaque || isSymmetric)
				visible.Add((wx, wy));

			if (prevSet)
			{
				if (prevOpaque && !opaque)
				{
					startSlope = Slope(depth, col);
				}
				if (!prevOpaque && opaque)
				{
					float newEnd = Slope(depth, col);
					ScanRow(visible, ox, oy, radius, cardinal, depth + 1, startSlope, newEnd, isOpaque);
				}
			}

			prevOpaque = opaque;
			prevSet = true;
		}

		if (prevSet && !prevOpaque)
		{
			ScanRow(visible, ox, oy, radius, cardinal, depth + 1, startSlope, endSlope, isOpaque);
		}
	}

	private static float Slope(int depth, int col) =>
		(2.0f * col - 1.0f) / (2.0f * depth);

	private static int RoundTiesUp(float v) =>
		(int)Math.Floor(v + 0.5f);

	private static int RoundTiesDown(float v) =>
		(int)Math.Ceiling(v - 0.5f);
}
