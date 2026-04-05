using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace MiniRPG.Module.Panel;

public enum InvSortMode { Name, Weight, Price }

public class InventoryPanelModule : ListPanelBase
{
	public override string PanelId => "inventory";
	public override PanelContainer PanelNode => _panel;

	public interface IHost
	{
		GameState State { get; }
		bool HasFocus { get; }
		void AddLog(string msg);
		void Dispatch(List<GameEvent> events);
		void FlushMap();
		void OpenChestFromInventory(Item chestItem);
		void CloseInventory();
	}

	private static readonly string[] FilterIds =
	[
		"all",
		ItemCategories.Weapon, ItemCategories.Armor, ItemCategories.Clothing,
		ItemCategories.Consumable, ItemCategories.Tool, ItemCategories.Material,
		ItemCategories.Food, ItemCategories.Misc,
	];

	private static readonly string[] FilterLabels =
		["全部", "武器", "护甲", "衣物", "消耗", "工具", "材料", "食物", "杂项"];

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly HBoxContainer _filterBar;
	private readonly RichTextLabel _detailBox;
	private readonly HBoxContainer _actionBar;
	private readonly Button _equipBtn;
	private readonly Button _useBtn;
	private readonly Button _dropBtn;
	private readonly Button _sortBtn;
	private readonly PopupMenu _contextMenu;
	private readonly IHost _host;

	private readonly List<Button> _filterButtons = [];

	private int _filterIndex;
	private InvSortMode _sortMode = InvSortMode.Name;
	private List<(int InvIndex, Item Item)> _displayItems = [];
	private ulong _lastClickTime;
	private int _lastClickIndex = -1;
	private const ulong DoubleClickMs = 400;

	public override bool Visible
	{
		get => _panel.Visible;
		set
		{
			_panel.Visible = value;
			if (value) Refresh();
		}
	}

