using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public enum StatusTab { Limb, Capacity, Tag, Buff, Equip, Needs, Health }

public class StatusPanelModule : IPanel
{
	private enum StatusContentMode
	{
		Actor,
		Corpse,
	}

	public string PanelId { get; }
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	bool IPanel.Visible { get => Visible; set => Visible = value; }

	public event Action? CloseRequested;

	bool IPanel.HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up":
				MoveCursor(-1);
				return true;
			case "down":
				MoveCursor(1);
				return true;
			case "left" or "tab_prev":
				if (_contentMode == StatusContentMode.Actor)
				{
					CycleTab(-1);
					return true;
				}
				return false;
			case "right" or "tab_next":
				if (_contentMode == StatusContentMode.Actor)
				{
					CycleTab(1);
					return true;
				}
				return false;
			case "close":
				RequestClose();
				return true;
		}

		return false;
	}

	private static readonly StatusTab[] Tabs =
		[StatusTab.Limb, StatusTab.Capacity, StatusTab.Tag, StatusTab.Buff, StatusTab.Equip, StatusTab.Needs, StatusTab.Health];

	private readonly PanelContainer _panel;
	private readonly Label _nameInfo;
	private readonly HBoxContainer _tabBar;
	private readonly Label _hintBar;
	private readonly List<Button> _tabButtons;
	private readonly RichTextLabel _contentText;
	private readonly List<string> _lines = [];

	private StatusTab _currentTab = StatusTab.Limb;
	private int _cursor;
	private GameState? _cachedState;
	private Actor? _cachedActor;
	private Item? _cachedCorpse;
	private Vector3I _cachedCorpseCell;
	private int _cachedFloor;
	private int _cachedTurn;
	private bool _cachedUsePlayerHeader = true;
	private StatusContentMode _contentMode = StatusContentMode.Actor;

	public bool Dirty { get; set; }

	public StatusPanelModule(PanelContainer panel, string panelId = "status")
	{
		PanelId = panelId;
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_nameInfo = vbox.GetNode<Label>("HeaderBar/NameInfo");
		_tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		_contentText = vbox.GetNode<RichTextLabel>("ContentText");
		_hintBar = vbox.GetNode<Label>("HintBar");

		var tabLabels = new string[Tabs.Length];
		for (var i = 0; i < Tabs.Length; i++)
			tabLabels[i] = ActorStatusTextBuilder.GetTabLabel(Tabs[i]);

		_tabButtons = TabHelper.BuildTabButtons(_tabBar, tabLabels, Tabs, SetTab, panelId);
		UpdateTabHighlight();
	}

	public void FlushIfDirty()
	{
		if (!Dirty)
			return;

		Dirty = false;
		if (_cachedState == null)
			return;

		if (_contentMode == StatusContentMode.Corpse)
		{
			RefreshCorpse(_cachedState, _cachedCorpse, _cachedCorpseCell);
			return;
		}

		Refresh(_cachedState, _cachedActor, _cachedFloor, _cachedTurn, _cachedUsePlayerHeader);
	}

	public void SetTab(StatusTab tab)
	{
		if (_contentMode != StatusContentMode.Actor)
			return;

		_currentTab = tab;
		_cursor = 0;
		UpdateTabHighlight();
		BuildLines();
		RenderContent();
	}

	public void CycleTab(int dir)
	{
		if (_contentMode != StatusContentMode.Actor)
			return;

		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
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

	public void Refresh(GameState state, Actor? actor, int floor, int turn = 0, bool usePlayerHeader = true)
	{
		_cachedState = state;
		_cachedActor = actor;
		_cachedFloor = floor;
		_cachedTurn = turn;
		_cachedUsePlayerHeader = usePlayerHeader;
		_cachedCorpse = null;
		_contentMode = StatusContentMode.Actor;
		Dirty = false;
		_tabBar.Visible = true;
		_hintBar.Visible = true;
		if (actor == null)
		{
			ClearContent();
			return;
		}

		_nameInfo.Text = usePlayerHeader
			? ActorStatusTextBuilder.BuildPlayerHeader(state, actor, floor, turn)
			: ActorStatusTextBuilder.BuildInspectHeader(state, actor);
		UpdateTabHighlight();
		BuildLines();
		RenderContent();
	}

	public void RefreshCorpse(GameState state, Item? corpse, Vector3I cell)
	{
		_cachedState = state;
		_cachedCorpse = corpse;
		_cachedCorpseCell = cell;
		_cachedActor = null;
		_contentMode = StatusContentMode.Corpse;
		Dirty = false;
		_tabBar.Visible = false;
		_hintBar.Visible = false;
		if (corpse == null)
		{
			ClearContent();
			return;
		}

		_nameInfo.Text = ActorStatusTextBuilder.BuildCorpseHeader(state, corpse, cell);
		BuildLines();
		RenderContent();
	}

	private void UpdateTabHighlight()
	{
		for (var i = 0; i < _tabButtons.Count && i < Tabs.Length; i++)
			_tabButtons[i].Text = ActorStatusTextBuilder.GetTabLabel(Tabs[i]);

		TabHelper.UpdateTabHighlight(_tabButtons, Tabs, _currentTab);
	}

	private void BuildLines()
	{
		_lines.Clear();
		if (_contentMode == StatusContentMode.Corpse)
		{
			if (_cachedState != null && _cachedCorpse != null)
				ActorStatusTextBuilder.BuildCorpseLines(_lines, _cachedState, _cachedCorpse);
			return;
		}

		if (_cachedActor == null)
			return;

		if (_cachedState == null)
		{
			ActorStatusTextBuilder.BuildLines(_lines, _currentTab, _cachedActor);
		}
		else
		{
			ActorStatusTextBuilder.BuildLines(_lines, _currentTab, _cachedState, _cachedActor);
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

	private void ClearContent()
	{
		_nameInfo.Text = string.Empty;
		_contentText.Clear();
		_lines.Clear();
	}

	private void RequestClose()
	{
		if (CloseRequested != null)
			CloseRequested.Invoke();
		else
			_panel.Visible = false;
	}
}
