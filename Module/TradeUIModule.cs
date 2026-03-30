using System.Collections.Generic;
using MiniRPG.Core;

namespace MiniRPG.Module;

/// <summary>
/// 交易 UI 流程：商店浏览、购买、出售。
/// </summary>
public class TradeUIModule
{
	private readonly IGameUI _ui;

	public TradeUIModule(IGameUI ui) => _ui = ui;

	public void OpenTradeMenu(GameEvent e)
	{
		var player = ActorModule.GetPlayer(_ui.State);
		var merchant = e.TargetId != null ? ActorModule.GetById(_ui.State, e.TargetId) : null;
		if (player == null || merchant == null) return;

		ShowTradeGoods(player, merchant);
	}

	private void ShowTradeGoods(Actor player, Actor merchant)
	{
		var goods = TradeModule.ListGoods(merchant);

		_ui.AddLog($"═══ {merchant.DisplayName}的商店 ═══  你的金币: {player.Gold}G");
		if (goods.Count == 0)
			_ui.AddLog("  (货架空空如也)");
		for (var i = 0; i < goods.Count; i++)
		{
			var (_, slot) = goods[i];
			var tagDesc = FormatItemTags(slot.Item);
			_ui.AddLog($"  [{i + 1}] 购买 {slot.Item.Name}  {slot.Item.Price}G  库存:{slot.Stock}{tagDesc}");
		}
		var sellIdx = goods.Count + 1;
		_ui.AddLog($"  [{sellIdx}] 出售物品给商人");
		_ui.AddLog($"  [0] 离开");

		_ui.EnterSelection(n =>
		{
			if (n == 0) { _ui.AddLog("你离开了商店"); return; }

			if (n == sellIdx)
			{
				ShowSellMenu(player, merchant);
				return;
			}

			if (n < 1 || n > goods.Count) { _ui.AddLog("无效选择"); return; }

			var (slotIdx, _) = goods[n - 1];
			var result = TradeModule.Buy(player, merchant, slotIdx);
			_ui.AddLog(result.Message);
			if (result.Ok)
				_ui.AddLog($"  💰 剩余金币: {player.Gold}G");

			ShowTradeGoods(player, merchant);
		});
	}

	private void ShowSellMenu(Actor player, Actor merchant)
	{
		var items = InventoryModule.List(player);
		if (items.Count == 0)
		{
			_ui.AddLog("背包里没有可出售的物品");
			ShowTradeGoods(player, merchant);
			return;
		}

		_ui.AddLog($"═══ 出售物品 ═══  💰{player.Gold}G  商人资金: {merchant.Gold}G");
		for (var i = 0; i < items.Count; i++)
		{
			var (_, item) = items[i];
			var sellPrice = item.Price / 2;
			var eqMark = item.Equipped ? " [已装备]" : "";
			_ui.AddLog($"  [{i + 1}] {item.Name}{eqMark}  售价:{sellPrice}G");
		}
		_ui.AddLog("  [0] 返回商店");

		_ui.EnterSelection(n =>
		{
			if (n == 0) { ShowTradeGoods(player, merchant); return; }
			if (n < 1 || n > items.Count) { _ui.AddLog("无效选择"); return; }

			var (invIdx, _) = items[n - 1];
			var result = TradeModule.Sell(player, merchant, invIdx);
			_ui.AddLog(result.Message);
			if (result.Ok)
				_ui.AddLog($"  💰 剩余金币: {player.Gold}G");

			ShowSellMenu(player, merchant);
		});
	}

	public static string FormatItemTags(Item item)
	{
		if (item.Tags.Count == 0) return "";
		var parts = new List<string>();
		foreach (var (key, val) in item.Tags)
			parts.Add($"{key}+{val}");
		return $"  ({string.Join(", ", parts)})";
	}
}
