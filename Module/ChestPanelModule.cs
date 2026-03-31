using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core;

namespace MiniRPG.Module;

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

	private readonly List<Panel> _itemRows = [];
	private Item? _chestItem;
	private int _cursor;
	private int _hoverIndex = -1;

	private static readonly Color ColorNormal = new(0.8f, 0.8f, 0.8f);
	private static readonly Color ColorSelected = new(1f, 1f, 1f);
	private static readonly Color ColorHover = new(0.3f, 0.3f, 0.4f);
	private static readonly Color ColorSelectedBg = new(0.2f, 0.25f, 0.35f);
	private static readonly Color ColorTransparent = new(0, 0, 0, 0);

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
		foreach (var old in _itemRows)
			old.QueueFree();
		_itemRows.Clear();

		var contents = _chestItem?.Contents;
		if (contents == null || contents.Count == 0)
		{
			var empty = new Label { Text = "  (空)" };
			empty.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
			var wrapper = new Panel { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			var sb = new StyleBoxFlat { BgColor = ColorTransparent };
			wrapper.AddThemeStyleboxOverride("panel", sb);
			wrapper.AddChild(empty);
			_itemList.AddChild(wrapper);
			_itemRows.Add(wrapper);
			return;
		}

		for (var i = 0; i < contents.Count; i++)
		{
			var row = CreateItemRow(i, contents[i]);
			_itemList.AddChild(row);
			_itemRows.Add(row);
		}
	}

	private Panel CreateItemRow(int index, Item item)
	{
		var row = new Panel
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 24),
			MouseFilter = Control.MouseFilterEnum.Stop,
		};

		var sb = new StyleBoxFlat { BgColor = ColorTransparent };
		row.AddThemeStyleboxOverride("panel", sb);

		var hbox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		hbox.AddThemeConstantOverride("separation", 6);

		var nameLabel = new Label
		{
			Text = item.Name,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};

		var statsLabel = new Label
		{
			Text = FormatInlineStats(item),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		statsLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));

		var weightLabel = new Label
		{
			Text = $"{item.EffectiveWeight:F1}kg",
			CustomMinimumSize = new Vector2(50, 0),
			HorizontalAlignment = HorizontalAlignment.Right,
		};
		weightLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));

		hbox.AddChild(nameLabel);
		hbox.AddChild(statsLabel);
		hbox.AddChild(weightLabel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 4);
		margin.AddThemeConstantOverride("margin_right", 4);
		margin.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		margin.MouseFilter = Control.MouseFilterEnum.Ignore;
		hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
		nameLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		statsLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
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
				.GetChild<Label>(0);
			nameLabel.AddThemeColorOverride("font_color", isSelected ? ColorSelected : ColorNormal);
		}
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

		var item = contents[_cursor];
		var detail = new StringBuilder();
		detail.Append($"[color=#ffffff]{item.Name}[/color]");
		var catDef = ItemCategoryDef.Get(item.Category);
		detail.AppendLine($"  [color=#888888][{catDef?.Name ?? item.Category}][/color]");

		if (item.SharpDamage > 0 || item.BluntDamage > 0)
		{
			detail.Append("[color=#ff6666]伤害:[/color] ");
			if (item.SharpDamage > 0) detail.Append($"锐{item.SharpDamage:F0} ");
			if (item.BluntDamage > 0) detail.Append($"钝{item.BluntDamage:F0} ");
			detail.AppendLine();
		}

		if (item.SharpArmor > 0 || item.BluntArmor > 0)
		{
			detail.Append("[color=#6699ff]护甲:[/color] ");
			if (item.SharpArmor > 0) detail.Append($"锐防{item.SharpArmor:F0} ");
			if (item.BluntArmor > 0) detail.Append($"钝防{item.BluntArmor:F0} ");
			detail.AppendLine();
		}

		detail.Append($"[color=#888888]重量: {item.EffectiveWeight:F1}kg  价格: {item.Price}G[/color]");
		_detailBox.AppendText(detail.ToString());
	}

	private static string FormatInlineStats(Item item)
	{
		var parts = new List<string>();
		if (item.SharpDamage > 0) parts.Add($"锐{item.SharpDamage:F0}");
		if (item.BluntDamage > 0) parts.Add($"钝{item.BluntDamage:F0}");
		if (item.SharpArmor > 0) parts.Add($"锐防{item.SharpArmor:F0}");
		if (item.BluntArmor > 0) parts.Add($"钝防{item.BluntArmor:F0}");
		return string.Join(" ", parts);
	}
}
