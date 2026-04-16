using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Module.Editor;
using MiniRPG.Module.WorldTool;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RuntimeWorldToolSessionTests
{
	public RuntimeWorldToolSessionTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
		TerrainBuildRuleRegistry.Load();
	}

	[Fact]
	public void ResetForSession_DefaultsToSelectTerrain_AndRefreshesBrushState()
	{
		var session = new RuntimeWorldToolSession(CreateState(0, 1, playerX: 3, playerY: 4, playerZ: 5));

		session.SetToolMode(WorldToolMode.Demolish);
		session.SetCategory(WorldToolCategory.Facility);
		session.SetHover(new Vector3I(4, 5, 0));
		session.ResetForSession();

		Assert.Equal(WorldToolMode.Select, session.CurrentToolMode);
		Assert.Equal(WorldToolCategory.Terrain, session.CurrentCategory);
		Assert.Null(session.HoverWorld);
		Assert.NotEmpty(session.TerrainBrushes);
		Assert.All(session.TerrainBrushes, brush => Assert.True(TerrainBuildRuleRegistry.IsAllowed(brush.Id)));
		Assert.Equal(session.TerrainBrushes[0].Id, session.CurrentBrush.Id);
	}

	[Fact]
	public void FacilityRotation_SeedsFromFacingOnce_AndPersistsAcrossCategorySwitches()
	{
		var session = new RuntimeWorldToolSession(CreateState(1, 0));

		session.SetCategory(WorldToolCategory.Facility);
		Assert.Equal(FacilityRotation.East, session.FacilityRotation);

		session.RotateFacility(1);
		Assert.Equal(FacilityRotation.South, session.FacilityRotation);

		session.SetCategory(WorldToolCategory.Terrain);
		session.SetCategory(WorldToolCategory.Facility);
		Assert.Equal(FacilityRotation.South, session.FacilityRotation);
	}

	[Fact]
	public void ResolveHoverState_TerrainBuild_UsesHoveredCellAtCurrentLayer()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		var session = new RuntimeWorldToolSession(state);

		session.SetToolMode(WorldToolMode.Build);

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(4, 5, 3)));

		Assert.Equal(WorldToolMode.Build, session.CurrentToolMode);
		Assert.Equal(new Vector3I(4, 5, 3), preview.ResolvedTargetCell);
		Assert.False(preview.ShowInfoOverlay);
	}

	[Fact]
	public void ResolveHoverState_TerrainBuild_OnSolidCell_UsesFirstAirInDefaultDirection()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		state.World!.SetTerrain(4, 5, 3, Terrains.Stone);
		var session = new RuntimeWorldToolSession(state);

		session.SetToolMode(WorldToolMode.Build);

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(4, 5, 3)));

		Assert.Equal(new Vector3I(4, 5, 2), preview.ResolvedTargetCell);
		Assert.True(preview.CanApply);
	}

	[Fact]
	public void ResolveHoverState_TerrainBuild_OnSolidCell_WithReverseStack_UsesFirstAirInReverseDirection()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		state.World!.SetTerrain(4, 5, 3, Terrains.Stone);
		var session = new RuntimeWorldToolSession(state);

		session.SetToolMode(WorldToolMode.Build);

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(4, 5, 3), reverseStack: true));

		Assert.Equal(new Vector3I(4, 5, 4), preview.ResolvedTargetCell);
		Assert.True(preview.CanApply);
	}

	[Fact]
	public void ResolveHoverState_TerrainBuild_OnAir_WithReverseStack_KeepsHoveredCell()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		var session = new RuntimeWorldToolSession(state);

		session.SetToolMode(WorldToolMode.Build);

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(4, 5, 3), reverseStack: true));

		Assert.Equal(new Vector3I(4, 5, 3), preview.ResolvedTargetCell);
		Assert.True(preview.CanApply);
	}

	[Fact]
	public void ResolveHoverState_TerrainBuild_WithReverseStackWithoutOpenAir_IsBlocked()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		for (var z = 3; z <= 19; z++)
			state.World!.SetTerrain(4, 5, z, Terrains.Stone);
		var session = new RuntimeWorldToolSession(state);

		session.SetToolMode(WorldToolMode.Build);

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(4, 5, 3), reverseStack: true));

		Assert.Null(preview.ResolvedTargetCell);
		Assert.False(preview.CanApply);
	}

	[Fact]
	public void ResolveHoverState_TerrainSelectAndDemolish_UseHoveredLayerExactly()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		state.World!.SetTerrain(2, 3, 1, Terrains.Stone);
		state.World.SetTerrain(2, 3, 2, Terrains.Dirt);
		var session = new RuntimeWorldToolSession(state);

		var selectPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(2, 3, 1)));
		Assert.Equal(WorldToolMode.Select, selectPreview.ToolMode);
		Assert.Equal(new Vector3I(2, 3, 1), selectPreview.ResolvedTargetCell);
		Assert.True(selectPreview.CanApply);
		Assert.True(selectPreview.ShowInfoOverlay);

		session.SetToolMode(WorldToolMode.Demolish);
		var demolishPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(2, 3, 2)));
		Assert.Equal(new Vector3I(2, 3, 2), demolishPreview.ResolvedTargetCell);
		Assert.True(demolishPreview.CanApply);
		Assert.True(demolishPreview.ShowInfoOverlay);
	}

	[Fact]
	public void ResolveHoverState_FacilityBuild_UsesHoveredCellAtCurrentLayer()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		var session = new RuntimeWorldToolSession(state);

		session.SetCategory(WorldToolCategory.Facility);
		session.SetToolMode(WorldToolMode.Build);

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(6, 7, 2), reverseStack: true));

		Assert.Equal(WorldToolCategory.Facility, preview.Category);
		Assert.Equal(new Vector3I(6, 7, 2), preview.ResolvedTargetCell);
		Assert.True(preview.CanApply);
		Assert.False(preview.ShowInfoOverlay);
	}

	[Fact]
	public void ResolveHoverState_FacilitySelectAndDemolish_UseHoveredLayerExactly()
	{
		var state = CreateState(0, 1);
		AddFacility(state, "facility-under-test", FacilityIds.Bed, 8, 9, 2);
		var session = new RuntimeWorldToolSession(state);

		session.SetCategory(WorldToolCategory.Facility);

		var selectPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(8, 9, 2)));
		Assert.Equal(WorldToolMode.Select, selectPreview.ToolMode);
		Assert.Equal(new Vector3I(8, 9, 2), selectPreview.ResolvedTargetCell);
		Assert.Equal("facility-under-test", selectPreview.ResolvedEntityId);
		Assert.True(selectPreview.CanApply);
		Assert.True(selectPreview.ShowInfoOverlay);

		var emptyLayerPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(8, 9, 0)));
		Assert.Null(emptyLayerPreview.ResolvedTargetCell);
		Assert.False(emptyLayerPreview.CanApply);
		Assert.False(emptyLayerPreview.ShowInfoOverlay);

		session.SetToolMode(WorldToolMode.Demolish);
		var demolishPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(8, 9, 2)));
		Assert.Equal(new Vector3I(8, 9, 2), demolishPreview.ResolvedTargetCell);
		Assert.Equal("facility-under-test", demolishPreview.ResolvedEntityId);
		Assert.True(demolishPreview.CanApply);
		Assert.True(demolishPreview.ShowInfoOverlay);
	}

	[Fact]
	public void FacilityBrushes_ExposePreviewWhenEntityRenderEntryExists()
	{
		var session = new RuntimeWorldToolSession(CreateState(0, 1));

		var brush = Assert.Single(
			session.FacilityBrushes,
			entry => string.Equals(entry.Id, FacilityIds.Bed, StringComparison.Ordinal));
		var preview = Assert.IsType<BrushPreview>(brush.Preview);

		Assert.Equal("res://Assets/Art/Generated/facilities/facility_bed_4dir.png", preview.TexturePath);
		Assert.Equal(new Rect2I(0, 0, 256, 256), preview.Region);
	}

	[Fact]
	public void EntityPreviewResolver_ReturnsNullForMissingEntry()
	{
		Assert.Null(BrushPreviewResolver.ResolveEntityPreview("runtime_tool_missing_preview"));
	}

	private static GameState CreateState(
		int facingX,
		int facingY,
		int playerX = 0,
		int playerY = 0,
		int playerZ = 0)
	{
		var state = new GameState
		{
			WorldSeed = 2468,
			PlayerId = "player",
			PlayerX = playerX,
			PlayerY = playerY,
			PlayerZ = playerZ,
			World = new WorldMap(2468, new AirGenerator()),
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
		};
		var player = new Actor
		{
			Id = state.PlayerId,
			X = playerX,
			Y = playerY,
			Z = playerZ,
			FacingX = facingX,
			FacingY = facingY,
			Faction = Factions.Player,
			PrimaryDomainId = DomainIds.Player,
		};
		ActorModule.Add(state, player);
		return state;
	}

	private static void AddFacility(GameState state, string instanceId, string facilityDefId, int x, int y, int z)
	{
		state.Facilities[instanceId] = new FacilityInstance
		{
			Id = instanceId,
			FacilityDefId = facilityDefId,
			AnchorX = x,
			AnchorY = y,
			Z = z,
			Rotation = FacilityRotation.South,
			Stage = FacilityStage.Active,
			OwnerDomainId = DomainIds.Player,
			HitPoints = 10,
			MaxHitPoints = 10,
		};
		state.World!.AttachFacilityState(state.Facilities);
		state.World.RebuildFacilityIndex();
	}

	private sealed class AirGenerator : IMapGenerator
	{
		public string Id => "runtime_tool_air";
		public string Name => "Runtime Tool Air";

		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
