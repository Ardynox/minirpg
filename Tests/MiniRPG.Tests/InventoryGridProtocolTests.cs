using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

[Collection("GridInventorySerial")]
public sealed class InventoryGridProtocolTests : IDisposable
{
	public InventoryGridProtocolTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
		ItemSizeRegistry.OverrideForTesting(
			byId: new Dictionary<string, ItemSizeDef>(StringComparer.Ordinal)
			{
				["small_potion"] = new() { Width = 1, Height = 1, AllowRotation = false },
				["pistol"] = new() { Width = 2, Height = 2, AllowRotation = true },
				["rifle"] = new() { Width = 1, Height = 4, AllowRotation = true },
				["test_backpack"] = new()
				{
					Width = 2,
					Height = 2,
					AllowRotation = false,
					ProvidesGrid = new ItemProvidedGrid
					{
						Kind = GridContainerKind.Backpack,
						Width = 4,
						Height = 4,
					},
				},
			});
	}

	public void Dispose()
	{
		ItemSizeRegistry.ResetForTesting();
	}

	[Fact]
	public void InventoryMoveItem_ValidMove_AcceptedAndPlacementWritten()
	{
		var state = CreateStateWithEquippedBackpack(out var actor, out var backpack);
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);
		var pistol = MakeItem("pistol");
		InventoryModule.Add(actor, pistol);

		var result = ServerActionGateway.Execute(state, new InventoryMoveItemClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = pistol.InstanceId,
			TargetGridId = subGridId,
			X = 2,
			Y = 2,
			Rotated = false,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Null(result.ErrorCode);
		var placement = actor.GridInventories[subGridId].FindPlacement(pistol.InstanceId);
		Assert.NotNull(placement);
		Assert.Equal(2, placement!.X);
		Assert.Equal(2, placement.Y);
	}

	[Fact]
	public void InventoryMoveItem_OutOfBounds_RejectedWithGridOutOfBoundsCode()
	{
		var state = CreateStateWithEquippedBackpack(out var actor, out _);
		var pistol = MakeItem("pistol");
		InventoryModule.Add(actor, pistol);

		var result = ServerActionGateway.Execute(state, new InventoryMoveItemClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = pistol.InstanceId,
			TargetGridId = GridInventory.PocketsId,
			X = 10,
			Y = 10,
			Rotated = false,
		});

		Assert.Equal(ServerActionStatus.Rejected, result.Status);
		Assert.Equal("grid_out_of_bounds", result.ErrorCode);
	}

	[Fact]
	public void InventoryMoveItem_OverlapsExisting_RejectedWithOverlapCode()
	{
		var state = CreateStateWithEquippedBackpack(out var actor, out var backpack);
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);
		var pistol = MakeItem("pistol");
		var rifle = MakeItem("rifle");
		InventoryModule.Add(actor, pistol);
		InventoryModule.Add(actor, rifle);

		ServerActionGateway.Execute(state, new InventoryMoveItemClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = pistol.InstanceId,
			TargetGridId = subGridId,
			X = 0,
			Y = 0,
			Rotated = false,
		});
		var overlap = ServerActionGateway.Execute(state, new InventoryMoveItemClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = rifle.InstanceId,
			TargetGridId = subGridId,
			X = 0,
			Y = 0,
			Rotated = false,
		});

		Assert.Equal(ServerActionStatus.Rejected, overlap.Status);
		Assert.Equal("grid_overlaps_existing", overlap.ErrorCode);
	}

	[Fact]
	public void InventoryRotateItem_RotationFits_AcceptsAndUpdatesPlacement()
	{
		var state = CreateStateWithEquippedBackpack(out var actor, out var backpack);
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);
		var rifle = MakeItem("rifle");
		InventoryModule.Add(actor, rifle);
		InventoryModule.TryMoveTo(actor, rifle.InstanceId, subGridId, 0, 0, rotated: false);

		var result = ServerActionGateway.Execute(state, new InventoryRotateItemClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = rifle.InstanceId,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		var placement = actor.GridInventories[subGridId].FindPlacement(rifle.InstanceId)!;
		Assert.True(placement.Rotated);
		Assert.Equal(4, placement.Width);
		Assert.Equal(1, placement.Height);
	}

	[Fact]
	public void InventoryAutoPack_RestoresAllPlacements_AfterScramble()
	{
		var state = CreateStateWithEquippedBackpack(out var actor, out var backpack);
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);
		var pistol = MakeItem("pistol");
		InventoryModule.Add(actor, pistol);
		actor.GridInventories[subGridId].Placements.Clear();

		var result = ServerActionGateway.Execute(state, new InventoryAutoPackClientCommand { ActorId = actor.Id });

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.NotNull(actor.GridInventories[subGridId].FindPlacement(pistol.InstanceId));
	}

	[Fact]
	public void InventoryToggleEquip_ItemInstanceId_PathSucceeds()
	{
		var state = CreateBaseState(out var actor);
		var item = MakeEquippableItem();
		InventoryModule.Add(actor, item);

		var result = ServerActionGateway.Execute(state, new InventoryToggleEquipClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = item.InstanceId,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.True(actor.Inventory[0].Equipped);
	}

	[Fact]
	public void InventoryToggleEquip_LegacyIndexFallback_StillWorks()
	{
		var state = CreateBaseState(out var actor);
		var item = MakeEquippableItem();
		InventoryModule.Add(actor, item);

		var result = ServerActionGateway.Execute(state, new InventoryToggleEquipClientCommand
		{
			ActorId = actor.Id,
			InventoryIndex = 0,
			ItemInstanceId = null,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.True(actor.Inventory[0].Equipped);
	}

	[Fact]
	public void InventoryDrop_ItemInstanceId_PathSucceeds()
	{
		var state = CreateBaseState(out var actor);
		var item = MakeItem("small_potion");
		InventoryModule.Add(actor, item);

		var result = ServerActionGateway.Execute(state, new InventoryDropClientCommand
		{
			ActorId = actor.Id,
			ItemInstanceId = item.InstanceId,
		});

		Assert.Equal(ServerActionStatus.Accepted, result.Status);
		Assert.Empty(actor.Inventory);
	}

	private static GameState CreateBaseState(out Actor actor)
	{
		var state = new GameState
		{
			WorldSeed = 4242,
			PlayerId = "hero",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "test",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(4242, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(4242),
		};

		actor = MakeActor();
		ActorModule.Add(state, actor);
		return state;
	}

	private static GameState CreateStateWithEquippedBackpack(out Actor actor, out Item backpack)
	{
		var state = CreateBaseState(out actor);
		backpack = MakeItem("test_backpack", BodyParts.Torso);
		InventoryModule.Add(actor, backpack);
		InventoryModule.Equip(actor, backpack);
		return state;
	}

	private static Actor MakeActor()
	{
		return new Actor
		{
			Id = "hero",
			TemplateId = "player",
			X = 1,
			Y = 1,
			Z = 0,
			Glyph = "@",
			DisplayName = "Hero",
			Faction = Factions.Player,
			FacingX = 1,
			FacingY = 0,
			Limbs =
			[
				new Limb
				{
					Id = "torso",
					Name = "Torso",
					MaxDurability = 10,
					Durability = 10,
					Material = "flesh",
					BodyPart = BodyParts.Torso,
					EquipLayers = [EquipLayer.Shell, EquipLayer.Middle],
					EquipSlots =
					[
						new EquipSlot { LimbId = "torso", BodyPart = BodyParts.Torso, Layer = EquipLayer.Shell },
						new EquipSlot { LimbId = "torso", BodyPart = BodyParts.Torso, Layer = EquipLayer.Middle },
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
	}

	private static Item MakeItem(string id, string? bodyPart = null)
	{
		var item = new Item
		{
			Id = id,
			Name = id,
			Category = ItemCategories.Misc,
			MaxStack = 1,
			StackCount = 1,
			BodyPart = bodyPart ?? string.Empty,
			Layer = bodyPart != null ? EquipLayer.Shell : default,
		};
		item.EnsureRuntimeState();
		return item;
	}

	private static Item MakeEquippableItem()
	{
		var item = new Item
		{
			Id = "arm_wrap",
			Name = "Arm Wrap",
			Category = ItemCategories.Clothing,
			BodyPart = BodyParts.Torso,
			Layer = EquipLayer.Middle,
			CoveredParts = [BodyParts.Torso],
		};
		item.EnsureRuntimeState();
		return item;
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));

		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
