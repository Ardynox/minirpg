using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace MiniRPG.Module.Panel;

public enum SkillTab { All, Combat, Utility, Social }

public class SkillManagerModule : ListPanelBase
{
	public override string PanelId => "skill_mgr";
	public override PanelContainer PanelNode => _panel;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly RichTextLabel _detailText;
	private readonly List<Button> _tabButtons;

	private List<InteractionDef> _filtered = [];
	private List<InteractionDef> _allSkills = [];
	private SkillTab _currentTab = SkillTab.All;
	private Actor? _player;

	private static readonly SkillTab[] Tabs = [SkillTab.All, SkillTab.Combat, SkillTab.Utility, SkillTab.Social];
	private static readonly string[] TabLabels = ["全部", "战斗", "工具", "社交"];

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

	public SkillManagerModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		var tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		var content = vbox.GetNode<HBoxContainer>("Content");
		var leftScroll = content.GetNode<ScrollContainer>("LeftColumn");
		var skillList = leftScroll.GetNode<VBoxContainer>("SkillList");
		BindListNodes(leftScroll, skillList);
		_detailText = content.GetNode("RightColumn").GetNode<RichTextLabel>("DetailText");
		_tabButtons = TabHelper.BuildTabButtons(tabBar, TabLabels, Tabs, SetTab);
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

	public void Close() { Visible = false; _player = null; }

	public override void Refresh()
	{
		FilterSkills();
		RenderHeader();
		TabHelper.UpdateTabHighlight(_tabButtons, Tabs, _currentTab);
		RebuildRows(_filtered.Count, ApplyRowContent, "  暂无技能");
		OnSelectionChanged();
	}

	public void SetTab(SkillTab tab) { _currentTab = tab; _cursor = 0; Refresh(); }
	public void CycleTab(int dir) { var idx = Array.IndexOf(Tabs, _currentTab); idx = (idx + dir + Tabs.Length) % Tabs.Length; SetTab(Tabs[idx]); }

	protected override int GetRowDataCount() => _filtered.Count;
	protected override void OnSelectionChanged() { UpdateRowVisuals(_filtered.Count); RenderDetail(); }

	private void FilterSkills()
	{
		if (_currentTab == SkillTab.All) _filtered = [.. _allSkills];
		else
		{
			var cat = _currentTab switch { SkillTab.Combat => "combat", SkillTab.Utility => "utility", SkillTab.Social => "social", _ => "" };
			_filtered = _allSkills.FindAll(s => s.Category == cat);
		}
		if (_cursor >= _filtered.Count) _cursor = Math.Max(0, _filtered.Count - 1);
	}

	private void RenderHeader()
	{
		var combat = _allSkills.FindAll(s => s.Category == "combat").Count;
		var utility = _allSkills.FindAll(s => s.Category == "utility").Count;
		var social = _allSkills.FindAll(s => s.Category == "social").Count;
		_header.Clear();
		_header.AppendText($"[center]── 技能管理 ──[/center]\n" +
			$"[color=#ff6666]战斗: {combat}[/color]  [color=#66ccff]工具: {utility}[/color]  [color=#66ff88]社交: {social}[/color]  [color=#888888]共: {_allSkills.Count}[/color]");
	}

	private void ApplyRowContent(Button row, int i)
	{
		var skill = _filtered[i];
		var icon = skill.Category switch { "combat" => "⚔", "utility" => "🔧", "social" => "💬", _ => "◆" };
		var power = skill.Power > 0 ? $" [{skill.Power}]" : "";
		row.Text = $" {icon} {skill.Name}{power}";
		if (i != _cursor && i != _hoverIndex)
			row.ThemeTypeVariation = skill.Category switch
			{
				"combat" => "CombatRowButton",
				"utility" => "UtilityRowButton",
				"social" => "SocialRowButton",
				_ => "RowButton",
			};
	}

	protected override void UpdateRowVisuals(int count, bool transparentBg = true)
	{
		for (var i = 0; i < _itemRows.Count && i < count; i++)
		{
			if (i == _cursor) _itemRows[i].ThemeTypeVariation = "SelectedRowButton";
			else if (i == _hoverIndex) _itemRows[i].ThemeTypeVariation = "HoveredRowButton";
			else
			{
				_itemRows[i].ThemeTypeVariation = _filtered[i].Category switch
				{
					"combat" => "CombatRowButton",
					"utility" => "UtilityRowButton",
					"social" => "SocialRowButton",
					_ => "RowButton",
				};
			}
		}
	}

	private void RenderDetail()
	{
		_detailText.Clear();
		if (_filtered.Count == 0 || _cursor < 0 || _cursor >= _filtered.Count) { _detailText.AppendText("[color=#888888]选择一个技能查看详情[/color]"); return; }
		var skill = _filtered[_cursor];
		var sb = new StringBuilder();
		var color = skill.Category switch { "combat" => "#ff6666", "utility" => "#66ccff", "social" => "#66ff88", _ => "#cccccc" };
		var catLabel = skill.Category switch { "combat" => "战斗", "utility" => "工具", "social" => "社交", _ => skill.Category };
		sb.AppendLine($"[b][color={color}]{skill.Name}[/color][/b]");
		sb.AppendLine($"[color=#888888]{catLabel}技能[/color]");
		sb.AppendLine();
		if (skill.Power > 0) sb.AppendLine($"[color=#ffcc00]威力: {skill.Power}[/color]");
		if (skill.Cooldown > 0) sb.AppendLine($"[color=#aaaaaa]冷却: {skill.Cooldown} 回合[/color]");
		if (skill.Range > 1) sb.AppendLine($"[color=#aaaaaa]射程: {skill.Range}[/color]");
		else if (skill.Range == 0) sb.AppendLine("[color=#aaaaaa]射程: 自身[/color]");
		if (!string.IsNullOrEmpty(skill.DamageType))
		{
			var dmgLabel = skill.DamageType switch { "sharp" => "锐伤", "blunt" => "钝伤", "poison" => "毒伤", _ => skill.DamageType };
			sb.AppendLine($"[color=#cc8866]伤害类型: {dmgLabel}[/color]");
		}
		if (!string.IsNullOrEmpty(skill.EffectType))
		{
			var effectLabel = skill.EffectType switch
			{
				"melee_attack" => "近战攻击", "heavy_attack" => "重击", "poison_attack" => "毒攻击", "drain_attack" => "吸血攻击",
				"block" => "格挡", "dig" => "挖掘", "trade" => "交易", "talk" => "对话", "tame" => "驯服", "combat" => "战斗", _ => skill.EffectType,
			};
			sb.AppendLine($"[color=#aaaaaa]效果: {effectLabel}[/color]");
		}
		sb.AppendLine();
		if (!string.IsNullOrEmpty(skill.Description)) { sb.AppendLine(skill.Description); sb.AppendLine(); }
		if (skill.Required.Count > 0)
		{
			sb.AppendLine("[color=#ffcc00]─── 需求条件 ───[/color]");
			foreach (var (k, v) in skill.Required) sb.AppendLine($"  [color=#aaaaaa]{k} ≥ {v}[/color]");
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
		if (!string.IsNullOrEmpty(source)) { sb.AppendLine(); sb.AppendLine($"[color=#666666]来源: {source}[/color]"); }
		_detailText.AppendText(sb.ToString());
	}

	private string GetSkillSource(InteractionDef skill)
	{
		if (_player == null) return "";
		foreach (var item in _player.Inventory)
			if (item.Equipped && item.GrantedSkills.Contains(skill.Id))
				return $"装备「{item.Name}」";
		return "自身能力";
	}
}
