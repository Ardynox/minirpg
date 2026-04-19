using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public enum SkillBarTab { All, Combat, Utility, Social }

public sealed class SkillBarModule : IPanel, ITooltipRegistrar
{
	private const int GridColumns = 6;
	private const float TileWidth = 70f;
	private const float TileHeight = 56f;

	private static readonly SkillBarTab[] Tabs =
		[SkillBarTab.All, SkillBarTab.Combat, SkillBarTab.Utility, SkillBarTab.Social];

	private static readonly string[] TabLabels = ["All", "Combat", "Utility", "Social"];
	private static readonly Dictionary<string, StyleBoxFlat> StyleCache = [];
	private static readonly Color ArmedBorder = UIColors.FocusBorder;

	public string PanelId => "skill_bar";
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public bool Dirty { get; set; }
	public bool CanFocus => true;
	public string? ArmedSkillId { get; set; }

	public event Action? CloseRequested;
	public event Action<InteractionDef>? ConfirmRequested;

	private readonly PanelContainer _panel;
	private readonly RichTextLabel _header;
	private readonly ScrollContainer _gridScroll;
	private readonly GridContainer _grid;
	private readonly Label _emptyLabel;
	private readonly RichTextLabel _detailText;
	private readonly List<Button> _tabButtons;
	private readonly List<Button> _skillButtons = [];

	private List<InteractionDef> _allSkills = [];
	private List<InteractionDef> _filtered = [];
	private SkillBarTab _currentTab = SkillBarTab.All;
	private Actor? _player;
	private int _selectedIndex;
	private int _hoverIndex = -1;
	private RichTooltipLayer? _tooltipLayer;

	public void RegisterTooltips(RichTooltipLayer layer) => _tooltipLayer = layer;

	public SkillBarModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode<VBoxContainer>("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("HeaderBar/Header");
		var tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		_gridScroll = vbox.GetNode<ScrollContainer>("GridScroll");
		_emptyLabel = _gridScroll.GetNode<Label>("GridHost/EmptyLabel");
		_grid = _gridScroll.GetNode<GridContainer>("GridHost/SkillGrid");
		_grid.Columns = GridColumns;
		_detailText = vbox.GetNode<ScrollContainer>("DetailScroll").GetNode<RichTextLabel>("DetailText");

		var closeBtn = vbox.GetNode<Button>("ActionBar/CloseBtn");
		closeBtn.FocusMode = Control.FocusModeEnum.None;
		closeBtn.Pressed += () => CloseRequested?.Invoke();

		_tabButtons = TabHelper.BuildTabButtons(tabBar, TabLabels, Tabs, SetTab, PanelId);
	}

	public void Open(Actor? player)
	{
		_player = player;
		_allSkills = player != null ? SkillQuery.GetSkills(player) : [];
		_currentTab = SkillBarTab.All;
		_selectedIndex = 0;
		_hoverIndex = -1;
		Dirty = false;
		Visible = true;
		Refresh();
	}

	public void Close()
	{
		Visible = false;
	}

	public void Refresh(Actor? player)
	{
		_player = player;
		_allSkills = player != null ? SkillQuery.GetSkills(player) : [];
		if (Visible)
			Refresh();
		else
			Dirty = true;
	}

	public void FlushIfDirty()
	{
		if (!Dirty)
			return;

		Dirty = false;
		Refresh();
	}

	public bool HandleCommand(string cmd)
	{
		switch (cmd)
		{
			case "left": return StepSelection(-1);
			case "right": return StepSelection(1);
			case "up": return StepSelection(-GridColumns);
			case "down": return StepSelection(GridColumns);
			case "tab_prev": CycleTab(-1); return true;
			case "tab_next": CycleTab(1); return true;
			case "confirm": return ActivateSelectedSkill();
			case "close": CloseRequested?.Invoke(); return true;
			default: return false;
		}
	}

	public void OnFocus() { }

	public void OnBlur()
	{
		_hoverIndex = -1;
		UpdateCellVisuals();
	}

	public void Refresh()
	{
		var selectedSkillId = GetSelectedSkillId();
		FilterSkills();
		RestoreSelection(selectedSkillId);
		ClampSelection();
		RebuildGrid();
		RenderHeader();
		UpdateTabTexts();
		TabHelper.UpdateTabHighlight(_tabButtons, Tabs, _currentTab);
		UpdateCellContent();
		UpdateCellVisuals();
		RenderDetail();
		EnsureSelectedVisible();
		Dirty = false;
	}

