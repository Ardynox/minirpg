using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Facility;

namespace MiniRPG.Module.WorldTool;

internal sealed class RuntimeWorldToolBarModule
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Button _selectToolButton;
	private readonly Button _buildToolButton;
	private readonly Button _demolishToolButton;
	private readonly Button _terrainCategoryButton;
	private readonly Button _facilityCategoryButton;
	private readonly ScrollContainer _brushScroll;
	private readonly GridContainer _brushGrid;
	private readonly Label _currentBrushLabel;
	private readonly Label _summaryLabel;
	private readonly HBoxContainer _rotationRow;
	private readonly Button _rotateLeftButton;
	private readonly Label _rotationLabel;
	private readonly Button _rotateRightButton;

	private IReadOnlyList<RuntimeWorldToolBrush> _lastBrushes = Array.Empty<RuntimeWorldToolBrush>();
	private int _lastSelectedIndex = -1;
	public RuntimeWorldToolBarModule(PanelContainer panel)
	{
		_panel = panel;
		_panel.Visible = false;
		_panel.MouseFilter = Control.MouseFilterEnum.Stop;
		_panel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_panel.OffsetLeft = 16f;
		_panel.OffsetTop = 16f;
		_panel.CustomMinimumSize = new Vector2(420f, 0f);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		_panel.AddChild(margin);

		var root = new VBoxContainer();
		root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		root.AddThemeConstantOverride("separation", 6);
		margin.AddChild(root);

		_titleLabel = new Label();
		root.AddChild(_titleLabel);

		var toolRow = new HBoxContainer();
		toolRow.AddThemeConstantOverride("separation", 6);
		root.AddChild(toolRow);
		_selectToolButton = CreateToggleButton(toolRow);
		_buildToolButton = CreateToggleButton(toolRow);
		_demolishToolButton = CreateToggleButton(toolRow);

		var categoryRow = new HBoxContainer();
		categoryRow.AddThemeConstantOverride("separation", 6);
		root.AddChild(categoryRow);
		_terrainCategoryButton = CreateToggleButton(categoryRow);
		_facilityCategoryButton = CreateToggleButton(categoryRow);

		_brushScroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0f, 130f),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		root.AddChild(_brushScroll);

		_brushGrid = new GridContainer
		{
			Columns = 4,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_brushGrid.AddThemeConstantOverride("h_separation", 6);
		_brushGrid.AddThemeConstantOverride("v_separation", 6);
		_brushScroll.AddChild(_brushGrid);

		_currentBrushLabel = new Label();
		root.AddChild(_currentBrushLabel);

		_summaryLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		root.AddChild(_summaryLabel);

		_rotationRow = new HBoxContainer();
		_rotationRow.AddThemeConstantOverride("separation", 6);
		root.AddChild(_rotationRow);
		_rotateLeftButton = new Button { CustomMinimumSize = new Vector2(40f, 0f) };
		_rotationRow.AddChild(_rotateLeftButton);
		_rotationLabel = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_rotationRow.AddChild(_rotationLabel);
		_rotateRightButton = new Button { CustomMinimumSize = new Vector2(40f, 0f) };
		_rotationRow.AddChild(_rotateRightButton);

		_selectToolButton.Pressed += () => ToolModeSelected?.Invoke(WorldToolMode.Select);
		_buildToolButton.Pressed += () => ToolModeSelected?.Invoke(WorldToolMode.Build);
		_demolishToolButton.Pressed += () => ToolModeSelected?.Invoke(WorldToolMode.Demolish);
		_terrainCategoryButton.Pressed += () => CategorySelected?.Invoke(WorldToolCategory.Terrain);
		_facilityCategoryButton.Pressed += () => CategorySelected?.Invoke(WorldToolCategory.Facility);
		_rotateLeftButton.Pressed += () => RotateRequested?.Invoke(-1);
		_rotateRightButton.Pressed += () => RotateRequested?.Invoke(1);

		RefreshTexts();
	}

	public event Action<WorldToolMode>? ToolModeSelected;
	public event Action<WorldToolCategory>? CategorySelected;
	public event Action<int>? BrushSelected;
	public event Action<int>? RotateRequested;

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public bool IsPointerOver(Vector2 globalPosition) =>
		_panel.Visible && _panel.GetGlobalRect().HasPoint(globalPosition);

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.TOrFallback("ui.runtime_tool.title", "World Tools");
		_selectToolButton.Text = LocalizationService.TOrFallback("ui.runtime_tool.tool.select", "Select");
		_buildToolButton.Text = LocalizationService.TOrFallback("ui.runtime_tool.tool.build", "Build");
		_demolishToolButton.Text = LocalizationService.TOrFallback("ui.runtime_tool.tool.demolish", "Demolish");
		_terrainCategoryButton.Text = LocalizationService.TOrFallback("ui.runtime_tool.category.terrain", "Terrain");
		_facilityCategoryButton.Text = LocalizationService.TOrFallback("ui.runtime_tool.category.facility", "Facility");
		_rotateLeftButton.Text = "<";
		_rotateRightButton.Text = ">";
	}

	public void Render(
		WorldToolMode toolMode,
		WorldToolCategory category,
		IReadOnlyList<RuntimeWorldToolBrush> brushes,
		int selectedIndex,
		string summary,
		FacilityRotation rotation,
		bool showRotationControls)
	{
		_selectToolButton.ButtonPressed = toolMode == WorldToolMode.Select;
		_buildToolButton.ButtonPressed = toolMode == WorldToolMode.Build;
		_demolishToolButton.ButtonPressed = toolMode == WorldToolMode.Demolish;
		_terrainCategoryButton.ButtonPressed = category == WorldToolCategory.Terrain;
		_facilityCategoryButton.ButtonPressed = category == WorldToolCategory.Facility;
		_summaryLabel.Text = summary;
		_rotationRow.Visible = showRotationControls;
		_rotationLabel.Text = LocalizationService.TOrFallback(
			$"ui.runtime_tool.rotation.{rotation.ToString().ToLowerInvariant()}",
			rotation.ToString());

		if (!ReferenceEquals(_lastBrushes, brushes) || _lastSelectedIndex != selectedIndex)
		{
			_lastBrushes = brushes;
			_lastSelectedIndex = selectedIndex;
			RebuildBrushGrid(brushes, selectedIndex);
		}

		UpdateCurrentBrushLabel(brushes, selectedIndex);
	}

	private void RebuildBrushGrid(IReadOnlyList<RuntimeWorldToolBrush> brushes, int selectedIndex)
	{
		foreach (var child in _brushGrid.GetChildren())
			child.QueueFree();

		var clampedIndex = brushes.Count > 0 ? Math.Clamp(selectedIndex, 0, brushes.Count - 1) : -1;
		for (var i = 0; i < brushes.Count; i++)
		{
			var brush = brushes[i];
			var button = new Button
			{
				CustomMinimumSize = new Vector2(84f, 40f),
				ToggleMode = true,
				ButtonPressed = i == clampedIndex,
				TooltipText = brush.Label,
			};
			button.Text = string.IsNullOrWhiteSpace(brush.Glyph)
				? brush.Label
				: $"{brush.Glyph} {brush.Label}";

			var brushIndex = i;
			button.Pressed += () =>
			{
				BrushSelected?.Invoke(brushIndex);
			};
			_brushGrid.AddChild(button);
		}
	}

	private void UpdateCurrentBrushLabel(IReadOnlyList<RuntimeWorldToolBrush> brushes, int selectedIndex)
	{
		if (brushes.Count == 0 || selectedIndex < 0)
		{
			_currentBrushLabel.Text = LocalizationService.TOrFallback("ui.runtime_tool.current.none", "No brush selected");
			return;
		}

		var brush = brushes[Math.Clamp(selectedIndex, 0, brushes.Count - 1)];
		_currentBrushLabel.Text = string.IsNullOrWhiteSpace(brush.Glyph)
			? brush.Label
			: $"{brush.Glyph} {brush.Label}";
	}

	private static Button CreateToggleButton(Node parent)
	{
		var button = new Button
		{
			ToggleMode = true,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		parent.AddChild(button);
		return button;
	}
}
