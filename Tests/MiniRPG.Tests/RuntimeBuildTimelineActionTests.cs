using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RuntimeBuildTimelineActionTests
{
	public RuntimeBuildTimelineActionTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void SubmitPlayerAction_TerrainBuild_AppliesWithoutConsumingTurn()
	{
		var (state, player) = CreateAirTimelineState();
		state.RuntimeFreeBuild = true;
		state.World!.SetTerrain(2, 1, 0, Terrains.WallStone);
		TimelineTurnManager.Reset(state);

		var result = TimelineTurnManager.SubmitPlayerAction(
			state,
			TimelinePlayerAction.TerrainBuild(Terrains.Floor, 1, 1, 0));

		Assert.False(result.ActionConsumed);
		Assert.True(result.PlayerTurnReady);
		Assert.False(result.HasPendingAutoStep);
		Assert.Equal(player.Id, result.ActingActorId);
		Assert.Equal(0, state.Turn);
		Assert.True(TimelineTurnManager.IsPlayerTurn(state));
		Assert.Equal(Terrains.Floor, state.World.GetTerrain(1, 1, 0).StringId);
	}

	[Fact]
	public void SubmitPlayerAction_TerrainDemolish_AppliesWithoutConsumingTurn()
	{
		var (state, player) = CreateAirTimelineState();
		state.RuntimeFreeBuild = true;
		state.World!.SetTerrain(1, 1, 0, Terrains.WallStone);
		TimelineTurnManager.Reset(state);

		var result = TimelineTurnManager.SubmitPlayerAction(
			state,
			TimelinePlayerAction.TerrainDemolish(1, 1, 0));

		Assert.False(result.ActionConsumed);
		Assert.True(result.PlayerTurnReady);
		Assert.False(result.HasPendingAutoStep);
		Assert.Equal(player.Id, result.ActingActorId);
		Assert.Equal(0, state.Turn);
		Assert.True(TimelineTurnManager.IsPlayerTurn(state));
		Assert.Equal(Terrains.Air, state.World.GetTerrain(1, 1, 0).StringId);
	}

	[Fact]
	public void SubmitPlayerAction_FacilityPlaceBlueprint_AppliesWithoutConsumingTurn()
	{
		var (state, player) = CreateFloorTimelineState();
		state.RuntimeFreeBuild = true;
		TimelineTurnManager.Reset(state);

		var result = TimelineTurnManager.SubmitPlayerAction(
			state,
			TimelinePlayerAction.FacilityPlaceBlueprint(
				FacilityIds.Stove,
				player.X + 1,
				player.Y,
				player.Z,
				FacilityRotation.North));

		Assert.False(result.ActionConsumed);
		Assert.True(result.PlayerTurnReady);
		Assert.False(result.HasPendingAutoStep);
		Assert.Equal(player.Id, result.ActingActorId);
		Assert.Equal(0, state.Turn);
		Assert.True(TimelineTurnManager.IsPlayerTurn(state));
		Assert.Contains(
			state.Facilities.Values,
			facility => facility.FacilityDefId == FacilityIds.Stove
				&& facility.AnchorX == player.X + 1
				&& facility.AnchorY == player.Y
				&& facility.Z == player.Z);
	}

	[Fact]
	public void SubmitPlayerAction_FacilityDemolish_AppliesWithoutConsumingTurn()
	{
		var (state, player, facility) = CreateTimelineConstructionState();
		state.RuntimeFreeBuild = true;
		TimelineTurnManager.Reset(state);

		var result = TimelineTurnManager.SubmitPlayerAction(
			state,
			TimelinePlayerAction.FacilityDemolish(facility.Id));

		Assert.False(result.ActionConsumed);
		Assert.True(result.PlayerTurnReady);
		Assert.False(result.HasPendingAutoStep);
		Assert.Equal(player.Id, result.ActingActorId);
		Assert.Equal(0, state.Turn);
		Assert.True(TimelineTurnManager.IsPlayerTurn(state));
		Assert.DoesNotContain(facility.Id, state.Facilities.Keys);
	}

	private static (GameState State, Actor Player) CreateAirTimelineState()
	{
		var state = new GameState
		{
			WorldSeed = 6789,
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			World = new WorldMap(6789, new AirGenerator()),
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		player.BrainId = null;
		player.PrimaryDomainId = DomainIds.Player;
		ActorModule.Add(state, player);
		return (state, player);
	}

	private static (GameState State, Actor Player) CreateFloorTimelineState()
	{
		var state = new GameState
		{
			WorldSeed = 6790,
			PlayerId = "player",
			PlayerX = 8,
			PlayerY = 8,
			PlayerZ = 0,
			World = new WorldMap(6790, new FloorGenerator()),
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;
		player.PrimaryDomainId = DomainIds.Player;
		ActorModule.Add(state, player);
		return (state, player);
	}

	private static (GameState State, Actor Player, FacilityInstance Facility) CreateTimelineConstructionState()
	{
		var (state, player) = CreateFloorTimelineState();
		var placement = FacilityConstructionModule.TryPlaceBlueprint(
			state,
			FacilityIds.Stove,
			player.X + 1,
			player.Y,
			player.Z,
			FacilityRotation.North,
			DomainIds.Player);
		Assert.True(placement.Success);
		return (state, player, Assert.IsType<FacilityInstance>(placement.Facility));
	}

	private sealed class AirGenerator : IMapGenerator
	{
		public string Id => "runtime_build_air";
		public string Name => "Runtime Build Air";

		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Air));

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "runtime_build_floor";
		public string Name => "Runtime Build Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(chunk.Coord.Cz == 0
				? TerrainRegistry.GetId(Terrains.Floor)
				: TerrainRegistry.GetId(Terrains.WallStone));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
