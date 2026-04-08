using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

public class GroundPanelModule : ListPanelBase
{
	public override string PanelId => "ground";
	public override PanelContainer PanelNode => _panel;
	public override bool CanFocus => false;

	public interface IHost
	{
		GameState State { get; }
		void AddLog(string msg);
		void Dispatch(List<GameEvent> events);
		void FlushMap();
		void OpenChestPanel(Item chestItem);
		void OpenCorpseHarvest(Item corpseItem);
		void StripCorpse(Item corpseItem);
		void ButcherCorpse(Item corpseItem);
		void PickupGroundItem(Item item);
		bool TryHandleItemRightClick(Item item);
	}

	private readonly PanelContainer _panel;
	private readonly Label _header;
	private readonly Button _pickupBtn;
	private readonly Button _pickupAllBtn;
	private readonly PopupMenu _contextMenu;
	private readonly IHost _host;

	private List<Item> _groundItems = [];
	private int _cachedX = int.MinValue;
	private int _cachedY = int.MinValue;
	private bool _dirty = true;

	public override bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public override bool HandleCommand(string cmd) => false;

	public override void OnBlur()
	{
	}

	public GroundPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode<VBoxContainer>("MarginContainer/VBox");
		_header = vbox.GetNode<Label>("Header");
		var itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		var itemList = itemScroll.GetNode<VBoxContainer>("ItemList");
		BindListNodes(itemScroll, itemList);
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_pickupBtn = actionBar.GetNode<Button>("PickupBtn");
		_pickupAllBtn = actionBar.GetNode<Button>("PickupAllBtn");