	private void SetTab(SkillBarTab tab)
	{
		if (_currentTab == tab)
			return;

		_currentTab = tab;
		_selectedIndex = 0;
		_hoverIndex = -1;
		Refresh();
	}

	private void CycleTab(int dir)
	{
		var idx = Array.IndexOf(Tabs, _currentTab);
		idx = (idx + dir + Tabs.Length) % Tabs.Length;
		SetTab(Tabs[idx]);
	}

	public bool StepSelection(int delta)
	{
		if (_filtered.Count == 0)
			return false;

		var target = Math.Clamp(_selectedIndex + delta, 0, _filtered.Count - 1);
		if (target == _selectedIndex)
			return false;

		_selectedIndex = target;
		UpdateCellVisuals();
		RenderDetail();
		EnsureSelectedVisible();
		return true;
	}

	private bool ActivateSelectedSkill()
	{
		if (_selectedIndex < 0 || _selectedIndex >= _filtered.Count)
			return false;

		ConfirmRequested?.Invoke(_filtered[_selectedIndex]);
		return true;
	}

	private void FilterSkills()
	{
		if (_currentTab == SkillBarTab.All)
		{
			_filtered = [.. _allSkills];
			return;
		}

		var category = _currentTab switch
		{
			SkillBarTab.Combat => "combat",
			SkillBarTab.Utility => "utility",
			SkillBarTab.Social => "social",
			_ => string.Empty,
		};
		_filtered = _allSkills.FindAll(skill => skill.Category == category);
	}

	private void RestoreSelection(string? skillId)
	{
		if (string.IsNullOrEmpty(skillId))
			return;

		for (var i = 0; i < _filtered.Count; i++)
		{
			if (_filtered[i].Id != skillId)
				continue;

			_selectedIndex = i;
			return;
		}
	}

	private void ClampSelection()
	{
		if (_filtered.Count == 0)
		{
			_selectedIndex = 0;
			_hoverIndex = -1;
			return;
		}

		_selectedIndex = Math.Clamp(_selectedIndex, 0, _filtered.Count - 1);
		if (_hoverIndex >= _filtered.Count)
			_hoverIndex = -1;
	}

	private string? GetSelectedSkillId()
	{
		if (_selectedIndex < 0 || _selectedIndex >= _filtered.Count)
			return null;
		return _filtered[_selectedIndex].Id;
	}

	private void RebuildGrid()
	{
		while (_skillButtons.Count > _filtered.Count)
		{
			_skillButtons[^1].QueueFree();
			_skillButtons.RemoveAt(_skillButtons.Count - 1);
		}

		while (_skillButtons.Count < _filtered.Count)
		{
			var button = CreateSkillButton(_skillButtons.Count);
			_grid.AddChild(button);
			_skillButtons.Add(button);
		}

		_emptyLabel.Visible = _filtered.Count == 0;
		_grid.Visible = _filtered.Count > 0;
	}

	private Button CreateSkillButton(int index)
	{
		var button = new Button
		{
			FocusMode = Control.FocusModeEnum.None,
			Alignment = HorizontalAlignment.Center,
			CustomMinimumSize = new Vector2(TileWidth, TileHeight),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			ClipText = true,
		};

		var capturedIndex = index;
		button.Pressed += () => SelectIndex(capturedIndex);
		button.MouseEntered += () => OnCellHover(capturedIndex);
		button.MouseExited += () => OnCellHoverExit(capturedIndex);
		PanelButtonScaleRegistry.Track(PanelId, button);

		if (_tooltipLayer != null)
		{
			_tooltipLayer.Attach(button, () =>
			{
				if (capturedIndex < 0 || capturedIndex >= _filtered.Count)
					return string.Empty;
				return SkillTooltipBuilder.BuildSkillDetailBbcode(
					_filtered[capturedIndex], _player, state: null, ArmedSkillId);
			});
		}

		return button;
	}

	private void SelectIndex(int index)
	{
		if (index < 0 || index >= _filtered.Count)
			return;

		_selectedIndex = index;
		UpdateCellVisuals();
		RenderDetail();
		EnsureSelectedVisible();

		if (index < _skillButtons.Count)
			ButtonPressFlash.Flash(_skillButtons[index]);
	}

