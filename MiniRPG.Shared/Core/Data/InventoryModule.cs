using System;
using System.Collections.Generic;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public class InventoryResult
{
	public bool Ok { get; set; }
	public string Message { get; set; } = "";
}

/// <summary>
/// 网格背包"移动 / 旋转 / 整理"的执行结果。
/// </summary>
public sealed class GridMoveResult
{
	public bool Ok { get; init; }
	public string Message { get; init; } = "";
	public string? ErrorCode { get; init; }
	public string? ResultingGridId { get; init; }
	public int X { get; init; }
	public int Y { get; init; }
	public bool Rotated { get; init; }

	public static GridMoveResult Reject(string message, string errorCode) =>
		new() { Ok = false, Message = message, ErrorCode = errorCode };

	public static GridMoveResult Accept(string gridId, int x, int y, bool rotated, string message = "") =>
		new()
		{
			Ok = true,
			Message = message,
			ResultingGridId = gridId,
			X = x,
			Y = y,
			Rotated = rotated,
		};
}

public static class GridErrorCodes
{
	public const string GridNotFound = "GridNotFound";
	public const string ItemNotFound = "ItemNotFound";
	public const string OutOfBounds = "OutOfBounds";
	public const string OverlapsExisting = "OverlapsExisting";
	public const string GridFull = "GridFull";
	public const string InvalidActor = "InvalidActor";
	public const string RotationDisallowed = "RotationDisallowed";
}

/// <summary>
/// 背包模块：纯函数，操作 <see cref="Actor.Inventory"/>（事实源）+ <see cref="Actor.GridInventories"/>（空间索引）+ 装备槽。
/// 任何修改 inventory 的 API 都顺手维护 grid placement，避免 UI 与协议层各自手动同步。
/// </summary>
public static class InventoryModule
{
	// ── 网格生命周期 ─────────────────────────────────────

	/// <summary>
	/// 保证 actor.GridInventories 与 actor.Inventory 一致：
	/// 1) 必有 pockets 默认网格；2) 装备的容器物品 expose 子网格；3) 清掉孤儿 placement；
	/// 4) 给未被任何 grid 收纳的物品 auto-pack。允许调用任意次。
	/// </summary>
	public static void EnsureGridSynchronized(Actor actor)
	{
		if (actor == null) return;

		actor.GridInventories ??= new Dictionary<string, GridInventory>(StringComparer.Ordinal);
		EnsurePocketsExists(actor);

		var liveInstances = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in actor.Inventory)
		{
			if (!string.IsNullOrEmpty(item.InstanceId))
				liveInstances.Add(item.InstanceId);
		}

