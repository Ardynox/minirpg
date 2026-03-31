using System;
using System.Collections.Generic;

namespace MiniRPG.Core.World;

/// <summary>
/// 统一寻路入口：短距离（≤16格曼哈顿）用 A*，长距离用 JPS。
/// 四方向移动（上下左右），不支持对角线。
/// </summary>
public static class Pathfinding
{
	public delegate bool IsWalkableFunc(int x, int y);

	private const int AStarThreshold = 16;
	private const int MaxSearchNodes = 2048;

	private static readonly (int Dx, int Dy)[] Dirs = { (0, -1), (0, 1), (-1, 0), (1, 0) };

	/// <summary>
	/// 寻路：返回从 (sx,sy) 到 (tx,ty) 的路径（不含起点，含终点）。
	/// 找不到路径返回 null。
	/// </summary>
	public static List<(int X, int Y)>? FindPath(int sx, int sy, int tx, int ty, IsWalkableFunc isWalkable)
	{
		if (sx == tx && sy == ty) return [];

		var dist = Math.Abs(tx - sx) + Math.Abs(ty - sy);
		return dist <= AStarThreshold
			? AStar(sx, sy, tx, ty, isWalkable)
			: JPS(sx, sy, tx, ty, isWalkable);
	}

	/// <summary>
	/// 只取下一步：返回路径上的第一个格子坐标，或 null。
	/// </summary>
	public static (int X, int Y)? NextStep(int sx, int sy, int tx, int ty, IsWalkableFunc isWalkable)
	{
		var path = FindPath(sx, sy, tx, ty, isWalkable);
		return path is { Count: > 0 } ? path[0] : null;
	}

	// ══════════════════════════════════════════════════════
	//  A* (短距离，≤16格)
	// ══════════════════════════════════════════════════════

	private static List<(int, int)>? AStar(int sx, int sy, int tx, int ty, IsWalkableFunc isWalkable)
	{
		var open = new PriorityQueue<(int X, int Y), int>();
		var cameFrom = new Dictionary<(int, int), (int, int)>();
		var gScore = new Dictionary<(int, int), int>();

		var start = (sx, sy);
		var goal = (tx, ty);
		gScore[start] = 0;
		open.Enqueue(start, Heuristic(sx, sy, tx, ty));

		int nodesExpanded = 0;

		while (open.Count > 0)
		{
			var current = open.Dequeue();
			if (current == goal)
				return ReconstructPath(cameFrom, current);

			if (++nodesExpanded > MaxSearchNodes)
				return null;

			var currentG = gScore[current];

			foreach (var (dx, dy) in Dirs)
			{
				var nx = current.X + dx;
				var ny = current.Y + dy;
				var neighbor = (nx, ny);

				if (neighbor != goal && !isWalkable(nx, ny))
					continue;

				var tentativeG = currentG + 1;
				if (gScore.TryGetValue(neighbor, out var existingG) && tentativeG >= existingG)
					continue;

				gScore[neighbor] = tentativeG;
				cameFrom[neighbor] = current;
				open.Enqueue(neighbor, tentativeG + Heuristic(nx, ny, tx, ty));
			}
		}

		return null;
	}

	// ══════════════════════════════════════════════════════
	//  JPS - Jump Point Search (长距离，>16格)
	// ══════════════════════════════════════════════════════

	private static List<(int, int)>? JPS(int sx, int sy, int tx, int ty, IsWalkableFunc isWalkable)
	{
		var open = new PriorityQueue<(int X, int Y), int>();
		var cameFrom = new Dictionary<(int, int), (int, int)>();
		var gScore = new Dictionary<(int, int), int>();

		var start = (sx, sy);
		var goal = (tx, ty);
		gScore[start] = 0;
		open.Enqueue(start, Heuristic(sx, sy, tx, ty));

		int nodesExpanded = 0;

		while (open.Count > 0)
		{
			var current = open.Dequeue();
			if (current == goal)
				return ReconstructJPSPath(cameFrom, current);

			if (++nodesExpanded > MaxSearchNodes)
				return null;

			var currentG = gScore[current];

			var neighbors = cameFrom.ContainsKey(current)
				? GetJPSNeighbors(current, cameFrom[current], isWalkable)
				: GetAllNeighbors(current, isWalkable, goal);

			foreach (var (nx, ny) in neighbors)
			{
				var jumpResult = Jump(current.X, current.Y, nx - current.X, ny - current.Y, tx, ty, isWalkable);
				if (jumpResult == null) continue;

				var (jx, jy) = jumpResult.Value;
				var jumpDist = Math.Abs(jx - current.X) + Math.Abs(jy - current.Y);
				var tentativeG = currentG + jumpDist;

				if (gScore.TryGetValue((jx, jy), out var existingG) && tentativeG >= existingG)
					continue;

				gScore[(jx, jy)] = tentativeG;
				cameFrom[(jx, jy)] = current;
				open.Enqueue((jx, jy), tentativeG + Heuristic(jx, jy, tx, ty));
			}
		}

		return null;
	}

