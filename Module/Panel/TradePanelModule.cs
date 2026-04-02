using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public enum TradeTab { Buy, Sell }

/// <summary>
/// 交易面板 UI：显示对方的商品列表 / 玩家的可出售物品列表，支持购买和出售。
/// 实现 IPanel 接口，由 PanelManager 统一管理焦点和键盘。
/// </summary>
public class TradePanelModule : IPanel
{
	public string PanelId => "trade";
	public PanelContainer PanelNode => _panel;
	bool IPanel.Visible { get => _panel.Visible; set => _panel.Visible = value; }

	bool IPanel.HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1); return true;
			case "down": MoveCursor(1); return true;
			case "action1": DoAction(); return true;
			case "action2": DoAction(); return true;
			case "left" or "tab_prev": SetTab(TradeTab.Buy); return true;
			case "right" or "tab_next": SetTab(TradeTab.Sell); return true;
			case "confirm": DoAction(); return true;
			default:
				if (int.TryParse(cmd, out var num) && num >= 1) { SelectByNumber(num); return true; }
				return false;
		}
	}

	public event Action? OnTradeClosed;
	void IPanel.OnBlur() => OnTradeClosed?.Invoke();

	public event Action<TradeTab, int>? OnTradeAction;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly HBoxContainer _tabBar;
	private readonly ScrollContainer _itemScroll;
	private readonly VBoxContainer _itemList;
	private readonly RichTextLabel _detailBox;
	private readonly RichTextLabel _hintBar;
	private readonly Button _buyBtn;
	private readonly Button _sellBtn;
	private readonly Button _closeBtn;

	private readonly List<Button> _tabButtons = [];
	private readonly List<Button> _itemRows = [];
	private int _cursor;
	private int _hoverIndex = -1;
	private TradeTab _currentTab = TradeTab.Buy;

	private List<TradeGood> _buyGoods = [];
	private List<(int InvIndex, Item Item)> _sellItems = [];

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public TradeTab CurrentTab => _currentTab;
	public int ItemCount => _currentTab == TradeTab.Buy ? _buyGoods.Count : _sellItems.Count;

	public TradePanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		_tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		_itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		_itemList = _itemScroll.GetNode<VBoxContainer>("ItemList");
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		_hintBar = vbox.GetNode<RichTextLabel>("HintBar");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_buyBtn = actionBar.GetNode<Button>("BuyBtn");
		_sellBtn = actionBar.GetNode<Button>("SellBtn");
		_closeBtn = actionBar.GetNode<Button>("CloseBtn");

		_buyBtn.FocusMode = Control.FocusModeEnum.None;
		_sellBtn.FocusMode = Control.FocusModeEnum.None;
		_closeBtn.FocusMode = Control.FocusModeEnum.None;

		_buyBtn.Pressed += () => DoAction();
		_sellBtn.Pressed += () => DoAction();
		_closeBtn.Pressed += () => OnTradeClosed?.Invoke();

		BuildTabButtons();
		_hintBar.Clear();
		_hintBar.AppendText("[color=#666666]↑↓选择 ←→切换买/卖 E确认 Esc关闭[/color]");
	}

	private void BuildTabButtons()
	{
		string[] labels = ["购买", "出售"];
		for (var i = 0; i < labels.Length; i++)
		{
			var btn = new Button
			{
				Text = labels[i],
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
			};
			var tab = (TradeTab)i;
			btn.Pressed += () => SetTab(tab);
			_tabBar.AddChild(btn);
			_tabButtons.Add(btn);
		}
	}

	public void Open(Actor player, Actor trader)
	{
		_currentTab = TradeTab.Buy;
		_cursor = 0;
		RefreshData(player, trader);
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
		ClearRows();
	}

	/// <summary>刷新数据并重建 UI。</summary>
	public void RefreshData(Actor player, Actor trader)
	{
		_buyGoods = TradeModule.ListGoods(trader);
		_sellItems = InventoryModule.List(player).FindAll(x => !x.Item.Equipped && x.Item.Price > 0);
		RebuildUI(player, trader);
	}

	public void SetTab(TradeTab tab)
	{
		_currentTab = tab;
		_cursor = 0;
		UpdateTabHighlight();
		RebuildRows();
		UpdateRowVisuals();
		RenderDetail();
		UpdateActionButtons();
	}

	public void MoveCursor(int delta)
	{
		var count = ItemCount;
		if (count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, count - 1);
		UpdateRowVisuals();
		RenderDetail();
		UpdateActionButtons();
		if (_cursor >= 0 && _cursor < _itemRows.Count)
			RowStyleHelper.EnsureVisible(_itemScroll, _itemRows[_cursor]);
	}

	public void SelectByNumber(int number)
	{
		var index = number - 1;
		if (index >= 0 && index < ItemCount)
		{
			_cursor = index;
			UpdateRowVisuals();
			DoAction();
		}
	}

	public TradeGood? GetSelectedBuyGood()
	{
		if (_currentTab != TradeTab.Buy || _cursor < 0 || _cursor >= _buyGoods.Count) return null;
		return _buyGoods[_cursor];
	}

	public (int InvIndex, Item Item)? GetSelectedSellItem()
	{
		if (_currentTab != TradeTab.Sell || _cursor < 0 || _cursor >= _sellItems.Count) return null;
		return _sellItems[_cursor];
	}

	private void DoAction()
	{
		if (ItemCount == 0) return;
		OnTradeAction?.Invoke(_currentTab, _cursor);
	}

	private void RebuildUI(Actor player, Actor trader)
	{
		_header.Clear();
		_header.AppendText(
			$"[center]── 交易: {trader.DisplayName} ──[/center]\n" +
			$"[color=#ffcc00]你: {player.Gold}G[/color]  " +
			$"[color=#88ccff]{trader.DisplayName}: {trader.Gold}G[/color]");
		UpdateTabHighlight();
		RebuildRows();
		UpdateRowVisuals();
		RenderDetail();
		UpdateActionButtons();
	}

	public void RefreshHeader(Actor player, Actor trader)
	{
		_header.Clear();
		_header.AppendText(
			$"[center]── 交易: {trader.DisplayName} ──[/center]\n" +
			$"[color=#ffcc00]你: {player.Gold}G[/color]  " +
			$"[color=#88ccff]{trader.DisplayName}: {trader.Gold}G[/color]");
	}

	private void RebuildRows()
	{
		var count = ItemCount;
		var needed = Math.Max(count, 1);

		while (_itemRows.Count > needed)
		{
			_itemRows[^1].QueueFree();
			_itemRows.RemoveAt(_itemRows.Count - 1);
		}
		while (_itemRows.Count < needed)
		{
			var row = CreateRow(_itemRows.Count);
			_itemList.AddChild(row);
			_itemRows.Add(row);
		}

		if (count == 0)
		{
			_itemRows[0].Text = _currentTab == TradeTab.Buy ? "  (对方没有可交易的商品)" : "  (没有可出售的物品)";
			_itemRows[0].Disabled = true;
			_itemRows[0].AddThemeColorOverride("font_disabled_color", UIColors.TextDim);
		}
		else if (_currentTab == TradeTab.Buy)
		{
			for (var i = 0; i < _buyGoods.Count; i++)
			{
				var g = _buyGoods[i];
				var src = g.From == TradeGood.Source.Shop ? "" : " [私]";
				var stock = g.Stock > 1 ? $" x{g.Stock}" : "";
				_itemRows[i].Text = $"  [{i + 1}] {g.Item.Name}  {g.BuyPrice}G{stock}{src}";
				_itemRows[i].Disabled = false;
			}
		}
		else
		{
			for (var i = 0; i < _sellItems.Count; i++)
			{
				var (_, item) = _sellItems[i];
				var sp = TradeModule.SellPrice(item);
				_itemRows[i].Text = $"  [{i + 1}] {item.Name}  售价:{sp}G";
				_itemRows[i].Disabled = false;
			}
		}

		if (_cursor >= count)
			_cursor = Math.Max(0, count - 1);
	}

	private void ClearRows()
	{
		foreach (var row in _itemRows)
			row.QueueFree();
		_itemRows.Clear();
		_cursor = 0;
		_hoverIndex = -1;
	}

	private Button CreateRow(int index)
	{
		var row = new Button
		{
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 26),
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
		};

		var idx = index;
		row.Pressed += () => { _cursor = idx; UpdateRowVisuals(); RenderDetail(); };
		row.MouseEntered += () => { _hoverIndex = idx; UpdateRowVisuals(); };
		row.MouseExited += () => { if (_hoverIndex == idx) _hoverIndex = -1; UpdateRowVisuals(); };

		return row;
	}

	private void UpdateRowVisuals()
	{
		var count = ItemCount;
		for (var i = 0; i < _itemRows.Count && i < count; i++)
			RowStyleHelper.Apply(_itemRows[i], i == _cursor, i == _hoverIndex, transparentBg: true);
	}

	private void UpdateTabHighlight()
	{
		for (var i = 0; i < _tabButtons.Count; i++)
			_tabButtons[i].ButtonPressed = (TradeTab)i == _currentTab;
	}

	private void UpdateActionButtons()
	{
		var hasItem = ItemCount > 0 && _cursor >= 0 && _cursor < ItemCount;
		_buyBtn.Visible = _currentTab == TradeTab.Buy;
		_sellBtn.Visible = _currentTab == TradeTab.Sell;
		_buyBtn.Disabled = !hasItem;
		_sellBtn.Disabled = !hasItem;
	}

	private void RenderDetail()
	{
		_detailBox.Clear();

		Item? item = null;
		if (_currentTab == TradeTab.Buy && _cursor >= 0 && _cursor < _buyGoods.Count)
			item = _buyGoods[_cursor].Item;
		else if (_currentTab == TradeTab.Sell && _cursor >= 0 && _cursor < _sellItems.Count)
			item = _sellItems[_cursor].Item;

		if (item == null)
		{
			_detailBox.AppendText("[color=#888888]选择商品查看详情[/color]");
			return;
		}

		_detailBox.AppendText(ItemFormatHelper.BuildDetail(item));
	}
}
