using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;

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
		void TakeChestItem(Item chestItem, int itemIndex);
		void TakeAllChestItems(Item chestItem);
		void CloseChestPanel();
		void OpenPutIntoChestSelection(Item chestItem);
		void PersistChestItem(Item chestItem);
		bool TryHandleItemRightClick(Item item);
	}

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly RichTextLabel _detailBox;
	private readonly Button _takeBtn;
	private readonly Button _takeAllBtn;
	private readonly Button _putBtn;
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
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		var itemScroll = vbox.GetNode<ScrollContainer>("ItemScroll");
		var itemList = itemScroll.GetNode<VBoxContainer>("ItemList");
		BindListNodes(itemScroll, itemList);
		_detailBox = vbox.GetNode<RichTextLabel>("DetailBox");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_takeBtn = actionBar.GetNode<Button>("TakeBtn");
		_takeAllBtn = actionBar.GetNode<Button>("TakeAllBtn");
		_putBtn = actionBar.GetNode<Button>("PutBtn");
		var closeBtn = actionBar.GetNode<Button>("CloseBtn");

		_takeBtn.FocusMode = Control.FocusModeEnum.None;
		_takeAllBtn.FocusMode = Control.FocusModeEnum.None;
		_putBtn.FocusMode = Control.FocusModeEnum.None;
		closeBtn.FocusMode = Control.FocusModeEnum.None;

		_takeBtn.Pressed += () => TryTake();
		_takeAllBtn.Pressed += () => TryTakeAll();
		_putBtn.Pressed += () => TryPut();
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
		RebuildRows(count, ApplyRowContent, LocalizationService.T("ui.common.empty_inline"));
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
		var stats = ItemFormatHelper.InlineStats(_host.State, item);
		var weight = ItemFormatHelper.BuildWeight(_host.State, item);
		var statSegment = string.IsNullOrWhiteSpace(stats) ? string.Empty : $"  {stats}";
		var weightSegment = string.IsNullOrWhiteSpace(weight) ? string.Empty : $"  {weight}";
		row.Text = $"{ItemFormatHelper.GetDisplayName(_host.State, item)}{statSegment}{weightSegment}";
	}

	protected override void HandleRowGuiInput(InputEvent ev, int index)
	{
		if (ev is not InputEventMouseButton mb || !mb.Pressed || index < 0 || index >= GetRowDataCount())
			return;

		if (mb.ButtonIndex != MouseButton.Right)
			return;

		_cursor = index;
		OnSelectionChanged();
		if (_host.TryHandleItemRightClick(_chestItem!.Contents![index]))
		{
			Refresh();
			_host.FlushMap();
		}
	}

	public void TryTake()
	{
		if (_chestItem?.Contents == null) return;
		if (_cursor < 0 || _cursor >= _chestItem.Contents.Count) return;
		_host.TakeChestItem(_chestItem, _cursor);
		Refresh();
		_host.FlushMap();
	}

	public void TryTakeAll()
	{
		if (_chestItem?.Contents == null) return;
		_host.TakeAllChestItems(_chestItem);
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
		var name = _chestItem == null
			? LocalizationService.T("ui.chest.default_name")
			: ItemFormatHelper.GetDisplayName(_host.State, _chestItem);
		_header.AppendText($"[center]{LocalizationService.T("ui.chest.header", ("name", name), ("count", count))}[/center]");
	}

	private void UpdateActionButtons()
	{
		var hasItems = GetRowDataCount() > 0;
		_takeBtn.Disabled = !hasItems;
		_takeAllBtn.Disabled = !hasItems;
		_takeBtn.Text = $"[E] {LocalizationService.T("ui.chest.take")}";
		_putBtn.Text = $"[P] {LocalizationService.T("ui.chest.put")}";
	}

	private void RenderDetail()
	{
		_detailBox.Clear();
		var contents = _chestItem?.Contents;
		if (contents == null || _cursor < 0 || _cursor >= contents.Count)
		{
			_detailBox.AppendText(LocalizationService.T("ui.common.detail_hint.item"));
			return;
		}
		_detailBox.AppendText(ItemFormatHelper.BuildDetail(_host.State, contents[_cursor]));
		_detailBox.AppendText($"\n[color=#666666]{LocalizationService.TOrFallback("ui.chest.detail_hint", "双击取出 | 右键更多操作")}[/color]");
	}
}
