using System;
using System.Collections.Generic;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
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
	Environment,
}

internal enum MapEditorPlacementPreviewKind
{
	Terrain,
	Fixture,
}

public enum MapEditorBrushApplyResult
{
	None,
	Applied,
	ConnectivityRequired,
}

public readonly record struct MapEditorBrush(string Id, string Label, string? Glyph = null);

internal readonly record struct MapEditorPlacementPreview(
	MapEditorPlacementPreviewKind Kind,
	Vector3I HoverCell,
	Vector3I TargetCell,
	string BrushId,
	string? BrushGlyph,
	bool CanPlace,
	bool ShowGhost);

public sealed class MapEditorSession
{
	private const int TerrainPreviewScanDepth = 16;
	private readonly GameState _state;
	private readonly MapEditorHistory _history = new();
	private readonly List<MapEditorBrush> _terrainBrushes = [];
	private readonly List<MapEditorBrush> _fixtureBrushes = [];

	private int _terrainBrushIndex;
	private int _fixtureBrushIndex;
	private int _selectedZ;
	private Vector3I? _hoverWorld;

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
	public int CameraZ => _selectedZ;
	public string? SavePath { get; private set; }
	public Vector3I? HoverWorld => _hoverWorld;
	public bool StartedFromMenu => EntryMode == MapEditorEntryMode.MenuBlank;
	public bool CanCenterOnPlayer => EntryMode == MapEditorEntryMode.InGame;
	public bool CanUndo => _history.CanUndo;
	public bool CanRedo => _history.CanRedo;
	public int UndoCount => _history.UndoCount;
	public bool IgnoreConnectivityRequirement { get; private set; }

	public IReadOnlyList<MapEditorBrush> TerrainBrushes => _terrainBrushes;
	public IReadOnlyList<MapEditorBrush> FixtureBrushes => _fixtureBrushes;
	public IReadOnlyList<MapEditorBrush> CurrentBrushes => CurrentCategory switch
	{
		MapEditorBrushCategory.Terrain => _terrainBrushes,
		MapEditorBrushCategory.Fixture => _fixtureBrushes,
		_ => _terrainBrushes,
	};

	public int CurrentBrushIndex =>
		CurrentCategory == MapEditorBrushCategory.Terrain ? _terrainBrushIndex : _fixtureBrushIndex;

