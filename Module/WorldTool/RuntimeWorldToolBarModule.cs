using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Facility;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Render;

namespace MiniRPG.Module.WorldTool;

internal sealed class RuntimeWorldToolBarModule
{
	private readonly PanelContainer _panel;
	private readonly HBoxContainer _headerRow;
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
	private readonly Dictionary<MapEditorBrushPreview, Texture2D?> _brushPreviewCache = [];

	private IReadOnlyList<RuntimeWorldToolBrush> _lastBrushes = Array.Empty<RuntimeWorldToolBrush>();
	private int _lastSelectedIndex = -1;
	private WorldToolCategory _lastCategory = WorldToolCategory.Terrain;

	// Keep terrain swatches aligned with the map editor bar.
	private static readonly Dictionary<string, Color> TerrainSwatchColors = new()
	{
		["grass_block"] = new Color(0.3f, 0.7f, 0.2f),
		["grass"] = new Color(0.3f, 0.7f, 0.2f),
		["dirt"] = new Color(0.55f, 0.35f, 0.15f),
		["stone"] = new Color(0.5f, 0.5f, 0.5f),
		["sand"] = new Color(0.9f, 0.85f, 0.6f),
		["water"] = new Color(0.2f, 0.4f, 0.8f),
		["mountain"] = new Color(0.4f, 0.4f, 0.45f),
		["wall_stone"] = new Color(0.45f, 0.45f, 0.45f),
		["wall_soil"] = new Color(0.5f, 0.3f, 0.15f),
		["wall_granite"] = new Color(0.35f, 0.35f, 0.38f),
		["wall_obsidian"] = new Color(0.15f, 0.12f, 0.18f),
		["wall_iron"] = new Color(0.55f, 0.55f, 0.6f),
		["tree"] = new Color(0.15f, 0.45f, 0.1f),
		["lava"] = new Color(1.0f, 0.3f, 0.0f),
		["snow"] = new Color(0.95f, 0.95f, 1.0f),
		["ice"] = new Color(0.7f, 0.85f, 1.0f),
		["floor"] = new Color(0.6f, 0.55f, 0.45f),
		["rubble"] = new Color(0.5f, 0.45f, 0.35f),
		["swamp"] = new Color(0.3f, 0.45f, 0.2f),
		["marsh"] = new Color(0.35f, 0.5f, 0.3f),
		["gravel"] = new Color(0.6f, 0.58f, 0.55f),
		["fungus"] = new Color(0.5f, 0.3f, 0.5f),
		["crystal_vein"] = new Color(0.6f, 0.4f, 0.8f),
		["ore_coal"] = new Color(0.2f, 0.2f, 0.2f),
		["ore_iron"] = new Color(0.55f, 0.45f, 0.35f),
		["ore_copper"] = new Color(0.7f, 0.45f, 0.2f),
	};

	private static readonly Color DefaultSwatchColor = new(0.5f, 0.5f, 0.5f);

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

		_headerRow = new HBoxContainer();
		_headerRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		root.AddChild(_headerRow);

		_titleLabel = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		_headerRow.AddChild(_titleLabel);

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
			CustomMinimumSize = new Vector2(0f, 120f),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		root.AddChild(_brushScroll);

		_brushGrid = new GridContainer
		{
			Columns = 6,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		_brushGrid.AddThemeConstantOverride("h_separation", 4);
		_brushGrid.AddThemeConstantOverride("v_separation", 4);
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

	public PanelContainer PanelNode => _panel;
	public Control DragHandle => _headerRow;

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
		var shouldRebuildBrushGrid = !ReferenceEquals(_lastBrushes, brushes)
			|| _lastSelectedIndex != selectedIndex
			|| _lastCategory != category;

		_lastBrushes = brushes;
		_lastSelectedIndex = selectedIndex;
		_lastCategory = category;
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

		if (shouldRebuildBrushGrid)
			RebuildBrushGrid(brushes, selectedIndex, category);

		UpdateCurrentBrushLabel(brushes, selectedIndex);
	}

