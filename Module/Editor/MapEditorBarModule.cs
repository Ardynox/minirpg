using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Weather;

namespace MiniRPG.Module.Editor;

public sealed class MapEditorBarModule
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Button _terrainButton;
	private readonly Button _fixtureButton;
	private readonly Button _environmentButton;
	private readonly HBoxContainer _toolRow;
	private readonly Button _selectToolButton;
	private readonly Button _buildToolButton;
	private readonly Button _demolishToolButton;
	private readonly ScrollContainer _brushScroll;
	private readonly GridContainer _brushGrid;
	private readonly Label _currentBrushLabel;
	private readonly VBoxContainer _environmentControls;
	private readonly HSlider _timeSlider;
	private readonly OptionButton _weatherSelect;
	private readonly OptionButton _intensitySelect;
	private readonly OptionButton _lightingSelect;
	private readonly Button _turnPlayButton;
	private readonly Label _infoBar;
	private readonly Button _undoButton;
	private readonly Button _redoButton;
	private readonly Label _historyCountLabel;
	private readonly Button _heightDownButton;
	private readonly Label _heightLabel;
	private readonly Button _heightUpButton;
	private readonly HBoxContainer _optionsRow;
	private readonly CheckButton _ignoreConnectivityButton;
	private readonly Label _hintLabel;
	private readonly Button _centerButton;
	private readonly Button _saveButton;
	private readonly Button _exitButton;

	private IReadOnlyList<MapEditorBrush> _lastBrushes = Array.Empty<MapEditorBrush>();
	private int _lastSelectedIndex = -1;
	private MapEditorBrushCategory _lastCategory = MapEditorBrushCategory.Terrain;
	private bool _suppressEvents;

	// ── Terrain color swatches (matches IsometricVoxelRenderer.TerrainColors) ──
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
	private static readonly string[] WeatherOptionTextKeys =
	[
		"weather.type.clear",
		"weather.type.rain",
		"weather.type.fog",
		"weather.type.snow",
		"weather.type.storm",
		"weather.type.thunderstorm",
		"weather.type.sandstorm",
	];
	private static readonly string[] WeatherIntensityTextKeys =
	[
		"weather.intensity.light",
		"weather.intensity.normal",
		"weather.intensity.heavy",
	];
	private static readonly string[] LightingProfileTextKeys =
	[
		"render.lighting.profile.default",
		"render.lighting.profile.cinematic",
		"render.lighting.profile.soft",
	];

	public MapEditorBarModule(PanelContainer panel)
	{
		_panel = panel;
		var root = panel.GetNode<VBoxContainer>("Margin/VBox");
		_titleLabel = root.GetNode<Label>("Title");
		var categoryRow = root.GetNode<HBoxContainer>("CategoryRow");
		_terrainButton = categoryRow.GetNode<Button>("TerrainBtn");
		_fixtureButton = categoryRow.GetNode<Button>("FixtureBtn");
		_environmentButton = categoryRow.GetNode<Button>("EnvironmentBtn");
		_toolRow = root.GetNode<HBoxContainer>("ToolRow");
		_selectToolButton = _toolRow.GetNode<Button>("SelectBtn");
		_buildToolButton = _toolRow.GetNode<Button>("BuildBtn");
		_demolishToolButton = _toolRow.GetNode<Button>("DemolishBtn");
		_brushScroll = root.GetNode<ScrollContainer>("BrushScroll");
		_brushGrid = root.GetNode<GridContainer>("BrushScroll/BrushGrid");
		_currentBrushLabel = root.GetNode<Label>("CurrentBrush");
		_environmentControls = root.GetNode<VBoxContainer>("EnvironmentControls");
		_timeSlider = _environmentControls.GetNode<HSlider>("TimeSlider");
		_weatherSelect = _environmentControls.GetNode<OptionButton>("WeatherSelect");
		_intensitySelect = _environmentControls.GetNode<OptionButton>("IntensitySelect");
		_lightingSelect = _environmentControls.GetNode<OptionButton>("LightingSelect");
		_turnPlayButton = _environmentControls.GetNode<Button>("TurnPlayBtn");
		_infoBar = root.GetNode<Label>("InfoBar");
		var undoRedoRow = root.GetNode<HBoxContainer>("UndoRedoRow");
		_undoButton = undoRedoRow.GetNode<Button>("UndoBtn");
		_redoButton = undoRedoRow.GetNode<Button>("RedoBtn");
		_historyCountLabel = undoRedoRow.GetNode<Label>("HistoryCount");
		var heightRow = root.GetNode<HBoxContainer>("HeightRow");
		_heightDownButton = heightRow.GetNode<Button>("HeightDownBtn");
		_heightLabel = heightRow.GetNode<Label>("HeightLabel");
		_heightUpButton = heightRow.GetNode<Button>("HeightUpBtn");
		_optionsRow = root.GetNode<HBoxContainer>("OptionsRow");
		_ignoreConnectivityButton = _optionsRow.GetNode<CheckButton>("IgnoreSupportBtn");
		_hintLabel = root.GetNode<Label>("Hint");
		var actions = root.GetNode<HBoxContainer>("Actions");
		_centerButton = actions.GetNode<Button>("CenterBtn");
		_saveButton = actions.GetNode<Button>("SaveBtn");
		_exitButton = actions.GetNode<Button>("ExitBtn");

		_terrainButton.Pressed += () => CategorySelected?.Invoke(MapEditorBrushCategory.Terrain);
		_fixtureButton.Pressed += () => CategorySelected?.Invoke(MapEditorBrushCategory.Fixture);
		_environmentButton.Pressed += () => CategorySelected?.Invoke(MapEditorBrushCategory.Environment);
		_selectToolButton.Pressed += () => ToolModeSelected?.Invoke(MapEditorToolMode.Select);
		_buildToolButton.Pressed += () => ToolModeSelected?.Invoke(MapEditorToolMode.Build);
		_demolishToolButton.Pressed += () => ToolModeSelected?.Invoke(MapEditorToolMode.Demolish);
		_undoButton.Pressed += () => UndoRequested?.Invoke();
		_redoButton.Pressed += () => RedoRequested?.Invoke();
		_heightDownButton.Pressed += () => HeightChanged?.Invoke(1);
		_heightUpButton.Pressed += () => HeightChanged?.Invoke(-1);
		_centerButton.Pressed += () => CenterRequested?.Invoke();
		_saveButton.Pressed += () => SaveRequested?.Invoke();
		_exitButton.Pressed += () => ExitRequested?.Invoke();
		_ignoreConnectivityButton.Toggled += pressed =>
		{
			if (!_suppressEvents) IgnoreConnectivityRequirementChanged?.Invoke(pressed);
		};

		_timeSlider.ValueChanged += value =>
		{
			if (!_suppressEvents) TimeOfDayChanged?.Invoke((int)value);
		};
		_weatherSelect.ItemSelected += index =>
		{
			if (!_suppressEvents) WeatherTypeChanged?.Invoke((int)index);
		};
		_intensitySelect.ItemSelected += index =>
		{
			if (!_suppressEvents) WeatherIntensityChanged?.Invoke((int)index);
		};
		_lightingSelect.ItemSelected += index =>
		{
			if (!_suppressEvents) LightingProfileChanged?.Invoke((int)index);
		};

		_turnPlayButton.Pressed += () => TurnControllerRequested?.Invoke();

		RefreshTexts();
	}

	public bool IsPointerOver(Vector2 globalPos)
		=> _panel.Visible && _panel.GetGlobalRect().HasPoint(globalPos);

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	// ── Events ──
	public event Action<MapEditorBrushCategory>? CategorySelected;
	public event Action<MapEditorToolMode>? ToolModeSelected;
	public event Action<int>? BrushSelected;
	public event Action? UndoRequested;
	public event Action? RedoRequested;
	public event Action? SaveRequested;
	public event Action? ExitRequested;
	public event Action? CenterRequested;
	public event Action<int>? HeightChanged;
	public event Action<bool>? IgnoreConnectivityRequirementChanged;
	public event Action<int>? TimeOfDayChanged;
	public event Action<int>? WeatherTypeChanged;
	public event Action<int>? WeatherIntensityChanged;
	public event Action<int>? LightingProfileChanged;
	public event Action? TurnControllerRequested;

	public void Open(bool showCenterButton)
	{
		_centerButton.Visible = showCenterButton;
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
	}

	public void RefreshTexts()
	{
		_titleLabel.Text = LocalizationService.T("ui.map_editor.title");
		_terrainButton.Text = LocalizationService.T("ui.map_editor.category.terrain");
		_fixtureButton.Text = LocalizationService.T("ui.map_editor.category.fixture");
		_environmentButton.Text = LocalizationService.T("ui.map_editor.category.environment");
		_selectToolButton.Text = LocalizationService.T("ui.map_editor.tool.select");
		_buildToolButton.Text = LocalizationService.T("ui.map_editor.tool.build");
		_demolishToolButton.Text = LocalizationService.T("ui.map_editor.tool.demolish");
		_hintLabel.Text = LocalizationService.T("ui.map_editor.hint.v2");
		_centerButton.Text = LocalizationService.T("ui.map_editor.center");
		_saveButton.Text = LocalizationService.T("ui.map_editor.save");
		_exitButton.Text = LocalizationService.T("ui.map_editor.exit");
		_undoButton.Text = LocalizationService.T("ui.map_editor.undo");
		_redoButton.Text = LocalizationService.T("ui.map_editor.redo");
		_ignoreConnectivityButton.Text = LocalizationService.T("ui.map_editor.ignore_support");
		_turnPlayButton.Text = LocalizationService.T("ui.map_editor.turn_controller");
		RebuildEnvironmentOptions();
		UpdateCurrentBrushLabel();
	}

	public void Render(
		MapEditorBrushCategory category,
		MapEditorToolMode toolMode,
		IReadOnlyList<MapEditorBrush> brushes,
		int selectedIndex)
	{
		var shouldRebuildBrushGrid = !ReferenceEquals(_lastBrushes, brushes)
			|| _lastSelectedIndex != selectedIndex
			|| _lastCategory != category;

		_lastBrushes = brushes;
		_lastSelectedIndex = selectedIndex;
		_lastCategory = category;
		_terrainButton.ButtonPressed = category == MapEditorBrushCategory.Terrain;
		_fixtureButton.ButtonPressed = category == MapEditorBrushCategory.Fixture;
		_environmentButton.ButtonPressed = category == MapEditorBrushCategory.Environment;
		_selectToolButton.ButtonPressed = toolMode == MapEditorToolMode.Select;
		_buildToolButton.ButtonPressed = toolMode == MapEditorToolMode.Build;
		_demolishToolButton.ButtonPressed = toolMode == MapEditorToolMode.Demolish;

		var isBrushCategory = category is MapEditorBrushCategory.Terrain or MapEditorBrushCategory.Fixture;
		_toolRow.Visible = isBrushCategory;
		_brushScroll.Visible = isBrushCategory;
		_currentBrushLabel.Visible = isBrushCategory;
		_environmentControls.Visible = category == MapEditorBrushCategory.Environment;
		_optionsRow.Visible = category == MapEditorBrushCategory.Terrain;

		if (isBrushCategory && shouldRebuildBrushGrid)
			RebuildBrushGrid(brushes, selectedIndex, category);

		UpdateCurrentBrushLabel();
	}

	public void UpdateInfo(int cameraX, int cameraY, int cameraZ, bool canUndo, bool canRedo, int undoCount)
	{
		_infoBar.Text = $"X: {cameraX}  Y: {cameraY}  Z: {cameraZ}";
		_undoButton.Disabled = !canUndo;
		_redoButton.Disabled = !canRedo;
		_historyCountLabel.Text = undoCount.ToString();
	}

	public void UpdateHeight(int z)
	{
		_heightLabel.Text = $"Z: {z}";
	}

	public void SetIgnoreConnectivityRequirement(bool ignore)
	{
		_suppressEvents = true;
		try
		{
			_ignoreConnectivityButton.ButtonPressed = ignore;
		}
		finally
		{
			_suppressEvents = false;
		}
	}

	public void SetEnvironmentState(int timeOfDay, int weatherTypeIndex, int intensityIndex, int lightingIndex)
	{
		_suppressEvents = true;
		try
		{
			_timeSlider.Value = timeOfDay;
			if (weatherTypeIndex >= 0 && weatherTypeIndex < _weatherSelect.ItemCount)
				_weatherSelect.Select(weatherTypeIndex);
			if (intensityIndex >= 0 && intensityIndex < _intensitySelect.ItemCount)
				_intensitySelect.Select(intensityIndex);
			if (lightingIndex >= 0 && lightingIndex < _lightingSelect.ItemCount)
				_lightingSelect.Select(lightingIndex);
		}
		finally
		{
			_suppressEvents = false;
		}
	}

	// ── Private ──

	private void RebuildBrushGrid(IReadOnlyList<MapEditorBrush> brushes, int selectedIndex, MapEditorBrushCategory category)
	{
		// Clear existing buttons
		foreach (var child in _brushGrid.GetChildren())
			child.QueueFree();

		var clampedIndex = brushes.Count > 0 ? Math.Clamp(selectedIndex, 0, brushes.Count - 1) : -1;

		for (var i = 0; i < brushes.Count; i++)
		{
			var brush = brushes[i];
			var btn = new Button
			{
				CustomMinimumSize = new Vector2(48, 48),
				ToggleMode = true,
				ButtonPressed = i == clampedIndex,
				TooltipText = brush.Label,
				ClipText = true,
			};

			if (category == MapEditorBrushCategory.Terrain)
			{
				// Color swatch for terrain
				var color = TerrainSwatchColors.GetValueOrDefault(brush.Id, DefaultSwatchColor);
				var styleNormal = new StyleBoxFlat { BgColor = color, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4 };
				var stylePressed = new StyleBoxFlat { BgColor = color, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, BorderColor = new Color(1f, 0.85f, 0.3f), BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3 };
				btn.AddThemeStyleboxOverride("normal", styleNormal);
				btn.AddThemeStyleboxOverride("hover", styleNormal);
				btn.AddThemeStyleboxOverride("pressed", stylePressed);
				btn.AddThemeStyleboxOverride("focus", stylePressed);
			}
			else
			{
				// Glyph label for fixture
				btn.Text = brush.Glyph ?? brush.Id[..Math.Min(2, brush.Id.Length)];
			}

			var index = i;
			btn.Pressed += () => BrushSelected?.Invoke(index);
			_brushGrid.AddChild(btn);
		}
	}

	private void UpdateCurrentBrushLabel()
	{
		if (_lastBrushes.Count == 0 || _lastSelectedIndex < 0)
		{
			_currentBrushLabel.Text = LocalizationService.TOrFallback("ui.map_editor.current_brush.none", "No brush selected");
			return;
		}

		var clampedIndex = Math.Clamp(_lastSelectedIndex, 0, _lastBrushes.Count - 1);
		var brush = _lastBrushes[clampedIndex];
		_currentBrushLabel.Text = $"[{brush.Id}] {brush.Label}";
	}

	private void RebuildEnvironmentOptions()
	{
		var weatherIndex = _weatherSelect.ItemCount > 0 ? (int)_weatherSelect.Selected : 0;
		var intensityIndex = _intensitySelect.ItemCount > 0 ? (int)_intensitySelect.Selected : 1;
		var lightingIndex = _lightingSelect.ItemCount > 0 ? (int)_lightingSelect.Selected : 0;

		_suppressEvents = true;
		try
		{
			RebuildOptionButton(_weatherSelect, WeatherOptionTextKeys, weatherIndex);
			RebuildOptionButton(_intensitySelect, WeatherIntensityTextKeys, intensityIndex);
			RebuildOptionButton(_lightingSelect, LightingProfileTextKeys, lightingIndex);
		}
		finally
		{
			_suppressEvents = false;
		}
	}

	private static void RebuildOptionButton(OptionButton button, IReadOnlyList<string> textKeys, int selectedIndex)
	{
		button.Clear();
		for (var i = 0; i < textKeys.Count; i++)
			button.AddItem(LocalizationService.T(textKeys[i]), i);

		if (button.ItemCount == 0)
			return;

		var clampedIndex = selectedIndex >= 0 && selectedIndex < button.ItemCount
			? selectedIndex
			: 0;
		button.Select(clampedIndex);
	}
}
