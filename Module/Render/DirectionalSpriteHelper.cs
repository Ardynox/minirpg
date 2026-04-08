using System;

namespace MiniRPG.Module.Render;

public static class DirectionalSpriteHelper
{
	public const int DefaultDirectionRow = 0;

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
			(0, 1) => 0,
			(-1, 1) => 1,
			(-1, 0) => 2,
			(-1, -1) => 3,
			(0, -1) => 4,
			(1, -1) => 5,
			(1, 0) => 6,
			(1, 1) => 7,
			_ => DefaultDirectionRow,
		};
	}
}
