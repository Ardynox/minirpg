using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MiniRPG.Core.Config;

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
		void SubmitPlayerAction(TimelinePlayerAction action);
		void FlushMap();
		void OpenChestFromInventory(Item chestItem);
		void CloseInventory();
		bool TryHandleItemRightClick(Item item);
	}

	private static readonly string[] FilterIds =
	[
		"all",
		ItemCategories.Weapon, ItemCategories.Armor, ItemCategories.Clothing,
		ItemCategories.Consumable, ItemCategories.Tool, ItemCategories.Material,
		ItemCategories.Food, ItemCategories.Ammo, ItemCategories.Misc,
	];

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly HBoxContainer _filterBar;
	private readonly HBoxContainer _secondaryFilterBar;
	private readonly RichTextLabel _detailBox;
	private readonly HBoxContainer _actionBar;
	private readonly Button _equipBtn;
	private readonly Button _useBtn;
	private readonly Button _dropBtn;
	private readonly Button _sortBtn;
	private readonly PopupMenu _contextMenu;
	private readonly IHost _host;

	private readonly List<Button> _filterButtons = [];
	private readonly List<Button> _secondaryFilterButtons = [];
	private List<string> _secondaryFilterIds = ["all"];

	private int _filterIndex;
	private int _secondaryFilterIndex;
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
			case "action4": CycleSecondaryFilter(1); return true;
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
		_secondaryFilterBar = vbox.GetNodeOrNull<HBoxContainer>("SubFilterBar") ?? new HBoxContainer
		{
			Name = "SubFilterBar",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		if (_secondaryFilterBar.GetParent() == null)
		{
			_secondaryFilterBar.AddThemeConstantOverride("separation", 2);
			vbox.AddChild(_secondaryFilterBar);
			vbox.MoveChild(_secondaryFilterBar, _filterBar.GetIndex() + 1);
		}
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
		RefreshSecondaryFilters([]);
		WireActionButtons();
		_contextMenu.IdPressed += OnContextMenuAction;
	}

	public override void Refresh()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;
		RefreshSecondaryFilters(player.Inventory);
		RebuildList(player);
		RebuildRows(_displayItems.Count, ApplyRowContent, LocalizationService.T("ui.common.empty_inline"));
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
		for (var i = 0; i < FilterIds.Length; i++)
		{
			var btn = new Button
			{
				Text = GetFilterLabel(i),
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
				ThemeTypeVariation = "TabButton",
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

	public void SetFilter(int index)
	{
		_filterIndex = index;
		_secondaryFilterIndex = 0;
		_cursor = 0;
		Refresh();
	}

	public void CycleFilter(int dir)
	{
		_filterIndex = (_filterIndex + dir + FilterIds.Length) % FilterIds.Length;
		_secondaryFilterIndex = 0;
		_cursor = 0;
		Refresh();
	}

	public void SetSecondaryFilter(int index)
	{
		if (index < 0 || index >= _secondaryFilterIds.Count)
			return;

		_secondaryFilterIndex = index;
		_cursor = 0;
		Refresh();
	}

	public void CycleSecondaryFilter(int dir)
	{
		if (_secondaryFilterIds.Count <= 1)
			return;

		_secondaryFilterIndex = (_secondaryFilterIndex + dir + _secondaryFilterIds.Count) % _secondaryFilterIds.Count;
		_cursor = 0;
		Refresh();
	}

	public void CycleSort() { _sortMode = _sortMode switch { InvSortMode.Name => InvSortMode.Weight, InvSortMode.Weight => InvSortMode.Price, _ => InvSortMode.Name }; _cursor = 0; Refresh(); }

	public bool TryEquip()
	{
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null || _cursor < 0 || _cursor >= _displayItems.Count) return false;
		var (invIdx, _) = _displayItems[_cursor];
		var result = InventoryModule.ToggleEquip(player, invIdx, _host.State);
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
		if (item.Category == ItemCategories.Food || item.Tags.ContainsKey(ItemTags.Nutrition))
		{
			_host.SubmitPlayerAction(TimelinePlayerAction.EatInventory(invIdx));
			Refresh();
			_host.FlushMap();
			return true;
		}
		if (item.Tags.ContainsKey(ItemTags.Healing))
		{
			_host.AddLog(LocalizationService.TOrFallback(
				"log.inventory.medical_supply_only",
				"{item} 是医疗耗材，请使用治疗技能。",
				("item", ItemFormatHelper.GetDisplayName(_host.State, item))));
			return false;
		}
		_host.AddLog(LocalizationService.T("log.inventory.not_consumable", ("item", ItemFormatHelper.GetDisplayName(_host.State, item))));
		return false;
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
		var secondaryFilterId = CurrentSecondaryFilterId;
		_displayItems = filterId == "all"
			? all
			: all.Where(x => string.Equals(x.Item.Category, filterId, StringComparison.Ordinal)).ToList();
		if (!string.Equals(secondaryFilterId, "all", StringComparison.Ordinal))
		{
			_displayItems = _displayItems
				.Where(x => string.Equals(x.Item.SubCategory, secondaryFilterId, StringComparison.Ordinal))
				.ToList();
		}
		_displayItems = _sortMode switch
		{
			InvSortMode.Weight => [.. _displayItems.OrderByDescending(x => x.Item.Equipped).ThenByDescending(x => x.Item.EffectiveWeight)],
			InvSortMode.Price => [.. _displayItems.OrderByDescending(x => x.Item.Equipped).ThenByDescending(x => x.Item.Price)],
			_ => [.. _displayItems.OrderByDescending(x => x.Item.Equipped).ThenBy(x => ItemFormatHelper.GetDisplayName(_host.State, x.Item))],
		};
		if (_cursor >= _displayItems.Count) _cursor = Math.Max(0, _displayItems.Count - 1);
	}

	private void ApplyRowContent(Button row, int i)
	{
		var item = _displayItems[i].Item;
		var eqTag = item.Equipped ? "[E] " : "    ";
		var stats = ItemFormatHelper.InlineStats(_host.State, item);
		var weight = ItemFormatHelper.BuildWeight(_host.State, item);
		var statSegment = string.IsNullOrWhiteSpace(stats) ? string.Empty : $"  {stats}";
		var weightSegment = string.IsNullOrWhiteSpace(weight) ? string.Empty : $"  {weight}";
		row.Text = $"{eqTag}{ItemFormatHelper.GetDisplayName(_host.State, item)}{statSegment}{weightSegment}";
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
			if (_host.TryHandleItemRightClick(_displayItems[index].Item))
			{
				Refresh();
				_host.FlushMap();
				return;
			}
			ShowContextMenu(mb.GlobalPosition);
		}
	}

	private void ShowContextMenu(Vector2 pos)
	{
		if (_cursor < 0 || _cursor >= _displayItems.Count) return;
		var (_, item) = _displayItems[_cursor];
		_contextMenu.Clear();
		if (item.IsContainer) _contextMenu.AddItem(LocalizationService.T("ui.inventory.context.open"), 3);
		_contextMenu.AddItem(item.Equipped ? LocalizationService.T("ui.inventory.context.unequip") : LocalizationService.T("ui.inventory.context.equip"), 0);
		if (item.Category == ItemCategories.Food || item.Tags.ContainsKey(ItemTags.Nutrition))
			_contextMenu.AddItem(LocalizationService.T("ui.inventory.context.eat"), 1);
		_contextMenu.AddItem(LocalizationService.T("ui.inventory.context.drop"), 2);
		_contextMenu.Position = new Vector2I((int)pos.X, (int)pos.Y);
		_contextMenu.ResetSize();
		_contextMenu.Popup();
	}

	private void UpdateFilterHighlight()
	{
		for (var i = 0; i < _filterButtons.Count; i++)
		{
			_filterButtons[i].Text = GetFilterLabel(i);
			_filterButtons[i].ButtonPressed = i == _filterIndex;
		}

		for (var i = 0; i < _secondaryFilterButtons.Count; i++)
		{
			_secondaryFilterButtons[i].Text = GetSecondaryFilterLabel(_secondaryFilterIds[i]);
			_secondaryFilterButtons[i].ButtonPressed = i == _secondaryFilterIndex;
		}

		_secondaryFilterBar.Visible = _secondaryFilterIds.Count > 1;
	}

	private void UpdateActionButtons()
	{
		var hasItem = _cursor >= 0 && _cursor < _displayItems.Count;
		_equipBtn.Disabled = !hasItem;
		_useBtn.Disabled = !hasItem;
		_dropBtn.Disabled = !hasItem;
		if (hasItem)
		{
			var (_, item) = _displayItems[_cursor];
			_equipBtn.Text = item.Equipped ? LocalizationService.T("ui.inventory.unequip") : LocalizationService.T("ui.inventory.equip");
		}
		else _equipBtn.Text = LocalizationService.T("ui.inventory.equip");
		if (hasItem)
		{
			var (_, item) = _displayItems[_cursor];
			var canEat = item.Category == ItemCategories.Food || item.Tags.ContainsKey(ItemTags.Nutrition);
			_useBtn.Disabled = !canEat;
			_useBtn.Text = canEat
				? LocalizationService.T("ui.inventory.eat")
				: LocalizationService.T("ui.inventory.use");
		}
		else
		{
			_useBtn.Text = LocalizationService.T("ui.inventory.use");
		}
		_dropBtn.Text = LocalizationService.T("ui.inventory.drop");
		_sortBtn.Text = LocalizationService.T("ui.inventory.sort");
	}

	private void RenderHeader(Actor player)
	{
		var wt = player.CarryWeight;
		var maxWt = player.MaxCarryWeight;
		var wtColor = player.IsOverweight ? "#ff4444" : "#cccccc";
		var sortLabel = _sortMode switch
		{
			InvSortMode.Weight => LocalizationService.T("ui.inventory.sort_mode.weight"),
			InvSortMode.Price => LocalizationService.T("ui.inventory.sort_mode.price"),
			_ => LocalizationService.T("ui.inventory.sort_mode.name"),
		};
		var focusTag = _host.HasFocus ? " [color=#66ff88]●[/color]" : " [color=#666666]○[/color]";
		_header.Clear();
		_header.AppendText($"[center]{LocalizationService.T("ui.inventory.header.title")}{focusTag}[/center]\n" +
			LocalizationService.T("ui.inventory.header.meta",
				("gold", player.Gold),
				("weight_color", wtColor),
				("current_weight", wt.ToString("F1")),
				("max_weight", maxWt.ToString("F1"))) +
			LocalizationService.T("ui.inventory.header.summary", ("sort", sortLabel), ("count", _displayItems.Count)));
	}

	private void RenderDetail()
	{
		_detailBox.Clear();
		if (_cursor < 0 || _cursor >= _displayItems.Count) { _detailBox.AppendText(LocalizationService.T("ui.common.detail_hint.item")); return; }
		var (_, item) = _displayItems[_cursor];
		_detailBox.AppendText(ItemFormatHelper.BuildDetail(_host.State, item));
	}

	private static string GetFilterLabel(int index)
	{
		if (index < 0 || index >= FilterIds.Length)
			return string.Empty;

		return FilterIds[index] switch
		{
			"all" => LocalizationService.T("ui.inventory.filter.all"),
			_ => GameLocalizer.LocalizeItemCategoryName(FilterIds[index], FilterIds[index]),
		};
	}

	private string CurrentSecondaryFilterId
	{
		get
		{
			if (_secondaryFilterIds.Count == 0)
				return "all";

			var safeIndex = Math.Clamp(_secondaryFilterIndex, 0, _secondaryFilterIds.Count - 1);
			return _secondaryFilterIds[safeIndex];
		}
	}

	private void RefreshSecondaryFilters(IEnumerable<Item> items)
	{
		var previousId = CurrentSecondaryFilterId;
		var primaryFilterId = FilterIds[_filterIndex];
		var nextIds = new List<string> { "all" };

		if (!string.Equals(primaryFilterId, "all", StringComparison.Ordinal))
		{
			var subCategoryIds = items
				.Where(item =>
					string.Equals(item.Category, primaryFilterId, StringComparison.Ordinal)
					&& !string.IsNullOrWhiteSpace(item.SubCategory))
				.Select(item => item.SubCategory)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(GetSecondaryFilterLabel, StringComparer.Ordinal)
				.ToList();
			nextIds.AddRange(subCategoryIds);
		}

		_secondaryFilterIds = nextIds;
		var preservedIndex = _secondaryFilterIds.FindIndex(id => string.Equals(id, previousId, StringComparison.Ordinal));
		_secondaryFilterIndex = preservedIndex >= 0 ? preservedIndex : 0;
		RebuildSecondaryFilterButtons();
	}

	private void RebuildSecondaryFilterButtons()
	{
		while (_secondaryFilterButtons.Count > _secondaryFilterIds.Count)
		{
			_secondaryFilterButtons[^1].QueueFree();
			_secondaryFilterButtons.RemoveAt(_secondaryFilterButtons.Count - 1);
		}

		while (_secondaryFilterButtons.Count < _secondaryFilterIds.Count)
		{
			var idx = _secondaryFilterButtons.Count;
			var button = new Button
			{
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
				ThemeTypeVariation = "TabButton",
			};
			button.Pressed += () => SetSecondaryFilter(idx);
			_secondaryFilterBar.AddChild(button);
			_secondaryFilterButtons.Add(button);
		}
	}

	private static string GetSecondaryFilterLabel(string filterId)
	{
		if (string.Equals(filterId, "all", StringComparison.Ordinal))
			return LocalizationService.T("ui.inventory.filter.all");

		return ItemSubcategoryRegistry.Get(filterId)?.Name ?? filterId;
	}
}
