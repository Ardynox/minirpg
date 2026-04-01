using System;
using System.Collections.Generic;
using Godot;
namespace MiniRPG.Module.Panel;

public class ChestPanelModule
{
	public interface IHost
	{
		GameState State { get; }
		void AddLog(string msg);
		void FlushMap();
		void CloseChestPanel();
		void OpenPutIntoChestSelection(Item chestItem);
	}

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly ScrollContainer _itemScroll;
	private readonly VBoxContainer _itemList;
	private readonly RichTextLabel _detailBox;
	private readonly RichTextLabel _hintBar;
	private readonly Button _takeBtn;
	private readonly Button _takeAllBtn;
	private readonly Button _putBtn;
	private readonly Button _closeBtn;
	private readonly IHost _host;

	private readonly List<Button> _itemRows = [];
	private Item? _chestItem;
	private int _cursor;
	private int _hoverIndex = -1;

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public Item? CurrentChest => _chestItem;

	public ChestPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		_itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		_itemList = _itemScroll.GetNode<VBoxContainer>("ItemList");
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		_hintBar = vbox.GetNode<RichTextLabel>("HintBar");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_takeBtn = actionBar.GetNode<Button>("TakeBtn");
		_takeAllBtn = actionBar.GetNode<Button>("TakeAllBtn");
		_putBtn = actionBar.GetNode<Button>("PutBtn");
		_closeBtn = actionBar.GetNode<Button>("CloseBtn");

		_takeBtn.FocusMode = Control.FocusModeEnum.None;
		_takeAllBtn.FocusMode = Control.FocusModeEnum.None;
		_putBtn.FocusMode = Control.FocusModeEnum.None;
		_closeBtn.FocusMode = Control.FocusModeEnum.None;

		_takeBtn.Pressed += () => TryTake();
		_takeAllBtn.Pressed += () => TryTakeAll();
		_putBtn.Pressed += () => TryPut();
		_closeBtn.Pressed += () => _host.CloseChestPanel();

		_hintBar.Clear();
		_hintBar.AppendText("[color=#666666]↑↓选择 E取出 P放入 Esc关闭[/color]");
	}

	public void Open(Item chestItem)
	{
		_chestItem = chestItem;
		_cursor = 0;
		Visible = true;
		Refresh();
	}

	public void Close()
	{
		_chestItem = null;
		Visible = false;
	}

	public void Refresh()
	{
		if (_chestItem?.Contents == null) return;
		var contents = _chestItem.Contents;
		if (_cursor >= contents.Count)
			_cursor = Math.Max(0, contents.Count - 1);

		RebuildItemNodes();
		RenderHeader();
		UpdateRowVisuals();
		RenderDetail();
		UpdateActionButtons();
	}

	public void MoveCursor(int delta)
	{
		if (_chestItem?.Contents == null || _chestItem.Contents.Count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, _chestItem.Contents.Count - 1);
		UpdateRowVisuals();
		RenderDetail();
		UpdateActionButtons();
		if (_cursor >= 0 && _cursor < _itemRows.Count)
			RowStyleHelper.EnsureVisible(_itemScroll, _itemRows[_cursor]);
	}

	public void TryTake()
	{
		if (_chestItem?.Contents == null) return;
		var contents = _chestItem.Contents;
		if (_cursor < 0 || _cursor >= contents.Count) return;

		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;

		var item = contents[_cursor];
		contents.RemoveAt(_cursor);
		InventoryModule.Add(player, item);
		_host.AddLog($"从{_chestItem.Name}中取出了 {item.Name}");
		Refresh();
		_host.FlushMap();
	}

	public void TryTakeAll()
	{
		if (_chestItem?.Contents == null) return;
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;

		var count = _chestItem.Contents.Count;
		foreach (var item in _chestItem.Contents)
			InventoryModule.Add(player, item);
		_chestItem.Contents.Clear();
		_host.AddLog($"从{_chestItem.Name}中取出了 {count} 件物品");
		Refresh();
		_host.FlushMap();
	}

	public void TryPut()
	{
		if (_chestItem == null) return;
		_host.OpenPutIntoChestSelection(_chestItem);
	}

	private void RenderHeader()
	{
		_header.Clear();
		var count = _chestItem?.Contents?.Count ?? 0;
		var name = _chestItem?.Name ?? "宝箱";
		_header.AppendText($"[center]── {name} ({count}件) ──[/center]");
	}

	private void RebuildItemNodes()
	{
		var contents = _chestItem?.Contents;
		var count = contents?.Count ?? 0;
		var needed = Math.Max(count, 1);

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

		if (count == 0)
		{
			_itemRows[0].Text = "  (空)";
			_itemRows[0].Disabled = true;
			_itemRows[0].AddThemeColorOverride("font_disabled_color", UIColors.TextDim);
		}
		else
		{
			for (var i = 0; i < count; i++)
			{
				var item = contents![i];
				var stats = ItemFormatHelper.InlineStats(item);
				var weight = $" {item.EffectiveWeight:F1}kg";
				_itemRows[i].Text = $"{item.Name}  {stats}{weight}";
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
			CustomMinimumSize = new Vector2(0, 26),
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
			_cursor = index;
			TryTake();
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
		var contents = _chestItem?.Contents;
		if (contents == null) return;

		for (var i = 0; i < _itemRows.Count && i < contents.Count; i++)
			RowStyleHelper.Apply(_itemRows[i], i == _cursor, i == _hoverIndex, transparentBg: true);
	}

	private void UpdateActionButtons()
	{
		var hasItems = _chestItem?.Contents != null && _chestItem.Contents.Count > 0;
		_takeBtn.Disabled = !hasItems;
		_takeAllBtn.Disabled = !hasItems;
	}

	private void RenderDetail()
	{
		_detailBox.Clear();
		var contents = _chestItem?.Contents;
		if (contents == null || _cursor < 0 || _cursor >= contents.Count)
		{
			_detailBox.AppendText("[color=#888888]选择物品查看详情[/color]");
			return;
		}
		_detailBox.AppendText(ItemFormatHelper.BuildDetail(contents[_cursor]));
	}
}