		_pickupBtn.FocusMode = Control.FocusModeEnum.None;
		_pickupAllBtn.FocusMode = Control.FocusModeEnum.None;
		_pickupBtn.Pressed += DoPickupSelected;
		_pickupAllBtn.Pressed += DoPickupAll;
		_contextMenu = new PopupMenu { Name = "ContextMenu" };
		_panel.AddChild(_contextMenu);
		_contextMenu.IdPressed += OnContextMenuAction;
	}

	public void Invalidate() => _dirty = true;

	public override void Refresh()
	{
		if (_host.State.World == null)
		{
			_groundItems = [];
			_cursor = 0;
			_cachedX = _host.State.PlayerX;
			_cachedY = _host.State.PlayerY;
			_dirty = false;
			RebuildRows(0, ApplyRowContent, LocalizationService.T("ui.common.empty_inline"));
			RenderHeader();
			UpdateActionButtons();
			return;
		}

		var px = _host.State.PlayerX;
		var py = _host.State.PlayerY;
		if (_dirty || px != _cachedX || py != _cachedY)
		{
			_groundItems = MapModule.PeekGroundItems(_host.State, px, py);
			_cachedX = px;
			_cachedY = py;
			_dirty = false;
		}

		if (_cursor >= _groundItems.Count)
			_cursor = Math.Max(0, _groundItems.Count - 1);

		RebuildRows(_groundItems.Count, ApplyRowContent, LocalizationService.T("ui.common.empty_inline"));
		RenderHeader();
		UpdateRowVisuals(_groundItems.Count);
		UpdateActionButtons();
	}

	protected override int GetRowDataCount() => _groundItems.Count;

	protected override void OnSelectionChanged()
	{
		UpdateRowVisuals(_groundItems.Count);
		UpdateActionButtons();
	}

	protected override Button CreateRow(int index)
	{
		var row = base.CreateRow(index);
		var idx = index;
		row.GuiInput += ev => OnRowInput(ev, idx);
		return row;
	}

	private void ApplyRowContent(Button row, int index)
	{
		var item = _groundItems[index];
		var icon = item.IsContainer ? "[C]" : "   ";
		var itemName = ItemFormatHelper.GetDisplayName(_host.State, item);
		var stats = ItemFormatHelper.InlineStats(_host.State, item);
		var weight = ItemFormatHelper.BuildWeight(_host.State, item);
		var nameText = item.IsContainer
			? LocalizationService.T("ui.ground.container_name", ("name", itemName), ("count", item.Contents?.Count ?? 0))
			: itemName;
		var statSegment = string.IsNullOrWhiteSpace(stats) ? string.Empty : $"  {stats}";
		var weightSegment = string.IsNullOrWhiteSpace(weight) ? string.Empty : $"  {weight}";
		row.Text = $"{icon} {nameText}{statSegment}{weightSegment}";
	}

	private void OnRowInput(InputEvent ev, int index)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed || index < 0 || index >= _groundItems.Count)
			return;

		if (mb.ButtonIndex != MouseButton.Right)
			return;

		_cursor = index;
		OnSelectionChanged();
		if (_host.TryHandleItemRightClick(_groundItems[index]))
		{
			Refresh();
			_host.FlushMap();
			return;
		}

		ShowContextMenu(_groundItems[index], mb.GlobalPosition);
	}

	private void ShowContextMenu(Item item, Vector2 position)
	{
		_contextMenu.Clear();
		if (item.IsCorpse)
		{
			_contextMenu.AddItem(LocalizationService.TOrFallback("ui.ground.context.open", "Open"), 0);
			_contextMenu.AddItem(LocalizationService.TOrFallback("ui.ground.context.strip", "Strip"), 1);
			_contextMenu.AddItem(LocalizationService.TOrFallback("ui.ground.context.butcher", "Butcher"), 2);
			_contextMenu.AddItem(LocalizationService.TOrFallback("ui.ground.context.harvest", "Harvest"), 3);
		}
		else if (item.IsContainer)
		{
			_contextMenu.AddItem(LocalizationService.TOrFallback("ui.ground.context.open", "Open"), 0);
		}
		else
		{
			_contextMenu.AddItem(LocalizationService.TOrFallback("ui.ground.context.pickup", "Pick up"), 4);
		}

		_contextMenu.Position = new Vector2I((int)position.X, (int)position.Y);
		_contextMenu.ResetSize();
		_contextMenu.Popup();
	}

	private void OnContextMenuAction(long id)
	{
		if (_cursor < 0 || _cursor >= _groundItems.Count)
			return;

		var item = _groundItems[_cursor];
		switch (id)
		{
			case 0:
				_host.OpenChestPanel(item);
				break;
			case 1:
				_host.StripCorpse(item);
				break;
			case 2:
				_host.ButcherCorpse(item);
				break;
			case 3:
				_host.OpenCorpseHarvest(item);
				break;
			case 4:
				_host.PickupGroundItem(item);
				break;
		}

		Refresh();
		_host.FlushMap();
	}

	public void DoPickupSelected()
	{
		if (_host.State.World == null)
			return;

		if (_cursor < 0 || _cursor >= _groundItems.Count)
			return;

		var item = _groundItems[_cursor];
		if (item.IsContainer)
		{
			_host.OpenChestPanel(item);
			return;
		}

		_host.PickupGroundItem(item);
		Refresh();
	}

	public void DoPickupAll()
	{
		if (_host.State.World == null)
			return;

		var nonContainers = _groundItems.FindAll(static item => !item.IsContainer);
		foreach (var item in nonContainers)
			_host.PickupGroundItem(item);

		Refresh();
	}

	public void DoInteractSelected()
	{
		if (_host.State.World == null)
			return;

		if (_cursor < 0 || _cursor >= _groundItems.Count)
			return;

		var item = _groundItems[_cursor];
		if (item.IsContainer)
			_host.OpenChestPanel(item);
		else
			DoPickupSelected();
	}

	private void RenderHeader()
	{
		if (_groundItems.Count == 0)
		{
			_header.Text = LocalizationService.T("ui.ground.header.empty");
			_header.ThemeTypeVariation = "HintLabel";
			return;
		}

		_header.Text = LocalizationService.T("ui.ground.header.count", ("count", _groundItems.Count));
		_header.ThemeTypeVariation = "";
	}

	private void UpdateActionButtons()
	{
		var hasItems = _groundItems.Count > 0;
		_pickupBtn.Disabled = !hasItems;
		_pickupAllBtn.Disabled = !hasItems;

		if (hasItems && _cursor >= 0 && _cursor < _groundItems.Count)
		{
			var item = _groundItems[_cursor];
			_pickupBtn.Text = item.IsContainer
				? LocalizationService.T("ui.ground.open")
				: LocalizationService.T("ui.ground.pickup");
		}
		else
		{
			_pickupBtn.Text = LocalizationService.T("ui.ground.pickup");
		}

		_pickupAllBtn.Text = LocalizationService.T("ui.ground.pickup_all");
	}
}
