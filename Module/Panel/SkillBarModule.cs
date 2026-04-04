using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

public enum SkillBarTab { All, Combat, Utility, Social }

public sealed class SkillBarModule : IPanel
{
	private const int GridColumns = 6;
	private const float TileWidth = 70f;
	private const float TileHeight = 56f;

	private static readonly SkillBarTab[] Tabs =
		[SkillBarTab.All, SkillBarTab.Combat, SkillBarTab.Utility, SkillBarTab.Social];

	private static readonly string[] TabLabels = ["全部", "战斗", "工具", "社交"];
	private static readonly Dictionary<string, StyleBoxFlat> StyleCache = [];

	public string PanelId => "skill_bar";
	public PanelContainer PanelNode => _panel;
	public bool Visible { get => _panel.Visible; set => _panel.Visible = value; }
	public bool Dirty { get; set; }
	public bool CanFocus => true;

	public event Action? CloseRequested;

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

	public SkillBarModule(PanelContainer panel)
	{
		_panel = panel;
		var vbox = panel.GetNode<VBoxContainer>("MarginContainer/VBox");
		_header = vbox.GetNode<RichTextLabel>("Header");
		var tabBar = vbox.GetNode<HBoxContainer>("TabBar");
		_gridScroll = vbox.GetNode<ScrollContainer>("GridScroll");
		_emptyLabel = _gridScroll.GetNode<Label>("GridHost/EmptyLabel");
		_grid = _gridScroll.GetNode<GridContainer>("GridHost/SkillGrid");
		_grid.Columns = GridColumns;
		_detailText = vbox.GetNode<ScrollContainer>("DetailScroll").GetNode<RichTextLabel>("DetailText");

		var closeBtn = vbox.GetNode<Button>("ActionBar/CloseBtn");
		closeBtn.FocusMode = Control.FocusModeEnum.None;
		closeBtn.Pressed += () => CloseRequested?.Invoke();

		_tabButtons = TabHelper.BuildTabButtons(tabBar, TabLabels, Tabs, SetTab);
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
			case "confirm": return true;
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
			button.TooltipText = skill.Name;
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
			ApplyButtonStyle(button, skill.Category, selected, hovered);
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
			$"[center]── 技能栏 ──[/center]\n" +
			$"[color=#ff6666]战斗: {combat}[/color]  [color=#66ccff]工具: {utility}[/color]  [color=#66ff88]社交: {social}[/color]  [color=#888888]总计: {_allSkills.Count}[/color]");
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
			_detailText.AppendText("[color=#888888]当前分类暂无技能。[/color]");
			return;
		}

		var skill = _filtered[_selectedIndex];
		var color = GetCategoryAccent(skill.Category).ToHtml(false);
		var metaParts = new List<string>();
		if (skill.Power > 0)
			metaParts.Add($"威力 {skill.Power}");
		if (skill.Cooldown > 0)
			metaParts.Add($"冷却 {skill.Cooldown}");
		metaParts.Add($"射程 {GetRangeLabel(skill.Range)}");

		var sb = new StringBuilder();
		sb.Append($"[b][color=#{color}]{skill.Name}[/color][/b]");
		sb.Append($"  [color=#888888]{GetCategoryLabel(skill.Category)}[/color]");
		if (metaParts.Count > 0)
		{
			sb.AppendLine();
			sb.Append($"[color=#aaaaaa]{string.Join("  ", metaParts)}[/color]");
		}
		if (!string.IsNullOrWhiteSpace(skill.Description))
		{
			sb.AppendLine();
			sb.AppendLine();
			sb.Append(skill.Description.Trim());
		}

		_detailText.AppendText(sb.ToString());
	}

	private static string BuildTileLabel(InteractionDef skill)
	{
		return $"{GetCategorySymbol(skill.Category)} {BuildShortName(skill.Name)}";
	}

	private static string BuildShortName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return "--";

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
		"combat" => "⚔",
		"utility" => "⚙",
		"social" => "✦",
		_ => "◆",
	};

	private static string GetCategoryLabel(string category) => category switch
	{
		"combat" => "战斗",
		"utility" => "工具",
		"social" => "社交",
		_ => category,
	};

	private static string GetRangeLabel(int range) => range switch
	{
		0 => "自身",
		1 => "邻近",
		_ => range.ToString(),
	};

	private static void ApplyButtonStyle(Button button, string category, bool selected, bool hovered)
	{
		var style = GetStyle(category, selected, hovered);
		button.AddThemeStyleboxOverride("normal", style);
		button.AddThemeStyleboxOverride("hover", style);
		button.AddThemeStyleboxOverride("pressed", style);
		button.AddThemeStyleboxOverride("focus", style);

		var fontColor = selected ? Colors.White : new Color(0.92f, 0.94f, 0.98f);
		button.AddThemeColorOverride("font_color", fontColor);
		button.AddThemeColorOverride("font_hover_color", fontColor);
		button.AddThemeColorOverride("font_pressed_color", fontColor);
		button.AddThemeColorOverride("font_focus_color", fontColor);
	}

	private static StyleBoxFlat GetStyle(string category, bool selected, bool hovered)
	{
		var key = $"{category}:{selected}:{hovered}";
		if (StyleCache.TryGetValue(key, out var cached))
			return cached;

		var (baseColor, accentColor) = GetPalette(category);
		var style = new StyleBoxFlat
		{
			BgColor = selected
				? baseColor.Lightened(0.24f)
				: hovered
					? baseColor.Lightened(0.12f)
					: baseColor,
			BorderColor = selected ? UIColors.FocusBorder : accentColor,
		};
		style.SetBorderWidthAll(selected ? 2 : 1);
		style.SetCornerRadiusAll(6);
		style.SetContentMarginAll(6);
		StyleCache[key] = style;
		return style;
	}

	private static (Color BaseColor, Color AccentColor) GetPalette(string category) => category switch
	{
		"combat" => (new Color(0.27f, 0.16f, 0.18f), new Color(0.88f, 0.35f, 0.38f)),
		"utility" => (new Color(0.16f, 0.23f, 0.29f), new Color(0.38f, 0.74f, 0.94f)),
		"social" => (new Color(0.16f, 0.28f, 0.22f), new Color(0.42f, 0.88f, 0.58f)),
		_ => (new Color(0.18f, 0.18f, 0.22f), UIColors.IdleBorder),
	};

	private static Color GetCategoryAccent(string category) => GetPalette(category).AccentColor;
}
