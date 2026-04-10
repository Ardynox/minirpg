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
/// 背包模块：纯函数，操作 Actor.Inventory 和装备槽。
/// </summary>
public static class InventoryModule
{
	public static void Add(Actor actor, Item item)
	{
		item.EnsureRuntimeState();
		item.Equipped = false;
		if (TryMergeIntoExisting(actor.Inventory, item))
			return;
		actor.Inventory.Add(item);
	}

	public static Item? RemoveAt(Actor actor, int index)
	{
		if (index < 0 || index >= actor.Inventory.Count) return null;
		var item = actor.Inventory[index];
		if (item.Equipped) Unequip(actor, item);
		actor.Inventory.RemoveAt(index);
		return item;
	}

	public static bool ConsumeAt(Actor actor, int index, int amount = 1)
	{
		if (amount <= 0 || index < 0 || index >= actor.Inventory.Count)
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
		if (index < 0 || index >= actor.Inventory.Count)
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
		return new InventoryResult { Ok = true, Message = LocalizationService.T("inventory.equipped", ("item", itemName)) };
	}

	/// <summary>卸下已装备的物品。</summary>
	public static InventoryResult Unequip(Actor actor, Item item, GameState? state = null)
	{
		var slot = actor.FindSlotByItemId(item.InstanceId);
		if (slot != null) slot.ItemId = null;
		item.Equipped = false;
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
			dropped.Add(item);
		}
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
		return new InventoryResult { Ok = true, Message = LocalizationService.T("inventory.dropped", ("item", itemName)) };
	}

	public static int ConsumeMatching(Actor actor, Func<Item, bool> predicate, int amount)
	{
		if (amount <= 0)
			return 0;

		var remaining = amount;
		for (var i = actor.Inventory.Count - 1; i >= 0 && remaining > 0; i--)
		{
			var item = actor.Inventory[i];
			if (!predicate(item))
				continue;

			var used = Math.Min(item.SafeStackCount, remaining);
			if (item.SafeStackCount > used)
				item.StackCount -= used;
			else
				actor.Inventory.RemoveAt(i);
			remaining -= used;
		}

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
		if (!incoming.IsStackable || incoming.SafeStackCount <= 0)
			return false;

		foreach (var existing in items)
		{
			var moved = existing.MergeFrom(incoming);
			if (incoming.SafeStackCount <= 0)
				return true;
			if (moved > 0 && incoming.SafeStackCount <= 0)
				return true;
		}

		return incoming.SafeStackCount <= 0;
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
	}
}
