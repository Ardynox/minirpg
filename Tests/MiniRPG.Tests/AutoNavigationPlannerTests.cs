using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AutoNavigationPlannerTests
{
	public AutoNavigationPlannerTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void PlanNextStep_SameCell_ReturnsReached()
	{
		var world = new WorldMap(101, new FlatFloorGenerator());

		var result = AutoNavigationPlanner.PlanNextStep(world, 4, 5, 0, 4, 5, 0);

		Assert.True(result.Success);
		Assert.True(result.ReachedTarget);
		Assert.Null(result.Step);
	}

	[Fact]
	public void PlanNextStep_SameFloor_ReturnsCardinalMove()
	{
		var world = new WorldMap(102, new FlatFloorGenerator());

		var result = AutoNavigationPlanner.PlanNextStep(world, 1, 1, 0, 4, 1, 0);

		Assert.True(result.Success);
		Assert.False(result.ReachedTarget);
		Assert.NotNull(result.Step);
		Assert.Equal(AutoNavigationStepKind.Move, result.Step!.Value.Kind);
		Assert.Equal(1, result.Step.Value.Dx);
		Assert.Equal(0, result.Step.Value.Dy);
		Assert.Equal(0, result.Step.Value.Dz);
	}

	[Fact]
	public void PlanNextStep_CrossFloorWithStairs_ReturnsClimb()
	{
		var world = new WorldMap(103, new FlatFloorGenerator());
		world.SetFixture(2, 2, 0, WorldMap.ResolveFixtureGlyph(Entities.StairDown), Entities.StairDown);

		var result = AutoNavigationPlanner.PlanNextStep(world, 2, 2, 0, 2, 2, 1);

		Assert.True(result.Success);
		Assert.False(result.ReachedTarget);
		Assert.NotNull(result.Step);
		Assert.Equal(AutoNavigationStepKind.Climb, result.Step!.Value.Kind);
		Assert.Equal(1, result.Step.Value.Dz);
		Assert.Equal(2, result.Step.Value.TargetX);
		Assert.Equal(2, result.Step.Value.TargetY);
		Assert.Equal(1, result.Step.Value.TargetZ);
	}

	[Fact]
	public void PlanNextStep_UnreachableTarget_ReturnsFailure()
	{
		var world = new WorldMap(104, new FlatFloorGenerator());
		world.SetTerrain(1, 0, 0, Terrains.WallStone);
		world.SetTerrain(-1, 0, 0, Terrains.WallStone);
		world.SetTerrain(0, 1, 0, Terrains.WallStone);
		world.SetTerrain(0, -1, 0, Terrains.WallStone);

		var result = AutoNavigationPlanner.PlanNextStep(world, 0, 0, 0, 4, 0, 0);

		Assert.False(result.Success);
		Assert.False(result.ReachedTarget);
		Assert.Equal(AutoNavigationFailureReason.Unreachable, result.FailureReason);
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			var floorId = TerrainRegistry.GetId(Terrains.Floor);
			for (var y = 0; y < ChunkData.Size; y++)
			{
				for (var x = 0; x < ChunkData.Size; x++)
				{
					chunk.SetTerrain(x, y, floorId);
					chunk.SetHardness(x, y, 0);
				}
			}
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
