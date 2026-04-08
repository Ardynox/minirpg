using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ShadowcastFOVTests
{
	private static readonly ShadowcastFOV.IsOpaqueFunc NeverOpaque = (_, _) => false;

	// ── Compute: basic ───────────────────────────────────

	[Fact]
	public void Compute_OriginAlwaysVisible()
	{
		var visible = ShadowcastFOV.Compute(5, 5, 3, NeverOpaque);
		Assert.Contains((5, 5), visible);
	}

	[Fact]
	public void Compute_RadiusZero_OnlyOrigin()
	{
		var visible = ShadowcastFOV.Compute(5, 5, 0, NeverOpaque);
		Assert.Single(visible);
		Assert.Contains((5, 5), visible);
	}

	[Fact]
	public void Compute_OpenField_IncludesCardinalAtRadius()
	{
		var visible = ShadowcastFOV.Compute(5, 5, 4, NeverOpaque);
		// Cardinal directions at exactly radius distance
		Assert.Contains((5, 1), visible); // North
		Assert.Contains((5, 9), visible); // South
		Assert.Contains((1, 5), visible); // West
		Assert.Contains((9, 5), visible); // East
	}

	[Fact]
	public void Compute_OpenField_ExcludesBeyondRadius()
	{
		var visible = ShadowcastFOV.Compute(5, 5, 3, NeverOpaque);
		// Cardinal at radius+1 should not be visible
		Assert.DoesNotContain((5, 1), visible); // 4 away
		Assert.DoesNotContain((5, 9), visible);
		Assert.DoesNotContain((1, 5), visible);
		Assert.DoesNotContain((9, 5), visible);
	}

	// ── Compute: wall blocking ───────────────────────────

	[Fact]
	public void Compute_WallBlocksSightBehindIt()
	{
		// Wall at (5, 3), observer at (5, 5), radius 5
		ShadowcastFOV.IsOpaqueFunc wallAt53 = (x, y) => x == 5 && y == 3;
		var visible = ShadowcastFOV.Compute(5, 5, 5, wallAt53);

		// The wall itself should be visible
		Assert.Contains((5, 3), visible);
		// Tiles behind the wall (further north) should be blocked
		Assert.DoesNotContain((5, 1), visible);
	}

	[Fact]
	public void Compute_WallDoesNotBlockAdjacentColumns()
	{
		// Single wall at (5, 3) should not block (4, 2) or (6, 2)
		ShadowcastFOV.IsOpaqueFunc wallAt53 = (x, y) => x == 5 && y == 3;
		var visible = ShadowcastFOV.Compute(5, 5, 5, wallAt53);

		// Adjacent columns should still be visible
		Assert.Contains((4, 3), visible);
		Assert.Contains((6, 3), visible);
	}

	// ── Compute: symmetry ────────────────────────────────

	[Fact]
	public void Compute_Symmetry_ASeesB_IffBSeesA()
	{
		// Place some walls
		var walls = new HashSet<(int, int)> { (5, 3), (7, 5), (3, 7) };
		ShadowcastFOV.IsOpaqueFunc isOpaque = (x, y) => walls.Contains((x, y));

		int radius = 8;
		var fromA = ShadowcastFOV.Compute(5, 5, radius, isOpaque);
		// Check symmetry for all visible tiles
		foreach (var (bx, by) in fromA)
		{
			if (bx == 5 && by == 5) continue;
			var fromB = ShadowcastFOV.Compute(bx, by, radius, isOpaque);
			Assert.Contains((5, 5), fromB);
		}
	}

	[Fact]
	public void Compute_Symmetry_OpenField()
	{
		var fromA = ShadowcastFOV.Compute(10, 10, 5, NeverOpaque);
		foreach (var (bx, by) in fromA)
		{
			var fromB = ShadowcastFOV.Compute(bx, by, 5, NeverOpaque);
			Assert.Contains((10, 10), fromB);
		}
	}

	// ── Compute: enclosed room ───────────────────────────

	[Fact]
	public void Compute_EnclosedRoom_OnlySeesInterior()
	{
		// 3x3 room with walls on all sides, observer at center
		ShadowcastFOV.IsOpaqueFunc roomWalls = (x, y) =>
			x == 4 || x == 6 || y == 4 || y == 6;

		var visible = ShadowcastFOV.Compute(5, 5, 10, roomWalls);

		// Should see the walls
		Assert.Contains((4, 5), visible);
		Assert.Contains((6, 5), visible);
		Assert.Contains((5, 4), visible);
		Assert.Contains((5, 6), visible);

		// Should NOT see beyond walls
		Assert.DoesNotContain((3, 5), visible);
		Assert.DoesNotContain((7, 5), visible);
		Assert.DoesNotContain((5, 3), visible);
		Assert.DoesNotContain((5, 7), visible);
	}

	// ── ComputeDirectional ───────────────────────────────

	[Fact]
	public void ComputeDirectional_EqualRadii_ReturnsCopyOfFull()
	{
		var full = ShadowcastFOV.Compute(5, 5, 4, NeverOpaque);
		var directional = ShadowcastFOV.ComputeDirectional(5, 5, 4, 4, 1, 0, full);
		Assert.Equal(full.Count, directional.Count);
	}

	[Fact]
	public void ComputeDirectional_FacingEast_FrontHasMoreRange()
	{
		var full = ShadowcastFOV.Compute(5, 5, 6, NeverOpaque);
		var directional = ShadowcastFOV.ComputeDirectional(5, 5, 6, 2, 1, 0, full);

		// Origin always included
		Assert.Contains((5, 5), directional);

		// Front (east) at distance 5 should be visible
		Assert.Contains((10, 5), directional);

		// Rear (west) at distance 5 should NOT be visible (rear radius = 2)
		Assert.DoesNotContain((0, 5), directional);
	}

	[Fact]
	public void ComputeDirectional_FacingNorth_ClipsRear()
	{
		var full = ShadowcastFOV.Compute(5, 5, 5, NeverOpaque);
		var directional = ShadowcastFOV.ComputeDirectional(5, 5, 5, 1, 0, -1, full);

		// Front (north) at distance 4 should be visible
		Assert.Contains((5, 1), directional);

		// Rear (south) at distance 4 should NOT be visible
		Assert.DoesNotContain((5, 9), directional);
	}

	[Fact]
	public void ComputeDirectional_OriginAlwaysIncluded()
	{
		var full = ShadowcastFOV.Compute(5, 5, 3, NeverOpaque);
		var directional = ShadowcastFOV.ComputeDirectional(5, 5, 3, 0, 1, 0, full);
		Assert.Contains((5, 5), directional);
	}

	// ── Compute: large radius ────────────────────────────

	[Fact]
	public void Compute_LargeRadius_DoesNotThrow()
	{
		var visible = ShadowcastFOV.Compute(0, 0, 50, NeverOpaque);
		Assert.Contains((0, 0), visible);
		Assert.True(visible.Count > 100);
	}
}