	public MapEditorBrush CurrentBrush
	{
		get
		{
			var brushes = CurrentBrushes;
			return brushes.Count > 0 ? brushes[Math.Clamp(CurrentBrushIndex, 0, brushes.Count - 1)] : default;
		}
	}

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
		_selectedZ = _state.PlayerZ;
		_hoverWorld = null;
		IgnoreConnectivityRequirement = false;
		_history.Clear();
	}

	public void Exit()
	{
		Active = false;
		_hoverWorld = null;
		_history.Clear();
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
		_selectedZ = _state.PlayerZ;
	}

	public void AdjustZ(int delta)
	{
		_selectedZ = Math.Clamp(_selectedZ + delta, -20, 20);
	}

	public void ToggleCategory()
	{
		CurrentCategory = CurrentCategory switch
		{
			MapEditorBrushCategory.Terrain => MapEditorBrushCategory.Fixture,
			MapEditorBrushCategory.Fixture => MapEditorBrushCategory.Environment,
			_ => MapEditorBrushCategory.Terrain,
		};
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

	public bool SetHover(Vector3I? hoverWorld)
	{
		if (_hoverWorld == hoverWorld)
			return false;

		_hoverWorld = hoverWorld;
		return true;
	}

	public void SetIgnoreConnectivityRequirement(bool ignore) =>
		IgnoreConnectivityRequirement = ignore;

	internal MapEditorPlacementPreview? ResolvePlacementPreview(Vector3I? hoverWorld)
	{
		var (preview, _) = ResolvePlacementPreviewCore(hoverWorld);
		return preview;
	}

	public MapEditorBrushApplyResult ApplyBrush(int x, int y, int pickedZ)
	{
		if (_state.World == null)
			return MapEditorBrushApplyResult.None;

		var (preview, applyResult) = ResolvePlacementPreviewCore(new Vector3I(x, y, pickedZ));
		if (preview is not { CanPlace: true } resolved)
			return applyResult;

		if (resolved.Kind == MapEditorPlacementPreviewKind.Terrain)
		{
			var oldTerrain = _state.World.GetTerrain(resolved.TargetCell.X, resolved.TargetCell.Y, resolved.TargetCell.Z).StringId;
			_history.Execute(new SetTerrainCommand(
				resolved.TargetCell.X,
				resolved.TargetCell.Y,
				resolved.TargetCell.Z,
				resolved.BrushId,
				oldTerrain), _state.World);
			return MapEditorBrushApplyResult.Applied;
		}

		var oldFixtureId = _state.World.GetFixtureId(resolved.TargetCell.X, resolved.TargetCell.Y, resolved.TargetCell.Z);
		var oldGlyph = EntityAccess.ResolveFixtureGlyph(oldFixtureId);
		var newGlyph = resolved.BrushGlyph ?? string.Empty;
		_history.Execute(new SetFixtureCommand(
			resolved.TargetCell.X,
			resolved.TargetCell.Y,
			resolved.TargetCell.Z,
			resolved.BrushId,
			newGlyph,
			oldFixtureId,
			oldGlyph), _state.World);
		return MapEditorBrushApplyResult.Applied;
	}

	public void EraseBrush(int x, int y, int pickedZ)
	{
		if (_state.World == null)
			return;

		if (CurrentCategory == MapEditorBrushCategory.Terrain)
		{
			var oldTerrain = _state.World.GetTerrain(x, y, pickedZ).StringId;
			if (oldTerrain is Terrains.Air or Terrains.Void) return;
			_history.Execute(new EraseTerrainCommand(x, y, pickedZ, oldTerrain), _state.World);
			return;
		}

		if (CurrentCategory == MapEditorBrushCategory.Fixture)
		{
			var oldFixtureId = _state.World.GetFixtureId(x, y, pickedZ);
			if (string.IsNullOrEmpty(oldFixtureId)) return;
			var oldGlyph = EntityAccess.ResolveFixtureGlyph(oldFixtureId);
			_history.Execute(new EraseFixtureCommand(x, y, pickedZ, oldFixtureId, oldGlyph), _state.World);
		}
	}

	public bool Undo()
	{
		if (_state.World == null) return false;
		return _history.Undo(_state.World);
	}

	public bool Redo()
	{
		if (_state.World == null) return false;
		return _history.Redo(_state.World);
	}

	private (MapEditorPlacementPreview? Preview, MapEditorBrushApplyResult ApplyResult) ResolvePlacementPreviewCore(Vector3I? hoverWorld)
	{
		if (_state.World == null || hoverWorld is not { } hoverCell)
			return (null, MapEditorBrushApplyResult.None);

		var brush = CurrentBrush;
		return CurrentCategory switch
		{
			MapEditorBrushCategory.Terrain => ResolveTerrainPlacementPreview(hoverCell, brush),
			MapEditorBrushCategory.Fixture => ResolveFixturePlacementPreview(hoverCell, brush),
			_ => (null, MapEditorBrushApplyResult.None),
		};
	}

	private (MapEditorPlacementPreview Preview, MapEditorBrushApplyResult ApplyResult) ResolveTerrainPlacementPreview(
		Vector3I hoverCell,
		MapEditorBrush brush)
	{
		var targetCell = ResolveTerrainTargetCell(hoverCell);
		var previewGlyph = TerrainRegistry.Get(brush.Id)?.Glyph;
		if (targetCell == null)
		{
			return (new MapEditorPlacementPreview(
					MapEditorPlacementPreviewKind.Terrain,
					hoverCell,
					hoverCell,
					brush.Id,
					previewGlyph,
					CanPlace: false,
					ShowGhost: false),
				MapEditorBrushApplyResult.None);
		}

		var targetTerrain = _state.World!.GetTerrain(targetCell.Value.X, targetCell.Value.Y, targetCell.Value.Z).StringId;
		if (targetTerrain == brush.Id)
		{
			return (new MapEditorPlacementPreview(
					MapEditorPlacementPreviewKind.Terrain,
					hoverCell,
					targetCell.Value,
					brush.Id,
					previewGlyph,
					CanPlace: false,
					ShowGhost: false),
				MapEditorBrushApplyResult.None);
		}

		var requiresConnectivity = brush.Id is not (Terrains.Air or Terrains.Void) &&
			!IgnoreConnectivityRequirement;
		if (requiresConnectivity && !BlockPlacementRules.HasFaceConnectedTerrain(
				_state.World!,
				targetCell.Value.X,
				targetCell.Value.Y,
				targetCell.Value.Z))
		{
			return (new MapEditorPlacementPreview(
					MapEditorPlacementPreviewKind.Terrain,
					hoverCell,
					targetCell.Value,
					brush.Id,
					previewGlyph,
					CanPlace: false,
					ShowGhost: false),
				MapEditorBrushApplyResult.ConnectivityRequired);
		}

		return (new MapEditorPlacementPreview(
				MapEditorPlacementPreviewKind.Terrain,
				hoverCell,
				targetCell.Value,
				brush.Id,
				previewGlyph,
				CanPlace: true,
				ShowGhost: brush.Id is not (Terrains.Air or Terrains.Void)),
			MapEditorBrushApplyResult.Applied);
	}

	private (MapEditorPlacementPreview Preview, MapEditorBrushApplyResult ApplyResult) ResolveFixturePlacementPreview(
		Vector3I hoverCell,
		MapEditorBrush brush)
	{
		var existingFixtureId = _state.World!.GetFixtureId(hoverCell.X, hoverCell.Y, hoverCell.Z);
		var previewGlyph = brush.Glyph ?? EntityAccess.ResolveFixtureGlyph(brush.Id);
		var canPlace = !string.Equals(existingFixtureId, brush.Id, StringComparison.Ordinal);
		return (new MapEditorPlacementPreview(
				MapEditorPlacementPreviewKind.Fixture,
				hoverCell,
				hoverCell,
				brush.Id,
				previewGlyph,
				canPlace,
				ShowGhost: canPlace),
			canPlace ? MapEditorBrushApplyResult.Applied : MapEditorBrushApplyResult.None);
	}

	private Vector3I? ResolveTerrainTargetCell(Vector3I hoverCell)
	{
		var pickedTerrain = _state.World!.GetTerrain(hoverCell.X, hoverCell.Y, hoverCell.Z).StringId;
		if (pickedTerrain is Terrains.Air or Terrains.Void)
			return hoverCell;

		var placeZ = hoverCell.Z - 1;
		for (var scanned = 0; scanned < TerrainPreviewScanDepth; scanned++, placeZ--)
		{
			var terrain = _state.World.GetTerrain(hoverCell.X, hoverCell.Y, placeZ).StringId;
			if (terrain is Terrains.Air or Terrains.Void)
				return new Vector3I(hoverCell.X, hoverCell.Y, placeZ);
		}

		return null;
	}

	private void RefreshFixtureBrushes()
	{
		_fixtureBrushes.Clear();

		foreach (var (id, def) in FixtureRegistry.All)
		{
			var glyph = EntityAccess.ResolveFixtureGlyph(id);
			_fixtureBrushes.Add(new MapEditorBrush(
				id,
				GameLocalizer.LocalizeFixtureName(id),
				glyph));
		}

		if (_fixtureBrushes.Count == 0)
		{
			_fixtureBrushes.Add(new MapEditorBrush(Entities.Door, GameLocalizer.LocalizeFixtureName(Entities.Door), "D"));
		}

		_fixtureBrushIndex = Math.Clamp(_fixtureBrushIndex, 0, Math.Max(0, _fixtureBrushes.Count - 1));
	}
}