		// 1) 装备的容器物品需要拥有自己的子网格；移除已不再装备的容器子网格。
		var equippedContainerInstances = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in actor.Inventory)
		{
			if (!item.Equipped) continue;
			var providedGrid = ItemSizeRegistry.GetProvidedGrid(item);
			if (providedGrid == null) continue;
			equippedContainerInstances.Add(item.InstanceId);
			var gridId = GridInventory.MakeEquippedId(item.InstanceId);
			if (!actor.GridInventories.TryGetValue(gridId, out var existing))
			{
				actor.GridInventories[gridId] = new GridInventory
				{
					Id = gridId,
					Width = providedGrid.Width,
					Height = providedGrid.Height,
					Kind = providedGrid.Kind,
				};
			}
			else if (existing.Width != providedGrid.Width || existing.Height != providedGrid.Height || existing.Kind != providedGrid.Kind)
			{
				// 容器尺寸定义改了 → 重建网格，原 placement 全部清掉等下面 auto-pack
				existing.Width = providedGrid.Width;
				existing.Height = providedGrid.Height;
				existing.Kind = providedGrid.Kind;
				existing.Placements.Clear();
			}
		}

		var deadGridIds = new List<string>();
		foreach (var (gridId, _) in actor.GridInventories)
		{
			if (gridId.StartsWith(GridInventory.EquippedPrefix, StringComparison.Ordinal))
			{
				var ownerInstanceId = gridId[GridInventory.EquippedPrefix.Length..];
				if (!equippedContainerInstances.Contains(ownerInstanceId))
					deadGridIds.Add(gridId);
			}
		}
		foreach (var dead in deadGridIds)
			actor.GridInventories.Remove(dead);

		// 2) 清掉指向已不存在物品的 placement
		foreach (var grid in actor.GridInventories.Values)
			grid.Placements.RemoveAll(p => !liveInstances.Contains(p.ItemInstanceId));

		// 3) 给装备容器物品本身（出现在主 inventory 里）也算"已放置"——它在装备槽里不占主网格
		var placedInstances = new HashSet<string>(StringComparer.Ordinal);
		foreach (var grid in actor.GridInventories.Values)
			foreach (var p in grid.Placements)
				placedInstances.Add(p.ItemInstanceId);
		foreach (var instanceId in equippedContainerInstances)
			placedInstances.Add(instanceId);

		// 4) 给未被收纳的物品 auto-pack（按 backpack > vest > belt > pocket 顺序）。
		//    装备槽里的物品不占主网格 placement，跳过——上面 step 3 也只把"提供子网格的容器"算作已放置。
		foreach (var item in actor.Inventory)
		{
			if (string.IsNullOrEmpty(item.InstanceId)) continue;
			if (item.Equipped) continue;
			if (placedInstances.Contains(item.InstanceId)) continue;
			TryAutoPlaceIntoAnyGrid(actor, item);
		}
	}

	/// <summary>
	/// 强制重建网格：扔掉所有现有 placement，从 inventory 重新 auto-pack。
	/// spawn / 加载存档时调用；放不下的物品保持在 inventory 里 unplaced（UI 会显示溢出条）。
	/// </summary>
	public static void RebuildGridFromLegacyList(Actor actor)
	{
		if (actor == null) return;
		actor.GridInventories ??= new Dictionary<string, GridInventory>(StringComparer.Ordinal);

		// 保留 pockets（容器结构由装备状态推导，每次 sync 时刷新）
		actor.GridInventories.Clear();
		EnsurePocketsExists(actor);
		EnsureGridSynchronized(actor);
	}

	/// <summary>
	/// 列出 actor 身上有 inventory 但没有 grid placement 的物品（"溢出态"），UI 应展示警告条。
	/// </summary>
	public static List<Item> GetUnplacedItems(Actor actor)
	{
		var result = new List<Item>();
		if (actor == null) return result;

		var placed = new HashSet<string>(StringComparer.Ordinal);
		foreach (var grid in actor.GridInventories.Values)
			foreach (var p in grid.Placements)
				placed.Add(p.ItemInstanceId);

		foreach (var item in actor.Inventory)
		{
			if (item.Equipped) continue;
			if (ItemSizeRegistry.IsContainerEquipment(item) && item.Equipped) continue;
			if (string.IsNullOrEmpty(item.InstanceId)) continue;
			if (!placed.Contains(item.InstanceId))
				result.Add(item);
		}
		return result;
	}

	private static void EnsurePocketsExists(Actor actor)
	{
		if (actor.GridInventories.ContainsKey(GridInventory.PocketsId)) return;
		actor.GridInventories[GridInventory.PocketsId] = new GridInventory
		{
			Id = GridInventory.PocketsId,
			Width = ItemSizeRegistry.DefaultPocketWidth,
			Height = ItemSizeRegistry.DefaultPocketHeight,
			Kind = GridContainerKind.Pocket,
		};
	}

	private static GridPlacement? TryAutoPlaceIntoAnyGrid(Actor actor, Item item)
	{
		if (string.IsNullOrEmpty(item.InstanceId)) return null;
		var def = ItemSizeRegistry.GetDef(item);
		foreach (var grid in EnumerateGridsByPriority(actor))
		{
			var placement = grid.TryAutoPlace(item.InstanceId, def.Width, def.Height, def.AllowRotation);
			if (placement != null) return placement;
		}
		return null;
	}

	private static IEnumerable<GridInventory> EnumerateGridsByPriority(Actor actor)
	{
		// 装备的大背包/胸挂/腰带优先，口袋作为最后兜底（口袋是"贴身"，应留给关键小物件）
		var backpacks = new List<GridInventory>();
		var vests = new List<GridInventory>();
		var belts = new List<GridInventory>();
		var others = new List<GridInventory>();
		GridInventory? pocket = null;

		foreach (var (id, grid) in actor.GridInventories)
		{
			if (string.Equals(id, GridInventory.PocketsId, StringComparison.Ordinal))
			{
				pocket = grid;
				continue;
			}

			switch (grid.Kind)
			{
				case GridContainerKind.Backpack: backpacks.Add(grid); break;
				case GridContainerKind.Vest: vests.Add(grid); break;
				case GridContainerKind.Belt: belts.Add(grid); break;
				default: others.Add(grid); break;
			}
		}

		foreach (var g in backpacks) yield return g;
		foreach (var g in vests) yield return g;
		foreach (var g in belts) yield return g;
		foreach (var g in others) yield return g;
		if (pocket != null) yield return pocket;
	}

	private static void RemoveFromAllGrids(Actor actor, string instanceId)
	{
		if (actor == null || string.IsNullOrEmpty(instanceId)) return;
		foreach (var grid in actor.GridInventories.Values)
			grid.RemovePlacement(instanceId);
	}

	// ── 兼容旧 API：始终落 inventory；grid 同步进行 best-effort auto-place ──

	/// <summary>
	/// 加入背包：保持旧契约（永远成功），并尽量给新物品在 grid 上找位。
	/// 放不下时物品仍进 inventory（unplaced），让玩家手动整理。
	/// 协议 / 拖拽走 <see cref="TryAddStrict"/>，那条路径会拒绝放不下。
	/// </summary>
	public static void Add(Actor actor, Item item)
	{
		if (actor == null || item == null) return;
		item.EnsureRuntimeState();
		item.Equipped = false;

		EnsureGridSynchronized(actor);

		if (TryMergeIntoExisting(actor.Inventory, item))
			return;

		actor.Inventory.Add(item);
		TryAutoPlaceIntoAnyGrid(actor, item);
	}

	/// <summary>
	/// 拖拽 / 协议路径：放不下就拒绝，inventory 不动。
	/// 失败原因可由 <see cref="GridErrorCodes"/> 取得。
	/// </summary>
	public static GridMoveResult TryAddStrict(Actor actor, Item item)
	{
		if (actor == null) return GridMoveResult.Reject("invalid actor", GridErrorCodes.InvalidActor);
		if (item == null) return GridMoveResult.Reject("invalid item", GridErrorCodes.ItemNotFound);

		item.EnsureRuntimeState();
		item.Equipped = false;
		EnsureGridSynchronized(actor);

		if (TryMergeIntoExisting(actor.Inventory, item))
		{
			return GridMoveResult.Accept(GridInventory.PocketsId, 0, 0, false);
		}

		// 先试探：能否放下
		var def = ItemSizeRegistry.GetDef(item);
		GridInventory? acceptingGrid = null;
		GridPlacement? probe = null;
		foreach (var grid in EnumerateGridsByPriority(actor))
		{
			probe = grid.TryAutoPlace(item.InstanceId, def.Width, def.Height, def.AllowRotation);
			if (probe != null) { acceptingGrid = grid; break; }
		}
		if (acceptingGrid == null || probe == null)
		{
			return GridMoveResult.Reject("no space", GridErrorCodes.GridFull);
		}

		actor.Inventory.Add(item);
		return GridMoveResult.Accept(acceptingGrid.Id, probe.X, probe.Y, probe.Rotated);
	}

	public static Item? RemoveAt(Actor actor, int index)
	{
		if (actor == null || index < 0 || index >= actor.Inventory.Count) return null;
		var item = actor.Inventory[index];
		if (item.Equipped) Unequip(actor, item);
		actor.Inventory.RemoveAt(index);
		RemoveFromAllGrids(actor, item.InstanceId);
		EnsureGridSynchronized(actor);
		return item;
	}

	public static Item? RemoveByInstance(Actor actor, string instanceId)
	{
		if (actor == null || string.IsNullOrEmpty(instanceId)) return null;
		var index = actor.Inventory.FindIndex(i =>
			string.Equals(i.InstanceId, instanceId, StringComparison.Ordinal));
		return index >= 0 ? RemoveAt(actor, index) : null;
	}

	public static bool ConsumeAt(Actor actor, int index, int amount = 1)
	{
		if (amount <= 0 || actor == null || index < 0 || index >= actor.Inventory.Count)
			return false;

		var item = actor.Inventory[index];
		if (item.SafeStackCount < amount)
			return false;

		if (item.SafeStackCount > amount)
		{
			item.StackCount -= amount;
			return true;
		}

		return RemoveAt(actor, index) != null;
	}

	/// <summary>装备/卸下切换（基于装备槽）。</summary>
	public static InventoryResult ToggleEquip(Actor actor, int index, GameState? state = null)
	{
		if (actor == null || index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = LocalizationService.T("inventory.invalid_index") };

		var item = actor.Inventory[index];

		if (item.Equipped)
			return Unequip(actor, item, state);

		return Equip(actor, item, state);
	}

	/// <summary>装备物品到匹配的空闲槽位。</summary>
	public static InventoryResult Equip(Actor actor, Item item, GameState? state = null)
	{
		var itemName = IdentificationModule.GetItemDisplayName(state, item);
		if (!item.IsEquippable)
			return new InventoryResult { Message = LocalizationService.T("inventory.cannot_equip", ("item", itemName)) };

		var slot = actor.FindFreeSlot(item.BodyPart, item.Layer);
		if (slot == null)
		{
			var existingSlot = FindOccupiedSlot(actor, item.BodyPart, item.Layer);
			if (existingSlot != null)
			{
				var oldItem = actor.Inventory.Find(i => i.InstanceId == existingSlot.ItemId);
				if (oldItem != null) Unequip(actor, oldItem, state);
				slot = existingSlot;
			}
			else
			{
				return new InventoryResult
				{
					Message = LocalizationService.T("inventory.no_slot",
						("body_part", GameLocalizer.LocalizeBodyPart(item.BodyPart)),
						("layer", GameLocalizer.LocalizeEquipLayer(item.Layer)))
				};
			}
		}

		item.EnsureRuntimeState();
		slot.ItemId = item.InstanceId;
		item.Equipped = true;
		// 装上后：如果 item 自身是网格容器（背包/胸挂/腰带），把它从主网格移除占位，
		// 同时 sync 会创建新的子网格挂在 GridInventories 下。
		RemoveFromAllGrids(actor, item.InstanceId);
		EnsureGridSynchronized(actor);
		return new InventoryResult { Ok = true, Message = LocalizationService.T("inventory.equipped", ("item", itemName)) };
	}

	/// <summary>卸下已装备的物品。</summary>
	public static InventoryResult Unequip(Actor actor, Item item, GameState? state = null)
	{
		var slot = actor.FindSlotByItemId(item.InstanceId);
		if (slot != null) slot.ItemId = null;
		item.Equipped = false;
		// 卸下网格容器时：sync 会自动销毁对应子网格，里面的物品掉回主 inventory（"unplaced"），
		// UI 提示玩家手动整理。同时把卸下的容器物品自己 auto-place 回主网格。
		EnsureGridSynchronized(actor);
		return new InventoryResult
		{
			Ok = true,
			Message = LocalizationService.T("inventory.unequipped", ("item", IdentificationModule.GetItemDisplayName(state, item))),
		};
	}

	/// <summary>
	/// 肢体被摧毁时，卸下该肢体上所有装备并返回掉落物品列表。
	/// </summary>
	public static List<Item> OnLimbDestroyed(Actor actor, Limb limb)
	{
		var dropped = new List<Item>();
		foreach (var slot in limb.EquipSlots)
		{
			if (slot.ItemId == null) continue;
			var item = actor.Inventory.Find(i => i.InstanceId == slot.ItemId);
			if (item == null) continue;

			item.Equipped = false;
			slot.ItemId = null;
			actor.Inventory.Remove(item);
			RemoveFromAllGrids(actor, item.InstanceId);
			dropped.Add(item);
		}
		EnsureGridSynchronized(actor);
		return dropped;
	}

	public static InventoryResult Use(Actor actor, int index, GameState? state = null)
	{
		if (index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = LocalizationService.T("inventory.invalid_index") };

		var item = actor.Inventory[index];
		var itemName = IdentificationModule.GetItemDisplayName(state, item);

		if (item.Tags.ContainsKey(ItemTags.Healing))
		{
			return new InventoryResult
			{
				Message = LocalizationService.TOrFallback(
					"inventory.medical_supply_only",
					"{item} is a medical supply. Use a tending skill instead.",
					("item", itemName)),
			};
		}

		if (!item.Tags.ContainsKey(ItemTags.Healing))
			return new InventoryResult { Message = LocalizationService.T("inventory.not_consumable_try_equip", ("item", itemName)) };
		return new InventoryResult { Message = LocalizationService.T("inventory.not_consumable_try_equip", ("item", itemName)) };
	}

	public static InventoryResult Drop(Actor actor, int index, GameState? state = null)
	{
		if (index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = LocalizationService.T("inventory.invalid_index") };

		var item = actor.Inventory[index];
		var itemName = IdentificationModule.GetItemDisplayName(state, item);
		if (item.Equipped) Unequip(actor, item, state);
		actor.Inventory.RemoveAt(index);
		RemoveFromAllGrids(actor, item.InstanceId);
		EnsureGridSynchronized(actor);
		return new InventoryResult { Ok = true, Message = LocalizationService.T("inventory.dropped", ("item", itemName)) };
	}

	public static int ConsumeMatching(Actor actor, Func<Item, bool> predicate, int amount)
	{
		if (amount <= 0)
			return 0;

		var remaining = amount;
		var changed = false;
		for (var i = actor.Inventory.Count - 1; i >= 0 && remaining > 0; i--)
		{
			var item = actor.Inventory[i];
			if (!predicate(item))
				continue;

			var used = Math.Min(item.SafeStackCount, remaining);
			if (item.SafeStackCount > used)
			{
				item.StackCount -= used;
			}
			else
			{
				actor.Inventory.RemoveAt(i);
				RemoveFromAllGrids(actor, item.InstanceId);
				changed = true;
			}
			remaining -= used;
		}

		if (changed) EnsureGridSynchronized(actor);
		return amount - remaining;
	}

	public static List<(int Index, Item Item)> List(Actor actor)
	{
		var result = new List<(int, Item)>();
		for (var i = 0; i < actor.Inventory.Count; i++)
			result.Add((i, actor.Inventory[i]));
		return result;
	}

	public static int CountMatching(Actor actor, Func<Item, bool> predicate)
	{
		var total = 0;
		foreach (var item in actor.Inventory)
		{
			if (predicate(item))
				total += item.SafeStackCount;
		}

		return total;
	}

	public static Item? FindBestMatching(Actor actor, Func<Item, bool> predicate, Comparison<Item>? comparison = null)
	{
		Item? best = null;
		foreach (var item in actor.Inventory)
		{
			if (!predicate(item))
				continue;

			if (best == null)
			{
				best = item;
				continue;
			}

			if (comparison != null && comparison(item, best) < 0)
				best = item;
		}

		return best;
	}

	// ── 网格操作（UI / 协议入口） ────────────────────

	/// <summary>
	/// 把已经在背包里的物品移到指定网格的指定位置（rotated 决定旋转）。
	/// 不存在 / 越界 / 重叠 / 不允许旋转都返回失败 + 错误码；成功时旧 placement 已被清除、新 placement 已写入。
	/// </summary>
	public static GridMoveResult TryMoveTo(
		Actor actor,
		string itemInstanceId,
		string targetGridId,
		int x,
		int y,
		bool rotated)
	{
		if (actor == null) return GridMoveResult.Reject("invalid actor", GridErrorCodes.InvalidActor);
		if (string.IsNullOrEmpty(itemInstanceId))
			return GridMoveResult.Reject("invalid item", GridErrorCodes.ItemNotFound);
		if (string.IsNullOrEmpty(targetGridId))
			return GridMoveResult.Reject("invalid grid", GridErrorCodes.GridNotFound);

		EnsureGridSynchronized(actor);

		var item = actor.Inventory.Find(i =>
			string.Equals(i.InstanceId, itemInstanceId, StringComparison.Ordinal));
		if (item == null)
			return GridMoveResult.Reject("item missing", GridErrorCodes.ItemNotFound);
		if (!actor.GridInventories.TryGetValue(targetGridId, out var grid))
			return GridMoveResult.Reject("grid missing", GridErrorCodes.GridNotFound);

		var def = ItemSizeRegistry.GetDef(item);
		if (rotated && !def.AllowRotation)
			return GridMoveResult.Reject("cannot rotate", GridErrorCodes.RotationDisallowed);

		var w = rotated ? def.Height : def.Width;
		var h = rotated ? def.Width : def.Height;
		if (x < 0 || y < 0 || x + w > grid.Width || y + h > grid.Height)
			return GridMoveResult.Reject("out of bounds", GridErrorCodes.OutOfBounds);
		if (!grid.CanPlaceAt(x, y, w, h, excludeInstanceId: itemInstanceId))
			return GridMoveResult.Reject("overlaps existing", GridErrorCodes.OverlapsExisting);

		// 清掉所有旧 placement（也许在别的 grid 里），再写入新 placement
		RemoveFromAllGrids(actor, itemInstanceId);
		grid.Placements.Add(new GridPlacement
		{
			ItemInstanceId = itemInstanceId,
			X = x,
			Y = y,
			Width = w,
			Height = h,
			Rotated = rotated,
		});

		return GridMoveResult.Accept(targetGridId, x, y, rotated);
	}

	/// <summary>
	/// 在原地旋转：物品保持在原 grid，左上角不变；如果旋转后会越界 / 重叠则拒绝。
	/// </summary>
	public static GridMoveResult TryRotate(Actor actor, string itemInstanceId)
	{
		if (actor == null) return GridMoveResult.Reject("invalid actor", GridErrorCodes.InvalidActor);
		if (string.IsNullOrEmpty(itemInstanceId))
			return GridMoveResult.Reject("invalid item", GridErrorCodes.ItemNotFound);

		EnsureGridSynchronized(actor);

		GridInventory? containing = null;
		GridPlacement? placement = null;
		foreach (var (_, grid) in actor.GridInventories)
		{
			placement = grid.FindPlacement(itemInstanceId);
			if (placement != null) { containing = grid; break; }
		}
		if (containing == null || placement == null)
			return GridMoveResult.Reject("item missing", GridErrorCodes.ItemNotFound);

		var item = actor.Inventory.Find(i =>
			string.Equals(i.InstanceId, itemInstanceId, StringComparison.Ordinal));
		var def = ItemSizeRegistry.GetDef(item);
		if (!def.AllowRotation)
			return GridMoveResult.Reject("cannot rotate", GridErrorCodes.RotationDisallowed);

		var newRotated = !placement.Rotated;
		var w = newRotated ? def.Height : def.Width;
		var h = newRotated ? def.Width : def.Height;
		if (placement.X + w > containing.Width || placement.Y + h > containing.Height)
			return GridMoveResult.Reject("out of bounds", GridErrorCodes.OutOfBounds);
		if (!containing.CanPlaceAt(placement.X, placement.Y, w, h, excludeInstanceId: itemInstanceId))
			return GridMoveResult.Reject("overlaps existing", GridErrorCodes.OverlapsExisting);

		placement.Width = w;
		placement.Height = h;
		placement.Rotated = newRotated;
		return GridMoveResult.Accept(containing.Id, placement.X, placement.Y, newRotated);
	}

	/// <summary>
	/// "整理"按钮：清空所有 placement，按 backpack > vest > belt > pocket 顺序重新 auto-pack。
	/// 返回放下的物品数（unplaced 物品保持在 inventory，UI 提示）。
	/// </summary>
	public static int AutoPackAll(Actor actor)
	{
		if (actor == null) return 0;
		EnsureGridSynchronized(actor);

		foreach (var grid in actor.GridInventories.Values)
			grid.Placements.Clear();

		var placed = 0;
		foreach (var item in actor.Inventory)
		{
			if (item.Equipped) continue;
			if (string.IsNullOrEmpty(item.InstanceId)) continue;
			if (TryAutoPlaceIntoAnyGrid(actor, item) != null)
				placed++;
		}
		return placed;
	}

	private static EquipSlot? FindOccupiedSlot(Actor actor, string bodyPart, EquipLayer layer)
	{
		foreach (var limb in actor.Limbs)
			foreach (var slot in limb.EquipSlots)
				if (slot.BodyPart == bodyPart && slot.Layer == layer && slot.ItemId != null)
					return slot;
		return null;
	}

	private static bool TryMergeIntoExisting(List<Item> items, Item incoming)
	{
		// 注意：判断"是否还剩"用 StackCount 而不是 SafeStackCount —— SafeStackCount 被 saturate
		// 到至少 1，导致原版 Add 的 merge 路径永远不返回 true、合堆后空 incoming 仍被 push 进 inventory。
		if (!incoming.IsStackable || incoming.StackCount <= 0)
			return false;

		foreach (var existing in items)
		{
			existing.MergeFrom(incoming);
			if (incoming.StackCount <= 0)
				return true;
		}

		return incoming.StackCount <= 0;
	}

	public static void NormalizeEquipmentReferences(Actor actor)
	{
		foreach (var item in actor.Inventory)
			item.EnsureRuntimeState();

		var assignedInstanceIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var slot in actor.AllEquipSlots)
		{
			if (string.IsNullOrWhiteSpace(slot.ItemId))
				continue;

			var equipped = actor.Inventory.Find(item => string.Equals(item.InstanceId, slot.ItemId, StringComparison.Ordinal));
			if (equipped == null)
			{
				equipped = actor.Inventory.Find(item =>
					string.Equals(item.Id, slot.ItemId, StringComparison.Ordinal)
					&& !assignedInstanceIds.Contains(item.InstanceId));
			}

			if (equipped == null)
			{
				slot.ItemId = null;
				continue;
			}

			slot.ItemId = equipped.InstanceId;
			equipped.Equipped = true;
			assignedInstanceIds.Add(equipped.InstanceId);
		}

		foreach (var item in actor.Inventory)
		{
			if (assignedInstanceIds.Contains(item.InstanceId))
			{
				item.Equipped = true;
				continue;
			}

			if (!item.Equipped || !item.IsEquippable)
			{
				item.Equipped = false;
				continue;
			}

			var slot = actor.FindFreeSlot(item.BodyPart, item.Layer);
			if (slot == null)
			{
				item.Equipped = false;
				continue;
			}

			slot.ItemId = item.InstanceId;
			assignedInstanceIds.Add(item.InstanceId);
		}

		EnsureGridSynchronized(actor);
	}
}
