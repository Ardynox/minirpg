using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 物品：可交易、可持有。Tags 可为物品附加任意属性（如 "治疗"、"力量" 等）。
/// </summary>
public class Item
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public int Price { get; set; }
	public Dictionary<string, int> Tags { get; set; } = new();
}

/// <summary>
/// 商店货架槽位：一个物品 + 库存数量。
/// </summary>
public class ShopSlot
{
	public Item Item { get; set; } = new();
	public int Stock { get; set; } = 1;
}
