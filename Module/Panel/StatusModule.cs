using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

public enum StatusTab { Limb, Capacity, Tag, Buff, Equip, Needs, Health }

public class StatusPanelModule : IPanel
{
	public string PanelId => "status";
	public PanelContainer PanelNode => _panel;
	bool IPanel.Visible { get => _panel.Visible; set => _panel.Visible = value; }

	bool IPanel.HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1); return true;
			case "down": MoveCursor(1); return true;
			case "left" or "tab_prev": CycleTab(-1); return true;
			case "right" or "tab_next": CycleTab(1); return true;
			case "close": _panel.Visible = false; return true;
		}

		return false;
	}

	private static readonly StatusTab[] Tabs =
		[StatusTab.Limb, StatusTab.Capacity, StatusTab.Tag, StatusTab.Buff, StatusTab.Equip, StatusTab.Needs, StatusTab.Health];

	private readonly PanelContainer _panel;
	private readonly Label _nameInfo;
	private readonly Label _tabLabel;
	private readonly RichTextLabel _contentText;
	private readonly List<string> _lines = [];

	private StatusTab _currentTab = StatusTab.Limb;
	private int _cursor;
	private GameState? _cachedState;
	private Actor? _cachedPlayer;

	public bool Dirty { get; set; }
	private int _cachedFloor;
	private int _cachedTurn;

	public StatusPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_nameInfo = vbox.GetNode<Label>("HeaderBar/NameInfo");
		_tabLabel = vbox.GetNode<Label>("TabLabel");
		_contentText = vbox.GetNode<RichTextLabel>("ContentText");
		UpdateTabLabel();
	}

	public void FlushIfDirty()
	{
		if (!Dirty)
			return;

		Dirty = false;
		if (_cachedState != null)
			Refresh(_cachedState, _cachedPlayer, _cachedFloor, _cachedTurn);
	}

	public void SetTab(StatusTab tab)
	{
		_currentTab = tab;
		_cursor = 0;
		UpdateTabLabel();
		RenderContent();
	}

	public void CycleTab(int dir)
	{
		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
	}

	public void HandleCommand(string cmd, Action? onClose = null)
	{
		switch (cmd)
		{
			case "up":
				MoveCursor(-1);
				break;
			case "down":
				MoveCursor(1);
				break;
			case "prev":
				CycleTab(-1);
				break;
			case "next":
				CycleTab(1);
				break;
			case "close":
				_panel.Visible = false;
				onClose?.Invoke();
				break;
		}
	}

	public void MoveCursor(int delta)
	{
		if (_lines.Count == 0)
			return;

		var prev = _cursor;
		_cursor = Math.Clamp(_cursor + delta, 0, _lines.Count - 1);
		if (_cursor != prev)
			RenderContent();
	}

	public void Refresh(GameState state, Actor? player, int floor, int turn = 0)
	{
		_cachedState = state;
		_cachedPlayer = player;
		_cachedFloor = floor;
		_cachedTurn = turn;
		Dirty = false;
		if (player == null)
		{
			_nameInfo.Text = string.Empty;
			_contentText.Clear();
			_lines.Clear();
			return;
		}

		_nameInfo.Text = ActorStatusTextBuilder.BuildPlayerHeader(state, player, floor, turn);
		UpdateTabLabel();
		BuildLines();
		RenderContent();
	}

	private void UpdateTabLabel()
	{
		var sb = new StringBuilder();
		for (var i = 0; i < Tabs.Length; i++)
		{
			if (i > 0)
				sb.Append("  ");

			var label = ActorStatusTextBuilder.GetTabLabel(Tabs[i]);
			sb.Append(Tabs[i] == _currentTab ? $"[{label}]" : label);
		}

		_tabLabel.Text = sb.ToString();
	}

	private void BuildLines()
	{
		_lines.Clear();
		if (_cachedPlayer == null)
			return;

		if (_cachedState == null)
		{
			ActorStatusTextBuilder.BuildLines(_lines, _currentTab, _cachedPlayer);
		}
		else
		{
			ActorStatusTextBuilder.BuildLines(_lines, _currentTab, _cachedState, _cachedPlayer);
		}
		if (_cursor >= _lines.Count)
			_cursor = Math.Max(0, _lines.Count - 1);
	}

	private void RenderContent()
	{
		_contentText.Clear();
		if (_lines.Count == 0)
			return;

		var sb = new StringBuilder();
		for (var i = 0; i < _lines.Count; i++)
		{
			if (i > 0)
				sb.Append('\n');

			if (i == _cursor)
				sb.Append($"[color={UIColors.HexSelected}]鈻?{_lines[i]}[/color]");
			else
				sb.Append($"  {_lines[i]}");
		}

		_contentText.AppendText(sb.ToString());
	}
}
