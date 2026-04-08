using MiniRPG.Core.Data;
using MiniRPG.Core.Trade;
using Xunit;

namespace MiniRPG.Tests;

public sealed class TradeModuleTests
{
	public TradeModuleTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	private static Actor MakeTrader(int gold = 500)
	{
		var trader = PresetDB.SpawnActor("player", "trader");
		trader.Gold = gold;
		trader.ShopSlots.Clear();
		trader.Inventory.Clear();
		return trader;
	}

	private static Actor MakeBuyer(int gold = 100)
	{
		var buyer = PresetDB.SpawnActor("player", "buyer");
		buyer.Gold = gold;
		buyer.Inventory.Clear();
		return buyer;
	}

	private static Item MakeItem(string id = "potion", int price = 20) =>
		new() { Id = id, Name = "Potion", Price = price, StackCount = 1, MaxStack = 10 };

	// ── ListGoods ────────────────────────────────────────

	[Fact]
	public void ListGoods_EmptyTrader_ReturnsEmpty()
	{
		var trader = MakeTrader();
		Assert.Empty(TradeModule.ListGoods(trader));
	}

	[Fact]
	public void ListGoods_ShopSlots_Listed()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 30);
		item.EnsureRuntimeState();
		trader.ShopSlots.Add(new ShopSlot { Item = item, Stock = 5 });

		var goods = TradeModule.ListGoods(trader);
		Assert.Single(goods);
		Assert.Equal(TradeGood.Source.Shop, goods[0].From);
		Assert.Equal(5, goods[0].Stock);
	}

	[Fact]
	public void ListGoods_InventoryItems_Listed()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 15);
		item.EnsureRuntimeState();
		trader.Inventory.Add(item);

		var goods = TradeModule.ListGoods(trader);
		Assert.Single(goods);
		Assert.Equal(TradeGood.Source.Inventory, goods[0].From);
	}

	[Fact]
	public void ListGoods_EquippedItems_Excluded()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 15);
		item.Equipped = true;
		item.EnsureRuntimeState();
		trader.Inventory.Add(item);

		Assert.Empty(TradeModule.ListGoods(trader));
	}

	[Fact]
	public void ListGoods_ZeroPriceItems_Excluded()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 0);
		item.EnsureRuntimeState();
		trader.Inventory.Add(item);

		Assert.Empty(TradeModule.ListGoods(trader));
	}

	[Fact]
	public void ListGoods_ZeroStockShopSlot_Excluded()
	{
		var trader = MakeTrader();
		var item = MakeItem();
		item.EnsureRuntimeState();
		trader.ShopSlots.Add(new ShopSlot { Item = item, Stock = 0 });

		Assert.Empty(TradeModule.ListGoods(trader));
	}

	// ── Buy ──────────────────────────────────────────────

	[Fact]
	public void Buy_ShopItem_TransfersGold()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 30);
		item.EnsureRuntimeState();
		trader.ShopSlots.Add(new ShopSlot { Item = item, Stock = 3 });

		var buyer = MakeBuyer(gold: 100);
		var good = TradeModule.ListGoods(trader)[0];

		var result = TradeModule.Buy(buyer, trader, good);
		Assert.True(result.Ok);
		Assert.Equal(70, buyer.Gold);
		Assert.Equal(530, trader.Gold);
	}

	[Fact]
	public void Buy_InsufficientGold_Fails()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 200);
		item.EnsureRuntimeState();
		trader.ShopSlots.Add(new ShopSlot { Item = item, Stock = 1 });

		var buyer = MakeBuyer(gold: 50);
		var good = TradeModule.ListGoods(trader)[0];

		var result = TradeModule.Buy(buyer, trader, good);
		Assert.False(result.Ok);
		Assert.Equal(50, buyer.Gold); // Unchanged
	}

	[Fact]
	public void Buy_ShopItem_ReducesStock()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 10);
		item.EnsureRuntimeState();
		trader.ShopSlots.Add(new ShopSlot { Item = item, Stock = 3 });

		var buyer = MakeBuyer(gold: 100);
		var good = TradeModule.ListGoods(trader)[0];

		TradeModule.Buy(buyer, trader, good);
		Assert.Equal(2, trader.ShopSlots[0].Stock);
	}

	[Fact]
	public void Buy_InventoryItem_TransfersItem()
	{
		var trader = MakeTrader();
		var item = MakeItem(price: 15);
		item.EnsureRuntimeState();
		trader.Inventory.Add(item);

		var buyer = MakeBuyer(gold: 100);
		var good = TradeModule.ListGoods(trader)[0];

		var result = TradeModule.Buy(buyer, trader, good);
		Assert.True(result.Ok);
		Assert.Empty(trader.Inventory);
		Assert.Contains(buyer.Inventory, i => i.Id == "potion");
	}

	// ── Sell ─────────────────────────────────────────────

	[Fact]
	public void Sell_ValidItem_TransfersAtHalfPrice()
	{
		var seller = MakeBuyer(gold: 0);
		var item = MakeItem(price: 40);
		item.EnsureRuntimeState();
		seller.Inventory.Add(item);

		var trader = MakeTrader(gold: 500);

		var result = TradeModule.Sell(seller, trader, 0);
		Assert.True(result.Ok);
		Assert.Equal(20, result.Price); // Half price
		Assert.Equal(20, seller.Gold);
		Assert.Equal(480, trader.Gold);
	}

	[Fact]
	public void Sell_EquippedItem_Fails()
	{
		var seller = MakeBuyer();
		var item = MakeItem(price: 40);
		item.Equipped = true;
		item.EnsureRuntimeState();
		seller.Inventory.Add(item);

		var trader = MakeTrader();
		var result = TradeModule.Sell(seller, trader, 0);
		Assert.False(result.Ok);
	}

	[Fact]
	public void Sell_WorthlessItem_Fails()
	{
		var seller = MakeBuyer();
		var item = MakeItem(price: 0);
		item.EnsureRuntimeState();
		seller.Inventory.Add(item);

		var trader = MakeTrader();
		var result = TradeModule.Sell(seller, trader, 0);
		Assert.False(result.Ok);
	}

	[Fact]
	public void Sell_TraderCantAfford_Fails()
	{
		var seller = MakeBuyer();
		var item = MakeItem(price: 1000);
		item.EnsureRuntimeState();
		seller.Inventory.Add(item);

		var trader = MakeTrader(gold: 10);
		var result = TradeModule.Sell(seller, trader, 0);
		Assert.False(result.Ok);
	}

	[Fact]
	public void Sell_InvalidIndex_Fails()
	{
		var seller = MakeBuyer();
		var trader = MakeTrader();
		var result = TradeModule.Sell(seller, trader, -1);
		Assert.False(result.Ok);
	}

	// ── SellPrice ────────────────────────────────────────

	[Fact]
	public void SellPrice_HalfOfBuyPrice()
	{
		var item = MakeItem(price: 100);
		Assert.Equal(50, TradeModule.SellPrice(item));
	}

	[Fact]
	public void SellPrice_OddPrice_RoundsDown()
	{
		var item = MakeItem(price: 3);
		Assert.Equal(1, TradeModule.SellPrice(item));
	}

	[Fact]
	public void SellPrice_ZeroPrice_ReturnsZero()
	{
		var item = MakeItem(price: 0);
		Assert.Equal(0, TradeModule.SellPrice(item));
	}
}
