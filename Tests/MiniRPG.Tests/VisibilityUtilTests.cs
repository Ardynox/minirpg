using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// VisibilityUtil.HasLineOfSight requires a WorldMap instance.
/// Uses the same FlatFloorGenerator pattern as SkillCastingTestHelper.
/// </summary>
public sealed class VisibilityUtilTests
{
	public VisibilityUtilTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static WorldMap CreateFlatWorld() =>
		new(12345, new FlatFloorTestGenerator());

	// ── Same tile ────────────────────────────────────────

	[Fact]
	public void HasLineOfSight_SameTile_ReturnsTrue()
	{
		var world = CreateFlatWorld();
		Assert.True(VisibilityUtil.HasLineOfSight(world, 5, 5, 5, 5, 0));
	}

	// ── Clear LOS ────────────────────────────────────────

	[Fact]
	public void HasLineOfSight_ClearPath_ReturnsTrue()
	{
		var world = CreateFlatWorld();
		Assert.True(VisibilityUtil.HasLineOfSight(world, 0, 0, 5, 0, 0));
	}

	[Fact]
	public void HasLineOfSight_Diagonal_ClearPath()
	{
		var world = CreateFlatWorld();
		Assert.True(VisibilityUtil.HasLineOfSight(world, 0, 0, 5, 5, 0));
	}

	[Fact]
	public void HasLineOfSight_Adjacent_ReturnsTrue()
	{
		var world = CreateFlatWorld();
		Assert.True(VisibilityUtil.HasLineOfSight(world, 5, 5, 6, 5, 0));
		Assert.True(VisibilityUtil.HasLineOfSight(world, 5, 5, 5, 6, 0));
	}

	// ── Blocked LOS ──────────────────────────────────────

	[Fact]
	public void HasLineOfSight_WallBlocking_ReturnsFalse()
	{
		var world = CreateFlatWorld();
		// Place a wall between observer and target
		world.SetTerrain(3, 0, 0, Terrains.WallStone);

		Assert.False(VisibilityUtil.HasLineOfSight(world, 0, 0, 5, 0, 0));
	}

	[Fact]
	public void HasLineOfSight_WallAtTarget_ReturnsTrue()
	{
		var world = CreateFlatWorld();
		// Wall at the target itself — the Bresenham check returns true
		// when reaching the target before checking opacity
		world.SetTerrain(5, 0, 0, Terrains.WallStone);

		Assert.True(VisibilityUtil.HasLineOfSight(world, 0, 0, 5, 0, 0));
	}

	[Fact]
	public void HasLineOfSight_WallAtOrigin_DoesNotBlock()
	{
		var world = CreateFlatWorld();
		// Wall at origin — origin is never checked for opacity
		world.SetTerrain(0, 0, 0, Terrains.WallStone);

		Assert.True(VisibilityUtil.HasLineOfSight(world, 0, 0, 5, 0, 0));
	}

	// ── Symmetry ─────────────────────────────────────────

	[Fact]
	public void HasLineOfSight_Symmetric_ClearPath()
	{
		var world = CreateFlatWorld();
		Assert.True(VisibilityUtil.HasLineOfSight(world, 0, 0, 5, 3, 0));
		Assert.True(VisibilityUtil.HasLineOfSight(world, 5, 3, 0, 0, 0));
	}

	// ── Negative coordinates ─────────────────────────────

	[Fact]
	public void HasLineOfSight_NegativeCoordinates_Works()
	{
		var world = CreateFlatWorld();
		Assert.True(VisibilityUtil.HasLineOfSight(world, -5, -5, 0, 0, 0));
	}

	// ── Helper ───────────────────────────────────────────

	private sealed class FlatFloorTestGenerator : IMapGenerator
	{
		public string Id => "flat_floor_test";
		public string Name => "Flat Floor Test";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
