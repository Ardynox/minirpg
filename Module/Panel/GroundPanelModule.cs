using System;
using System.Collections.Generic;
using Godot;
namespace MiniRPG.Module.Panel;

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
	private int _cachedX = int.MinValue;
	private int _cachedY = int.MinValue;
	private bool _dirty = true;

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

	/// <summary>标记地面物品已变化，下次 Refresh 时重新查询。</summary>
	public void Invalidate() => _dirty = true;

	public void Refresh()
	{
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
		var needed = Math.Max(_groundItems.Count, 1);

		while (_itemRows.Count > needed)
		{
			_itemRows[^1].QueueFree();
			_itemRows.RemoveAt(_itemRows.Count - 1);
		}
		while (_itemRows.Count < needed)
		{
			var row = CreateEmptyRow(_itemRows.Count);
			_itemList.AddChild(row);
			_itemRows.Add(row);
		}

		if (_groundItems.Count == 0)
		{
			_itemRows[0].Text = "  地上没有物品";
			_itemRows[0].Disabled = true;
			_itemRows[0].AddThemeColorOverride("font_disabled_color", UIColors.TextDim);
		}
		else
		{
			for (var i = 0; i < _groundItems.Count; i++)
			{
				var item = _groundItems[i];
				var icon = item.IsContainer ? "📦 " : "· ";
				var nameText = item.IsContainer
					? $"{item.Name} ({item.Contents?.Count ?? 0}件)"
					: item.Name;
				_itemRows[i].Text = $"{icon}{nameText}  {item.EffectiveWeight:F1}kg";
				_itemRows[i].Disabled = false;
			}
		}
	}

	private Button CreateEmptyRow(int index)
	{
		var row = new Button
		{
			Flat = true,
			FocusMode = Control.FocusModeEnum.None,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 22),
			Alignment = HorizontalAlignment.Left,
			ClipText = true,
		};

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
