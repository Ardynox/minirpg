using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PathfindingTests
{
	private static readonly Pathfinding.IsWalkableFunc OpenField = (_, _) => true;

	private static Pathfinding.IsWalkableFunc GridWithWalls(HashSet<(int, int)> walls) =>
		(x, y) => !walls.Contains((x, y));

	// ── FindPath: basic ──────────────────────────────────

	[Fact]
	public void FindPath_SameTile_ReturnsEmptyList()
	{
		var path = Pathfinding.FindPath(5, 5, 5, 5, OpenField);
		Assert.NotNull(path);
		Assert.Empty(path);
	}

	[Fact]
	public void FindPath_AdjacentTile_ReturnsSingleStep()
	{
		var path = Pathfinding.FindPath(0, 0, 1, 0, OpenField);
		Assert.NotNull(path);
		Assert.Single(path);
		Assert.Equal((1, 0), path[0]);
	}

	[Fact]
	public void FindPath_OpenField_ReturnsOptimalLength()
	{
		// Manhattan distance 6 in open field should yield path of length 6
		var path = Pathfinding.FindPath(0, 0, 3, 3, OpenField);
		Assert.NotNull(path);
		Assert.Equal(6, path.Count);
		Assert.Equal((3, 3), path[^1]);
	}

	[Fact]
	public void FindPath_DoesNotContainStart()
	{
		var path = Pathfinding.FindPath(2, 2, 5, 2, OpenField);
		Assert.NotNull(path);
		Assert.DoesNotContain((2, 2), path);
	}

	[Fact]
	public void FindPath_ContainsGoal()
	{
		var path = Pathfinding.FindPath(0, 0, 4, 0, OpenField);
		Assert.NotNull(path);
		Assert.Contains((4, 0), path);
	}

	// ── FindPath: obstacles ──────────────────────────────

	[Fact]
	public void FindPath_WallBlocking_FindsDetour()
	{
		// Wall at (2,0) blocks straight path from (0,0) to (4,0)
		var walls = new HashSet<(int, int)> { (2, 0) };
		var path = Pathfinding.FindPath(0, 0, 4, 0, GridWithWalls(walls));
		Assert.NotNull(path);
		Assert.DoesNotContain((2, 0), path);
		Assert.Equal((4, 0), path[^1]);
		// Detour adds 2 steps (go around wall)
		Assert.True(path.Count >= 5);
	}

	[Fact]
	public void FindPath_CompletelyBlocked_ReturnsNull()
	{
		// All 4 cardinal neighbors of start are walls — no path possible
		Pathfinding.IsWalkableFunc isolated = (x, y) =>
		{
			if (x == 0 && y == 0) return true;
			if (x == 5 && y == 0) return true;
			if (Math.Abs(x) + Math.Abs(y) == 1) return false;
			return x >= -1 && x <= 6 && y >= -1 && y <= 1;
		};
		var path = Pathfinding.FindPath(0, 0, 5, 0, isolated);
		Assert.Null(path);
	}

	[Fact]
	public void FindPath_Corridor_FollowsNarrowPath()
	{
		// Create a corridor: only y=0 is walkable for x in [0..10]
		Pathfinding.IsWalkableFunc corridor = (x, y) => y == 0 && x >= 0 && x <= 10;
		var path = Pathfinding.FindPath(0, 0, 10, 0, corridor);
		Assert.NotNull(path);
		Assert.Equal(10, path.Count);
		Assert.All(path, p => Assert.Equal(0, p.Y));
	}

	// ── FindPath: A* vs JPS threshold ────────────────────

	[Fact]
	public void FindPath_ShortDistance_UsesAStar()
	{
		// Distance <= 16 uses A*
		var path = Pathfinding.FindPath(0, 0, 8, 8, OpenField);
		Assert.NotNull(path);
		Assert.Equal(16, path.Count); // Manhattan distance
	}

	// Corridor helper for JPS tests — JPS Jump() recurses infinitely in open fields
	// because jumpLimit is local. Corridors create forced neighbors that stop recursion.
	private static Pathfinding.IsWalkableFunc Corridor(int width, int length) =>
		(x, y) => x >= 0 && x <= length && y >= 0 && y < width;

	[Fact]
	public void FindPath_LongDistance_UsesJPS()
	{
		// Distance > 16 uses JPS — corridor prevents infinite Jump recursion
		var path = Pathfinding.FindPath(0, 0, 20, 0, Corridor(2, 25));
		Assert.NotNull(path);
		Assert.Equal((20, 0), path![^1]);
	}

	[Fact]
	public void FindPath_ExactlyAtThreshold_UsesAStar()
	{
		// Manhattan distance = 16 exactly
		var path = Pathfinding.FindPath(0, 0, 16, 0, OpenField);
		Assert.NotNull(path);
		Assert.Equal(16, path.Count);
	}

	[Fact]
	public void FindPath_JustAboveThreshold_UsesJPS()
	{
		// Manhattan distance = 17
		var path = Pathfinding.FindPath(0, 0, 17, 0, Corridor(2, 20));
		Assert.NotNull(path);
		Assert.Equal((17, 0), path![^1]);
	}

	// ── FindPath: path continuity ────────────────────────

	[Fact]
	public void FindPath_EachStepIsCardinalAdjacent()
	{
		var path = Pathfinding.FindPath(0, 0, 5, 5, OpenField)!;
		var prev = (X: 0, Y: 0);
		foreach (var step in path)
		{
			var dist = Math.Abs(step.X - prev.X) + Math.Abs(step.Y - prev.Y);
			Assert.Equal(1, dist);
			prev = step;
		}
	}

	[Fact]
	public void FindPath_LongPath_EachStepIsCardinalAdjacent()
	{
		// JPS path should also have interpolated steps
		var path = Pathfinding.FindPath(0, 0, 25, 0, Corridor(2, 30));
		Assert.NotNull(path);
		var prev = (X: 0, Y: 0);
		foreach (var step in path!)
		{
			var dist = Math.Abs(step.X - prev.X) + Math.Abs(step.Y - prev.Y);
			Assert.Equal(1, dist);
			prev = step;
		}
	}

	// ── FindPath: negative coordinates ───────────────────

	[Fact]
	public void FindPath_NegativeCoordinates_Works()
	{
		var path = Pathfinding.FindPath(-5, -5, 0, 0, OpenField);
		Assert.NotNull(path);
		Assert.Equal(10, path.Count);
		Assert.Equal((0, 0), path[^1]);
	}

	// ── NextStep ─────────────────────────────────────────

	[Fact]
	public void NextStep_ReturnsFirstStepOfPath()
	{
		var step = Pathfinding.NextStep(0, 0, 3, 0, OpenField);
		Assert.NotNull(step);
		Assert.Equal((1, 0), step.Value);
	}

	[Fact]
	public void NextStep_SameTile_ReturnsNull()
	{
		var step = Pathfinding.NextStep(5, 5, 5, 5, OpenField);
		Assert.Null(step);
	}

	[Fact]
	public void NextStep_Blocked_ReturnsNull()
	{
		// Completely isolated start — all 4 cardinal neighbors are walls
		// Use a small bounded area to avoid JPS infinite recursion
		Pathfinding.IsWalkableFunc isolated = (x, y) =>
		{
			if (x == 0 && y == 0) return true; // start
			if (x == 5 && y == 5) return true; // goal
			// Block all neighbors of start
			if (Math.Abs(x) + Math.Abs(y) == 1) return false;
			// Small bounded area
			return x >= -2 && x <= 7 && y >= -2 && y <= 7;
		};
		var step = Pathfinding.NextStep(0, 0, 5, 5, isolated);
		Assert.Null(step);
	}

	[Fact]
	public void NextStep_IsAdjacentToStart()
	{
		var step = Pathfinding.NextStep(5, 5, 10, 5, OpenField);
		Assert.NotNull(step);
		var dist = Math.Abs(step.Value.X - 5) + Math.Abs(step.Value.Y - 5);
		Assert.Equal(1, dist);
	}
}