	private void OnCellHover(int index)
	{
		if (index < 0 || index >= _filtered.Count)
			return;

		_hoverIndex = index;
		UpdateCellVisuals();
	}

	private void OnCellHoverExit(int index)
	{
		if (_hoverIndex == index)
			_hoverIndex = -1;

		UpdateCellVisuals();
	}

	private void UpdateCellContent()
	{
		for (var i = 0; i < _skillButtons.Count && i < _filtered.Count; i++)
		{
			var skill = _filtered[i];
			var button = _skillButtons[i];
			button.Text = BuildTileLabel(skill);
			// 不再写 button.TooltipText：RichTooltip factory 已包含完整名称 + 详情。
		}
	}

	private void UpdateCellVisuals()
	{
		for (var i = 0; i < _skillButtons.Count && i < _filtered.Count; i++)
		{
			var button = _skillButtons[i];
			var skill = _filtered[i];
			var selected = i == _selectedIndex;
			var hovered = i == _hoverIndex;
			var armed = string.Equals(skill.Id, ArmedSkillId, StringComparison.Ordinal);
			var cooldownRemaining = _player?.GetSkillCooldown(skill.Id) ?? 0;
			ApplyButtonStyle(button, skill.Category, selected, hovered, armed, cooldownRemaining > 0);
			PanelButtonScaleRegistry.Reapply(PanelId, button);
		}
	}

	private void EnsureSelectedVisible()
	{
		if (_selectedIndex < 0 || _selectedIndex >= _skillButtons.Count)
			return;

		var button = _skillButtons[_selectedIndex];
		var rowTop = button.Position.Y;
		var rowBottom = rowTop + button.Size.Y;
		var scrollTop = _gridScroll.ScrollVertical;
		var scrollBottom = scrollTop + _gridScroll.Size.Y;

		if (rowTop < scrollTop)
			_gridScroll.ScrollVertical = (int)rowTop;
		else if (rowBottom > scrollBottom)
			_gridScroll.ScrollVertical = (int)(rowBottom - _gridScroll.Size.Y);
	}

	private void RenderHeader()
	{
		var combat = CountCategory("combat");
		var utility = CountCategory("utility");
		var social = CountCategory("social");
		_header.Clear();
		_header.AppendText(
			$"[center]{LocalizationService.T("ui.skill_bar.title")}[/center]\n" +
			LocalizationService.T("ui.skill.summary",
				("combat", combat),
				("utility", utility),
				("social", social),
				("total", _allSkills.Count)) +
			"\n" +
			$"[color=#888888]{LocalizationService.T("ui.skill_bar.hint")}[/color]");
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

	private int CountCategory(string category)
	{
		var count = 0;
		foreach (var skill in _allSkills)
		{
			if (skill.Category == category)
				count++;
		}
		return count;
	}

	private void RenderDetail()
	{
		_detailText.Clear();
		if (_filtered.Count == 0 || _selectedIndex < 0 || _selectedIndex >= _filtered.Count)
		{
			_detailText.AppendText($"[color=#888888]{LocalizationService.T("ui.skill_bar.empty")}[/color]");
			return;
		}

		var skill = _filtered[_selectedIndex];
		var color = GetCategoryAccent(skill.Category).ToHtml(false);
		var cooldownRemaining = _player?.GetSkillCooldown(skill.Id) ?? 0;
		var metaParts = new List<string>();
		if (skill.Power > 0)
			metaParts.Add(LocalizationService.T("ui.skill.detail.power_inline", ("value", skill.Power)));
		if (skill.Cooldown > 0)
			metaParts.Add(LocalizationService.T("ui.skill.detail.cooldown_inline", ("value", skill.Cooldown)));
		metaParts.Add(LocalizationService.T("ui.skill.detail.range_inline", ("value", GetRangeLabel(skill.Range))));

		var statusKey = string.Equals(skill.Id, ArmedSkillId, StringComparison.Ordinal)
			? "ui.skill.detail.status.armed"
			: cooldownRemaining > 0
				? "ui.skill.detail.status.cooldown"
				: "ui.skill.detail.status.ready";

		var sb = new StringBuilder();
		sb.Append($"[b][color=#{color}]{skill.Name}[/color][/b]");
		sb.Append($"  [color=#888888]{GetCategoryLabel(skill.Category)}[/color]");
		if (metaParts.Count > 0)
		{
			sb.AppendLine();
			sb.Append($"[color=#aaaaaa]{string.Join("  ", metaParts)}[/color]");
		}

		sb.AppendLine();
		sb.AppendLine();
		sb.Append(LocalizationService.T(statusKey, ("value", cooldownRemaining)));
		if (!string.IsNullOrWhiteSpace(skill.Description))
		{
			sb.AppendLine();
			sb.AppendLine();
			sb.Append(skill.Description.Trim());
		}

		_detailText.AppendText(sb.ToString());
	}

	private string BuildTileLabel(InteractionDef skill)
	{
		var cooldownRemaining = _player?.GetSkillCooldown(skill.Id) ?? 0;
		var marker = string.Equals(skill.Id, ArmedSkillId, StringComparison.Ordinal)
			? "*"
			: GetCategorySymbol(skill.Category);
		var baseLabel = $"{marker} {BuildShortName(skill.Name)}";
		return cooldownRemaining > 0
			? $"{baseLabel}\nCD{cooldownRemaining}"
			: baseLabel;
	}

	private static string BuildShortName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "--";

		var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Length >= 2)
			return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";

