using System;

namespace MiniRPG.Core.World;

/// <summary>
/// 共享视线工具：用于批量 LOS 校验，避免为每个 observer 生成整张 FOV 图。
/// </summary>
public static class VisibilityUtil
{
	public static bool HasLineOfSight(WorldMap world, int ox, int oy, int tx, int ty, int z)
	{
		if (ox == tx && oy == ty) return true;

		int dx = tx - ox;
		int dy = ty - oy;
		int nx = Math.Abs(dx);
		int ny = Math.Abs(dy);
		int stepX = Math.Sign(dx);
		int stepY = Math.Sign(dy);
		int x = ox;
		int y = oy;
		int ix = 0;
		int iy = 0;

		while (ix < nx || iy < ny)
		{
			int decision = (1 + 2 * ix) * ny - (1 + 2 * iy) * nx;
			if (decision == 0)
			{
				x += stepX;
				y += stepY;
				ix++;
				iy++;
			}
			else if (decision < 0)
			{
				x += stepX;
				ix++;
			}
			else
			{
				y += stepY;
				iy++;
			}

			if (x == tx && y == ty)
				return true;

			if (world.BlocksSight(x, y, z))
				return false;
		}

		return true;
	}
}
