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
	public void ResetForSession_DefaultsToSelectTerrain_WithRuntimeTerrainBrushes()
	{
		var session = new RuntimeWorldToolSession(CreateState(0, 1));

		session.SetToolMode(WorldToolMode.Demolish);
		session.SetCategory(WorldToolCategory.Facility);
		session.SetHover(new Vector3I(4, 5, 0));
		session.AdjustHeightOffset(3);
		session.ResetForSession();

		Assert.Equal(WorldToolMode.Select, session.CurrentToolMode);
		Assert.Equal(WorldToolCategory.Terrain, session.CurrentCategory);
		Assert.Null(session.HoverWorld);
		Assert.Equal(0, session.HeightOffset);
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
	public void ResolveHoverState_TerrainBuild_UsesHeightOffsetOnResolvedTarget()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		var session = new RuntimeWorldToolSession(state);

		session.SetToolMode(WorldToolMode.Build);
		Assert.True(session.AdjustHeightOffset(2));

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(4, 5, 1)));

		Assert.Equal(WorldToolMode.Build, session.CurrentToolMode);
		Assert.Equal(new Vector3I(4, 5, 3), preview.ResolvedTargetCell);
	}

	[Fact]
	public void ResolveHoverState_TerrainSelectAndDemolish_UseHeightOffsetOnOccupiedCells()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		state.World!.SetTerrain(2, 3, 1, Terrains.Stone);
		state.World.SetTerrain(2, 3, 2, Terrains.Dirt);
		var session = new RuntimeWorldToolSession(state);

		Assert.True(session.AdjustHeightOffset(1));

		var selectPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(2, 3, 1)));
		Assert.Equal(WorldToolMode.Select, selectPreview.ToolMode);
		Assert.Equal(new Vector3I(2, 3, 2), selectPreview.ResolvedTargetCell);
		Assert.True(selectPreview.CanApply);

		session.SetToolMode(WorldToolMode.Demolish);
		var demolishPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(2, 3, 1)));
		Assert.Equal(new Vector3I(2, 3, 2), demolishPreview.ResolvedTargetCell);
		Assert.True(demolishPreview.CanApply);
	}

	[Fact]
	public void ResolveHoverState_FacilityBuild_UsesHeightOffsetOnPlacementTarget()
	{
		var state = CreateState(0, 1);
		state.RuntimeFreeBuild = true;
		var session = new RuntimeWorldToolSession(state);

		session.SetCategory(WorldToolCategory.Facility);
		session.SetToolMode(WorldToolMode.Build);
		Assert.True(session.AdjustHeightOffset(2));

		var preview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(6, 7, 0)));

		Assert.Equal(WorldToolCategory.Facility, preview.Category);
		Assert.Equal(new Vector3I(6, 7, 2), preview.ResolvedTargetCell);
		Assert.True(preview.CanApply);
	}

	[Fact]
	public void ResolveHoverState_FacilitySelectAndDemolish_UseHeightOffsetOnOccupiedCells()
	{
		var state = CreateState(0, 1);
		AddFacility(state, "facility-under-test", FacilityIds.Bed, 8, 9, 2);
		var session = new RuntimeWorldToolSession(state);

		session.SetCategory(WorldToolCategory.Facility);
		Assert.True(session.AdjustHeightOffset(2));

		var selectPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(8, 9, 0)));
		Assert.Equal(WorldToolMode.Select, selectPreview.ToolMode);
		Assert.Equal(new Vector3I(8, 9, 2), selectPreview.ResolvedTargetCell);
		Assert.Equal("facility-under-test", selectPreview.ResolvedEntityId);
		Assert.True(selectPreview.CanApply);

		session.SetToolMode(WorldToolMode.Demolish);
		var demolishPreview = Assert.IsType<WorldToolPreviewState>(
			session.ResolveHoverState(new Vector3I(8, 9, 0)));
		Assert.Equal(new Vector3I(8, 9, 2), demolishPreview.ResolvedTargetCell);
		Assert.Equal("facility-under-test", demolishPreview.ResolvedEntityId);
		Assert.True(demolishPreview.CanApply);
	}

	[Fact]
	public void FacilityBrushes_ExposePreviewWhenEntityRenderEntryExists()
	{
		var session = new RuntimeWorldToolSession(CreateState(0, 1));

		var brush = Assert.Single(
			session.FacilityBrushes,
			entry => string.Equals(entry.Id, FacilityIds.Bed, StringComparison.Ordinal));
		var preview = Assert.IsType<MapEditorBrushPreview>(brush.Preview);

		Assert.Equal("res://Assets/Art/Generated/facilities/facility_bed_4dir.png", preview.TexturePath);
		Assert.Equal(new Rect2I(0, 0, 256, 256), preview.Region);
	}

	[Fact]
	public void EntityPreviewResolver_ReturnsNullForMissingEntry()
	{
		Assert.Null(MapEditorBrushPreviewResolver.ResolveEntityPreview("runtime_tool_missing_preview"));
	}

	private static GameState CreateState(int facingX, int facingY)
	{
		var state = new GameState
		{
			WorldSeed = 2468,
			PlayerId = "player",
			PlayerX = 0,
			PlayerY = 0,
			PlayerZ = 0,
			World = new WorldMap(2468, new AirGenerator()),
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
		};
		var player = new Actor
		{
			Id = state.PlayerId,
			X = 0,
			Y = 0,
			Z = 0,
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
