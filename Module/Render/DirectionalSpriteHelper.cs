using System;

namespace MiniRPG.Module.Render;

public static class DirectionalSpriteHelper
{
	public const int DefaultDirectionRow = 1;

	public static (int Dx, int Dy) NormalizeFacing(int dx, int dy)
	{
		dx = Math.Sign(dx);
		dy = Math.Sign(dy);
		return dx == 0 && dy == 0 ? (0, 1) : (dx, dy);
	}

	public static int ResolveDirectionRow(int dx, int dy)
	{
		(dx, dy) = NormalizeFacing(dx, dy);
		return (dx, dy) switch
		{
			(1, -1) => 0,
			(1, 0) => 1,
			(1, 1) => 2,
			(0, 1) => 3,
			(-1, 1) => 4,
			(-1, 0) => 5,
			(-1, -1) => 6,
			(0, -1) => 7,
			_ => DefaultDirectionRow,
		};
	}
}
