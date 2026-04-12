using System;
using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class FacilityTimelineActionTests
{
	public FacilityTimelineActionTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void SubmitPlayerAction_FacilityDeliverAndConstruct_ConsumeTurns()
	{
		var (state, player, facility) = CreateTimelineConstructionState();
		AddInventoryStacks(player, "mat_stone", 18);
		AddInventoryStacks(player, "mat_wood", 6);
		TimelineTurnManager.Reset(state);

		var deliverResult = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.FacilityDeliver(facility.Id));

		Assert.True(deliverResult.ActionConsumed);
		Assert.Equal(FacilityStage.Construct, facility.Stage);
		Assert.Equal(1, state.Turn);

		var constructResult = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.FacilityConstruct(facility.Id));

		Assert.True(constructResult.ActionConsumed);
		Assert.Equal(FacilityStage.Active, facility.Stage);
		Assert.Equal(2, state.Turn);
	}

	[Fact]
	public void SubmitPlayerAction_FacilityConstruct_DoesNotConsumeWhenNotReady()
	{
		var (state, _, facility) = CreateTimelineConstructionState();
		TimelineTurnManager.Reset(state);

		var result = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.FacilityConstruct(facility.Id));

		Assert.False(result.ActionConsumed);
		Assert.True(result.PlayerTurnReady);
		Assert.Equal(FacilityStage.DeliverMaterials, facility.Stage);
		Assert.Equal(0, state.Turn);
	}

	private static (GameState State, Actor Player, FacilityInstance Facility) CreateTimelineConstructionState()
	{
		var state = new GameState
		{
			WorldSeed = 4321,
			PlayerId = "player",
			PlayerX = 8,
			PlayerY = 8,
			PlayerZ = 0,
			World = new WorldMap(4321, new FloorGenerator()),
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

	private static void AddInventoryStacks(Actor actor, string itemId, int totalCount)
	{
		var remaining = totalCount;
		while (remaining > 0)
		{
			var item = PresetDB.CloneItem(itemId);
			item.OwnerDomainId = DomainIds.Player;
			item.EnsureRuntimeState();
			var stackCount = System.Math.Min(item.MaxStack, remaining);
			item.StackCount = stackCount;
			item.EnsureRuntimeState();
			InventoryModule.Add(actor, item);
			remaining -= stackCount;
		}
	}

	private sealed class FloorGenerator : IMapGenerator
	{
		public string Id => "floor";
		public string Name => "Floor";

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
