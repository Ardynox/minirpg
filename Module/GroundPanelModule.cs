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

	private readonly List<Button> _itemRows = [];
	private List<Item> _groundItems = [];
	private int _cursor;
	private int _hoverIndex = -1;
	private ulong _lastClickTime;
	private int _lastClickIndex = -1;
	private const ulong DoubleClickMs = 400;

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
		if (_cursor >= 0 && _cursor < _itemRows.Count)
			RowStyleHelper.EnsureVisible(_itemScroll, _itemRows[_cursor]);
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
			var empty = new Button
			{
				Text = "  地上没有物品",
				Flat = true,
				FocusMode = Control.FocusModeEnum.None,
				Disabled = true,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			empty.AddThemeColorOverride("font_disabled_color", UIColors.TextDim);
			_itemList.AddChild(empty);
			_itemRows.Add(empty);
		}
	}

	private Button CreateItemRow(int index, Item item)
	{
		var icon = item.IsContainer ? "📦 " : "· ";
		var nameText = item.IsContainer
			? $"{item.Name} ({item.Contents?.Count ?? 0}件)"
			: item.Name;
		var text = $"{icon}{nameText}  {item.EffectiveWeight:F1}kg";

		var row = new Button
		{
			Text = text,
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 22),
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
		};

		RowStyleHelper.Apply(row, false, false,
			textOverride: item.IsContainer ? UIColors.TextContainer : null, transparentBg: true);

		var idx = index;
		row.GuiInput += ev => OnRowInput(ev, idx);
		row.MouseEntered += () => OnRowHover(idx);
		row.MouseExited += () => OnRowHoverExit(idx);

		return row;
	}

	private void OnRowInput(InputEvent ev, int index)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed) return;

		if (mb.ButtonIndex == MouseButton.Left)
		{
			var now = Time.GetTicksMsec();
			if (index == _lastClickIndex && now - _lastClickTime < DoubleClickMs)
			{
				_cursor = index;
				DoInteractSelected();
				_lastClickIndex = -1;
			}
			else
			{
				_cursor = index;
				UpdateRowVisuals();
				UpdateActionButtons();
				_lastClickIndex = index;
				_lastClickTime = now;
			}
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
			var selected = i == _cursor;
			var isContainer = _groundItems[i].IsContainer;
			RowStyleHelper.Apply(_itemRows[i], selected, i == _hoverIndex,
				textOverride: isContainer && !selected ? UIColors.TextContainer : null, transparentBg: true);
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
