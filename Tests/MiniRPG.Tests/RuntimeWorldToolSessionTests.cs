using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
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