	public override bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1, _displayItems.Count); return true;
			case "down": MoveCursor(1, _displayItems.Count); return true;
			case "action1": TryEquip(); return true;
			case "action5": TryUse(); return true;
			case "action2": TryDrop(); return true;
			case "action3": CycleSort(); return true;
			case "right" or "tab_next": CycleFilter(1); return true;
			case "left" or "tab_prev": CycleFilter(-1); return true;
			case "close": _host.CloseInventory(); return true;
		}
		return false;
	}

	public override void OnBlur() { }

	public InventoryPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		_filterBar = vbox.GetNode<HBoxContainer>("FilterBar");
		var itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		var itemList = itemScroll.GetNode<VBoxContainer>("ItemList");
		BindListNodes(itemScroll, itemList);
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		_actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_equipBtn = _actionBar.GetNode<Button>("EquipBtn");
		_useBtn = _actionBar.GetNode<Button>("UseBtn");
		_dropBtn = _actionBar.GetNode<Button>("DropBtn");
		_sortBtn = _actionBar.GetNode<Button>("SortBtn");
		_contextMenu = panel.GetNode<PopupMenu>("ContextMenu");

		BuildFilterButtons();
		WireActionButtons();
		_contextMenu.IdPressed += OnContextMenuAction;
	}

	public override void Refresh()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;
		RebuildList(player);
		RebuildRows(_displayItems.Count, ApplyRowContent, "  (空)");
		RenderHeader(player);
		UpdateFilterHighlight();
		UpdateRowVisuals(_displayItems.Count);
		RenderDetail();
		UpdateActionButtons();
	}

	protected override int GetRowDataCount() => _displayItems.Count;

	protected override void OnSelectionChanged()
	{
		UpdateRowVisuals(_displayItems.Count);
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

	private void BuildFilterButtons()
	{
		for (var i = 0; i < FilterLabels.Length; i++)
		{
			var btn = new Button
			{
				Text = FilterLabels[i],
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
			};
			var idx = i;
			btn.Pressed += () => SetFilter(idx);
			_filterBar.AddChild(btn);
			_filterButtons.Add(btn);
		}
		UpdateFilterHighlight();
	}

	private void WireActionButtons()
	{
		_equipBtn.FocusMode = Control.FocusModeEnum.None;
		_useBtn.FocusMode = Control.FocusModeEnum.None;
		_dropBtn.FocusMode = Control.FocusModeEnum.None;
		_sortBtn.FocusMode = Control.FocusModeEnum.None;
		_equipBtn.Pressed += () => TryEquip();
		_useBtn.Pressed += () => TryUse();
		_dropBtn.Pressed += () => TryDrop();
		_sortBtn.Pressed += () => CycleSort();
	}

	private void OnContextMenuAction(long id)
	{
		switch (id)
		{
			case 0: TryEquip(); break;
			case 1: TryUse(); break;
			case 2: TryDrop(); break;
			case 3: TryOpenContainer(); break;
		}
	}

	public void SetFilter(int index) { _filterIndex = index; _cursor = 0; Refresh(); }
	public void CycleFilter(int dir) { _filterIndex = (_filterIndex + dir + FilterIds.Length) % FilterIds.Length; _cursor = 0; Refresh(); }
	public void CycleSort() { _sortMode = _sortMode switch { InvSortMode.Name => InvSortMode.Weight, InvSortMode.Weight => InvSortMode.Price, _ => InvSortMode.Name }; _cursor = 0; Refresh(); }

	public bool TryEquip()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || _cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (invIdx, _) = _displayItems[_cursor];
		var result = InventoryModule.ToggleEquip(player, invIdx);
		_host.AddLog(result.Message);
		Refresh();
		_host.FlushMap();
		return true;
	}

	public bool TryUse()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || _cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (invIdx, item) = _displayItems[_cursor];
		if (!item.Tags.ContainsKey("治疗")) { _host.AddLog($"{item.Name} 不是消耗品"); return false; }
		var result = InventoryModule.Use(player, invIdx);
		_host.AddLog(result.Message);
		Refresh();
		_host.FlushMap();
		return true;
	}

	public bool TryOpenContainer()
	{
		if (_cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (_, item) = _displayItems[_cursor];
		if (!item.IsContainer) return false;
		_host.OpenChestFromInventory(item);
		return true;
	}

	public bool TryDrop()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || _cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (invIdx, _) = _displayItems[_cursor];
		var events = InteractionModule.DropItem(_host.State, player, invIdx);
		_host.Dispatch(events);
		Refresh();
		_host.FlushMap();
		return true;
	}

	private void RebuildList(Actor player)
	{
		var all = InventoryModule.List(player);
		var filterId = FilterIds[_filterIndex];
		_displayItems = filterId == "all" ? all : all.Where(x => x.Item.Category == filterId).ToList();
		_displayItems = _sortMode switch
		{
			InvSortMode.Weight => [.. _displayItems.OrderByDescending(x => x.Item.Equipped).ThenByDescending(x => x.Item.EffectiveWeight)],
			InvSortMode.Price => [.. _displayItems.OrderByDescending(x => x.Item.Equipped).ThenByDescending(x => x.Item.Price)],
			_ => [.. _displayItems.OrderByDescending(x => x.Item.Equipped).ThenBy(x => x.Item.Name)],
		};
		if (_cursor >= _displayItems.Count) _cursor = Math.Max(0, _displayItems.Count - 1);
	}

	private void ApplyRowContent(Button row, int i)
	{
		var item = _displayItems[i].Item;
		var eqTag = item.Equipped ? "[E] " : "    ";
		var stats = ItemFormatHelper.InlineStats(item);
		var weight = $" {item.EffectiveWeight:F1}kg";
		row.Text = $"{eqTag}{item.Name}  {stats}{weight}";
	}

	private void OnRowInput(InputEvent ev, int index)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed || index < 0 || index >= _displayItems.Count) return;
		if (mb.ButtonIndex == MouseButton.Left)
		{
			var now = Time.GetTicksMsec();
			if (index == _lastClickIndex && now - _lastClickTime < DoubleClickMs)
			{
				_cursor = index;
				var (_, dblItem) = _displayItems[_cursor];
				if (dblItem.IsContainer) _host.OpenChestFromInventory(dblItem); else TryEquip();
				_lastClickIndex = -1;
			}
			else
			{
				_cursor = index;
				OnSelectionChanged();
				_lastClickIndex = index;
				_lastClickTime = now;
			}
		}
		else if (mb.ButtonIndex == MouseButton.Right)
		{
			_cursor = index;
			OnSelectionChanged();
			ShowContextMenu(mb.GlobalPosition);
		}
	}

	private void ShowContextMenu(Vector2 pos)
	{
		if (_cursor < 0 || _cursor >= _displayItems.Count) return;
		var (_, item) = _displayItems[_cursor];
		_contextMenu.Clear();
		if (item.IsContainer) _contextMenu.AddItem("打开", 3);
		_contextMenu.AddItem(item.Equipped ? "卸下" : "装备", 0);
		if (item.Tags.ContainsKey("治疗")) _contextMenu.AddItem("使用", 1);
		_contextMenu.AddItem("丢弃", 2);
		_contextMenu.Position = new Vector2I((int)pos.X, (int)pos.Y);
		_contextMenu.ResetSize();
		_contextMenu.Popup();
	}

	private void UpdateFilterHighlight() { for (var i = 0; i < _filterButtons.Count; i++) _filterButtons[i].ButtonPressed = i == _filterIndex; }

	private void UpdateActionButtons()
	{
		var hasItem = _cursor >= 0 && _cursor < _displayItems.Count;
		_equipBtn.Disabled = !hasItem;
		_useBtn.Disabled = !hasItem;
		_dropBtn.Disabled = !hasItem;
		if (hasItem)
		{
			var (_, item) = _displayItems[_cursor];
			_equipBtn.Text = item.Equipped ? "卸下 [E]" : "装备 [E]";
			_useBtn.Disabled = !item.Tags.ContainsKey("治疗");
		}
		else _equipBtn.Text = "装备 [E]";
	}

	private void RenderHeader(Actor player)
	{
		var wt = player.CarryWeight;
		var maxWt = player.MaxCarryWeight;
		var wtColor = player.IsOverweight ? "#ff4444" : "#cccccc";
		var sortLabel = _sortMode switch { InvSortMode.Weight => "重量", InvSortMode.Price => "价格", _ => "名称" };
		var focusTag = _host.HasFocus ? " [color=#66ff88]●[/color]" : " [color=#666666]○[/color]";
		_header.Clear();
		_header.AppendText($"[center]── 背包 ──{focusTag}[/center]\n" +
			$"💰 {player.Gold}G  [color={wtColor}]⚖ {wt:F1}/{maxWt:F1}kg[/color]  " +
			$"[color=#888888]排序:{sortLabel}  {_displayItems.Count}件[/color]");
	}

	private void RenderDetail()
	{
		_detailBox.Clear();
		if (_cursor < 0 || _cursor >= _displayItems.Count) { _detailBox.AppendText("[color=#888888]选择物品查看详情[/color]"); return; }
		var (_, item) = _displayItems[_cursor];
		_detailBox.AppendText(ItemFormatHelper.BuildDetail(item));
	}
}