	private static (int, int)? Jump(int cx, int cy, int dx, int dy, int tx, int ty, IsWalkableFunc isWalkable)
	{
		var nx = cx + dx;
		var ny = cy + dy;

		if (!isWalkable(nx, ny) && !(nx == tx && ny == ty))
			return null;

		if (nx == tx && ny == ty)
			return (nx, ny);

		if (HasForcedNeighbor(nx, ny, dx, dy, isWalkable))
			return (nx, ny);

		var jumpLimit = MaxSearchNodes;
		if (--jumpLimit <= 0) return null;

		return Jump(nx, ny, dx, dy, tx, ty, isWalkable);
	}

	private static bool HasForcedNeighbor(int x, int y, int dx, int dy, IsWalkableFunc isWalkable)
	{
		if (dx != 0)
		{
			if (!isWalkable(x, y - 1) && isWalkable(x + dx, y - 1)) return true;
			if (!isWalkable(x, y + 1) && isWalkable(x + dx, y + 1)) return true;
		}
		else
		{
			if (!isWalkable(x - 1, y) && isWalkable(x - 1, y + dy)) return true;
			if (!isWalkable(x + 1, y) && isWalkable(x + 1, y + dy)) return true;
		}
		return false;
	}

	private static List<(int, int)> GetJPSNeighbors((int X, int Y) current, (int X, int Y) parent, IsWalkableFunc isWalkable)
	{
		var result = new List<(int, int)>();
		var dx = Math.Sign(current.X - parent.X);
		var dy = Math.Sign(current.Y - parent.Y);

		if (dx != 0)
		{
			if (isWalkable(current.X + dx, current.Y))
				result.Add((current.X + dx, current.Y));
			if (!isWalkable(current.X, current.Y - 1) && isWalkable(current.X + dx, current.Y - 1))
				result.Add((current.X + dx, current.Y - 1));
			if (!isWalkable(current.X, current.Y + 1) && isWalkable(current.X + dx, current.Y + 1))
				result.Add((current.X + dx, current.Y + 1));
		}
		else if (dy != 0)
		{
			if (isWalkable(current.X, current.Y + dy))
				result.Add((current.X, current.Y + dy));
			if (!isWalkable(current.X - 1, current.Y) && isWalkable(current.X - 1, current.Y + dy))
				result.Add((current.X - 1, current.Y + dy));
			if (!isWalkable(current.X + 1, current.Y) && isWalkable(current.X + 1, current.Y + dy))
				result.Add((current.X + 1, current.Y + dy));
		}

		return result;
	}

	private static List<(int, int)> GetAllNeighbors((int X, int Y) pos, IsWalkableFunc isWalkable, (int, int) goal)
	{
		var result = new List<(int, int)>();
		foreach (var (dx, dy) in Dirs)
		{
			var nx = pos.X + dx;
			var ny = pos.Y + dy;
			if (isWalkable(nx, ny) || (nx, ny) == goal)
				result.Add((nx, ny));
		}
		return result;
	}

	// ══════════════════════════════════════════════════════
	//  共用工具
	// ══════════════════════════════════════════════════════

	private static int Heuristic(int ax, int ay, int bx, int by) =>
		Math.Abs(ax - bx) + Math.Abs(ay - by);

	private static List<(int, int)> ReconstructPath(Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
	{
		var path = new List<(int, int)> { current };
		while (cameFrom.TryGetValue(current, out var prev))
		{
			current = prev;
			path.Add(current);
		}
		path.Reverse();
		path.RemoveAt(0);
		return path;
	}

	/// <summary>JPS 路径重建：跳点之间需要插值补全中间步。</summary>
	private static List<(int, int)> ReconstructJPSPath(Dictionary<(int, int), (int, int)> cameFrom, (int, int) current)
	{
		var jumpPoints = new List<(int, int)> { current };
		while (cameFrom.TryGetValue(current, out var prev))
		{
			current = prev;
			jumpPoints.Add(current);
		}
		jumpPoints.Reverse();

		var path = new List<(int, int)>();
		for (int i = 1; i < jumpPoints.Count; i++)
		{
			var (fx, fy) = jumpPoints[i - 1];
			var (gx, gy) = jumpPoints[i];
			InterpolateSteps(path, fx, fy, gx, gy);
		}
		return path;
	}

	private static void InterpolateSteps(List<(int, int)> path, int fx, int fy, int tx, int ty)
	{
		var x = fx;
		var y = fy;
		while (x != tx || y != ty)
		{
			if (x < tx) x++;
			else if (x > tx) x--;
			else if (y < ty) y++;
			else if (y > ty) y--;
			path.Add((x, y));
		}
	}
}
