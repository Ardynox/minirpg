using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 背包操作结果。
/// </summary>
public class InventoryResult
{
	public bool Ok { get; set; }
	public string Message { get; set; } = "";
}

/// <summary>
/// 背包模块：纯函数，操作 Actor.Inventory。
/// </summary>
public static class InventoryModule
{
	/// <summary>把物品放入背包。</summary>
	public static void Add(Actor actor, Item item)
	{
		actor.Inventory.Add(item);
	}

	/// <summary>从背包移除指定下标的物品。</summary>
	public static Item? RemoveAt(Actor actor, int index)
	{
		if (index < 0 || index >= actor.Inventory.Count) return null;
		var item = actor.Inventory[index];
		actor.Inventory.RemoveAt(index);
		return item;
	}

	/// <summary>装备/卸下切换。</summary>
	public static InventoryResult ToggleEquip(Actor actor, int index)
	{
		if (index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = "无效的物品编号" };

		var item = actor.Inventory[index];
		item.Equipped = !item.Equipped;
		var verb = item.Equipped ? "装备" : "卸下";
		return new InventoryResult { Ok = true, Message = $"{verb}了 {item.Name}" };
	}

	/// <summary>
	/// 使用消耗品：将物品 Tags 作为临时 Buff 施加，然后从背包移除。
	/// 仅对含 "治疗" tag 的物品生效（可扩展其他消耗类型）。
	/// </summary>
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

	/// <summary>丢弃物品。</summary>
	public static InventoryResult Drop(Actor actor, int index)
	{
		if (index < 0 || index >= actor.Inventory.Count)
			return new InventoryResult { Message = "无效的物品编号" };

		var item = actor.Inventory[index];
		actor.Inventory.RemoveAt(index);
		return new InventoryResult { Ok = true, Message = $"丢弃了 {item.Name}" };
	}

	/// <summary>列出背包中所有物品。</summary>
	public static List<(int Index, Item Item)> List(Actor actor)
	{
		var result = new List<(int, Item)>();
		for (var i = 0; i < actor.Inventory.Count; i++)
			result.Add((i, actor.Inventory[i]));
		return result;
	}
}
