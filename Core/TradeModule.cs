using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 交易结果：成功/失败 + 原因描述。
/// </summary>
public class TradeResult
{
	public bool Ok { get; set; }
	public string Message { get; set; } = "";
	public Item? Item { get; set; }
	public int Price { get; set; }
}

/// <summary>
/// 交易模块：纯函数，直接操作 Actor 字段（Gold / ShopSlots）。
/// </summary>
public static class TradeModule
{
	/// <summary>
	/// 玩家从商人处购买指定槽位的物品。
	/// </summary>
	public static TradeResult Buy(Actor buyer, Actor merchant, int slotIndex)
	{
		if (slotIndex < 0 || slotIndex >= merchant.ShopSlots.Count)
			return new TradeResult { Message = "无效的商品编号" };

		var slot = merchant.ShopSlots[slotIndex];
		if (slot.Stock <= 0)
			return new TradeResult { Message = $"{slot.Item.Name} 已售罄" };

		if (buyer.Gold < slot.Item.Price)
			return new TradeResult { Message = $"金币不足（需要 {slot.Item.Price}，当前 {buyer.Gold}）" };

		buyer.Gold -= slot.Item.Price;
		merchant.Gold += slot.Item.Price;
		slot.Stock--;

		ApplyItemTags(buyer, slot.Item);

		return new TradeResult
		{
			Ok = true, Item = slot.Item, Price = slot.Item.Price,
			Message = $"购买了 {slot.Item.Name}（-{slot.Item.Price}G）",
		};
	}

	/// <summary>
	/// 玩家向商人出售一个物品。售价 = 原价的一半（向下取整）。
	/// </summary>
	public static TradeResult Sell(Actor seller, Actor merchant, Item item)
	{
		var sellPrice = item.Price / 2;
		if (sellPrice <= 0)
			return new TradeResult { Message = $"{item.Name} 不值钱，无法出售" };

		if (merchant.Gold < sellPrice)
			return new TradeResult { Message = "商人金币不足，无法收购" };

		seller.Gold += sellPrice;
		merchant.Gold -= sellPrice;

		RemoveItemTags(seller, item);

		var existing = merchant.ShopSlots.Find(s => s.Item.Id == item.Id);
		if (existing != null)
			existing.Stock++;
		else
			merchant.ShopSlots.Add(new ShopSlot { Item = item, Stock = 1 });

		return new TradeResult
		{
			Ok = true, Item = item, Price = sellPrice,
			Message = $"出售了 {item.Name}（+{sellPrice}G）",
		};
	}

	/// <summary>列出商人所有可购买的货物（库存 > 0）。</summary>
	public static List<(int Index, ShopSlot Slot)> ListGoods(Actor merchant)
	{
		var result = new List<(int, ShopSlot)>();
		for (var i = 0; i < merchant.ShopSlots.Count; i++)
		{
			if (merchant.ShopSlots[i].Stock > 0)
				result.Add((i, merchant.ShopSlots[i]));
		}
		return result;
	}

	private static void ApplyItemTags(Actor actor, Item item)
	{
		actor.Experiences.Add(new Experience
		{
			Id = $"item_{item.Id}_{actor.Experiences.Count}",
			Name = item.Name,
			Tags = new Dictionary<string, int>(item.Tags),
		});
	}

	private static void RemoveItemTags(Actor actor, Item item)
	{
		var idx = actor.Experiences.FindIndex(e => e.Name == item.Name);
		if (idx >= 0) actor.Experiences.RemoveAt(idx);
	}
}
