using System;

namespace MiniRPG.Core.World;

/// <summary>
/// 世界坐标：格子级别的三维整数坐标。
/// X/Y 为水平面，Z 为深度（0=地表，正数=地下）。
/// </summary>
public readonly record struct WorldCoord(int X, int Y, int Z)
{
	public static WorldCoord operator +(WorldCoord a, WorldCoord b) =>
		new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

	public static WorldCoord operator -(WorldCoord a, WorldCoord b) =>
		new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

	public int ManhattanTo(WorldCoord other) =>
		Math.Abs(X - other.X) + Math.Abs(Y - other.Y) + Math.Abs(Z - other.Z);

	public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>
/// Chunk 坐标：chunk 级别的三维索引。
/// 每个 chunk 覆盖 ChunkSize x ChunkSize 的水平区域，Z 方向每层一个 chunk。
/// </summary>
public readonly record struct ChunkCoord(int Cx, int Cy, int Cz)
{
	public override string ToString() => $"[{Cx},{Cy},{Cz}]";
}

/// <summary>
/// 坐标系统工具：世界坐标 ↔ Chunk 坐标转换。
/// Chunk 大小固定为 32x32（水平），Z 方向 1:1 映射。
/// </summary>
public static class CoordUtil
{
	public const int ChunkSize = 32;

	/// <summary>Floor-division：对负坐标正确向下取整。</summary>
	private static int FloorDiv(int a, int b) =>
		a >= 0 ? a / b : (a - b + 1) / b;

	/// <summary>Floor-mod：对负坐标返回 [0, b) 范围的余数。</summary>
	private static int FloorMod(int a, int b)
	{
		var r = a % b;
		return r < 0 ? r + b : r;
	}

	public static ChunkCoord WorldToChunk(int x, int y, int z) =>
		new(FloorDiv(x, ChunkSize), FloorDiv(y, ChunkSize), z);

	public static ChunkCoord WorldToChunk(WorldCoord w) =>
		WorldToChunk(w.X, w.Y, w.Z);

	/// <summary>世界坐标 → chunk 内局部坐标 (0..ChunkSize-1)。</summary>
	public static (int Lx, int Ly) WorldToLocal(int x, int y) =>
		(FloorMod(x, ChunkSize), FloorMod(y, ChunkSize));

	/// <summary>Chunk 坐标 + 局部坐标 → 世界坐标。</summary>
	public static WorldCoord LocalToWorld(ChunkCoord c, int lx, int ly) =>
		new(c.Cx * ChunkSize + lx, c.Cy * ChunkSize + ly, c.Cz);

	/// <summary>局部坐标 → 一维数组索引。</summary>
	public static int LocalIndex(int lx, int ly) => ly * ChunkSize + lx;

	/// <summary>一维数组索引 → 局部坐标。</summary>
	public static (int Lx, int Ly) IndexToLocal(int index) =>
		(index % ChunkSize, index / ChunkSize);
}
