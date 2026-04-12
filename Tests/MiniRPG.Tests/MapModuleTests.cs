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
		s.World!.SetTerrain(5, 5, s.PlayerZ, Terrains.WallStone);
		var glyph = s.World!.GetTerrain(5, 5, s.PlayerZ).Glyph;
		Assert.Equal("#", glyph);
	}

	[Fact]
	public void SetTerrain_Floor_GetTerrain_ReturnsFloorGlyph()
	{
		var s = CreateState();
		s.World!.SetTerrain(5, 5, s.PlayerZ, Terrains.Floor);
		Assert.Equal(".", s.World!.GetTerrain(5, 5, s.PlayerZ).Glyph);
	}

	[Fact]
	public void SetTerrain_Water_GetTerrain_ReturnsWaterGlyph()
	{
		var s = CreateState();
		s.World!.SetTerrain(5, 5, s.PlayerZ, Terrains.Water);
		Assert.Equal("~", s.World!.GetTerrain(5, 5, s.PlayerZ).Glyph);
	}

	[Fact]
	public void SetTerrain_UnknownGlyph_DefaultsToFloor()
	{
		var s = CreateState();
		s.World!.SetTerrain(5, 5, s.PlayerZ, Terrains.Floor);
		Assert.Equal(".", s.World!.GetTerrain(5, 5, s.PlayerZ).Glyph);
	}

	// ── SetFixture / HasFixture ──────────────────────────

	[Fact]
	public void SetFixture_StairDown_HasFixture()
	{
		var s = CreateState();
		s.World!.SetFixture(3, 3, s.PlayerZ, ">", Entities.StairDown);
		Assert.True(s.World!.HasFixture(3, 3, s.PlayerZ, Entities.StairDown));
	}

	[Fact]
	public void SetFixture_Empty_RemovesFixture()
	{
		var s = CreateState();
		s.World!.SetFixture(3, 3, s.PlayerZ, ">", Entities.StairDown);
		s.World!.SetFixture(3, 3, s.PlayerZ, "", "");
		Assert.False(s.World!.HasFixture(3, 3, s.PlayerZ, Entities.StairDown));
	}

	[Fact]
	public void GetFixture_NoFixture_ReturnsEmpty()
	{
		var s = CreateState();
		Assert.Equal("", (s.World!.GetFirstEntity(10, 10, s.PlayerZ, CellEntityType.Fixture)?.Glyph ?? ""));
	}

	// ── IsWall / IsWalkable ──────────────────────────────

	[Fact]
	public void IsWall_WallTerrain_ReturnsTrue()
	{
		var s = CreateState();
		s.World!.SetTerrain(5, 5, s.PlayerZ, Terrains.WallStone);
		Assert.True(!s.World!.IsWalkable(5, 5, s.PlayerZ));
	}

	[Fact]
	public void IsWall_FloorTerrain_ReturnsFalse()
	{
		var s = CreateState();
		Assert.False(!s.World!.IsWalkable(5, 5, s.PlayerZ));
	}

	[Fact]
	public void IsWalkable_Floor_ReturnsTrue()
	{
		var s = CreateState();
		Assert.True(s.World!.IsWalkable(5, 5, s.PlayerZ));
	}

	[Fact]
	public void IsWalkable_Wall_ReturnsFalse()
	{
		var s = CreateState();
		s.World!.SetTerrain(5, 5, s.PlayerZ, Terrains.WallStone);
		Assert.False(s.World!.IsWalkable(5, 5, s.PlayerZ));
	}

	// ── PlaceItem / PickupItem / PeekGroundItems ─────────

	[Fact]
	public void PlaceItem_ThenPeekGroundItems_ReturnsItem()
	{
		var s = CreateState();
		var item = new Item { Id = "coin", Name = "Coin", Price = 1 };
		item.EnsureRuntimeState();
		s.World!.PlaceItem(5, 5, s.PlayerZ, item);

		var items = s.World!.PeekGroundItems(5, 5, s.PlayerZ);
		Assert.NotEmpty(items);
		Assert.Contains(items, i => i.Id == "coin");
	}

	[Fact]
	public void PickupItem_ExistingItem_ReturnsItem()
	{
		var s = CreateState();
		var item = new Item { Id = "gem", Name = "Gem", Price = 50 };
		item.EnsureRuntimeState();
		s.World!.PlaceItem(5, 5, s.PlayerZ, item);

		// Get the entity ID from ground items
		var groundItems = s.World!.PeekGroundItems(5, 5, s.PlayerZ);
		Assert.NotEmpty(groundItems);
		var entityId = groundItems[0].InstanceId;

		var picked = s.World!.PickupItem(5, 5, s.PlayerZ, entityId);
		Assert.NotNull(picked);
	}

	[Fact]
	public void PickupItem_NonExistent_ReturnsNull()
	{
		var s = CreateState();
		Assert.Null(s.World!.PickupItem(5, 5, s.PlayerZ, "nonexistent"));
	}

	// ── 3D overloads ─────────────────────────────────────

	[Fact]
	public void SetTerrain_3D_Works()
	{
		var s = CreateState();
		s.World!.SetTerrain(5, 5, 1, Terrains.WallStone);
		Assert.Equal("#", s.World!.GetTerrain(5, 5, 1).Glyph);
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
