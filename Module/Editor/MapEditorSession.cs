using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.World;

namespace MiniRPG.Module.Editor;

public enum MapEditorEntryMode
{
	MenuBlank,
	InGame,
}

public enum MapEditorBrushCategory
{
	Terrain,
	Fixture,
}

public readonly record struct MapEditorBrush(string Id, string Label, string? Glyph = null);

public sealed class MapEditorSession
{
	private readonly GameState _state;
	private readonly List<MapEditorBrush> _terrainBrushes = [];
	private readonly List<MapEditorBrush> _fixtureBrushes = [];

	private int _terrainBrushIndex;
	private int _fixtureBrushIndex;
	private Vector2I? _hoverWorld;

	public MapEditorSession(GameState state)
	{
		_state = state;
		RefreshLocalizedBrushes();
	}

	public bool Active { get; private set; }
	public MapEditorEntryMode EntryMode { get; private set; } = MapEditorEntryMode.InGame;
	public MapEditorBrushCategory CurrentCategory { get; private set; } = MapEditorBrushCategory.Terrain;
	public int CameraX { get; private set; }
	public int CameraY { get; private set; }
	public int CameraZ => _state.PlayerZ;
	public string? SavePath { get; private set; }
	public Vector2I? HoverWorld => _hoverWorld;
	public bool StartedFromMenu => EntryMode == MapEditorEntryMode.MenuBlank;
	public bool CanCenterOnPlayer => EntryMode == MapEditorEntryMode.InGame;

	public IReadOnlyList<MapEditorBrush> TerrainBrushes => _terrainBrushes;
	public IReadOnlyList<MapEditorBrush> FixtureBrushes => _fixtureBrushes;
	public IReadOnlyList<MapEditorBrush> CurrentBrushes =>
		CurrentCategory == MapEditorBrushCategory.Terrain ? _terrainBrushes : _fixtureBrushes;

	public int CurrentBrushIndex =>
		CurrentCategory == MapEditorBrushCategory.Terrain ? _terrainBrushIndex : _fixtureBrushIndex;

	public MapEditorBrush CurrentBrush => CurrentBrushes[Math.Clamp(CurrentBrushIndex, 0, CurrentBrushes.Count - 1)];

	public void RefreshTerrainBrushes()
	{
		_terrainBrushes.Clear();
		foreach (var terrain in TerrainRegistry.All)
		{
			if (terrain.StringId == Terrains.Void)
				continue;

			_terrainBrushes.Add(new MapEditorBrush(
				terrain.StringId,
				GameLocalizer.LocalizeTerrainName(terrain.StringId)));
		}

		if (_terrainBrushes.Count == 0)
			_terrainBrushes.Add(new MapEditorBrush(Terrains.Floor, Terrains.Floor));

		_terrainBrushIndex = Math.Clamp(_terrainBrushIndex, 0, _terrainBrushes.Count - 1);
	}

	public void RefreshLocalizedBrushes()
	{
		RefreshTerrainBrushes();
		RefreshFixtureBrushes();
	}

	public void Enter(MapEditorEntryMode entryMode, string? savePath)
	{
		RefreshLocalizedBrushes();
		Active = true;
		EntryMode = entryMode;
		CurrentCategory = MapEditorBrushCategory.Terrain;
		SavePath = savePath;
		CameraX = _state.PlayerX;
		CameraY = _state.PlayerY;
		_hoverWorld = null;
	}

	public void Exit()
	{
		Active = false;
		_hoverWorld = null;
	}

	public void UpdateSavePath(string? savePath)
	{
		SavePath = savePath;
	}

	public void MoveCamera(int dx, int dy)
	{
		CameraX += dx;
		CameraY += dy;
	}

	public void CenterOnPlayer()
	{
		CameraX = _state.PlayerX;
		CameraY = _state.PlayerY;
	}

	public void ToggleCategory()
	{
		CurrentCategory = CurrentCategory == MapEditorBrushCategory.Terrain
			? MapEditorBrushCategory.Fixture
			: MapEditorBrushCategory.Terrain;
	}

	public void SelectCategory(MapEditorBrushCategory category)
	{
		CurrentCategory = category;
	}

	public void SelectBrush(int index)
	{
		var brushes = CurrentBrushes;
		if (brushes.Count == 0)
			return;

		var normalized = Math.Clamp(index, 0, brushes.Count - 1);
		if (CurrentCategory == MapEditorBrushCategory.Terrain)
			_terrainBrushIndex = normalized;
		else
			_fixtureBrushIndex = normalized;
	}

	public void CycleBrush(int delta)
	{
		var brushes = CurrentBrushes;
		if (brushes.Count == 0)
			return;

		var count = brushes.Count;
		var next = ((CurrentBrushIndex + delta) % count + count) % count;
		SelectBrush(next);
	}

	public bool SetHover(Vector2I? hoverWorld)
	{
		if (_hoverWorld == hoverWorld)
			return false;

		_hoverWorld = hoverWorld;
		return true;
	}

	public void ApplyBrush(int x, int y)
	{
		if (_state.World == null)
			return;

		var z = CameraZ;
		var brush = CurrentBrush;
		if (CurrentCategory == MapEditorBrushCategory.Terrain)
		{
			_state.World.SetTerrain(x, y, z, brush.Id);
			return;
		}

		_state.World.SetFixture(x, y, z, brush.Glyph ?? string.Empty, brush.Id);
	}

	public void EraseBrush(int x, int y)
	{
		if (_state.World == null)
			return;

		var z = CameraZ;
		if (CurrentCategory == MapEditorBrushCategory.Terrain)
		{
			_state.World.SetTerrain(x, y, z, Terrains.Floor);
			return;
		}

		_state.World.SetFixture(x, y, z, string.Empty, string.Empty);
	}

	private void RefreshFixtureBrushes()
	{
		_fixtureBrushes.Clear();
		_fixtureBrushes.Add(new MapEditorBrush(Entities.Door, GameLocalizer.LocalizeFixtureName(Entities.Door), "D"));
		_fixtureBrushes.Add(new MapEditorBrush(Entities.StairUp, GameLocalizer.LocalizeFixtureName(Entities.StairUp), "<"));
		_fixtureBrushes.Add(new MapEditorBrush(Entities.StairDown, GameLocalizer.LocalizeFixtureName(Entities.StairDown), ">"));
		_fixtureBrushes.Add(new MapEditorBrush(Entities.Nest, GameLocalizer.LocalizeFixtureName(Entities.Nest), "N"));
		_fixtureBrushes.Add(new MapEditorBrush(Entities.House, GameLocalizer.LocalizeFixtureName(Entities.House), "H"));
		_fixtureBrushIndex = Math.Clamp(_fixtureBrushIndex, 0, _fixtureBrushes.Count - 1);
	}
}
