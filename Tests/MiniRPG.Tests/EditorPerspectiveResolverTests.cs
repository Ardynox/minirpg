using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class EditorPerspectiveResolverTests
{
	public EditorPerspectiveResolverTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void Resolve_IndoorRoom_FadesEastSouthWallsAndOverheadBlocks()
	{
		var state = CreateState(new FlatFloorGen());
		CreateEnclosedRoom(state.World!, 2, 2, 8, 8, 0);
		state.World!.SetTerrain(5, 5, -1, Terrains.WallStone);
		state.World.SetTerrain(8, 5, -1, Terrains.WallStone);
		state.World.SetTerrain(5, 8, -1, Terrains.WallStone);

		var result = EditorPerspectiveResolver.Resolve(
			state,
			viewCenterX: 5,
			viewCenterY: 5,
			targetZ: 0,
			zMin: -4,
			zMax: 2,
			hoverCell: new WorldCoord(5, 5, 0));

		Assert.Equal(new WorldCoord(5, 5, 0), result.FocusCell);
		Assert.True(result.IsIndoors);
		Assert.Contains(new WorldCoord(5, 5, -1), result.FadedCells);
		Assert.Contains(new WorldCoord(8, 5, 0), result.FadedCells);
		Assert.Contains(new WorldCoord(8, 5, -1), result.FadedCells);
		Assert.Contains(new WorldCoord(5, 8, 0), result.FadedCells);
		Assert.Contains(new WorldCoord(5, 8, -1), result.FadedCells);
		Assert.DoesNotContain(new WorldCoord(2, 5, 0), result.FadedCells);
		Assert.DoesNotContain(new WorldCoord(5, 2, 0), result.FadedCells);
	}

	[Fact]
	public void Resolve_OpenField_UsesGeometryFallbackWedge()
	{
		var state = CreateState(new FlatFloorGen());
		state.World!.SetTerrain(6, 5, 0, Terrains.WallStone);
		state.World.SetTerrain(5, 6, 0, Terrains.WallStone);
		state.World.SetTerrain(8, 5, 0, Terrains.WallStone);
		state.World.SetTerrain(6, 5, 1, Terrains.WallStone);

		var result = EditorPerspectiveResolver.Resolve(
			state,
			viewCenterX: 5,
			viewCenterY: 5,
			targetZ: 0,
			zMin: -4,
			zMax: 2,
			hoverCell: new WorldCoord(5, 5, 0));

		Assert.False(result.IsIndoors);
		Assert.Contains(new WorldCoord(6, 5, 0), result.FadedCells);
		Assert.Contains(new WorldCoord(5, 6, 0), result.FadedCells);
		Assert.DoesNotContain(new WorldCoord(4, 5, 0), result.FadedCells);
		Assert.DoesNotContain(new WorldCoord(5, 4, 0), result.FadedCells);
		Assert.DoesNotContain(new WorldCoord(8, 5, 0), result.FadedCells);
		Assert.DoesNotContain(new WorldCoord(6, 5, 1), result.FadedCells);
	}

	[Fact]
	public void Resolve_WithoutHover_FallsBackToViewCenter()
	{
		var state = CreateState(new FlatFloorGen());
		state.World!.SetTerrain(7, 6, 0, Terrains.WallStone);

		var result = EditorPerspectiveResolver.Resolve(
			state,
			viewCenterX: 6,
			viewCenterY: 6,
			targetZ: 0,
			zMin: -4,
			zMax: 2,
			hoverCell: null);

		Assert.Equal(new WorldCoord(6, 6, 0), result.FocusCell);
		Assert.Contains(new WorldCoord(7, 6, 0), result.FadedCells);
	}

	[Fact]
	public void Resolve_SolidHoverCell_DoesNotMarkRoomAsIndoors()
	{
		var state = CreateState(new FlatFloorGen());
		CreateEnclosedRoom(state.World!, 2, 2, 8, 8, 0);

		var result = EditorPerspectiveResolver.Resolve(
			state,
			viewCenterX: 5,
			viewCenterY: 5,
			targetZ: 0,
			zMin: -4,
			zMax: 2,
			hoverCell: new WorldCoord(8, 5, 0));

		Assert.False(result.IsIndoors);
	}

	private static GameState CreateState(IMapGenerator generator)
	{
		return new GameState
		{
			World = new WorldMap(42, generator),
			PlayerZ = 0,
		};
	}

	private static void CreateEnclosedRoom(WorldMap world, int minX, int minY, int maxX, int maxY, int z)
	{
		for (var x = minX; x <= maxX; x++)
		for (var y = minY; y <= maxY; y++)
		{
			if (x == minX || x == maxX || y == minY || y == maxY)
				world.SetTerrain(x, y, z, Terrains.WallStone);
		}
	}

	private sealed class FlatFloorGen : IMapGenerator
	{
		public string Id => "flat_floor_perspective";
		public string Name => "Flat Floor Perspective";

		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));

		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
