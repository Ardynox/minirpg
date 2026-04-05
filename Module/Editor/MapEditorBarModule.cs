using System;
using System.Collections.Generic;
using Godot;

namespace MiniRPG.Module.Editor;

public sealed class MapEditorBarModule
{
	private readonly PanelContainer _panel;
	private readonly Button _terrainButton;
	private readonly Button _fixtureButton;
	private readonly OptionButton _brushSelect;
	private readonly Label _currentBrushLabel;
	private readonly Button _centerButton;
	private bool _suppressBrushChange;

	public MapEditorBarModule(PanelContainer panel)
	{
		_panel = panel;
		var root = panel.GetNode<VBoxContainer>("Margin/VBox");
		var categoryRow = root.GetNode<HBoxContainer>("CategoryRow");
		_terrainButton = categoryRow.GetNode<Button>("TerrainBtn");
		_fixtureButton = categoryRow.GetNode<Button>("FixtureBtn");
		_brushSelect = root.GetNode<OptionButton>("BrushSelect");
		_currentBrushLabel = root.GetNode<Label>("CurrentBrush");
		var actions = root.GetNode<HBoxContainer>("Actions");
		_centerButton = actions.GetNode<Button>("CenterBtn");

		_terrainButton.Pressed += () => CategorySelected?.Invoke(MapEditorBrushCategory.Terrain);
		_fixtureButton.Pressed += () => CategorySelected?.Invoke(MapEditorBrushCategory.Fixture);
		_brushSelect.ItemSelected += index =>
		{
			if (_suppressBrushChange)
				return;

			BrushSelected?.Invoke((int)index);
		};
		_centerButton.Pressed += () => CenterRequested?.Invoke();
		actions.GetNode<Button>("SaveBtn").Pressed += () => SaveRequested?.Invoke();
		actions.GetNode<Button>("ExitBtn").Pressed += () => ExitRequested?.Invoke();
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

	public void Render(
		MapEditorBrushCategory category,
		IReadOnlyList<MapEditorBrush> brushes,
		int selectedIndex)
	{
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
			_currentBrushLabel.Text = $"当前笔刷: {brushes[clampedIndex].Label}";
		}
		else
		{
			_currentBrushLabel.Text = "当前笔刷: -";
		}
		_suppressBrushChange = false;
	}
}
