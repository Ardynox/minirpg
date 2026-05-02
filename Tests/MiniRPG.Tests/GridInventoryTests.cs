using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

[Collection("GridInventorySerial")]
public sealed class GridInventoryTests : IDisposable
{
	public GridInventoryTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
		// 隔离：本测试 fixture 完全控制 W×H 表，不让 item_sizes.json 默认值影响断言。
		ItemSizeRegistry.OverrideForTesting(
			byId: new Dictionary<string, ItemSizeDef>(StringComparer.Ordinal)
			{
				["small_potion"] = new() { Width = 1, Height = 1, AllowRotation = false },
				["bandage"] = new() { Width = 1, Height = 1, AllowRotation = false },
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
				["test_vest"] = new()
				{
					Width = 2,
					Height = 2,
					AllowRotation = false,
					ProvidesGrid = new ItemProvidedGrid
					{
						Kind = GridContainerKind.Vest,
						Width = 3,
						Height = 2,
					},
				},
			});
	}

	public void Dispose()
	{
		ItemSizeRegistry.ResetForTesting();
	}

	private static Actor MakeActor()
	{
		var actor = PresetDB.SpawnActor("player", "grid_test_" + Guid.NewGuid().ToString("N")[..8]);
		actor.Inventory.Clear();
		InventoryModule.RebuildGridFromLegacyList(actor);
		return actor;
	}

	private static Item MakeItem(string id, int stack = 1, int maxStack = 1, string? bodyPart = null)
	{
		var item = new Item
		{
			Id = id,
			Name = id,
			Category = ItemCategories.Misc,
			MaxStack = maxStack,
			StackCount = stack,
			BodyPart = bodyPart ?? string.Empty,
			Layer = bodyPart != null ? EquipLayer.Shell : default,
		};
		item.EnsureRuntimeState();
		return item;
	}

	// ── 1. 默认网格 ─────────────────────────────────────

	[Fact]
	public void EnsureGridSynchronized_CreatesPocketsAtDefaultSize()
	{
		var actor = MakeActor();
		Assert.True(actor.GridInventories.ContainsKey(GridInventory.PocketsId));
		var pocket = actor.GridInventories[GridInventory.PocketsId];
		Assert.Equal(ItemSizeRegistry.DefaultPocketWidth, pocket.Width);
		Assert.Equal(ItemSizeRegistry.DefaultPocketHeight, pocket.Height);
		Assert.Equal(GridContainerKind.Pocket, pocket.Kind);
	}

	// ── 2. Add 自动占位 ─────────────────────────────────

	[Fact]
	public void Add_SmallItem_AutoPlacesInPocket()
	{
		var actor = MakeActor();
		var item = MakeItem("small_potion");
		InventoryModule.Add(actor, item);
		var pocket = actor.GridInventories[GridInventory.PocketsId];
		var placement = pocket.FindPlacement(item.InstanceId);
		Assert.NotNull(placement);
		Assert.Equal(0, placement!.X);
		Assert.Equal(0, placement.Y);
		Assert.Equal(1, placement.Width);
		Assert.Equal(1, placement.Height);
	}

	// ── 3. Pocket 满了，旧 Add 仍允许（unplaced） ─────────

	[Fact]
	public void Add_FullPocket_LeavesNewItemUnplaced()
	{
		var actor = MakeActor();
		// 2x2 pocket = 4 格，先塞 4 个 1x1
		for (var i = 0; i < 4; i++)
			InventoryModule.Add(actor, MakeItem("small_potion"));

		var overflowItem = MakeItem("bandage");
		InventoryModule.Add(actor, overflowItem);

		Assert.Equal(5, actor.Inventory.Count);
		var pocket = actor.GridInventories[GridInventory.PocketsId];
		Assert.Equal(4, pocket.Placements.Count);
		Assert.Null(pocket.FindPlacement(overflowItem.InstanceId));
		Assert.Contains(InventoryModule.GetUnplacedItems(actor), it => it.InstanceId == overflowItem.InstanceId);
	}

	// ── 4. TryAddStrict 满了直接拒绝 ────────────────────

	[Fact]
	public void TryAddStrict_FullPocket_RejectsAndDoesNotMutateInventory()
	{
		var actor = MakeActor();
		for (var i = 0; i < 4; i++)
			InventoryModule.Add(actor, MakeItem("small_potion"));

		var overflowItem = MakeItem("bandage");
		var result = InventoryModule.TryAddStrict(actor, overflowItem);

		Assert.False(result.Ok);
		Assert.Equal(GridErrorCodes.GridFull, result.ErrorCode);
		Assert.Equal(4, actor.Inventory.Count);
	}

	// ── 5. RebuildFromLegacyList ───────────────────────

	[Fact]
	public void RebuildGridFromLegacyList_RecoversPlacementsForAllItems()
	{
		var actor = MakeActor();
		actor.Inventory.Clear();
		// 直接绕过 InventoryModule 模拟 spawn / save load
		var p1 = MakeItem("small_potion");
		var p2 = MakeItem("bandage");
		actor.Inventory.Add(p1);
		actor.Inventory.Add(p2);
		actor.GridInventories.Clear();

		InventoryModule.RebuildGridFromLegacyList(actor);

		var pocket = actor.GridInventories[GridInventory.PocketsId];
		Assert.Equal(2, pocket.Placements.Count);
		Assert.NotNull(pocket.FindPlacement(p1.InstanceId));
		Assert.NotNull(pocket.FindPlacement(p2.InstanceId));
	}

	// ── 6. 装备容器开放子网格 ───────────────────────────

	[Fact]
	public void EquipBackpack_ExposesSubGridAndRemovesFromMain()
	{
		var actor = MakeActor();
		var backpack = MakeItem("test_backpack", bodyPart: BodyParts.Torso);
		// 让 actor 有 Torso 的 Shell 槽
		EnsureTorsoShellSlot(actor);
		InventoryModule.Add(actor, backpack);

		// 装备前：backpack 在主 pocket 里（2x2）
		var pocketBefore = actor.GridInventories[GridInventory.PocketsId];
		Assert.NotNull(pocketBefore.FindPlacement(backpack.InstanceId));

		var equipResult = InventoryModule.Equip(actor, backpack);
		Assert.True(equipResult.Ok);

		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);
		Assert.True(actor.GridInventories.ContainsKey(subGridId));
		var sub = actor.GridInventories[subGridId];
		Assert.Equal(4, sub.Width);
		Assert.Equal(4, sub.Height);
		Assert.Equal(GridContainerKind.Backpack, sub.Kind);

		// 装备的 backpack 不再占主网格
		var pocketAfter = actor.GridInventories[GridInventory.PocketsId];
		Assert.Null(pocketAfter.FindPlacement(backpack.InstanceId));
	}

	// ── 7. 卸下容器收子网格 + 物品成 unplaced ────────────

	[Fact]
	public void UnequipBackpack_RemovesSubGrid_AndContentsBecomeUnplacedIfNoSpace()
	{
		var actor = MakeActor();
		EnsureTorsoShellSlot(actor);
		var backpack = MakeItem("test_backpack", bodyPart: BodyParts.Torso);
		InventoryModule.Add(actor, backpack);
		InventoryModule.Equip(actor, backpack);

		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);
		// 在子 backpack 里放 6 个小物品（pocket 装不下这么多）
		var inside = new List<Item>();
		for (var i = 0; i < 6; i++)
		{
			var it = MakeItem("small_potion");
			InventoryModule.Add(actor, it);
			inside.Add(it);
		}
		Assert.Equal(6, actor.GridInventories[subGridId].Placements.Count);

		var unequipResult = InventoryModule.Unequip(actor, backpack);
		Assert.True(unequipResult.Ok);
		Assert.False(actor.GridInventories.ContainsKey(subGridId));

		// 6 件物品仍在 inventory，但 pocket 只能装 4，剩 2 件 unplaced。
		Assert.Equal(7, actor.Inventory.Count); // 6 small + 1 backpack
		var unplaced = InventoryModule.GetUnplacedItems(actor);
		Assert.True(unplaced.Count >= 2);
	}

	// ── 8. TryMoveTo 越界 ──────────────────────────────

	[Fact]
	public void TryMoveTo_OutOfBounds_Rejects()
	{
		var actor = MakeActor();
		var item = MakeItem("small_potion");
		InventoryModule.Add(actor, item);

		var result = InventoryModule.TryMoveTo(
			actor, item.InstanceId, GridInventory.PocketsId, x: 5, y: 0, rotated: false);

		Assert.False(result.Ok);
		Assert.Equal(GridErrorCodes.OutOfBounds, result.ErrorCode);
	}

	// ── 9. TryMoveTo 重叠 ──────────────────────────────

	[Fact]
	public void TryMoveTo_OverlapsExistingItem_Rejects()
	{
		var actor = MakeActor();
		EnsureTorsoShellSlot(actor);
		var backpack = MakeItem("test_backpack", bodyPart: BodyParts.Torso);
		InventoryModule.Add(actor, backpack);
		InventoryModule.Equip(actor, backpack);
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);

		var pistol = MakeItem("pistol");
		InventoryModule.Add(actor, pistol);
		var rifle = MakeItem("rifle");
		InventoryModule.Add(actor, rifle);

		// 把 pistol 显式放到 (0,0)
		var moveA = InventoryModule.TryMoveTo(actor, pistol.InstanceId, subGridId, 0, 0, false);
		Assert.True(moveA.Ok);
		// rifle 1x4 想盖到 (0,0) → 与 pistol 重叠
		var moveB = InventoryModule.TryMoveTo(actor, rifle.InstanceId, subGridId, 0, 0, false);
		Assert.False(moveB.Ok);
		Assert.Equal(GridErrorCodes.OverlapsExisting, moveB.ErrorCode);
	}

	// ── 10. TryMoveTo 成功路径 + 旋转 ────────────────────

	[Fact]
	public void TryMoveTo_RotatedFits_AcceptsAndUpdatesPlacement()
	{
		var actor = MakeActor();
		EnsureTorsoShellSlot(actor);
		var backpack = MakeItem("test_backpack", bodyPart: BodyParts.Torso);
		InventoryModule.Add(actor, backpack);
		InventoryModule.Equip(actor, backpack);
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);

		var rifle = MakeItem("rifle");
		InventoryModule.Add(actor, rifle);
		// rifle 是 1x4，旋转后 4x1，放在 (0, 3)：占满最底排
		var move = InventoryModule.TryMoveTo(actor, rifle.InstanceId, subGridId, 0, 3, rotated: true);
		Assert.True(move.Ok);
		Assert.Equal(0, move.X);
		Assert.Equal(3, move.Y);
		Assert.True(move.Rotated);

		var placement = actor.GridInventories[subGridId].FindPlacement(rifle.InstanceId)!;
		Assert.Equal(4, placement.Width);
		Assert.Equal(1, placement.Height);
		Assert.True(placement.Rotated);
	}

	// ── 11. TryRotate 在原地 swap ─────────────────────────

	[Fact]
	public void TryRotate_RotationFits_SwapsWidthHeight()
	{
		var actor = MakeActor();
		EnsureTorsoShellSlot(actor);
		var backpack = MakeItem("test_backpack", bodyPart: BodyParts.Torso);
		InventoryModule.Add(actor, backpack);
		InventoryModule.Equip(actor, backpack);
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);

		var rifle = MakeItem("rifle");
		InventoryModule.Add(actor, rifle);
		// rifle 1x4 默认放在 (0,0) 占 column 0
		var rotate = InventoryModule.TryRotate(actor, rifle.InstanceId);
		Assert.True(rotate.Ok);

		var placement = actor.GridInventories[subGridId].FindPlacement(rifle.InstanceId)!;
		Assert.Equal(4, placement.Width);
		Assert.Equal(1, placement.Height);
		Assert.True(placement.Rotated);
	}

	// ── 12. AutoPackAll 重新整理所有物品 ────────────────

	[Fact]
	public void AutoPackAll_ClearsAndReplacesAllNonEquippedItems()
	{
		var actor = MakeActor();
		EnsureTorsoShellSlot(actor);
		var backpack = MakeItem("test_backpack", bodyPart: BodyParts.Torso);
		InventoryModule.Add(actor, backpack);
		InventoryModule.Equip(actor, backpack);

		var pistol = MakeItem("pistol");
		var rifle = MakeItem("rifle");
		InventoryModule.Add(actor, pistol);
		InventoryModule.Add(actor, rifle);

		// 把所有 placement 弄乱
		var subGridId = GridInventory.MakeEquippedId(backpack.InstanceId);
		actor.GridInventories[subGridId].Placements.Clear();

		var placed = InventoryModule.AutoPackAll(actor);
		Assert.True(placed >= 2);
		Assert.NotNull(actor.GridInventories[subGridId].FindPlacement(pistol.InstanceId));
		Assert.NotNull(actor.GridInventories[subGridId].FindPlacement(rifle.InstanceId));
	}

	// ── 13. RemoveByInstance 同步 ───────────────────────

	[Fact]
	public void RemoveByInstance_RemovesPlacementAndInventoryItem()
	{
		var actor = MakeActor();
		var item = MakeItem("small_potion");
		InventoryModule.Add(actor, item);
		Assert.Single(actor.GridInventories[GridInventory.PocketsId].Placements);

		var removed = InventoryModule.RemoveByInstance(actor, item.InstanceId);
		Assert.NotNull(removed);
		Assert.Empty(actor.Inventory);
		Assert.Empty(actor.GridInventories[GridInventory.PocketsId].Placements);
	}

	// ── 14. Stacking 不消耗额外格子 ─────────────────────

	[Fact]
	public void Add_StackingMerges_DoesNotConsumeAdditionalGridSlots()
	{
		var actor = MakeActor();
		var first = MakeItem("small_potion", stack: 3, maxStack: 10);
		InventoryModule.Add(actor, first);
		var second = MakeItem("small_potion", stack: 4, maxStack: 10);
		InventoryModule.Add(actor, second);

		Assert.Single(actor.Inventory);
		Assert.Equal(7, actor.Inventory[0].StackCount);
		Assert.Single(actor.GridInventories[GridInventory.PocketsId].Placements);
	}

	private static void EnsureTorsoShellSlot(Actor actor)
	{
		var torso = actor.Limbs.FirstOrDefault(l =>
			string.Equals(l.BodyPart, BodyParts.Torso, StringComparison.Ordinal));
		if (torso == null) return;
		if (torso.EquipSlots.Any(s => s.BodyPart == BodyParts.Torso && s.Layer == EquipLayer.Shell))
			return;
		torso.EquipSlots.Add(new EquipSlot
		{
			LimbId = torso.Id,
			BodyPart = BodyParts.Torso,
			Layer = EquipLayer.Shell,
		});
	}
}
