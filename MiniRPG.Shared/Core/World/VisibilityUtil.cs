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

	public static bool HasLineOfSight3D(WorldMap world, int ox, int oy, int oz, int tx, int ty, int tz)
	{
		if (ox == tx && oy == ty && oz == tz) return true;

		if (oz == tz)
			return HasLineOfSight(world, ox, oy, tx, ty, oz);

		int dx = tx - ox;
		int dy = ty - oy;
		int dz = tz - oz;
		int nx = Math.Abs(dx);
		int ny = Math.Abs(dy);
		int nz = Math.Abs(dz);
		int sx = dx > 0 ? 1 : dx < 0 ? -1 : 0;
		int sy = dy > 0 ? 1 : dy < 0 ? -1 : 0;
		int sz = dz > 0 ? 1 : dz < 0 ? -1 : 0;

		int x = ox, y = oy, z = oz;
		double tMaxX = nx > 0 ? 0.5 / nx : double.MaxValue;
		double tMaxY = ny > 0 ? 0.5 / ny : double.MaxValue;
		double tMaxZ = nz > 0 ? 0.5 / nz : double.MaxValue;
		double tDeltaX = nx > 0 ? 1.0 / nx : double.MaxValue;
		double tDeltaY = ny > 0 ? 1.0 / ny : double.MaxValue;
		double tDeltaZ = nz > 0 ? 1.0 / nz : double.MaxValue;

		int steps = nx + ny + nz;
		for (int i = 0; i < steps; i++)
		{
			if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
			{
				x += sx;
				tMaxX += tDeltaX;
			}
			else if (tMaxY <= tMaxX && tMaxY <= tMaxZ)
			{
				y += sy;
				tMaxY += tDeltaY;
			}
			else
			{
				z += sz;
				tMaxZ += tDeltaZ;
			}

			if (x == tx && y == ty && z == tz)
				return true;

			if (world.BlocksSight(x, y, z))
				return false;
		}

		return true;
	}

	public interface IBlocksSightProvider
	{
		bool BlocksSight(int x, int y, int z);
	}

	public static bool HasLineOfSight3D(IBlocksSightProvider provider, int ox, int oy, int oz, int tx, int ty, int tz)
	{
		if (ox == tx && oy == ty && oz == tz) return true;

		int dx = tx - ox;
		int dy = ty - oy;
		int dz = tz - oz;
		int nx = Math.Abs(dx);
		int ny = Math.Abs(dy);
		int nz = Math.Abs(dz);
		int sx = dx > 0 ? 1 : dx < 0 ? -1 : 0;
		int sy = dy > 0 ? 1 : dy < 0 ? -1 : 0;
		int sz = dz > 0 ? 1 : dz < 0 ? -1 : 0;

		int x = ox, y = oy, z = oz;
		double tMaxX = nx > 0 ? 0.5 / nx : double.MaxValue;
		double tMaxY = ny > 0 ? 0.5 / ny : double.MaxValue;
		double tMaxZ = nz > 0 ? 0.5 / nz : double.MaxValue;
		double tDeltaX = nx > 0 ? 1.0 / nx : double.MaxValue;
		double tDeltaY = ny > 0 ? 1.0 / ny : double.MaxValue;
		double tDeltaZ = nz > 0 ? 1.0 / nz : double.MaxValue;

		int steps = nx + ny + nz;
		for (int i = 0; i < steps; i++)
		{
			if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
			{
				x += sx;
				tMaxX += tDeltaX;
			}
			else if (tMaxY <= tMaxX && tMaxY <= tMaxZ)
			{
				y += sy;
				tMaxY += tDeltaY;
			}
			else
			{
				z += sz;
				tMaxZ += tDeltaZ;
			}

			if (x == tx && y == ty && z == tz)
				return true;

			if (provider.BlocksSight(x, y, z))
				return false;
		}

		return true;
	}
}
