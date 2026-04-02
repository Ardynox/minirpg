using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

public enum SkillTab { All, Combat, Utility, Social }

/// <summary>
/// 技能管理面板：左栏技能列表（按分类 Tab），右栏选中技能的完整详情。
/// 实现 IPanel，可聚焦，K 键切换开关。
/// </summary>
public class SkillManagerModule : IPanel
{
	public string PanelId => "skill_mgr";
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
	private readonly VBoxContainer _skillList;
	private readonly RichTextLabel _detailText;
	private readonly RichTextLabel _hintBar;

	private readonly List<Button> _tabButtons = [];
	private readonly List<Button> _rows = [];
	private List<InteractionDef> _filtered = [];
	private List<InteractionDef> _allSkills = [];
	private int _cursor;
	private int _hoverIndex = -1;
	private SkillTab _currentTab = SkillTab.All;
	private Actor? _player;

	private static readonly SkillTab[] Tabs = [SkillTab.All, SkillTab.Combat, SkillTab.Utility, SkillTab.Social];
	private static readonly string[] TabLabels = ["全部", "战斗", "工具", "社交"];

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public SkillManagerModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		_tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		var content = vbox.GetNode<HBoxContainer>("Content");
		_leftScroll = content.GetNode<ScrollContainer>("LeftColumn");
		_skillList = _leftScroll.GetNode<VBoxContainer>("SkillList");
		_detailText = content.GetNode("RightColumn").GetNode<RichTextLabel>("DetailText");
		_hintBar = vbox.GetNode<RichTextLabel>("HintBar");

		BuildTabButtons();
		_hintBar.Clear();
		_hintBar.AppendText("[color=#666666]↑↓选择 ←→分类 K/Esc关闭[/color]");
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

	public void Open(Actor? player)
	{
		_player = player;
		_currentTab = SkillTab.All;
		_cursor = 0;
		_allSkills = player != null ? SkillQuery.GetSkills(player) : [];
		Refresh();
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
		_player = null;
	}

	private void Refresh()
	{
		FilterSkills();
		RenderHeader();
		UpdateTabHighlight();
		RebuildRows();
		UpdateRowVisuals();
		RenderDetail();
	}

	public void SetTab(SkillTab tab)
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

	private void FilterSkills()
	{
		if (_currentTab == SkillTab.All)
		{
			_filtered = [.. _allSkills];
		}
		else
		{
			var cat = _currentTab switch
			{
				SkillTab.Combat => "combat",
				SkillTab.Utility => "utility",
				SkillTab.Social => "social",
				_ => "",
			};
			_filtered = _allSkills.FindAll(s => s.Category == cat);
		}

		if (_cursor >= _filtered.Count)
			_cursor = Math.Max(0, _filtered.Count - 1);
	}

	private void RenderHeader()
	{
		var combat = _allSkills.FindAll(s => s.Category == "combat").Count;
		var utility = _allSkills.FindAll(s => s.Category == "utility").Count;
		var social = _allSkills.FindAll(s => s.Category == "social").Count;
		_header.Clear();
		_header.AppendText(
			$"[center]── 技能管理 ──[/center]\n" +
			$"[color=#ff6666]战斗: {combat}[/color]  " +
			$"[color=#66ccff]工具: {utility}[/color]  " +
			$"[color=#66ff88]社交: {social}[/color]  " +
			$"[color=#888888]共: {_allSkills.Count}[/color]");
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
			_skillList.AddChild(row);
			_rows.Add(row);
		}

