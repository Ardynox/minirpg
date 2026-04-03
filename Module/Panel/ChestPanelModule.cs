using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Panel;

public class ChestPanelModule : ListPanelBase
{
	public override string PanelId => "chest";
	public override PanelContainer PanelNode => _panel;

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
	private readonly RichTextLabel _detailBox;
	private readonly Button _takeBtn;
	private readonly Button _takeAllBtn;
	private readonly IHost _host;

	private Item? _chestItem;

	public Item? CurrentChest => _chestItem;

	public override bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public override bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1, GetRowDataCount()); return true;
			case "down": MoveCursor(1, GetRowDataCount()); return true;
			case "action1": TryTake(); return true;
			case "action4": TryPut(); return true;
			case "close": _host.CloseChestPanel(); return true;
		}
		return false;
	}

	public override void OnBlur() { }

	public ChestPanelModule(PanelContainer panel, IHost host)
	{
		_panel = panel;
		_host = host;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		var itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		var itemList = itemScroll.GetNode<VBoxContainer>("ItemList");
		BindListNodes(itemScroll, itemList);
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_takeBtn = actionBar.GetNode<Button>("TakeBtn");
		_takeAllBtn = actionBar.GetNode<Button>("TakeAllBtn");
		var putBtn = actionBar.GetNode<Button>("PutBtn");
		var closeBtn = actionBar.GetNode<Button>("CloseBtn");

		_takeBtn.FocusMode = Control.FocusModeEnum.None;
		_takeAllBtn.FocusMode = Control.FocusModeEnum.None;
		putBtn.FocusMode = Control.FocusModeEnum.None;
		closeBtn.FocusMode = Control.FocusModeEnum.None;

		_takeBtn.Pressed += () => TryTake();
		_takeAllBtn.Pressed += () => TryTakeAll();
		putBtn.Pressed += () => TryPut();
		closeBtn.Pressed += () => _host.CloseChestPanel();
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

	public override void Refresh()
	{
		var count = GetRowDataCount();
		if (_cursor >= count) _cursor = Math.Max(0, count - 1);
		RebuildRows(count, ApplyRowContent, "  (空)");
		RenderHeader();
		OnSelectionChanged();
		UpdateActionButtons();
	}

	protected override int GetRowDataCount() => _chestItem?.Contents?.Count ?? 0;

	protected override void OnSelectionChanged()
	{
		UpdateRowVisuals(GetRowDataCount());
		RenderDetail();
		UpdateActionButtons();
	}

	protected override void OnRowPressed(int index)
	{
		if (index < 0 || index >= GetRowDataCount()) return;
		_cursor = index;
		TryTake();
	}

	private void ApplyRowContent(Button row, int i)
	{
		var item = _chestItem!.Contents![i];
		var stats = ItemFormatHelper.InlineStats(item);
		var weight = $" {item.EffectiveWeight:F1}kg";
		row.Text = $"{item.Name}  {stats}{weight}";
	}

	public void TryTake()
	{
		if (_chestItem?.Contents == null) return;
		if (_cursor < 0 || _cursor >= _chestItem.Contents.Count) return;
		var player = ActorModule.GetPlayer(_host.State);
		if (player == null) return;

		var item = _chestItem.Contents[_cursor];
		_chestItem.Contents.RemoveAt(_cursor);
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
		foreach (var item in _chestItem.Contents) InventoryModule.Add(player, item);
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

	private void UpdateActionButtons()
	{
		var hasItems = GetRowDataCount() > 0;
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
