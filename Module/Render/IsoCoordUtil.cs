using Godot;

namespace MiniRPG.Module.Render;

/// <summary>
/// 等距坐标工具：世界 3D 坐标 ↔ 屏幕 2D 坐标转换。
///
/// 等距投影公式（标准 2:1 菱形）：
///   screenX = (worldX - worldY) * TileHalfW
///   screenY = (worldX + worldY) * TileHalfH + worldZ * ZStep
///
/// 其中：
///   TileHalfW = 64  (128/2，等距菱形宽度的一半)
///   TileHalfH = 32  (64/2，等距菱形高度的一半)
///   ZStep     = 64  (每层 Z 的像素偏移 = 方块侧面高度)
///
/// Z 约定：Z=0 为基准地表，Z>0 为地下（屏幕上更低），Z&lt;0 为地上（屏幕上更高）。
///
/// 度量衡口径（详见 <c>Docs/开发约定.md</c>「度量衡口径」节）：
///   1 世界 cell（1 个体素方块）= 真实空间 <b>1.5 m × 1.5 m × 1.5 m</b>。
///   一个 chunk = 32 × 32 cell = 48 m × 48 m。
///   人物 ≈ 1.7 m，sprite 在屏幕上略高出格顶 ≈ 0.2 m，与"人占 1 cell"的逻辑寻路占位保持一致。
/// </summary>
public static class IsoCoordUtil
{
	/// <summary>等距菱形宽度的一半（像素）。</summary>
	public const float TileHalfW = 64f;

	/// <summary>等距菱形高度的一半（像素）。</summary>
	public const float TileHalfH = 32f;

	/// <summary>每层 Z 的垂直像素偏移（= 方块侧面高度，保持正方体比例）。</summary>
	public const float ZStep = 64f;

	/// <summary>
	/// 世界坐标 → 屏幕坐标（等距投影）。
	/// 返回的是方块顶面中心的屏幕位置。
	/// </summary>
	public static Vector2 WorldToScreen(int wx, int wy, int wz)
	{
		var sx = (wx - wy) * TileHalfW;
		var sy = (wx + wy) * TileHalfH + wz * ZStep;
		return new Vector2(sx, sy);
	}

	/// <summary>
	/// 世界坐标 → 屏幕坐标（浮点版本，用于平滑动画）。
	/// </summary>
	public static Vector2 WorldToScreen(float wx, float wy, float wz)
	{
		var sx = (wx - wy) * TileHalfW;
		var sy = (wx + wy) * TileHalfH + wz * ZStep;
		return new Vector2(sx, sy);
	}

	/// <summary>
	/// 屏幕坐标 → 世界坐标（给定 Z 层）。
	/// 等距逆变换：已知 screenX, screenY, targetZ，求 worldX, worldY。
	/// </summary>
	public static (float Wx, float Wy) ScreenToWorld(Vector2 screen, int targetZ)
	{
		// 逆变换：
		//   sx = (wx - wy) * TileHalfW
		//   sy = (wx + wy) * TileHalfH + targetZ * ZStep
		// 令 sy' = sy - targetZ * ZStep
		//   wx - wy = sx / TileHalfW
		//   wx + wy = sy' / TileHalfH
		// 解：
		//   wx = (sx / TileHalfW + sy' / TileHalfH) / 2
		//   wy = (sy' / TileHalfH - sx / TileHalfW) / 2

		var syAdjusted = screen.Y - targetZ * ZStep;
		var a = screen.X / TileHalfW;
		var b = syAdjusted / TileHalfH;
		return ((a + b) * 0.5f, (b - a) * 0.5f);
	}

	/// <summary>
	/// 屏幕坐标 → 世界格子坐标（给定 Z 层，四舍五入到最近格子）。
	/// </summary>
	public static (int Wx, int Wy) ScreenToWorldCell(Vector2 screen, int targetZ)
	{
		var (fx, fy) = ScreenToWorld(screen, targetZ);
		return ((int)Mathf.Floor(fx + 0.5f), (int)Mathf.Floor(fy + 0.5f));
	}

	/// <summary>
	/// 计算等距绘制排序键。
	/// painter's algorithm：按 (x+y) 从小到大（远到近），同对角线内按 Z 从大到小（低到高）。
	/// Z 约定：Z 越大越深（地下），在屏幕上应先绘制（被上层覆盖）。
	/// </summary>
	public static long SortKey(int wx, int wy, int wz)
	{
		// 主排序：x+y（远到近），次排序：z 降序（深到浅）。
		// 注意：不能对 depth 做位掩码，否则负值会回绕，破坏同对角线稳定排序。
		// 这里将 depth 映射为无符号单调值：z 越大（越深）→ depthOrder 越小（越先绘制）。
		long diagonal = wx + wy;
		var depthOrder = int.MaxValue - wz;
		return (diagonal << 32) | (uint)depthOrder;
	}

	/// <summary>
	/// 比较两个世界格的稳定绘制顺序。
	/// 在对角线和深度完全相同的情况下，继续按屏幕 X 从左到右做 tie-break，
	/// 避免共享边缘的面片在重绘时因为不稳定排序发生抖动。
	/// </summary>
	public static int CompareSortOrder(int ax, int ay, int az, int bx, int by, int bz)
	{
		var diagonalCompare = (ax + ay).CompareTo(bx + by);
		if (diagonalCompare != 0)
			return diagonalCompare;

		var depthCompare = bz.CompareTo(az);
		if (depthCompare != 0)
			return depthCompare;

		var screenXCompare = (ax - ay).CompareTo(bx - by);
		if (screenXCompare != 0)
			return screenXCompare;

		var xCompare = ax.CompareTo(bx);
		if (xCompare != 0)
			return xCompare;

		return ay.CompareTo(by);
	}
}