		var sb = new StringBuilder(2);
		foreach (var ch in name.Trim())
		{
			if (char.IsWhiteSpace(ch))
				continue;

			sb.Append(ch);
			if (sb.Length == 2)
				break;
		}

		return sb.Length > 0 ? sb.ToString() : "--";
	}

	private static string GetCategorySymbol(string category) => category switch
	{
		"combat" => "C",
		"utility" => "U",
		"social" => "S",
		_ => "O",
	};

	private static string GetCategoryLabel(string category) => category switch
	{
		"combat" => LocalizationService.T("skill.category.combat"),
		"utility" => LocalizationService.T("skill.category.utility"),
		"social" => LocalizationService.T("skill.category.social"),
		_ => category,
	};

	private static string GetRangeLabel(int range) => range switch
	{
		0 => LocalizationService.T("enum.range.self"),
		1 => LocalizationService.T("enum.range.adjacent"),
		_ => range.ToString(),
	};

	private static void ApplyButtonStyle(Button button, string category, bool selected, bool hovered, bool armed, bool coolingDown)
	{
		var style = GetStyle(category, selected, hovered, armed, coolingDown);
		button.AddThemeStyleboxOverride("normal", style);
		button.AddThemeStyleboxOverride("hover", style);
		button.AddThemeStyleboxOverride("pressed", style);
		button.AddThemeStyleboxOverride("focus", style);

		var fontColor = coolingDown
			? UIColors.TextDim
			: selected || armed
				? Colors.White
				: UIColors.TextNormal;
		button.AddThemeColorOverride("font_color", fontColor);
		button.AddThemeColorOverride("font_hover_color", fontColor);
		button.AddThemeColorOverride("font_pressed_color", fontColor);
		button.AddThemeColorOverride("font_focus_color", fontColor);
	}

	private static StyleBoxFlat GetStyle(string category, bool selected, bool hovered, bool armed, bool coolingDown)
	{
		var key = $"{category}:{selected}:{hovered}:{armed}:{coolingDown}";
		if (StyleCache.TryGetValue(key, out var cached))
			return cached;

		var (baseColor, accentColor) = GetPalette(category);
		if (coolingDown)
			baseColor = baseColor.Darkened(0.35f);

		var style = new StyleBoxFlat
		{
			BgColor = selected
				? baseColor.Lightened(0.24f)
				: hovered
					? baseColor.Lightened(0.12f)
					: baseColor,
			BorderColor = armed ? ArmedBorder : accentColor,
		};
		style.SetBorderWidthAll(armed || selected ? 2 : 1);
		style.SetCornerRadiusAll(6);
		style.SetContentMarginAll(6);
		StyleCache[key] = style;
		return style;
	}

	private static (Color BaseColor, Color AccentColor) GetPalette(string category) => category switch
	{
		"combat" => (new Color(0.2f, 0.1f, 0.12f), UIColors.TextCombat),
		"utility" => (new Color(0.1f, 0.15f, 0.22f), UIColors.TextUtility),
		"social" => (new Color(0.1f, 0.18f, 0.14f), UIColors.TextSocial),
		_ => (new Color(0.1f, 0.1f, 0.16f), UIColors.IdleBorder),
	};

	private static Color GetCategoryAccent(string category) => GetPalette(category).AccentColor;
}
