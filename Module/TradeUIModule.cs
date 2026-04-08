using System.Collections.Generic;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

/// <summary>
/// 交易 UI 流程编排：连接 TradeModule、TradePanelModule 和 PanelManager。
/// 任何拥有物品的生物都可以交易，货源来自对方的 ShopSlots 和 Inventory。
/// </summary>
public class TradeUIModule
{
	private readonly IGameUI _ui;
	private readonly TradePanelModule _panel;
	private readonly PanelManager _panels;

	private Actor? _trader;

	public bool InTrade => _panel.Visible;

	public TradeUIModule(IGameUI ui, TradePanelModule panel, PanelManager panels)
	{
		_ui = ui;
		_panel = panel;
		_panels = panels;

		_panel.OnTradeAction += OnTradeAction;
		_panel.OnTradeClosed += CloseTrade;
	}

	public void OpenTradeMenu(GameEvent e)
	{
		var player = ActorModule.GetPlayer(_ui.State);
		var trader = e.TargetId != null ? ActorModule.GetById(_ui.State, e.TargetId) : null;
		if (player == null || trader == null) return;

		_trader = trader;
		_panel.State = _ui.State;
		_panel.TryHandleItemRightClick = _ui.TryHandleItemRightClick;
		_panel.Open(player, trader);
		_panels.PushFocus(_panel);
		_ui.AddLog(LocalizationService.T("trade.start", ("trader", IdentificationModule.GetActorDisplayName(_ui.State, trader))));
	}

	public void CloseTrade()
	{
		var wasOpen = _panel.Visible;
		_panel.Close();
		_panel.State = null;
		_panel.TryHandleItemRightClick = null;
		if (wasOpen)
			_panels.OnPanelClosed(_panel);
		if (_trader != null && wasOpen)
			_ui.AddLog(LocalizationService.T("trade.end", ("trader", IdentificationModule.GetActorDisplayName(_ui.State, _trader))));
		_trader = null;
		_ui.FlushMap();
	}

	private void OnTradeAction(TradeTab tab, int index)
	{
		var player = ActorModule.GetPlayer(_ui.State);
		if (player == null || _trader == null) return;

		if (tab == TradeTab.Buy)
		{
			var good = _panel.GetSelectedBuyGood();
			if (good == null) return;

			var result = TradeModule.Buy(player, _trader, good, _ui.State);
			_ui.AddLog(result.Message);
			if (result.Ok)
				_ui.AddLog(LocalizationService.T("trade.gold_remaining", ("gold", player.Gold)));
		}
		else
		{
			var sel = _panel.GetSelectedSellItem();
			if (sel == null) return;
			var (invIdx, _) = sel.Value;

			var result = TradeModule.Sell(player, _trader, invIdx, _ui.State);
			_ui.AddLog(result.Message);
			if (result.Ok)
				_ui.AddLog(LocalizationService.T("trade.gold_remaining", ("gold", player.Gold)));
		}

		_panel.RefreshData(player, _trader);
		_panel.RefreshHeader(player, _trader);
	}

	public void Refresh()
	{
		var player = ActorModule.GetPlayer(_ui.State);
		if (player == null || _trader == null)
			return;

		_panel.State = _ui.State;
		_panel.TryHandleItemRightClick = _ui.TryHandleItemRightClick;
		_panel.RefreshData(player, _trader);
		_panel.RefreshHeader(player, _trader);
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
