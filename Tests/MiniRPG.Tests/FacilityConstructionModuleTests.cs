using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Facility;
using MiniRPG.Core.World;
using Xunit;

namespace MiniRPG.Tests;

public sealed class FacilityConstructionModuleTests
{
	public FacilityConstructionModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void TryPlaceBlueprint_CreatesDeliverTickets_AndRotatesFootprint()
	{
		var (state, player) = CreateConstructionState();

		var result = FacilityConstructionModule.TryPlaceBlueprint(
			state,
			FacilityIds.Stove,
			player.X + 1,
			player.Y,
			player.Z,
			FacilityRotation.East,
			DomainIds.Player);

		Assert.True(result.Success);
		var facility = Assert.IsType<FacilityInstance>(result.Facility);
		Assert.Equal(FacilityStage.DeliverMaterials, facility.Stage);
		Assert.True(state.World!.TryGetFacilityAt(player.X + 1, player.Y, player.Z, out var anchorFacility));
		Assert.Equal(facility.Id, anchorFacility!.Id);
		Assert.True(state.World.TryGetFacilityAt(player.X + 1, player.Y + 1, player.Z, out var chimneyFacility));
		Assert.Equal(facility.Id, chimneyFacility!.Id);

		var tickets = FacilityConstructionModule.GetConstructionTickets(state, facility.Id);
		Assert.Equal(2, tickets.Count);
		var requirements = tickets.ToDictionary(ticket => ticket.RequiredItemId, ticket => ticket.RequiredCount, StringComparer.Ordinal);
		Assert.Equal(18, requirements["mat_stone"]);
		Assert.Equal(6, requirements["mat_wood"]);
	}

	[Fact]
	public void TryPlaceBlueprint_RejectsBlockedCells()
	{
		var (state, player) = CreateConstructionState();
		state.World!.SetTerrain(player.X + 1, player.Y, player.Z, Terrains.WallStone);

		var result = FacilityConstructionModule.TryPlaceBlueprint(
			state,
			FacilityIds.Bed,
			player.X + 1,
			player.Y,
			player.Z,
			FacilityRotation.North,
			DomainIds.Player);

		Assert.False(result.Success);
		Assert.Equal("blocked", result.FailureReason);
		Assert.Contains(new ZoneCell(player.X + 1, player.Y, player.Z), result.Blockers);
	}

	[Fact]
	public void TryDeliverMaterials_ConsumesInventory_AndPromotesToConstruct()
	{
		var (state, player) = CreateConstructionState();
		var facility = PlaceStove(state, player);
		AddInventoryStacks(player, "mat_stone", 18);
		AddInventoryStacks(player, "mat_wood", 6);

		var result = FacilityConstructionModule.TryDeliverMaterials(state, player, facility.Id);

		Assert.True(result.Consumed);
		Assert.Equal(FacilityStage.Construct, facility.Stage);
		Assert.Empty(FacilityConstructionModule.GetMissingConstructionMaterials(facility));
		Assert.Equal(0, InventoryModule.CountMatching(player, item => item.Id == "mat_stone"));
		Assert.Equal(0, InventoryModule.CountMatching(player, item => item.Id == "mat_wood"));

		var delivered = facility.DeliveredConstructionMaterials.ToDictionary(item => item.ItemId, item => item.Count, StringComparer.Ordinal);
		Assert.Equal(18, delivered["mat_stone"]);
		Assert.Equal(6, delivered["mat_wood"]);

		var ticket = Assert.Single(FacilityConstructionModule.GetConstructionTickets(state, facility.Id));
		Assert.Equal(WorkTicketType.ConstructFacility, ticket.Type);
		Assert.Equal(1, ticket.WorkRemaining);
	}

	[Fact]
	public void TryDeliverMaterials_WithPartialInventory_StaysInDeliverStage()
	{
		var (state, player) = CreateConstructionState();
		var facility = PlaceStove(state, player);
		AddInventoryStacks(player, "mat_wood", 3);

		var result = FacilityConstructionModule.TryDeliverMaterials(state, player, facility.Id);

		Assert.True(result.Consumed);
		Assert.Equal(FacilityStage.DeliverMaterials, facility.Stage);
		var missing = FacilityConstructionModule.GetMissingConstructionMaterials(facility)
			.ToDictionary(item => item.ItemId, item => item.Count, StringComparer.Ordinal);
		Assert.Equal(18, missing["mat_stone"]);
		Assert.Equal(3, missing["mat_wood"]);

		var tickets = FacilityConstructionModule.GetConstructionTickets(state, facility.Id);
		Assert.Equal(2, tickets.Count);
		var requirements = tickets.ToDictionary(ticket => ticket.RequiredItemId, ticket => ticket.RequiredCount, StringComparer.Ordinal);
		Assert.Equal(18, requirements["mat_stone"]);
		Assert.Equal(3, requirements["mat_wood"]);
	}

	[Fact]
	public void TryConstructFacility_ActivatesConstructStage()
	{
		var (state, player) = CreateConstructionState();
		var facility = PlaceStove(state, player);
		AddInventoryStacks(player, "mat_stone", 18);
		AddInventoryStacks(player, "mat_wood", 6);
		Assert.True(FacilityConstructionModule.TryDeliverMaterials(state, player, facility.Id).Consumed);

		var result = FacilityConstructionModule.TryConstructFacility(state, player, facility.Id);

		Assert.True(result.Consumed);
		Assert.Equal(FacilityStage.Active, facility.Stage);
		Assert.Empty(FacilityConstructionModule.GetConstructionTickets(state, facility.Id));
		Assert.False(state.World!.IsWalkable(facility.AnchorX, facility.AnchorY, facility.Z));
	}

	private static (GameState State, Actor Player) CreateConstructionState()
	{
		var state = new GameState
		{
			WorldSeed = 9876,
			PlayerId = "player",
			PlayerX = 10,
			PlayerY = 10,
			PlayerZ = 0,
			World = new WorldMap(9876, new FloorGenerator()),
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

	private static FacilityInstance PlaceStove(GameState state, Actor player)
	{
		var result = FacilityConstructionModule.TryPlaceBlueprint(
			state,
			FacilityIds.Stove,
			player.X + 1,
			player.Y,
			player.Z,
			FacilityRotation.North,
			DomainIds.Player);
		Assert.True(result.Success);
		return Assert.IsType<FacilityInstance>(result.Facility);
	}

	private static void AddInventoryStacks(Actor actor, string itemId, int totalCount)
	{
		var remaining = totalCount;
		while (remaining > 0)
		{
			var item = PresetDB.CloneItem(itemId);
			item.OwnerDomainId = DomainIds.Player;
			item.EnsureRuntimeState();
			var stackCount = Math.Min(item.MaxStack, remaining);
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
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
