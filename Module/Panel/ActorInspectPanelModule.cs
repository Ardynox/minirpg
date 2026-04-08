using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

public sealed class ActorInspectPanelModule : IPanel
{
	public string PanelId => "actor_inspect";
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
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
				CycleTab(-1);
				return true;
			case "right" or "tab_next":
				CycleTab(1);
				return true;
			case "confirm":
				RequestClose();
				return true;
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
	private readonly Label _tabLabel;
	private readonly Label _hintLabel;
	private readonly RichTextLabel _contentText;
	private readonly List<string> _lines = [];

	private StatusTab _currentTab = StatusTab.Limb;
	private int _cursor;
	private GameState? _cachedState;
	private Actor? _cachedActor;

	public bool Dirty { get; set; }

	public ActorInspectPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_nameInfo = vbox.GetNode<Label>("HeaderBar/NameInfo");
		_tabLabel = vbox.GetNode<Label>("TabLabel");
		_contentText = vbox.GetNode<RichTextLabel>("ContentText");
		_hintLabel = vbox.GetNode<Label>("HintBar");
		UpdateTabLabel();
	}

	public void Open(GameState state, Actor actor)
	{
		_panel.Visible = true;
		Refresh(state, actor);
	}

	public void Close()
	{
		_panel.Visible = false;
		_cachedState = null;
		_cachedActor = null;
		_lines.Clear();
	}

	public void Refresh(GameState state, Actor? actor)
	{
		_cachedState = state;
		_cachedActor = actor;
		Dirty = false;
		if (actor == null)
		{
			_nameInfo.Text = string.Empty;
			_contentText.Clear();
			_lines.Clear();
			RefreshHint();
			return;
		}

		_nameInfo.Text = ActorStatusTextBuilder.BuildInspectHeader(state, actor);
		UpdateTabLabel();
		BuildLines();
		RefreshHint();
		RenderContent();
	}

	public void FlushIfDirty()
	{
		if (!Dirty)
			return;

		Dirty = false;
		if (_cachedState != null)
			Refresh(_cachedState, _cachedActor);
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

	private void CycleTab(int delta)
	{
		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + delta + Tabs.Length) % Tabs.Length;
		_currentTab = Tabs[idx];
		_cursor = 0;
		UpdateTabLabel();
		BuildLines();
		RenderContent();
	}

	private void MoveCursor(int delta)
	{
		if (_lines.Count == 0)
			return;

		var prev = _cursor;
		_cursor = Math.Clamp(_cursor + delta, 0, _lines.Count - 1);
		if (_cursor != prev)
			RenderContent();
	}

	private void BuildLines()
	{
		_lines.Clear();
		if (_cachedState == null || _cachedActor == null)
			return;

		ActorStatusTextBuilder.BuildLines(_lines, _currentTab, _cachedState, _cachedActor);
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
				sb.Append($"[color=#99ffaa]鈻?{_lines[i]}[/color]");
			else
				sb.Append($"  {_lines[i]}");
		}

		_contentText.AppendText(sb.ToString());
	}

	private void RefreshHint()
	{
		_hintLabel.Text = LocalizationService.T("ui.actor_inspect.hint.default");
	}

	private void RequestClose()
	{
		if (CloseRequested != null)
			CloseRequested.Invoke();
		else
			Close();
	}
}
