using System.Collections.Generic;

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

	/// <summary>装备/卸下切换（基于装备槽）。</summary>
	public static InventoryResult ToggleEquip(Actor actor, int index)
	{
		if (index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = "无效的物品编号" };

		var item = actor.Inventory[index];

		if (item.Equipped)
			return Unequip(actor, item);

		return Equip(actor, item);
	}

	/// <summary>装备物品到匹配的空闲槽位。</summary>
	public static InventoryResult Equip(Actor actor, Item item)
	{
		if (!item.IsEquippable)
			return new InventoryResult { Message = $"{item.Name} 不可装备" };

		var slot = actor.FindFreeSlot(item.BodyPart, item.Layer);
		if (slot == null)
		{
			var existingSlot = FindOccupiedSlot(actor, item.BodyPart, item.Layer);
			if (existingSlot != null)
			{
				var oldItem = actor.Inventory.Find(i => i.Id == existingSlot.ItemId);
				if (oldItem != null) Unequip(actor, oldItem);
				slot = existingSlot;
			}
			else
			{
				return new InventoryResult { Message = $"没有合适的装备槽位（需要 {item.BodyPart}/{item.Layer}）" };
			}
		}

		slot.ItemId = item.Id;
		item.Equipped = true;
		return new InventoryResult { Ok = true, Message = $"装备了 {item.Name}" };
	}

	/// <summary>卸下已装备的物品。</summary>
	public static InventoryResult Unequip(Actor actor, Item item)
	{
		var slot = actor.FindSlotByItemId(item.Id);
		if (slot != null) slot.ItemId = null;
		item.Equipped = false;
		return new InventoryResult { Ok = true, Message = $"卸下了 {item.Name}" };
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
			var item = actor.Inventory.Find(i => i.Id == slot.ItemId);
			if (item == null) continue;

			item.Equipped = false;
			slot.ItemId = null;
			actor.Inventory.Remove(item);
			dropped.Add(item);
		}
		return dropped;
	}

	public static InventoryResult Use(Actor actor, int index)
	{
		if (index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = "无效的物品编号" };

		var item = actor.Inventory[index];

		if (!item.Tags.ContainsKey("治疗"))
			return new InventoryResult { Message = $"{item.Name} 不是消耗品，试试装备它" };

		actor.Buffs.Add(new Buff
		{
			Id = $"used_{item.Id}_{actor.Buffs.Count}",
			Name = $"使用{item.Name}",
			RemainingTurns = 3,
			Tags = new Dictionary<string, int>(item.Tags),
		});

		actor.Inventory.RemoveAt(index);
		return new InventoryResult { Ok = true, Message = $"使用了 {item.Name}" };
	}

	public static InventoryResult Drop(Actor actor, int index)
	{
		if (index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = "无效的物品编号" };

		var item = actor.Inventory[index];
		if (item.Equipped) Unequip(actor, item);
		actor.Inventory.RemoveAt(index);
		return new InventoryResult { Ok = true, Message = $"丢弃了 {item.Name}" };
	}

	public static List<(int Index, Item Item)> List(Actor actor)
	{
		var result = new List<(int, Item)>();
		for (var i = 0; i < actor.Inventory.Count; i++)
			result.Add((i, actor.Inventory[i]));
		return result;
	}

	private static EquipSlot? FindOccupiedSlot(Actor actor, string bodyPart, EquipLayer layer)
	{
		foreach (var limb in actor.Limbs)
			foreach (var slot in limb.EquipSlots)
				if (slot.BodyPart == bodyPart && slot.Layer == layer && slot.ItemId != null)
					return slot;
		return null;
	}
}
