using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ServerActionGatewayTests
{
	public ServerActionGatewayTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void Pickup_Success_ChangesState_EmitsEvent_AndLeavesNoError()
	{
		var state = CreateState();
		var item = CreateItem("ground_berry", "Ground Berry");
		state.World!.PlaceItem(1, 1, 0, item);

		var result = ServerActionGateway.Execute(state, new PickupClientCommand
		{
			ActorId = "hero",
			ItemInstanceId = item.InstanceId,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Logs);
		var evt = Assert.Single(result.Events);
		Assert.Equal("item_picked_up", evt.Type);
		Assert.Contains(state.Actors["hero"].Inventory, entry => entry.InstanceId == item.InstanceId);
		Assert.Null(state.World!.PeekGroundItems(1, 1, 0).Find(i => string.Equals(i.InstanceId, item.InstanceId, StringComparison.Ordinal)));
	}

	[Fact]
	public void Pickup_InvalidActor_ReturnsRejectedErrorCode_AndNoEvents()
	{
		var state = CreateState();

		var result = ServerActionGateway.Execute(state, new PickupClientCommand
		{
			ActorId = "missing",
			ItemInstanceId = "item-1",
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal("invalid_actor", result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
	}

	[Fact]
	public void InventoryToggleEquip_Success_UpdatesState_AndLogsOutcome()
	{
		var state = CreateState();
		var item = CreateEquippableItem();
		state.Actors["hero"].Inventory.Add(item);

		var result = ServerActionGateway.Execute(state, new InventoryToggleEquipClientCommand
		{
			ActorId = "hero",
			InventoryIndex = 0,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
		Assert.True(state.Actors["hero"].Inventory[0].Equipped);
	}

	[Fact]
	public void InventoryDrop_Success_EmitsDropEvent_AndMovesItemToGround()
	{
		var state = CreateState();
		var item = CreateItem("drop_item", "Drop Item");
		state.Actors["hero"].Inventory.Add(item);

		var result = ServerActionGateway.Execute(state, new InventoryDropClientCommand
		{
			ActorId = "hero",
			InventoryIndex = 0,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Logs);
		var evt = Assert.Single(result.Events);
		Assert.Equal("item_dropped", evt.Type);
		Assert.Empty(state.Actors["hero"].Inventory);
		Assert.NotNull(state.World!.PeekGroundItems(1, 1, 0).Find(i => string.Equals(i.InstanceId, item.InstanceId, StringComparison.Ordinal)));
	}

	[Fact]
	public void InventoryDrop_InvalidIndex_IsRejectedWithErrorCode()
	{
		var state = CreateState();

		var result = ServerActionGateway.Execute(state, new InventoryDropClientCommand
		{
			ActorId = "hero",
			InventoryIndex = 99,
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal("invalid_inventory_index", result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
	}

	[Fact]
	public void ChestTake_FromGroundContainer_TransfersItem_AndWritesLog()
	{
		var state = CreateState();
		var item = CreateItem("chest_apple", "Chest Apple");
		var chest = CreateContainer("ground_chest", "Ground Chest", item);
		state.World!.PlaceItem(1, 1, 0, chest);

		var result = ServerActionGateway.Execute(state, new ChestTakeClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Ground,
			ContainerInstanceId = chest.InstanceId,
			ContainerX = 1,
			ContainerY = 1,
			ContainerZ = 0,
			ItemInstanceId = item.InstanceId,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
		Assert.Contains(state.Actors["hero"].Inventory, entry => entry.InstanceId == item.InstanceId);
		var remainingChest = state.World!.PeekGroundItems(1, 1, 0).Find(i => string.Equals(i.InstanceId, chest.InstanceId, StringComparison.Ordinal));
		Assert.NotNull(remainingChest);
		Assert.Empty(remainingChest!.Contents!);
	}

	[Fact]
	public void ChestTake_FromInventoryContainer_UsesOwnerInventoryContext()
	{
		var state = CreateState();
		var item = CreateItem("backpack_ration", "Backpack Ration");
		var backpack = CreateContainer("backpack", "Backpack", item);
		state.Actors["hero"].Inventory.Add(backpack);

		var result = ServerActionGateway.Execute(state, new ChestTakeClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Inventory,
			ContainerInstanceId = backpack.InstanceId,
			ContainerOwnerActorId = "hero",
			ItemInstanceId = item.InstanceId,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
		Assert.Contains(state.Actors["hero"].Inventory, entry => entry.InstanceId == item.InstanceId);
		Assert.Empty(backpack.Contents!);
	}

	[Fact]
	public void ChestTakeAll_Success_TransfersEntireContents_AndLogsCount()
	{
		var state = CreateState();
		var chest = CreateContainer(
			"stash",
			"Stash",
			CreateItem("stash_a", "Stash A"),
			CreateItem("stash_b", "Stash B"));
		state.World!.PlaceItem(1, 1, 0, chest);

		var result = ServerActionGateway.Execute(state, new ChestTakeAllClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Ground,
			ContainerInstanceId = chest.InstanceId,
			ContainerX = 1,
			ContainerY = 1,
			ContainerZ = 0,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
		Assert.Equal(2, state.Actors["hero"].Inventory.Count(entry => entry.Id.StartsWith("stash_", StringComparison.Ordinal)));
		var emptiedChest = state.World!.PeekGroundItems(1, 1, 0).Find(i => string.Equals(i.InstanceId, chest.InstanceId, StringComparison.Ordinal));
		Assert.NotNull(emptiedChest);
		Assert.Empty(emptiedChest!.Contents!);
	}

	[Fact]
	public void ChestTakeAll_InvalidContainer_IsRejected()
	{
		var state = CreateState();
		var result = ServerActionGateway.Execute(state, new ChestTakeAllClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Ground,
			ContainerInstanceId = "missing-container",
			ContainerX = 1,
			ContainerY = 1,
			ContainerZ = 0,
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal("invalid_container", result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
	}

	[Fact]
	public void ChestPut_RejectsEquippedItem_WithExplicitErrorCode()
	{
		var state = CreateState();
		var equipped = CreateEquippableItem();
		equipped.Equipped = true;
		state.Actors["hero"].Inventory.Add(equipped);
		var chest = CreateContainer("put_chest", "Put Chest");
		state.World!.PlaceItem(1, 1, 0, chest);

		var result = ServerActionGateway.Execute(state, new ChestPutClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Ground,
			ContainerInstanceId = chest.InstanceId,
			ContainerX = 1,
			ContainerY = 1,
			ContainerZ = 0,
			InventoryIndex = 0,
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal("item_equipped", result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
		Assert.Empty(chest.Contents!);
	}

	[Fact]
	public void ChestPut_Success_MovesItemToContainer_AndLogs()
	{
		var state = CreateState();
		var apple = CreateItem("pack_apple", "Pack Apple");
		state.Actors["hero"].Inventory.Add(apple);
		var chest = CreateContainer("put_chest_ok", "Put Chest");
		state.World!.PlaceItem(1, 1, 0, chest);

		var result = ServerActionGateway.Execute(state, new ChestPutClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Ground,
			ContainerInstanceId = chest.InstanceId,
			ContainerX = 1,
			ContainerY = 1,
			ContainerZ = 0,
			InventoryIndex = 0,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
		Assert.Empty(state.Actors["hero"].Inventory);
		var updatedChest = state.World!.PeekGroundItems(1, 1, 0).Find(i => string.Equals(i.InstanceId, chest.InstanceId, StringComparison.Ordinal));
		Assert.NotNull(updatedChest);
		Assert.Contains(updatedChest!.Contents!, item => item.InstanceId == apple.InstanceId);
	}

	[Fact]
	public void TradeBuy_Success_UpdatesGold_AndMissingGoodIsRejected()
	{
		var state = CreateState();
		state.Actors["hero"].Gold = 80;
		state.Actors["trader"].Gold = 10;
		state.Actors["trader"].ShopSlots.Add(new ShopSlot
		{
			Item = CreateItem("trade_potion", "Trade Potion", price: 25),
			Stock = 1,
		});

		var success = ServerActionGateway.Execute(state, new TradeBuyClientCommand
		{
			ActorId = "hero",
			TraderActorId = "trader",
			GoodSource = TradeGoodSourceKind.Shop,
			GoodIndex = 0,
		});

		Assert.Equal(ServerActionStatus.Accepted, success.Status);
		Assert.Null(success.ErrorCode);
		Assert.Empty(success.Events);
		Assert.Equal(2, success.Logs.Count);
		Assert.Equal(55, state.Actors["hero"].Gold);
		Assert.Equal(35, state.Actors["trader"].Gold);

		var missing = ServerActionGateway.Execute(state, new TradeBuyClientCommand
		{
			ActorId = "hero",
			TraderActorId = "trader",
			GoodSource = TradeGoodSourceKind.Shop,
			GoodIndex = 99,
		});

		Assert.Equal(ServerActionStatus.Rejected, missing.Status);
		Assert.Equal("trade_good_missing", missing.ErrorCode);
		Assert.Empty(missing.Events);
		Assert.Single(missing.Logs);
	}

	[Fact]
	public void TradeSell_Success_UpdatesGold_AndMissingTraderIsRejected()
	{
		var state = CreateState();
		state.Actors["hero"].Inventory.Add(CreateItem("sell_item", "Sell Item", price: 40));
		state.Actors["trader"].Gold = 100;

		var success = ServerActionGateway.Execute(state, new TradeSellClientCommand
		{
			ActorId = "hero",
			TraderActorId = "trader",
			InventoryIndex = 0,
		});

		Assert.Equal(ServerActionStatus.Accepted, success.Status);
		Assert.Null(success.ErrorCode);
		Assert.Empty(success.Events);
		Assert.Equal(2, success.Logs.Count);
		Assert.Equal(20, state.Actors["hero"].Gold);
		Assert.Equal(80, state.Actors["trader"].Gold);

		var missing = ServerActionGateway.Execute(state, new TradeSellClientCommand
		{
			ActorId = "hero",
			TraderActorId = "missing",
			InventoryIndex = 0,
		});

		Assert.Equal(ServerActionStatus.Rejected, missing.Status);
		Assert.Equal("invalid_trade_actor", missing.ErrorCode);
		Assert.Empty(missing.Events);
		Assert.Single(missing.Logs);
	}

	[Fact]
	public void DelegateAndReclaim_ReturnSuccessAndFailureCodes()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");

		var delegated = ServerActionGateway.Execute(state, new DelegateActorClientCommand
		{
			PlayerSessionId = "owner",
			ActorId = "hero",
			TargetPlayerSessionId = "guest",
		});

		Assert.Equal(ServerActionStatus.Accepted, delegated.Status);
		Assert.Null(delegated.ErrorCode);
		Assert.Empty(delegated.Events);
		Assert.Single(delegated.Logs);
		Assert.Equal("guest", RoomRuntimeModule.GetCurrentControllerPlayerId(state, "hero"));

		var delegateFailed = ServerActionGateway.Execute(state, new DelegateActorClientCommand
		{
			PlayerSessionId = "owner",
			ActorId = "hero",
			TargetPlayerSessionId = "missing",
		});

		Assert.Equal(ServerActionStatus.Rejected, delegateFailed.Status);
		Assert.Equal("delegate_failed", delegateFailed.ErrorCode);
		Assert.Empty(delegateFailed.Events);
		Assert.Single(delegateFailed.Logs);

		var reclaimed = ServerActionGateway.Execute(state, new ReclaimPrimaryActorClientCommand
		{
			ActorId = "hero",
			PlayerSessionId = "owner",
		});

		Assert.Equal(ServerActionStatus.Accepted, reclaimed.Status);
		Assert.Null(reclaimed.ErrorCode);
		Assert.Empty(reclaimed.Events);
		Assert.Single(reclaimed.Logs);
		Assert.Equal("owner", RoomRuntimeModule.GetCurrentControllerPlayerId(state, "hero"));

		var reclaimFailed = ServerActionGateway.Execute(state, new ReclaimPrimaryActorClientCommand
		{
			ActorId = "hero",
			PlayerSessionId = "guest",
		});

		Assert.Equal(ServerActionStatus.Rejected, reclaimFailed.Status);
		Assert.Equal("reclaim_failed", reclaimFailed.ErrorCode);
		Assert.Empty(reclaimFailed.Events);
		Assert.Single(reclaimFailed.Logs);
	}

	[Fact]
	public void ChestTake_InvalidContainer_IsRejected_WithErrorAndLog()
	{
		var state = CreateState();

		var result = ServerActionGateway.Execute(state, new ChestTakeClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Ground,
			ContainerInstanceId = "missing_container",
			ContainerX = 1,
			ContainerY = 1,
			ContainerZ = 0,
			ItemInstanceId = "missing_item",
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal("invalid_container", result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
	}

	[Fact]
	public void ChestPut_InvalidInventoryIndex_IsRejected_WithErrorCode()
	{
		var state = CreateState();
		var chest = CreateContainer("put_chest", "Put Chest");
		state.World!.PlaceItem(1, 1, 0, chest);

		var result = ServerActionGateway.Execute(state, new ChestPutClientCommand
		{
			ActorId = "hero",
			ContainerSource = ContainerSourceKind.Ground,
			ContainerInstanceId = chest.InstanceId,
			ContainerX = 1,
			ContainerY = 1,
			ContainerZ = 0,
			InventoryIndex = 42,
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal("invalid_inventory_index", result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.Single(result.Logs);
	}

	[Fact]
	public void ModalCommands_ManageReservations_InMultiplayer()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");

		var open = ServerActionGateway.Execute(state, new OpenModalClientCommand
		{
			PlayerSessionId = "owner",
			ModalId = "trade:trader",
		});
		Assert.Equal(ServerActionStatus.Accepted, open.Status);
		Assert.Contains("trade:trader", state.Room.InteractionReservations.Keys);

		var busy = ServerActionGateway.Execute(state, new OpenModalClientCommand
		{
			PlayerSessionId = "guest",
			ModalId = "trade:trader",
		});
		Assert.Equal(ServerActionStatus.Busy, busy.Status);
		Assert.Equal("reservation_busy", busy.ErrorCode);
		Assert.Equal("trade:trader", busy.ReservationKey);
		Assert.Equal("owner", busy.BusyByPlayerSessionId);

		var close = ServerActionGateway.Execute(state, new CloseModalClientCommand
		{
			PlayerSessionId = "owner",
			ModalId = "trade:trader",
		});
		Assert.Equal(ServerActionStatus.Accepted, close.Status);
		Assert.DoesNotContain("trade:trader", state.Room.InteractionReservations.Keys);
	}

	[Fact]
	public void CombatAttack_PvpDisabled_RejectsCrossPlayerAttack()
	{
		var state = CreateState();
		ConfigureMultiplayerCombat(state, pvpEnabled: false, teamMode: TeamMode.Solo, friendlyFire: true);

		var result = ServerActionGateway.Execute(state, new AttackClientCommand
		{
			PlayerSessionId = "owner",
			ActorId = "hero",
			TargetActorId = "enemy",
			SkillId = "basic_attack",
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal(ErrorCode.PvpDisabled.ToWireCode(), result.ErrorCode);
		Assert.Empty(result.Events);
	}

	[Fact]
	public void CombatAttack_ManualTeamFriendlyFireDisabled_RejectsSameTeamAttack()
	{
		var state = CreateState();
		ConfigureMultiplayerCombat(state, pvpEnabled: true, teamMode: TeamMode.Manual, friendlyFire: false);
		state.Room.Players["owner"].TeamId = "alpha";
		state.Room.Players["guest"].TeamId = "alpha";

		var result = ServerActionGateway.Execute(state, new AttackClientCommand
		{
			PlayerSessionId = "owner",
			ActorId = "hero",
			TargetActorId = "enemy",
			SkillId = "basic_attack",
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal(ErrorCode.FriendlyFireDisabled.ToWireCode(), result.ErrorCode);
		Assert.Empty(result.Events);
	}

	[Fact]
	public void CombatAttack_ManualTeamDifferentTeams_AllowsAttackEvaluation()
	{
		var state = CreateState();
		ConfigureMultiplayerCombat(state, pvpEnabled: true, teamMode: TeamMode.Manual, friendlyFire: false);
		state.Room.Players["owner"].TeamId = "alpha";
		state.Room.Players["guest"].TeamId = "beta";

		var result = ServerActionGateway.Execute(state, new AttackClientCommand
		{
			PlayerSessionId = "owner",
			ActorId = "hero",
			TargetActorId = "enemy",
			SkillId = "basic_attack",
		});

		Assert.NotEqual(ErrorCode.PvpDisabled.ToWireCode(), result.ErrorCode);
		Assert.NotEqual(ErrorCode.FriendlyFireDisabled.ToWireCode(), result.ErrorCode);
	}

	[Fact]
	public void HandleActorKilled_RewardsGold_AndEmitsLog()
	{
		var state = CreateState();

		var result = ServerActionGateway.HandleActorKilled(state, new GameEvent("actor_killed")
		{
			TargetId = "enemy",
			TargetActorName = "Enemy",
			TargetX = 3,
			TargetY = 1,
			TargetZ = 0,
			Damage = 7,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		Assert.Empty(result.Events);
		Assert.NotEmpty(result.Logs);
		Assert.Equal(7, state.Actors["hero"].Gold);
	}

	private static GameState CreateState()
	{
		var state = new GameState
		{
			WorldSeed = 424242,
			PlayerId = "hero",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "test",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(424242, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(424242),
		};

		ActorModule.Add(state, CreateActor("hero", Factions.Player, 1, 1, 0));
		ActorModule.Add(state, CreateActor("enemy", Factions.Hostile, 3, 1, 0));
		ActorModule.Add(state, CreateActor("trader", Factions.Friendly, 1, 2, 0));
		return state;
	}

	private static void ConfigureMultiplayerCombat(GameState state, bool pvpEnabled, TeamMode teamMode, bool friendlyFire)
	{
		state.Room.RoomId = "room-1";
		state.Room.RoomCode = "R1";
		state.Room.SimulationMode = RoomSimulationMode.CombatTurnBased;
		state.Room.Rules.PvpEnabled = pvpEnabled;
		state.Room.Rules.TeamMode = teamMode;
		state.Room.Rules.FriendlyFire = friendlyFire;

		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		RoomRuntimeModule.AssignPrimaryActor(state, "guest", "enemy");
	}

	private static Actor CreateActor(string id, string faction, int x, int y, int z) => new()
	{
		Id = id,
		TemplateId = "player",
		X = x,
		Y = y,
		Z = z,
		Glyph = "@",
		DisplayName = id,
		Faction = faction,
		FacingX = 1,
		FacingY = 0,
		Limbs =
		[
			new Limb
			{
				Id = $"{id}_arm",
				Name = "Arm",
				MaxDurability = 10,
				Durability = 10,
				Material = "flesh",
				BodyPart = BodyParts.Arm,
				EquipLayers = [EquipLayer.Middle],
				EquipSlots =
				[
					new EquipSlot
					{
						LimbId = $"{id}_arm",
						BodyPart = BodyParts.Arm,
						Layer = EquipLayer.Middle,
					},
				],
				Capacities = new Dictionary<string, float>
				{
					[Caps.Consciousness] = 1f,
					[Caps.Manipulation] = 1f,
					[Caps.Sight] = 1f,
				},
			},
		],
	};

	private static Item CreateItem(string id, string name, int price = 10)
	{
		var item = new Item
		{
			Id = id,
			Name = name,
			Price = price,
			Weight = 0.5f,
			MaxStack = 1,
			StackCount = 1,
			Category = ItemCategories.Misc,
		};
		item.EnsureRuntimeState();
		return item;
	}

	private static Item CreateEquippableItem()
	{
		var item = new Item
		{
			Id = "arm_wrap",
			Name = "Arm Wrap",
			Price = 8,
			Category = ItemCategories.Clothing,
			Weight = 0.5f,
			BodyPart = BodyParts.Arm,
			Layer = EquipLayer.Middle,
			CoveredParts = [BodyParts.Arm],
			GrantedSkills = [],
			Tags = new Dictionary<string, int>(),
		};
		item.EnsureRuntimeState();
		return item;
	}

	private static Item CreateContainer(string id, string name, params Item[] contents)
	{
		var container = CreateItem(id, name);
		container.Contents = contents.ToList();
		container.EnsureRuntimeState();
		return container;
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
