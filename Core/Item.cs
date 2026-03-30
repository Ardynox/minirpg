using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 物品：可交易、可持有、可装备。
/// 装备后作为 ITagSource 参与 Actor 的 Tag 聚合。
/// </summary>
public class Item : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public int Price { get; set; }
	public bool Equipped { get; set; }
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;
}

/// <summary>
/// 商店货架槽位：一个物品 + 库存数量。
/// </summary>
public class ShopSlot
{
	public Item Item { get; set; } = new();
	public int Stock { get; set; } = 1;
}
