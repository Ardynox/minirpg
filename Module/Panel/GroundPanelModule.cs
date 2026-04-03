using System;
using System.Collections.Generic;
using Godot;
namespace MiniRPG.Module.Panel;

/// <summary>
/// 地面物品面板：用单个 RichTextLabel 渲染物品列表，
/// 光标选中用 ▶ 前缀 + 颜色高亮，容器物品用蓝色标注。
/// </summary>
public class GroundPanelModule : IPanel
{
	public string PanelId => "ground";
	public PanelContainer PanelNode => _panel;
	bool IPanel.Visible { get => _panel.Visible; set => _panel.Visible = value; }
	bool IPanel.CanFocus => false;
	bool IPanel.HandleCommand(string cmd) => false;

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
	private readonly Label _header;
	private readonly RichTextLabel _contentText;
	private readonly Button _pickupBtn;
	private readonly Button _pickupAllBtn;
	private readonly IHost _host;

	private List<Item> _groundItems = [];
	private int _cursor;
	private int _cachedX = int.MinValue;
	private int _cachedY = int.MinValue;
	private bool _dirty = true;

	public bool Dirty { get; set; }

	public void FlushIfDirty()
	{
		if (!Dirty) return;
		Dirty = false;
		Refresh();
	}

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
		_header = vbox.GetNode<Label>("Header");
		_contentText = vbox.GetNode<RichTextLabel>("ContentText");
		var actionBar = vbox.GetNode<HBoxContainer>("ActionBar");
		_pickupBtn = actionBar.GetNode<Button>("PickupBtn");
		_pickupAllBtn = actionBar.GetNode<Button>("PickupAllBtn");

		_pickupBtn.FocusMode = Control.FocusModeEnum.None;
		_pickupAllBtn.FocusMode = Control.FocusModeEnum.None;
		_pickupBtn.Pressed += () => DoPickupSelected();
		_pickupAllBtn.Pressed += () => DoPickupAll();
	}

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

		RenderHeader();
		RenderContent();
		UpdateActionButtons();
	}

	public void MoveCursor(int delta)
	{
		if (_groundItems.Count == 0) return;
		var prev = _cursor;
		_cursor = Math.Clamp(_cursor + delta, 0, _groundItems.Count - 1);
		if (_cursor != prev)
		{
			RenderContent();
			UpdateActionButtons();
		}
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
		if (_groundItems.Count == 0)
		{
			_header.Text = "── 脚下 ── (空)";
			_header.ThemeTypeVariation = "HintLabel";
		}
		else
		{
			_header.Text = $"── 脚下 ── ({_groundItems.Count}件)";
			_header.ThemeTypeVariation = "";
		}
	}

	private void RenderContent()
	{
		_contentText.Clear();
		if (_groundItems.Count == 0) return;

		var sb = new System.Text.StringBuilder();
		for (var i = 0; i < _groundItems.Count; i++)
		{
			if (i > 0) sb.Append('\n');
			var item = _groundItems[i];
			var icon = item.IsContainer ? "📦 " : "· ";
			var nameText = item.IsContainer
				? $"{item.Name} ({item.Contents?.Count ?? 0}件)"
				: item.Name;
			var line = $"{icon}{nameText}  {item.EffectiveWeight:F1}kg";

			if (i == _cursor)
				sb.Append($"[color=#99ffaa]▶ {line}[/color]");
			else if (item.IsContainer)
				sb.Append($"[color=#99ddff]  {line}[/color]");
			else
				sb.Append($"  {line}");
		}
		_contentText.AppendText(sb.ToString());
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
