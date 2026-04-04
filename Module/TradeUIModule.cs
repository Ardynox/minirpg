using System.Collections.Generic;
using MiniRPG.Module.Panel;

namespace MiniRPG.Module;

/// <summary>
/// 浜ゆ槗 UI 娴佺▼缂栨帓锛氳繛鎺?TradeModule / TradePanelModule / PanelManager銆?/// 浠讳綍鏈夌墿鍝佺殑鐢熺墿閮藉彲浠ヤ氦鏄擄紝璐ф簮鏉ヨ嚜瀵规柟鐨?ShopSlots + Inventory銆?/// </summary>
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
		_panel.Open(player, trader);
		_panels.PushFocus(_panel);
		_ui.AddLog($"开始与 {trader.DisplayName} 交易");
	}

	public void CloseTrade()
	{
		var wasOpen = _panel.Visible;
		_panel.Close();
		if (wasOpen)
			_panels.OnPanelClosed(_panel);
		if (_trader != null && wasOpen)
			_ui.AddLog($"结束与 {_trader.DisplayName} 的交易");
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

			var result = TradeModule.Buy(player, _trader, good);
			_ui.AddLog(result.Message);
			if (result.Ok)
				_ui.AddLog($"  馃挵 鍓╀綑閲戝竵: {player.Gold}G");
		}
		else
		{
			var sel = _panel.GetSelectedSellItem();
			if (sel == null) return;
			var (invIdx, _) = sel.Value;

			var result = TradeModule.Sell(player, _trader, invIdx);
			_ui.AddLog(result.Message);
			if (result.Ok)
				_ui.AddLog($"  馃挵 鍓╀綑閲戝竵: {player.Gold}G");
		}

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
