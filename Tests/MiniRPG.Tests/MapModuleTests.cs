using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MapModuleTests
{
	public MapModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static GameState CreateState()
	{
		var state = new GameState
		{
			World = new WorldMap(42, new FlatFloorGen()),
			PlayerZ = 0,
		};
		return state;
	}

	// ── SetTerrain / GetTerrain ──────────────────────────

	[Fact]
	public void SetTerrain_Wall_GetTerrain_ReturnsWallGlyph()
	{
		var s = CreateState();
		MapModule.SetTerrain(s, 5, 5, "#");
		var glyph = MapModule.GetTerrain(s, 5, 5);
		Assert.Equal("#", glyph);
	}

	[Fact]
	public void SetTerrain_Floor_GetTerrain_ReturnsFloorGlyph()
	{
		var s = CreateState();
		MapModule.SetTerrain(s, 5, 5, ".");
		Assert.Equal(".", MapModule.GetTerrain(s, 5, 5));
	}

	[Fact]
	public void SetTerrain_Water_GetTerrain_ReturnsWaterGlyph()
	{
		var s = CreateState();
		MapModule.SetTerrain(s, 5, 5, "~");
		Assert.Equal("~", MapModule.GetTerrain(s, 5, 5));
	}

	[Fact]
	public void SetTerrain_UnknownGlyph_DefaultsToFloor()
	{
		var s = CreateState();
		MapModule.SetTerrain(s, 5, 5, "?");
		Assert.Equal(".", MapModule.GetTerrain(s, 5, 5));
	}

	// ── SetFixture / HasFixture ──────────────────────────

	[Fact]
	public void SetFixture_StairDown_HasFixture()
	{
		var s = CreateState();
		MapModule.SetFixture(s, 3, 3, ">");
		Assert.True(MapModule.HasFixture(s, 3, 3, Entities.StairDown));
	}

	[Fact]
	public void SetFixture_Empty_RemovesFixture()
	{
		var s = CreateState();
		MapModule.SetFixture(s, 3, 3, ">");
		MapModule.SetFixture(s, 3, 3, "");
		Assert.False(MapModule.HasFixture(s, 3, 3, Entities.StairDown));
	}

	[Fact]
	public void GetFixture_NoFixture_ReturnsEmpty()
	{
		var s = CreateState();
		Assert.Equal("", MapModule.GetFixture(s, 10, 10));
	}

	// ── IsWall / IsWalkable ──────────────────────────────

	[Fact]
	public void IsWall_WallTerrain_ReturnsTrue()
	{
		var s = CreateState();
		MapModule.SetTerrain(s, 5, 5, "#");
		Assert.True(MapModule.IsWall(s, 5, 5));
	}

	[Fact]
	public void IsWall_FloorTerrain_ReturnsFalse()
	{
		var s = CreateState();
		Assert.False(MapModule.IsWall(s, 5, 5));
	}

	[Fact]
	public void IsWalkable_Floor_ReturnsTrue()
	{
		var s = CreateState();
		Assert.True(MapModule.IsWalkable(s, 5, 5));
	}

	[Fact]
	public void IsWalkable_Wall_ReturnsFalse()
	{
		var s = CreateState();
		MapModule.SetTerrain(s, 5, 5, "#");
		Assert.False(MapModule.IsWalkable(s, 5, 5));
	}

	// ── PlaceItem / PickupItem / PeekGroundItems ─────────

	[Fact]
	public void PlaceItem_ThenPeekGroundItems_ReturnsItem()
	{
		var s = CreateState();
		var item = new Item { Id = "coin", Name = "Coin", Price = 1 };
		item.EnsureRuntimeState();
		MapModule.PlaceItem(s, 5, 5, item);

		var items = MapModule.PeekGroundItems(s, 5, 5);
		Assert.NotEmpty(items);
		Assert.Contains(items, i => i.Id == "coin");
	}

	[Fact]
	public void PickupItem_ExistingItem_ReturnsItem()
	{
		var s = CreateState();
		var item = new Item { Id = "gem", Name = "Gem", Price = 50 };
		item.EnsureRuntimeState();
		MapModule.PlaceItem(s, 5, 5, item);

		// Get the entity ID from ground items
		var groundItems = MapModule.PeekGroundItems(s, 5, 5);
		Assert.NotEmpty(groundItems);
		var entityId = groundItems[0].InstanceId;

		var picked = MapModule.PickupItem(s, 5, 5, entityId);
		Assert.NotNull(picked);
	}

	[Fact]
	public void PickupItem_NonExistent_ReturnsNull()
	{
		var s = CreateState();
		Assert.Null(MapModule.PickupItem(s, 5, 5, "nonexistent"));
	}

	// ── 3D overloads ─────────────────────────────────────

	[Fact]
	public void SetTerrain_3D_Works()
	{
		var s = CreateState();
		MapModule.SetTerrain(s, 5, 5, 1, "#");
		Assert.Equal("#", MapModule.GetTerrain(s, 5, 5, 1));
	}

	// ── Helper ───────────────────────────────────────────

	private sealed class FlatFloorGen : IMapGenerator
	{
		public string Id => "flat_floor_map";
		public string Name => "Flat Floor Map";
		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
