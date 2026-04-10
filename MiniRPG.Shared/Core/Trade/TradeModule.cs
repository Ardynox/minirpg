using System.Collections.Generic;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Trade;

public class TradeResult
{
	public bool Ok { get; set; }
	public string Message { get; set; } = "";
	public Item? Item { get; set; }
	public int Price { get; set; }
}

/// <summary>
/// 可交易商品条目：统一表示来自 ShopSlots 或 Inventory 的商品。
/// </summary>
public class TradeGood
{
	public enum Source { Shop, Inventory }

	public Source From { get; init; }
	public int Index { get; init; }
	public Item Item { get; init; } = null!;
	public int Stock { get; init; } = 1;

	public int BuyPrice => Item.Price;
}

/// <summary>
/// 交易模块：支持 ShopSlots（传统商人）和 Inventory（任意生物）两种货源。
/// 任何有物品的生物都可以参与交易。
/// </summary>
public static class TradeModule
{
	/// <summary>
	/// 列出对方所有可交易商品：先 ShopSlots（有库存的），再 Inventory（未装备的）。
	/// </summary>
	public static List<TradeGood> ListGoods(Actor trader)
	{
		var result = new List<TradeGood>();

		for (var i = 0; i < trader.ShopSlots.Count; i++)
		{
			var slot = trader.ShopSlots[i];
			if (slot.Stock > 0)
				result.Add(new TradeGood
				{
					From = TradeGood.Source.Shop, Index = i,
					Item = slot.Item, Stock = slot.Stock,
				});
		}

		for (var i = 0; i < trader.Inventory.Count; i++)
		{
			var item = trader.Inventory[i];
			if (!item.Equipped && item.Price > 0)
				result.Add(new TradeGood
				{
					From = TradeGood.Source.Inventory, Index = i,
					Item = item, Stock = 1,
				});
		}

		return result;
	}

	/// <summary>从对方购买一件商品。</summary>
	public static TradeResult Buy(Actor buyer, Actor trader, TradeGood good, GameState? state = null)
	{
		if (buyer.Gold < good.BuyPrice)
			return new TradeResult { Message = LocalizationService.T("trade.insufficient_gold", ("required", good.BuyPrice), ("current", buyer.Gold)) };

		buyer.Gold -= good.BuyPrice;
		trader.Gold += good.BuyPrice;

		Item bought;
		if (good.From == TradeGood.Source.Shop)
		{
			var slot = trader.ShopSlots[good.Index];
			slot.Stock--;
			bought = new Item
			{
				Id = slot.Item.Id, Name = slot.Item.Name,
				Price = slot.Item.Price, Category = slot.Item.Category,
				Weight = slot.Item.Weight, BodyPart = slot.Item.BodyPart,
				Layer = slot.Item.Layer,
				SharpDamage = slot.Item.SharpDamage, BluntDamage = slot.Item.BluntDamage,
				SharpArmor = slot.Item.SharpArmor, BluntArmor = slot.Item.BluntArmor,
				Tags = new Dictionary<string, int>(slot.Item.Tags),
				GrantedSkills = [.. slot.Item.GrantedSkills],
				CoveredParts = [.. slot.Item.CoveredParts],
			};
		}
		else
		{
			bought = trader.Inventory[good.Index];
			trader.Inventory.RemoveAt(good.Index);
		}

		InventoryModule.Add(buyer, bought);

		return new TradeResult
		{
			Ok = true, Item = bought, Price = good.BuyPrice,
			Message = LocalizationService.T(
				"trade.bought",
				("item", IdentificationModule.GetItemDisplayName(state, bought)),
				("price", good.BuyPrice)),
		};
	}

	/// <summary>向对方出售背包中指定下标的物品。售价 = 半价。</summary>
	public static TradeResult Sell(Actor seller, Actor trader, int inventoryIndex, GameState? state = null)
	{
		if (inventoryIndex < 0 || inventoryIndex >= seller.Inventory.Count)
			return new TradeResult { Message = LocalizationService.T("inventory.invalid_index") };

		var item = seller.Inventory[inventoryIndex];
		var itemName = IdentificationModule.GetItemDisplayName(state, item);
		if (item.Equipped)
			return new TradeResult { Message = LocalizationService.T("trade.unequip_before_sell", ("item", itemName)) };

		var sellPrice = item.Price / 2;
		if (sellPrice <= 0)
			return new TradeResult { Message = LocalizationService.T("trade.worthless", ("item", itemName)) };

		if (trader.Gold < sellPrice)
			return new TradeResult
			{
				Message = LocalizationService.T(
					"trade.trader_insufficient_gold",
					("trader", IdentificationModule.GetActorDisplayName(state, trader))),
			};

		seller.Gold += sellPrice;
		trader.Gold -= sellPrice;
		seller.Inventory.RemoveAt(inventoryIndex);

		InventoryModule.Add(trader, item);

		return new TradeResult
		{
			Ok = true, Item = item, Price = sellPrice,
			Message = LocalizationService.T("trade.sold", ("item", itemName), ("price", sellPrice)),
		};
	}

	/// <summary>计算物品的出售价格。</summary>
	public static int SellPrice(Item item) => item.Price / 2;
}
