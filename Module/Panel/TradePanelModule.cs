using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public enum TradeTab { Buy, Sell }

public class TradePanelModule : ListPanelBase
{
	public override string PanelId => "trade";
	public override PanelContainer PanelNode => _panel;

	public event Action? OnTradeClosed;
	public event Action<TradeTab, int>? OnTradeAction;
	public Func<Item, bool>? TryHandleItemRightClick { get; set; }

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly HBoxContainer _tabBar;
	private readonly RichTextLabel _detailBox;
	private readonly Button _buyBtn;
	private readonly Button _sellBtn;

	private GameState? _state;
	private List<TradeGood> _buyGoods = [];
	private List<(int InvIndex, Item Item)> _sellItems = [];
	private readonly List<Button> _tabButtons;
	private TradeTab _currentTab = TradeTab.Buy;

	private static readonly TradeTab[] Tabs = [TradeTab.Buy, TradeTab.Sell];
	private static readonly string[] TabLabels = ["Buy", "Sell"];

	public override bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public TradeTab CurrentTab => _currentTab;
	public int ItemCount => _currentTab == TradeTab.Buy ? _buyGoods.Count : _sellItems.Count;
	public GameState? State { get => _state; set => _state = value; }

	public override bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1, ItemCount); return true;
			case "down": MoveCursor(1, ItemCount); return true;
			case "action1": DoAction(); return true;
			case "action2": DoAction(); return true;
			case "left" or "tab_prev": SetTab(TradeTab.Buy); return true;
			case "right" or "tab_next": SetTab(TradeTab.Sell); return true;
			case "confirm": DoAction(); return true;
			case "close": OnTradeClosed?.Invoke(); return true;
			default:
				if (int.TryParse(cmd, out var num) && num >= 1) { SelectByNumber(num); return true; }
				return false;
		}
	}

	public override void OnBlur() { }

	public TradePanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		_tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		var itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		var itemList = itemScroll.GetNode<VBoxContainer>("ItemList");
		BindListNodes(itemScroll, itemList);
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_buyBtn = actionBar.GetNode<Button>("BuyBtn");
		_sellBtn = actionBar.GetNode<Button>("SellBtn");
		var closeBtn = actionBar.GetNode<Button>("CloseBtn");

		_buyBtn.FocusMode = Control.FocusModeEnum.None;
		_sellBtn.FocusMode = Control.FocusModeEnum.None;
		closeBtn.FocusMode = Control.FocusModeEnum.None;
		_buyBtn.Pressed += () => DoAction();
		_sellBtn.Pressed += () => DoAction();
		closeBtn.Pressed += () => OnTradeClosed?.Invoke();

		_tabButtons = TabHelper.BuildTabButtons(_tabBar, TabLabels, Tabs, SetTab, PanelId);
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
		foreach (var row in _itemRows) row.QueueFree();
		_itemRows.Clear();
		_cursor = 0;
		_hoverIndex = -1;
	}

	public void RefreshData(Actor player, Actor trader)
	{
		_buyGoods = TradeModule.ListGoods(trader);
		_sellItems = InventoryModule.List(player).FindAll(x => !x.Item.Equipped && x.Item.Price > 0);
		RefreshHeader(player, trader);
		Refresh();
	}

	public override void Refresh()
	{
		_tabButtons[0].Text = LocalizationService.T("ui.trade.buy");
		_tabButtons[1].Text = LocalizationService.T("ui.trade.sell");
		TabHelper.UpdateTabHighlight(_tabButtons, Tabs, _currentTab);
		RebuildRows(ItemCount, ApplyRowContent, _currentTab == TradeTab.Buy
			? LocalizationService.T("ui.trade.empty.buy")
			: LocalizationService.T("ui.trade.empty.sell"));
		OnSelectionChanged();
	}

	public void SetTab(TradeTab tab)
	{
		_currentTab = tab;
		_cursor = 0;
		Refresh();
	}

	public void SelectByNumber(int number)
	{
		var index = number - 1;
		if (index >= 0 && index < ItemCount)
		{
			_cursor = index;
			OnSelectionChanged();
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

	protected override int GetRowDataCount() => ItemCount;

	protected override void OnSelectionChanged()
	{
		UpdateRowVisuals(ItemCount);
		RenderDetail();
		UpdateActionButtons();
	}

	protected override Button CreateRow(int index)
	{
		var row = base.CreateRow(index);
		var idx = index;
		row.GuiInput += ev => OnRowInput(ev, idx);
		return row;
	}

	private void DoAction()
	{
		if (ItemCount == 0) return;
		OnTradeAction?.Invoke(_currentTab, _cursor);
	}

	private void OnRowInput(InputEvent ev, int index)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed || index < 0 || index >= ItemCount)
			return;

		if (mb.ButtonIndex != MouseButton.Right)
			return;

		_cursor = index;
		OnSelectionChanged();
		var item = _currentTab == TradeTab.Buy
			? _buyGoods[index].Item
			: _sellItems[index].Item;
		if (TryHandleItemRightClick?.Invoke(item) == true)
			Refresh();
	}

	public void RefreshHeader(Actor player, Actor trader)
	{
		var traderName = _state == null
			? trader.DisplayName
			: IdentificationModule.GetActorDisplayName(_state, trader);
		_header.Clear();
		_header.AppendText($"[center]{LocalizationService.T("ui.trade.header", ("trader", traderName))}[/center]\n" +
			LocalizationService.T("ui.trade.gold_summary",
				("player_gold", player.Gold),
				("trader", traderName),
				("trader_gold", trader.Gold)));
	}

	private void ApplyRowContent(Button row, int i)
	{
		if (_currentTab == TradeTab.Buy)
		{
			var g = _buyGoods[i];
			var src = g.From == TradeGood.Source.Shop ? "" : LocalizationService.T("ui.trade.private_stock");
			var stock = g.Stock > 1 ? $" x{g.Stock}" : "";
			var itemName = _state == null
				? g.Item.Name
				: ItemFormatHelper.GetDisplayName(_state, g.Item);
			var stats = _state == null ? ItemFormatHelper.InlineStats(g.Item) : ItemFormatHelper.InlineStats(_state, g.Item);
			var statSegment = string.IsNullOrWhiteSpace(stats) ? string.Empty : $"  {stats}";
			row.Text = $"  [{i + 1}] {itemName}{statSegment}  {g.BuyPrice}G{stock}{src}";
		}
		else
		{
			var (_, item) = _sellItems[i];
			var sp = TradeModule.SellPrice(item);
			var itemName = _state == null
				? item.Name
				: ItemFormatHelper.GetDisplayName(_state, item);
			var stats = _state == null ? ItemFormatHelper.InlineStats(item) : ItemFormatHelper.InlineStats(_state, item);
			var statSegment = string.IsNullOrWhiteSpace(stats) ? string.Empty : $"  {stats}";
			row.Text = LocalizationService.TOrFallback(
				"ui.trade.sell_row.rich",
				"[{index}] {item}{stats}  {price}G",
				("index", i + 1),
				("item", itemName),
				("stats", statSegment),
				("price", sp));
		}
	}

	private void UpdateActionButtons()
	{
		var hasItem = ItemCount > 0 && _cursor >= 0 && _cursor < ItemCount;
		_buyBtn.Visible = _currentTab == TradeTab.Buy;
		_sellBtn.Visible = _currentTab == TradeTab.Sell;
		_buyBtn.Disabled = !hasItem;
		_sellBtn.Disabled = !hasItem;
		_buyBtn.Text = $"[E] {LocalizationService.T("ui.trade.buy")}";
		_sellBtn.Text = $"[E] {LocalizationService.T("ui.trade.sell")}";
	}

	private void RenderDetail()
	{
		_detailBox.Clear();
		Item? item = null;
		if (_currentTab == TradeTab.Buy && _cursor >= 0 && _cursor < _buyGoods.Count) item = _buyGoods[_cursor].Item;
		else if (_currentTab == TradeTab.Sell && _cursor >= 0 && _cursor < _sellItems.Count) item = _sellItems[_cursor].Item;
		if (item == null) { _detailBox.AppendText(LocalizationService.T("ui.common.detail_hint.trade")); return; }
		_detailBox.AppendText(_state == null
			? ItemFormatHelper.BuildDetail(item)
			: ItemFormatHelper.BuildDetail(_state, item));
	}
}
