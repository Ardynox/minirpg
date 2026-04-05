using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

public enum QuestTab { Active, Completed, Failed }

public class QuestPanelModule : ListPanelBase
{
	public override string PanelId => "quest";
	public override PanelContainer PanelNode => _panel;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly RichTextLabel _detailText;
	private readonly List<Button> _tabButtons;
	private List<Quest> _filtered = [];
	private QuestTab _currentTab = QuestTab.Active;
	private GameState? _state;

	private static readonly QuestTab[] Tabs = [QuestTab.Active, QuestTab.Completed, QuestTab.Failed];
	private static readonly string[] TabLabels = ["进行中", "已完成", "已失败"];

	public override bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public override bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1, _filtered.Count); return true;
			case "down": MoveCursor(1, _filtered.Count); return true;
			case "left" or "tab_prev": CycleTab(-1); return true;
			case "right" or "tab_next": CycleTab(1); return true;
			case "close": Close(); return true;
		}
		return false;
	}
	public override void OnBlur() { }

	public QuestPanelModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		var tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		var content = vbox.GetNode<HBoxContainer>("Content");
		var leftScroll = content.GetNode<ScrollContainer>("LeftColumn");
		var questList = leftScroll.GetNode<VBoxContainer>("QuestList");
		BindListNodes(leftScroll, questList);
		_detailText = content.GetNode("RightColumn").GetNode<RichTextLabel>("DetailText");
		_tabButtons = TabHelper.BuildTabButtons(tabBar, TabLabels, Tabs, SetTab);
	}

	public void Open(GameState state)
	{
		_state = state;
		_currentTab = QuestTab.Active;
		_cursor = 0;
		Refresh();
		Visible = true;
	}

	public void Close() { Visible = false; _state = null; }

	public override void Refresh()
	{
		if (_state == null) return;
		FilterQuests();
		RenderHeader();
		TabHelper.UpdateTabHighlight(_tabButtons, Tabs, _currentTab);
		RebuildRows(_filtered.Count, ApplyRowContent, _currentTab switch
		{
			QuestTab.Active => "  暂无进行中的任务",
			QuestTab.Completed => "  暂无已完成的任务",
			_ => "  暂无已失败的任务",
		});
		OnSelectionChanged();
	}

	public void SetTab(QuestTab tab) { _currentTab = tab; _cursor = 0; Refresh(); }
	public void CycleTab(int dir) { var idx = Array.IndexOf(Tabs, _currentTab); idx = (idx + dir + Tabs.Length) % Tabs.Length; SetTab(Tabs[idx]); }

	protected override int GetRowDataCount() => _filtered.Count;
	protected override void OnSelectionChanged() { UpdateRowVisuals(_filtered.Count); RenderDetail(); }

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
		if (_currentTab == QuestTab.Active) _filtered.Sort((a, b) => b.AcceptedTurn.CompareTo(a.AcceptedTurn));
		else _filtered.Sort((a, b) => b.FinishedTurn.CompareTo(a.FinishedTurn));
	}

	private void RenderHeader()
	{
		if (_state == null) return;
		var active = _state.Quests.FindAll(q => q.Status == QuestStatus.Active).Count;
		var done = _state.Quests.FindAll(q => q.Status == QuestStatus.Completed).Count;
		_header.Clear();
		_header.AppendText($"[center]── 任务日志 ──[/center]\n" +
			$"[color=#66ff88]进行中: {active}[/color]  [color=#888888]已完成: {done}[/color]  [color=#888888]总计: {_state.Quests.Count}[/color]");
	}

	private void ApplyRowContent(Button row, int i)
	{
		var q = _filtered[i];
		var icon = q.Status switch { QuestStatus.Active => "◆", QuestStatus.Completed => "✓", _ => "✗" };
		var progress = "";
		if (q.Status == QuestStatus.Active && q.Objectives.Count > 0)
		{
			var doneCount = q.Objectives.FindAll(o => o.Done).Count;
			progress = $" ({doneCount}/{q.Objectives.Count})";
		}
		row.Text = $" {icon} {q.Title}{progress}";
	}

	private void RenderDetail()
	{
		_detailText.Clear();
		if (_filtered.Count == 0 || _cursor < 0 || _cursor >= _filtered.Count) { _detailText.AppendText("[color=#888888]选择一个任务查看详情[/color]"); return; }
		var q = _filtered[_cursor];
		var sb = new StringBuilder();
		var statusColor = q.Status switch { QuestStatus.Active => "#66ff88", QuestStatus.Completed => "#88ccff", _ => "#ff6666" };
		var statusText = q.Status switch { QuestStatus.Active => "进行中", QuestStatus.Completed => "已完成", _ => "已失败" };
		sb.AppendLine($"[b]{q.Title}[/b]");
		sb.AppendLine($"[color={statusColor}]{statusText}[/color]");
		sb.AppendLine();
		if (!string.IsNullOrEmpty(q.Source)) sb.AppendLine($"[color=#aaaaaa]来源: {q.Source}[/color]");
		sb.AppendLine($"[color=#aaaaaa]接取回合: {q.AcceptedTurn}[/color]");
		if (q.FinishedTurn > 0) sb.AppendLine($"[color=#aaaaaa]结束回合: {q.FinishedTurn}[/color]");
		sb.AppendLine();
		if (!string.IsNullOrEmpty(q.Description)) { sb.AppendLine(q.Description); sb.AppendLine(); }
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
