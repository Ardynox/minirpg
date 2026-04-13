using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Editor;
using Xunit;

namespace MiniRPG.Tests;

public sealed class PlacementConnectivityTests
{
	public PlacementConnectivityTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void TryPlace_RejectsFloatingBlockWithoutFaceNeighbor()
	{
		var (state, actor) = CreatePlacementState();

		var events = BlockPlaceModule.TryPlace(state, actor, 4, 5, 0, Terrains.WallStone);

		var failure = Assert.Single(events);
		Assert.Equal("block_place_failed", failure.Type);
		Assert.Equal("missing_support", failure.FailureReason);
		Assert.Equal(Terrains.Air, state.World!.GetTerrain(4, 5, 0).StringId);
	}

	[Fact]
	public void TryPlace_AllowsBlockWhenFaceNeighborExists()
	{
		var (state, actor) = CreatePlacementState();
		state.World!.SetTerrain(5, 5, 0, Terrains.WallStone);

		var events = BlockPlaceModule.TryPlace(state, actor, 4, 5, 0, Terrains.WallStone);

		var placed = Assert.Single(events);
		Assert.Equal("block_placed", placed.Type);
		Assert.Equal(Terrains.WallStone, state.World.GetTerrain(4, 5, 0).StringId);
	}

	[Fact]
	public void MapEditorSession_BlocksFloatingTerrainUnlessIgnoreToggleIsEnabled()
	{
		var state = CreateEditorState();
		var session = new MapEditorSession(state);
		SelectTerrainBrush(session, Terrains.Floor);

		var blocked = session.ApplyBrush(8, 8, 0);

		Assert.Equal(MapEditorBrushApplyResult.ConnectivityRequired, blocked);
		Assert.Equal(Terrains.Air, state.World!.GetTerrain(8, 8, 0).StringId);

		session.SetIgnoreConnectivityRequirement(true);

		var placed = session.ApplyBrush(8, 8, 0);

		Assert.Equal(MapEditorBrushApplyResult.Applied, placed);
		Assert.Equal(Terrains.Floor, state.World.GetTerrain(8, 8, 0).StringId);
	}

	[Fact]
	public void MapEditorSession_AllowsPlacementWhenPickedBlockProvidesSupport()
	{
		var state = CreateEditorState();
		state.World!.SetTerrain(3, 3, 0, Terrains.WallStone);
		var session = new MapEditorSession(state);
		SelectTerrainBrush(session, Terrains.Floor);

		var result = session.ApplyBrush(3, 3, 0);

		Assert.Equal(MapEditorBrushApplyResult.Applied, result);
		Assert.Equal(Terrains.Floor, state.World.GetTerrain(3, 3, -1).StringId);
	}

	[Fact]
	public void MapEditorSession_ResolvePlacementPreview_TerrainOnAirWithSupport_UsesHoveredCell()
	{
		var state = CreateEditorState();
		state.World!.SetTerrain(9, 8, 0, Terrains.WallStone);
		var session = new MapEditorSession(state);
		SelectTerrainBrush(session, Terrains.Floor);

		var preview = session.ResolvePlacementPreview(new Vector3I(8, 8, 0));

		var resolved = Assert.IsType<MapEditorPlacementPreview>(preview);
		Assert.Equal(MapEditorPlacementPreviewKind.Terrain, resolved.Kind);
		Assert.Equal(new Vector3I(8, 8, 0), resolved.HoverCell);
		Assert.Equal(new Vector3I(8, 8, 0), resolved.TargetCell);
		Assert.True(resolved.CanPlace);
		Assert.True(resolved.ShowGhost);
	}

	[Fact]
	public void MapEditorSession_ResolvePlacementPreview_TerrainOnSolidCell_UsesFirstAirAbove()
	{
		var state = CreateEditorState();
		state.World!.SetTerrain(3, 3, 0, Terrains.WallStone);
		var session = new MapEditorSession(state);
		SelectTerrainBrush(session, Terrains.Floor);

		var preview = session.ResolvePlacementPreview(new Vector3I(3, 3, 0));

		var resolved = Assert.IsType<MapEditorPlacementPreview>(preview);
		Assert.Equal(new Vector3I(3, 3, -1), resolved.TargetCell);
		Assert.True(resolved.CanPlace);
		Assert.True(resolved.ShowGhost);
	}