	private void RebuildBrushGrid(
		IReadOnlyList<RuntimeWorldToolBrush> brushes,
		int selectedIndex,
		WorldToolCategory category)
	{
		foreach (var child in _brushGrid.GetChildren())
			child.QueueFree();

		var clampedIndex = brushes.Count > 0 ? Math.Clamp(selectedIndex, 0, brushes.Count - 1) : -1;
		for (var i = 0; i < brushes.Count; i++)
		{
			var brush = brushes[i];
			var button = new Button
			{
				CustomMinimumSize = new Vector2(48f, 48f),
				ToggleMode = true,
				ButtonPressed = i == clampedIndex,
				TooltipText = brush.Label,
				ClipText = true,
			};

			if (category == WorldToolCategory.Terrain)
			{
				ApplyTerrainSwatchStyle(button, brush);
			}
			else if (!TryConfigurePreviewButton(button, brush))
			{
				button.Text = string.IsNullOrWhiteSpace(brush.Glyph)
					? brush.Id[..Math.Min(2, brush.Id.Length)]
					: brush.Glyph;
			}

			var brushIndex = i;
			button.Pressed += () => BrushSelected?.Invoke(brushIndex);
			_brushGrid.AddChild(button);
		}
	}

	private void ApplyTerrainSwatchStyle(Button button, RuntimeWorldToolBrush brush)
	{
		button.Text = string.Empty;
		var color = TerrainSwatchColors.GetValueOrDefault(brush.Id, DefaultSwatchColor);
		var styleNormal = new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
		};
		var stylePressed = new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusBottomLeft = 4,
			CornerRadiusBottomRight = 4,
			CornerRadiusTopLeft = 4,
			CornerRadiusTopRight = 4,
			BorderColor = new Color(1f, 0.85f, 0.3f),
			BorderWidthBottom = 3,
			BorderWidthTop = 3,
			BorderWidthLeft = 3,
			BorderWidthRight = 3,
		};
		button.AddThemeStyleboxOverride("normal", styleNormal);
		button.AddThemeStyleboxOverride("hover", styleNormal);
		button.AddThemeStyleboxOverride("pressed", stylePressed);
		button.AddThemeStyleboxOverride("focus", stylePressed);
	}

	private void UpdateCurrentBrushLabel(IReadOnlyList<RuntimeWorldToolBrush> brushes, int selectedIndex)
	{
		if (brushes.Count == 0 || selectedIndex < 0)
		{
			_currentBrushLabel.Text = LocalizationService.TOrFallback("ui.runtime_tool.current.none", "No brush selected");
			return;
		}

		var brush = brushes[Math.Clamp(selectedIndex, 0, brushes.Count - 1)];
		_currentBrushLabel.Text = $"[{brush.Id}] {brush.Label}";
	}

	private bool TryConfigurePreviewButton(Button button, RuntimeWorldToolBrush brush)
	{
		if (brush.Preview is not { } preview)
			return false;

		var texture = ResolveBrushPreviewTexture(preview);
		if (texture == null)
			return false;

		button.Text = string.Empty;
		button.Icon = texture;
		button.ExpandIcon = true;
		button.IconAlignment = HorizontalAlignment.Center;
		button.VerticalIconAlignment = VerticalAlignment.Center;
		return true;
	}

	private Texture2D? ResolveBrushPreviewTexture(MapEditorBrushPreview preview)
	{
		if (_brushPreviewCache.TryGetValue(preview, out var cached))
			return cached;

		var texture = ResAccess.Get<Texture2D>(preview.TexturePath);
		Texture2D? resolved = null;
		if (texture != null)
			resolved = preview.Region is { } region
				? CreateRegionPreviewTexture(texture, region)
				: texture;

		_brushPreviewCache[preview] = resolved;
		return resolved;
	}

	private static Texture2D CreateRegionPreviewTexture(Texture2D texture, Rect2I region)
	{
		var clampedRegion = ClampPreviewRegion(texture, region);
		if (clampedRegion.Position == Vector2I.Zero
			&& clampedRegion.Size.X == texture.GetWidth()
			&& clampedRegion.Size.Y == texture.GetHeight())
		{
			return texture;
		}

		return new AtlasTexture
		{
			Atlas = texture,
			Region = new Rect2(
				clampedRegion.Position.X,
				clampedRegion.Position.Y,
				clampedRegion.Size.X,
				clampedRegion.Size.Y),
		};
	}

	private static Rect2I ClampPreviewRegion(Texture2D texture, Rect2I region)
	{
		var textureWidth = Math.Max(1, (int)texture.GetWidth());
		var textureHeight = Math.Max(1, (int)texture.GetHeight());
		var x = Math.Clamp(region.Position.X, 0, textureWidth - 1);
		var y = Math.Clamp(region.Position.Y, 0, textureHeight - 1);
		var width = Math.Clamp(region.Size.X, 1, textureWidth - x);
		var height = Math.Clamp(region.Size.Y, 1, textureHeight - y);
		return new Rect2I(x, y, width, height);
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
