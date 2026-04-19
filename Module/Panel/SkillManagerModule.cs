using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

public enum SkillTab { All, Combat, Utility, Social }

public class SkillManagerModule : ListPanelBase, ITooltipRegistrar
{
	public override string PanelId => "skill_mgr";
	public override PanelContainer PanelNode => _panel;
	public override bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public string? ArmedSkillId { get; set; }
	public GameState? State { get; set; }

	public event Action<InteractionDef>? ConfirmRequested;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly RichTextLabel _detailText;
	private readonly List<Button> _tabButtons;

	private List<InteractionDef> _filtered = [];
	private List<InteractionDef> _allSkills = [];
	private SkillTab _currentTab = SkillTab.All;
	private Actor? _player;
	private RichTooltipLayer? _tooltipLayer;

	public void RegisterTooltips(RichTooltipLayer layer) => _tooltipLayer = layer;

	private static readonly SkillTab[] Tabs = [SkillTab.All, SkillTab.Combat, SkillTab.Utility, SkillTab.Social];
	private static readonly string[] TabLabels = ["All", "Combat", "Utility", "Social"];

	public override bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "up": MoveCursor(-1, _filtered.Count); return true;
			case "down": MoveCursor(1, _filtered.Count); return true;
			case "left" or "tab_prev": CycleTab(-1); return true;
			case "right" or "tab_next": CycleTab(1); return true;
			case "confirm": return ActivateSelectedSkill();
			case "close": Close(); return true;
		}
		return false;
	}

	public override void OnBlur() { }

	public SkillManagerModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		var tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		var content = vbox.GetNode<HBoxContainer>("Content");
		var leftScroll = content.GetNode<ScrollContainer>("LeftColumn");
		var skillList = leftScroll.GetNode<VBoxContainer>("SkillList");
		BindListNodes(leftScroll, skillList);
		_detailText = content.GetNode("RightColumn").GetNode<RichTextLabel>("DetailText");
		_tabButtons = TabHelper.BuildTabButtons(tabBar, TabLabels, Tabs, SetTab, PanelId);
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
		State = null;
	}

	public override void Refresh()
	{
		_allSkills = _player != null ? SkillQuery.GetSkills(_player) : [];
		FilterSkills();
		RenderHeader();
		UpdateTabTexts();
		TabHelper.UpdateTabHighlight(_tabButtons, Tabs, _currentTab);
		RebuildRows(_filtered.Count, ApplyRowContent, LocalizationService.T("ui.skill_manager.empty"));
		OnSelectionChanged();
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

	protected override int GetRowDataCount() => _filtered.Count;

	protected override void OnSelectionChanged()
	{
		UpdateRowVisuals(_filtered.Count);
		RenderDetail();
	}

	private bool ActivateSelectedSkill()
	{
		if (_cursor < 0 || _cursor >= _filtered.Count)
			return false;

		ConfirmRequested?.Invoke(_filtered[_cursor]);
		return true;
	}

	private void UpdateTabTexts()
	{
		if (_tabButtons.Count < Tabs.Length)
			return;

		_tabButtons[0].Text = LocalizationService.T("ui.common.tab.all");
		_tabButtons[1].Text = LocalizationService.T("skill.category.combat");
		_tabButtons[2].Text = LocalizationService.T("skill.category.utility");
		_tabButtons[3].Text = LocalizationService.T("skill.category.social");
	}

	private void FilterSkills()
	{
		if (_currentTab == SkillTab.All)
		{
			_filtered = [.. _allSkills];
		}
		else
		{
			var category = _currentTab switch
			{
				SkillTab.Combat => "combat",
				SkillTab.Utility => "utility",
				SkillTab.Social => "social",
				_ => string.Empty,
			};
			_filtered = _allSkills.FindAll(skill => skill.Category == category);
		}

		if (_cursor >= _filtered.Count)
			_cursor = Math.Max(0, _filtered.Count - 1);
	}

	private void RenderHeader()
	{
		var combat = _allSkills.FindAll(skill => skill.Category == "combat").Count;
		var utility = _allSkills.FindAll(skill => skill.Category == "utility").Count;
		var social = _allSkills.FindAll(skill => skill.Category == "social").Count;
		_header.Clear();
		_header.AppendText(
			$"[center]{LocalizationService.T("ui.skill_manager.title")}[/center]\n" +
			LocalizationService.T("ui.skill.summary",
				("combat", combat),
				("utility", utility),
				("social", social),
				("total", _allSkills.Count)) +
			"\n" +
			$"[color=#888888]{LocalizationService.T("ui.skill_manager.hint")}[/color]");
	}

	private void ApplyRowContent(Button row, int index)
	{
		var skill = _filtered[index];
		var icon = skill.Category switch
		{
			"combat" => "C",
			"utility" => "U",
			"social" => "S",
			_ => "O",
		};
		var cooldownRemaining = _player?.GetSkillCooldown(skill.Id) ?? 0;
		var status = string.Equals(skill.Id, ArmedSkillId, StringComparison.Ordinal)
			? " [ARM]"
			: cooldownRemaining > 0
				? $" [CD{cooldownRemaining}]"
				: "";
		var power = skill.Power > 0 ? $" [{skill.Power}]" : "";
		row.Text = $" {icon} {skill.Name}{power}{status}";
		if (index != _cursor && index != _hoverIndex)
		{
			row.ThemeTypeVariation = cooldownRemaining > 0
				? "DisabledRowButton"
				: skill.Category switch
				{
					"combat" => "CombatRowButton",
					"utility" => "UtilityRowButton",
					"social" => "SocialRowButton",
					_ => "RowButton",
				};
		}

		if (_tooltipLayer != null)
		{
			var capturedIndex = index;
			_tooltipLayer.Attach(row, () =>
			{
				if (capturedIndex < 0 || capturedIndex >= _filtered.Count)
					return string.Empty;
				return SkillTooltipBuilder.BuildSkillDetailBbcode(
					_filtered[capturedIndex], _player, State, ArmedSkillId);
			});
		}
	}

	protected override void UpdateRowVisuals(int count, bool transparentBg = true)
	{
		for (var i = 0; i < _itemRows.Count && i < count; i++)
		{
			var cooldownRemaining = _player?.GetSkillCooldown(_filtered[i].Id) ?? 0;
			if (i == _cursor)
			{
				_itemRows[i].ThemeTypeVariation = "SelectedRowButton";
			}
			else if (i == _hoverIndex)
			{
				_itemRows[i].ThemeTypeVariation = "HoveredRowButton";
			}
			else
			{
				_itemRows[i].ThemeTypeVariation = cooldownRemaining > 0
					? "DisabledRowButton"
					: _filtered[i].Category switch
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
		if (_filtered.Count == 0 || _cursor < 0 || _cursor >= _filtered.Count)
		{
			_detailText.AppendText(LocalizationService.T("ui.common.detail_hint.skill"));
			return;
		}

		_detailText.AppendText(SkillTooltipBuilder.BuildSkillDetailBbcode(
			_filtered[_cursor], _player, State, ArmedSkillId));
	}
}