	[Fact]
	public void MapEditorSession_ResolvePlacementPreview_TerrainWithoutConnectivity_IsBlocked()
	{
		var state = CreateEditorState();
		var session = new MapEditorSession(state);
		SelectTerrainBrush(session, Terrains.Floor);

		var preview = session.ResolvePlacementPreview(new Vector3I(8, 8, 0));

		var resolved = Assert.IsType<MapEditorPlacementPreview>(preview);
		Assert.Equal(new Vector3I(8, 8, 0), resolved.TargetCell);
		Assert.False(resolved.CanPlace);
		Assert.False(resolved.ShowGhost);
	}

	[Fact]
	public void MapEditorSession_ResolvePlacementPreview_IgnoreConnectivity_MakesTerrainPlaceable()
	{
		var state = CreateEditorState();
		var session = new MapEditorSession(state);
		SelectTerrainBrush(session, Terrains.Floor);
		session.SetIgnoreConnectivityRequirement(true);

		var preview = session.ResolvePlacementPreview(new Vector3I(8, 8, 0));

		var resolved = Assert.IsType<MapEditorPlacementPreview>(preview);
		Assert.True(resolved.CanPlace);
		Assert.True(resolved.ShowGhost);
	}

	[Fact]
	public void MapEditorSession_ResolvePlacementPreview_FixtureSameId_IsBlocked()
	{
		var state = CreateEditorState();
		state.World!.SetFixture(5, 5, 0, "D", Entities.Door);
		var session = new MapEditorSession(state);
		SelectFixtureBrush(session, Entities.Door);

		var preview = session.ResolvePlacementPreview(new Vector3I(5, 5, 0));

		var resolved = Assert.IsType<MapEditorPlacementPreview>(preview);
		Assert.Equal(MapEditorPlacementPreviewKind.Fixture, resolved.Kind);
		Assert.Equal(new Vector3I(5, 5, 0), resolved.TargetCell);
		Assert.False(resolved.CanPlace);
		Assert.False(resolved.ShowGhost);
	}

	[Fact]
	public void MapEditorSession_ResolvePlacementPreview_FixtureDifferentId_IsPlaceable()
	{
		var state = CreateEditorState();
		state.World!.SetFixture(5, 5, 0, "D", Entities.Door);
		var session = new MapEditorSession(state);
		SelectFixtureBrush(session, Entities.Nest);

		var preview = session.ResolvePlacementPreview(new Vector3I(5, 5, 0));

		var resolved = Assert.IsType<MapEditorPlacementPreview>(preview);
		Assert.Equal(MapEditorPlacementPreviewKind.Fixture, resolved.Kind);
		Assert.Equal(new Vector3I(5, 5, 0), resolved.TargetCell);
		Assert.True(resolved.CanPlace);
		Assert.True(resolved.ShowGhost);
	}

	private static (GameState State, Actor Actor) CreatePlacementState()
	{
		var state = new GameState
		{
			WorldSeed = 1234,
			World = new WorldMap(1234, new AirGenerator()),
		};
		var actor = new Actor { Id = "builder" };
		return (state, actor);
	}

	private static GameState CreateEditorState() =>
		new()
		{
			WorldSeed = 5678,
			PlayerId = "player",
			PlayerX = 0,
			PlayerY = 0,
			PlayerZ = 0,
			World = new WorldMap(5678, new AirGenerator()),
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
		};

	private static void SelectTerrainBrush(MapEditorSession session, string terrainId)
	{
		var index = session.TerrainBrushes
			.Select((brush, brushIndex) => (brush.Id, brushIndex))
			.First(entry => string.Equals(entry.Id, terrainId, StringComparison.Ordinal))
			.brushIndex;
		session.SelectCategory(MapEditorBrushCategory.Terrain);
		session.SelectBrush(index);
	}

	private static void SelectFixtureBrush(MapEditorSession session, string fixtureId)
	{
		var index = session.FixtureBrushes
			.Select((brush, brushIndex) => (brush.Id, brushIndex))
			.First(entry => string.Equals(entry.Id, fixtureId, StringComparison.Ordinal))
			.brushIndex;
		session.SelectCategory(MapEditorBrushCategory.Fixture);
		session.SelectBrush(index);
	}

	private sealed class AirGenerator : IMapGenerator
	{
		public string Id => "air";
		public string Name => "Air";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
