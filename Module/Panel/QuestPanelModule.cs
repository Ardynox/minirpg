using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

public enum QuestTab { Active, Completed, Failed }

/// <summary>
/// 任务面板：左栏任务列表，右栏任务详情。
/// 支持 Active / Completed / Failed 三个分类 Tab。
/// </summary>
public class QuestPanelModule : IPanel
{
	public string PanelId => "quest";
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
		}
		return false;
	}

	void IPanel.OnBlur() => Close();

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly HBoxContainer _tabBar;
	private readonly ScrollContainer _leftScroll;
	private readonly VBoxContainer _questList;
	private readonly RichTextLabel _detailText;
	private readonly RichTextLabel _hintBar;

	private readonly List<Button> _tabButtons = [];
	private readonly List<Button> _rows = [];
	private List<Quest> _filtered = [];
	private int _cursor;
	private int _hoverIndex = -1;
	private QuestTab _currentTab = QuestTab.Active;
	private GameState? _state;

	private static readonly QuestTab[] Tabs = [QuestTab.Active, QuestTab.Completed, QuestTab.Failed];
	private static readonly string[] TabLabels = ["进行中", "已完成", "已失败"];

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public QuestPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		_tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		var content = vbox.GetNode<HBoxContainer>("Content");
		_leftScroll = content.GetNode<ScrollContainer>("LeftColumn");
		_questList = _leftScroll.GetNode<VBoxContainer>("QuestList");
		_detailText = content.GetNode("RightColumn").GetNode<RichTextLabel>("DetailText");
		_hintBar = vbox.GetNode<RichTextLabel>("HintBar");

		BuildTabButtons();
		_hintBar.Clear();
		_hintBar.AppendText("[color=#666666]↑↓选择任务 ←→切换分类 J/Esc关闭[/color]");
	}

	private void BuildTabButtons()
	{
		for (var i = 0; i < TabLabels.Length; i++)
		{
			var btn = new Button
			{
				Text = TabLabels[i],
				ToggleMode = true,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
				FocusMode = Control.FocusModeEnum.None,
			};
			var tab = Tabs[i];
			btn.Pressed += () => SetTab(tab);
			_tabBar.AddChild(btn);
			_tabButtons.Add(btn);
		}
	}

	public void Open(GameState state)
	{
		_state = state;
		_currentTab = QuestTab.Active;
		_cursor = 0;
		Refresh();
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
		_state = null;
	}

	public void Refresh()
	{
		if (_state == null) return;
		FilterQuests();
		RenderHeader();
		UpdateTabHighlight();
		RebuildRows();
		UpdateRowVisuals();
		RenderDetail();
	}

	public void SetTab(QuestTab tab)
	{
		_currentTab = tab;
		_cursor = 0;
		Refresh();
	}

	public void CycleTab(int dir)
	{
		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
	}

	public void MoveCursor(int delta)
	{
		if (_filtered.Count == 0) return;
		_cursor = Math.Clamp(_cursor + delta, 0, _filtered.Count - 1);
		UpdateRowVisuals();
		RenderDetail();
		if (_cursor >= 0 && _cursor < _rows.Count)
			RowStyleHelper.EnsureVisible(_leftScroll, _rows[_cursor]);
	}

	private void FilterQuests()
	{
		if (_state == null) { _filtered = []; return; }
		var target = _currentTab switch
		{
			QuestTab.Active => QuestStatus.Active,
			QuestTab.Completed => QuestStatus.Completed,
			QuestTab.Failed => QuestStatus.Failed,
			_ => QuestStatus.Active,
		};
		_filtered = _state.Quests.FindAll(q => q.Status == target);

		if (_currentTab == QuestTab.Active)
			_filtered.Sort((a, b) => b.AcceptedTurn.CompareTo(a.AcceptedTurn));
		else
			_filtered.Sort((a, b) => b.FinishedTurn.CompareTo(a.FinishedTurn));

		if (_cursor >= _filtered.Count)
			_cursor = Math.Max(0, _filtered.Count - 1);
	}

	private void RenderHeader()
	{
		if (_state == null) return;
		var active = _state.Quests.FindAll(q => q.Status == QuestStatus.Active).Count;
		var done = _state.Quests.FindAll(q => q.Status == QuestStatus.Completed).Count;
		_header.Clear();
		_header.AppendText(
			$"[center]── 任务日志 ──[/center]\n" +
			$"[color=#66ff88]进行中: {active}[/color]  " +
			$"[color=#888888]已完成: {done}[/color]  " +
			$"[color=#888888]总计: {_state.Quests.Count}[/color]");
	}

	private void RebuildRows()
	{
		var count = _filtered.Count;
		var needed = Math.Max(count, 1);

		while (_rows.Count > needed)
		{
			_rows[^1].QueueFree();
			_rows.RemoveAt(_rows.Count - 1);
		}
		while (_rows.Count < needed)
		{
			var row = CreateRow(_rows.Count);
			_questList.AddChild(row);
			_rows.Add(row);
		}

		if (count == 0)
		{
			_rows[0].Text = _currentTab switch
			{
				QuestTab.Active => "  暂无进行中的任务",
				QuestTab.Completed => "  暂无已完成的任务",
				_ => "  暂无已失败的任务",
			};
			_rows[0].Disabled = true;
			_rows[0].AddThemeColorOverride("font_disabled_color", UIColors.TextDim);
		}
		else
		{
			for (var i = 0; i < count; i++)
			{
				var q = _filtered[i];
				var icon = q.Status switch
				{
					QuestStatus.Active => "◆",
					QuestStatus.Completed => "✓",
					_ => "✗",
				};
				var progress = "";
				if (q.Status == QuestStatus.Active && q.Objectives.Count > 0)
				{
					var doneCount = q.Objectives.FindAll(o => o.Done).Count;
					progress = $" ({doneCount}/{q.Objectives.Count})";
				}
				_rows[i].Text = $" {icon} {q.Title}{progress}";
				_rows[i].Disabled = false;
			}
		}
	}

	private Button CreateRow(int index)
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
		row.Pressed += () => { _cursor = idx; UpdateRowVisuals(); RenderDetail(); };
		row.MouseEntered += () => { _hoverIndex = idx; UpdateRowVisuals(); };
		row.MouseExited += () => { if (_hoverIndex == idx) _hoverIndex = -1; UpdateRowVisuals(); };

		return row;
	}

	private void UpdateRowVisuals()
	{
		var count = _filtered.Count;
		for (var i = 0; i < _rows.Count && i < count; i++)
			RowStyleHelper.Apply(_rows[i], i == _cursor, i == _hoverIndex, transparentBg: true);
	}

	private void UpdateTabHighlight()
	{
		for (var i = 0; i < _tabButtons.Count; i++)
			_tabButtons[i].ButtonPressed = Tabs[i] == _currentTab;
	}

	private void RenderDetail()
	{
		_detailText.Clear();

		if (_filtered.Count == 0 || _cursor < 0 || _cursor >= _filtered.Count)
		{
			_detailText.AppendText("[color=#888888]选择一个任务查看详情[/color]");
			return;
		}

		var q = _filtered[_cursor];
		var sb = new StringBuilder();

		var statusColor = q.Status switch
		{
			QuestStatus.Active => "#66ff88",
			QuestStatus.Completed => "#88ccff",
			_ => "#ff6666",
		};
		var statusText = q.Status switch
		{
			QuestStatus.Active => "进行中",
			QuestStatus.Completed => "已完成",
			_ => "已失败",
		};

		sb.AppendLine($"[b]{q.Title}[/b]");
		sb.AppendLine($"[color={statusColor}]{statusText}[/color]");
		sb.AppendLine();

		if (!string.IsNullOrEmpty(q.Source))
			sb.AppendLine($"[color=#aaaaaa]来源: {q.Source}[/color]");
		sb.AppendLine($"[color=#aaaaaa]接取回合: {q.AcceptedTurn}[/color]");
		if (q.FinishedTurn > 0)
			sb.AppendLine($"[color=#aaaaaa]结束回合: {q.FinishedTurn}[/color]");
		sb.AppendLine();

		if (!string.IsNullOrEmpty(q.Description))
		{
			sb.AppendLine(q.Description);
			sb.AppendLine();
		}

		if (q.Objectives.Count > 0)
		{
			sb.AppendLine("[color=#ffcc00]─── 目标 ───[/color]");
			foreach (var obj in q.Objectives)
			{
				var check = obj.Done ? "[color=#66ff88]✓[/color]" : "[color=#888888]○[/color]";
				var progress = obj.Target > 1 ? $" ({obj.Current}/{obj.Target})" : "";
				sb.AppendLine($"  {check} {obj.Text}{progress}");
			}
		}

		_detailText.AppendText(sb.ToString());
	}
}
