namespace MiniRPG.Core.Data;

/// <summary>
/// Shared 2D grid direction constants to keep adjacency logic consistent.
/// </summary>
public static class GridDirections
{
	public static readonly (int Dx, int Dy)[] Cardinal =
		[(0, -1), (0, 1), (-1, 0), (1, 0)];

	public static readonly (int Dx, int Dy)[] CardinalWithOrigin =
		[(0, 0), ..Cardinal];
}
