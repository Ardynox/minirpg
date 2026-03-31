using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core;

namespace MiniRPG.Module;

public class GroundPanelModule
{
	public interface IHost
	{
		GameState State { get; }
		void AddLog(string msg);
		void Dispatch(List<GameEvent> events);
		void FlushMap();
		void OpenChestPanel(Item chestItem);
		void PickupGroundItem(Item item);
	}

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly ScrollContainer _itemScroll;
	private readonly VBoxContainer _itemList;
	private readonly Button _pickupBtn;
	private readonly Button _pickupAllBtn;
	private readonly IHost _host;

	private readonly List<Panel> _itemRows = [];
	private List<Item> _groundItems = [];
	private int _cursor;
	private int _hoverIndex = -1;

	private static readonly Color ColorNormal = new(0.8f, 0.8f, 0.8f);
	private static readonly Color ColorSelected = new(1f, 1f, 1f);
	private static readonly Color ColorContainer = new(0.6f, 0.9f, 1f);
	private static readonly Color ColorHover = new(0.3f, 0.3f, 0.4f);
	private static readonly Color ColorSelectedBg = new(0.2f, 0.25f, 0.35f);
	private static readonly Color ColorTransparent = new(0, 0, 0, 0);

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public GroundPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		_itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		_itemList = _itemScroll.GetNode<VBoxContainer>("ItemList");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_pickupBtn = actionBar.GetNode<Button>("PickupBtn");
		_pickupAllBtn = actionBar.GetNode<Button>("PickupAllBtn");

		_pickupBtn.FocusMode = Control.FocusModeEnum.None;
		_pickupAllBtn.FocusMode = Control.FocusModeEnum.None;
		_pickupBtn.Pressed += () => DoPickupSelected();
		_pickupAllBtn.Pressed += () => DoPickupAll();
	}

	public void Refresh()
	{
		_groundItems = MapModule.PeekGroundItems(_host.State, _host.State.PlayerX, _host.State.PlayerY);
		if (_cursor >= _groundItems.Count)
			_cursor = Math.Max(0, _groundItems.Count - 1);

		RebuildItemNodes();
		RenderHeader();
		UpdateRowVisuals();
		UpdateActionButtons();
	}

	public void MoveCursor(int delta)
	{
		if (_groundItems.Count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, _groundItems.Count - 1);
		UpdateRowVisuals();
		UpdateActionButtons();
	}

	public void DoPickupSelected()
	{
		if (_cursor < 0 || _cursor >= _groundItems.Count) return;
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
		var nonContainers = _groundItems.FindAll(i => !i.IsContainer);
		foreach (var item in nonContainers)
			_host.PickupGroundItem(item);
		Refresh();
	}

	public void DoInteractSelected()
	{
		if (_cursor < 0 || _cursor >= _groundItems.Count) return;
		var item = _groundItems[_cursor];
		if (item.IsContainer)
			_host.OpenChestPanel(item);
		else
			DoPickupSelected();
	}

	private void RenderHeader()
	{
		_header.Clear();
		if (_groundItems.Count == 0)
			_header.AppendText("[color=#888888]── 脚下 ── (空)[/color]");
		else
			_header.AppendText($"── 脚下 ── ({_groundItems.Count}件)");
	}

	private void RebuildItemNodes()
	{
		foreach (var old in _itemRows)
			old.QueueFree();
		_itemRows.Clear();

		for (var i = 0; i < _groundItems.Count; i++)
		{
			var row = CreateItemRow(i, _groundItems[i]);
			_itemList.AddChild(row);
			_itemRows.Add(row);
		}

		if (_groundItems.Count == 0)
		{
			var empty = new Label { Text = "  地上没有物品" };
			empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
			var wrapper = new Panel { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			var sb = new StyleBoxFlat { BgColor = ColorTransparent };
			wrapper.AddThemeStyleboxOverride("panel", sb);
			wrapper.AddChild(empty);
			_itemList.AddChild(wrapper);
			_itemRows.Add(wrapper);
		}
	}

	private Panel CreateItemRow(int index, Item item)
	{
		var row = new Panel
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 22),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};

		var sb = new StyleBoxFlat { BgColor = ColorTransparent };
		row.AddThemeStyleboxOverride("panel", sb);

		var hbox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		hbox.AddThemeConstantOverride("separation", 6);

		var icon = item.IsContainer ? "📦" : "·";
		var iconLabel = new Label
		{
			Text = icon,
			CustomMinimumSize = new Vector2(20, 0),
			HorizontalAlignment = HorizontalAlignment.Center,
		};

		var nameText = item.IsContainer
			? $"{item.Name} ({item.Contents?.Count ?? 0}件)"
			: item.Name;
		var nameLabel = new Label
		{
			Text = nameText,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		var weightLabel = new Label
		{
			Text = $"{item.EffectiveWeight:F1}kg",
			CustomMinimumSize = new Vector2(50, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		weightLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));

		hbox.AddChild(iconLabel);
		hbox.AddChild(nameLabel);
		hbox.AddChild(weightLabel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 4);
		margin.AddThemeConstantOverride("margin_right", 4);
		margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		margin.MouseFilter = Control.MouseFilterEnum.Ignore;
		hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
		iconLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		weightLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		margin.AddChild(hbox);
		row.AddChild(margin);

		var idx = index;
		row.GuiInput += (ev) => OnRowInput(ev, idx);
		row.MouseEntered += () => OnRowHover(idx);
		row.MouseExited += () => OnRowHoverExit(idx);

		return row;
	}

	private void OnRowInput(InputEvent ev, int index)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed) return;

		if (mb.ButtonIndex == MouseButton.Left)
		{
			_cursor = index;
			var item = _groundItems[index];
			if (item.IsContainer)
				_host.OpenChestPanel(item);
			else
				DoPickupSelected();
		}
	}

	private void OnRowHover(int index)
	{
		_hoverIndex = index;
		UpdateRowVisuals();
	}

	private void OnRowHoverExit(int index)
	{
		if (_hoverIndex == index) _hoverIndex = -1;
		UpdateRowVisuals();
	}

	private void UpdateRowVisuals()
	{
		for (var i = 0; i < _itemRows.Count && i < _groundItems.Count; i++)
		{
			var row = _itemRows[i];
			var isSelected = i == _cursor;
			var isHover = i == _hoverIndex;

			Color bg;
			if (isSelected) bg = ColorSelectedBg;
			else if (isHover) bg = ColorHover;
			else bg = ColorTransparent;

			var stylebox = new StyleBoxFlat { BgColor = bg };
			row.AddThemeStyleboxOverride("panel", stylebox);

			var nameLabel = row.GetNode<MarginContainer>("MarginContainer")
				.GetNode<HBoxContainer>("HBoxContainer")
				.GetChild<Label>(1);

			var item = _groundItems[i];
			if (item.IsContainer)
				nameLabel.AddThemeColorOverride("font_color", isSelected ? ColorSelected : ColorContainer);
			else
				nameLabel.AddThemeColorOverride("font_color", isSelected ? ColorSelected : ColorNormal);
		}
	}

	private void UpdateActionButtons()
	{
		var hasItems = _groundItems.Count > 0;
		_pickupBtn.Disabled = !hasItems;
		_pickupAllBtn.Disabled = !hasItems;

		if (hasItems && _cursor >= 0 && _cursor < _groundItems.Count)
		{
			var item = _groundItems[_cursor];
			_pickupBtn.Text = item.IsContainer ? "打开 [F]" : "拾取 [F]";
		}
		else
		{
			_pickupBtn.Text = "拾取 [F]";
		}
	}
}
