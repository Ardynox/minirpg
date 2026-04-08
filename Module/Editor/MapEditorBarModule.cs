using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Editor;

public sealed class MapEditorBarModule
{
	private readonly PanelContainer _panel;
	private readonly Label _titleLabel;
	private readonly Button _terrainButton;
	private readonly Button _fixtureButton;
	private readonly OptionButton _brushSelect;
	private readonly Label _currentBrushLabel;
	private readonly Label _hintLabel;
	private readonly Button _centerButton;
	private readonly Button _saveButton;
	private readonly Button _exitButton;
	private bool _suppressBrushChange;
	private IReadOnlyList<MapEditorBrush> _lastBrushes = Array.Empty<MapEditorBrush>();
	private int _lastSelectedIndex = -1;

	public MapEditorBarModule(PanelContainer panel)
	{
		_panel = panel;
		var root = panel.GetNode<VBoxContainer>("Margin/VBox");
		_titleLabel = root.GetNode<Label>("Title");
		var categoryRow = root.GetNode<HBoxContainer>("CategoryRow");
		_terrainButton = categoryRow.GetNode<Button>("TerrainBtn");
		_fixtureButton = categoryRow.GetNode<Button>("FixtureBtn");
		_brushSelect = root.GetNode<OptionButton>("BrushSelect");
		_currentBrushLabel = root.GetNode<Label>("CurrentBrush");
		_hintLabel = root.GetNode<Label>("Hint");
		var actions = root.GetNode<HBoxContainer>("Actions");
		_centerButton = actions.GetNode<Button>("CenterBtn");
		_saveButton = actions.GetNode<Button>("SaveBtn");
		_exitButton = actions.GetNode<Button>("ExitBtn");

		_terrainButton.Pressed += () => CategorySelected?.Invoke(MapEditorBrushCategory.Terrain);
		_fixtureButton.Pressed += () => CategorySelected?.Invoke(MapEditorBrushCategory.Fixture);
		_brushSelect.ItemSelected += index =>
		{
			if (_suppressBrushChange)
				return;

			BrushSelected?.Invoke((int)index);
		};
		_centerButton.Pressed += () => CenterRequested?.Invoke();
		_saveButton.Pressed += () => SaveRequested?.Invoke();
		_exitButton.Pressed += () => ExitRequested?.Invoke();
		RefreshTexts();
	}

	public bool Visible
	{
		get => _panel.Visible;
		set => _panel.Visible = value;
	}

	public event Action<MapEditorBrushCategory>? CategorySelected;
	public event Action<int>? BrushSelected;
	public event Action? SaveRequested;
	public event Action? ExitRequested;
	public event Action? CenterRequested;

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
		_hintLabel.Text = LocalizationService.T("ui.map_editor.hint");
		_centerButton.Text = LocalizationService.T("ui.map_editor.center");
		_saveButton.Text = LocalizationService.T("ui.map_editor.save");
		_exitButton.Text = LocalizationService.T("ui.map_editor.exit");
		UpdateCurrentBrushLabel();
	}

	public void Render(
		MapEditorBrushCategory category,
		IReadOnlyList<MapEditorBrush> brushes,
		int selectedIndex)
	{
		_lastBrushes = brushes;
		_lastSelectedIndex = selectedIndex;
		_terrainButton.ButtonPressed = category == MapEditorBrushCategory.Terrain;
		_fixtureButton.ButtonPressed = category == MapEditorBrushCategory.Fixture;

		_suppressBrushChange = true;
		_brushSelect.Clear();
		for (var i = 0; i < brushes.Count; i++)
			_brushSelect.AddItem(brushes[i].Label);

		if (brushes.Count > 0)
		{
			var clampedIndex = Math.Clamp(selectedIndex, 0, brushes.Count - 1);
			_brushSelect.Select(clampedIndex);
			_currentBrushLabel.Text = LocalizationService.T("ui.map_editor.current_brush", ("brush", brushes[clampedIndex].Label));
		}
		else
		{
			_currentBrushLabel.Text = LocalizationService.T("ui.map_editor.current_brush.none");
		}
		_suppressBrushChange = false;
	}

	private void UpdateCurrentBrushLabel()
	{
		if (_lastBrushes.Count == 0 || _lastSelectedIndex < 0)
		{
			_currentBrushLabel.Text = LocalizationService.T("ui.map_editor.current_brush.none");
			return;
		}

		var clampedIndex = Math.Clamp(_lastSelectedIndex, 0, _lastBrushes.Count - 1);
		_currentBrushLabel.Text = LocalizationService.T("ui.map_editor.current_brush", ("brush", _lastBrushes[clampedIndex].Label));
	}
}