		if (count == 0)
		{
			_rows[0].Text = "  暂无技能";
			_rows[0].Disabled = true;
			_rows[0].AddThemeColorOverride("font_disabled_color", UIColors.TextDim);
		}
		else
		{
			for (var i = 0; i < count; i++)
			{
				var skill = _filtered[i];
				var icon = skill.Category switch
				{
					"combat" => "⚔",
					"utility" => "🔧",
					"social" => "💬",
					_ => "◆",
				};
				var power = skill.Power > 0 ? $" [{skill.Power}]" : "";
				_rows[i].Text = $" {icon} {skill.Name}{power}";
				_rows[i].Disabled = false;

				var color = skill.Category switch
				{
					"combat" => new Color(1f, 0.4f, 0.4f),
					"utility" => new Color(0.4f, 0.8f, 1f),
					"social" => new Color(0.4f, 1f, 0.53f),
					_ => UIColors.TextNormal,
				};
				_rows[i].AddThemeColorOverride("font_color", color);
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
			_detailText.AppendText("[color=#888888]选择一个技能查看详情[/color]");
			return;
		}

		var skill = _filtered[_cursor];
		var sb = new StringBuilder();

		var color = skill.Category switch
		{
			"combat" => "#ff6666",
			"utility" => "#66ccff",
			"social" => "#66ff88",
			_ => "#cccccc",
		};
		var catLabel = skill.Category switch
		{
			"combat" => "战斗",
			"utility" => "工具",
			"social" => "社交",
			_ => skill.Category,
		};

		sb.AppendLine($"[b][color={color}]{skill.Name}[/color][/b]");
		sb.AppendLine($"[color=#888888]{catLabel}技能[/color]");
		sb.AppendLine();

		if (skill.Power > 0)
			sb.AppendLine($"[color=#ffcc00]威力: {skill.Power}[/color]");
		if (skill.Cooldown > 0)
			sb.AppendLine($"[color=#aaaaaa]冷却: {skill.Cooldown} 回合[/color]");
		if (skill.Range > 1)
			sb.AppendLine($"[color=#aaaaaa]射程: {skill.Range}[/color]");
		else if (skill.Range == 0)
			sb.AppendLine("[color=#aaaaaa]射程: 自身[/color]");

		if (!string.IsNullOrEmpty(skill.DamageType))
		{
			var dmgLabel = skill.DamageType switch
			{
				"sharp" => "锐伤",
				"blunt" => "钝伤",
				"poison" => "毒伤",
				_ => skill.DamageType,
			};
			sb.AppendLine($"[color=#cc8866]伤害类型: {dmgLabel}[/color]");
		}

		if (!string.IsNullOrEmpty(skill.EffectType))
		{
			var effectLabel = skill.EffectType switch
			{
				"melee_attack" => "近战攻击",
				"heavy_attack" => "重击",
				"poison_attack" => "毒攻击",
				"drain_attack" => "吸血攻击",
				"block" => "格挡",
				"dig" => "挖掘",
				"trade" => "交易",
				"talk" => "对话",
				"tame" => "驯服",
				"combat" => "战斗",
				_ => skill.EffectType,
			};
			sb.AppendLine($"[color=#aaaaaa]效果: {effectLabel}[/color]");
		}

		sb.AppendLine();

		if (!string.IsNullOrEmpty(skill.Description))
		{
			sb.AppendLine(skill.Description);
			sb.AppendLine();
		}

		if (skill.Required.Count > 0)
		{
			sb.AppendLine("[color=#ffcc00]─── 需求条件 ───[/color]");
			foreach (var (k, v) in skill.Required)
				sb.AppendLine($"  [color=#aaaaaa]{k} ≥ {v}[/color]");
		}
		if (skill.CapacityRequired.Count > 0)
		{
			sb.AppendLine("[color=#ffcc00]─── 能力需求 ───[/color]");
			foreach (var (k, v) in skill.CapacityRequired)
			{
				var def = PresetDB.GetCapacity(k);
				var name = def?.Name ?? k;
				sb.AppendLine($"  [color=#aaaaaa]{name} ≥ {v * 100:F0}%[/color]");
			}
		}

		var source = GetSkillSource(skill);
		if (!string.IsNullOrEmpty(source))
		{
			sb.AppendLine();
			sb.AppendLine($"[color=#666666]来源: {source}[/color]");
		}

		_detailText.AppendText(sb.ToString());
	}

	private string GetSkillSource(InteractionDef skill)
	{
		if (_player == null) return "";
		foreach (var item in _player.Inventory)
		{
			if (item.Equipped && item.GrantedSkills.Contains(skill.Id))
				return $"装备「{item.Name}」";
		}
		return "自身能力";
	}
}
