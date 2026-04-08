using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RoomContextAnalyzerTests
{
	public RoomContextAnalyzerTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static GameState CreateState(IMapGenerator gen)
	{
		return new GameState
		{
			World = new WorldMap(42, gen),
			PlayerZ = 0,
		};
	}

	// ── IsIndoors ────────────────────────────────────────

	[Fact]
	public void IsIndoors_OpenField_ReturnsFalse()
	{
		var state = CreateState(new FlatFloorGen());
		Assert.False(RoomContextAnalyzer.IsIndoors(state, 5, 5, 0));
	}

	[Fact]
	public void IsIndoors_NullWorld_ReturnsFalse()
	{
		var state = new GameState { World = null };
		Assert.False(RoomContextAnalyzer.IsIndoors(state, 0, 0, 0));
	}

	[Fact]
	public void IsIndoors_SolidTile_ReturnsFalse()
	{
		var state = CreateState(new FlatFloorGen());
		state.World!.SetTerrain(5, 5, 0, Terrains.WallStone);
		Assert.False(RoomContextAnalyzer.IsIndoors(state, 5, 5, 0));
	}

	[Fact]
	public void IsIndoors_SmallRoom_ReturnsTrue()
	{
		var state = CreateState(new FlatFloorGen());
		// Create a 3x3 room with walls
		for (int x = 2; x <= 8; x++)
			for (int y = 2; y <= 8; y++)
			{
				if (x == 2 || x == 8 || y == 2 || y == 8)
					state.World!.SetTerrain(x, y, 0, Terrains.WallStone);
			}
		// Center of room should be indoors
		Assert.True(RoomContextAnalyzer.IsIndoors(state, 5, 5, 0));
	}

	// ── AnalyzeRoom ──────────────────────────────────────

	[Fact]
	public void AnalyzeRoom_OpenField_NotIndoors()
	{
		var state = CreateState(new FlatFloorGen());
		var snapshot = RoomContextAnalyzer.AnalyzeRoom(state, 5, 5, 0);
		Assert.False(snapshot.IsIndoors);
	}

	[Fact]
	public void AnalyzeRoom_SmallRoom_IsIndoors()
	{
		var state = CreateState(new FlatFloorGen());
		for (int x = 2; x <= 8; x++)
			for (int y = 2; y <= 8; y++)
			{
				if (x == 2 || x == 8 || y == 2 || y == 8)
					state.World!.SetTerrain(x, y, 0, Terrains.WallStone);
			}
		var snapshot = RoomContextAnalyzer.AnalyzeRoom(state, 5, 5, 0);
		Assert.True(snapshot.IsIndoors);
		Assert.True(snapshot.CellCount > 0);
	}

	[Fact]
	public void AnalyzeRoom_NullWorld_ReturnsExposedRoom()
	{
		var state = new GameState { World = null };
		var snapshot = RoomContextAnalyzer.AnalyzeRoom(state, 0, 0, 0);
		Assert.False(snapshot.IsIndoors);
	}

	[Fact]
	public void AnalyzeRoom_SolidTile_ReturnsExposedRoom()
	{
		var state = CreateState(new FlatFloorGen());
		state.World!.SetTerrain(5, 5, 0, Terrains.WallStone);
		var snapshot = RoomContextAnalyzer.AnalyzeRoom(state, 5, 5, 0);
		Assert.False(snapshot.IsIndoors);
	}

	// ── Helper ───────────────────────────────────────────

	private sealed class FlatFloorGen : IMapGenerator
	{
		public string Id => "flat_floor_room";
		public string Name => "Flat Floor Room";
		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
