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

public enum MapEditorToolMode
{
	Select,
	Build,
	Demolish,
}

internal enum MapEditorHoverStateKind
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

internal readonly record struct MapEditorHoverState(
	MapEditorToolMode ToolMode,
	MapEditorBrushCategory Category,
	MapEditorHoverStateKind Kind,
	Vector3I RawHoverCell,
	Vector3I? ResolvedTargetCell,
	string BrushId,
	string? BrushGlyph,
	bool CanApply,
	bool ShowGhost,
	bool ShowInfoOverlay);

public sealed class MapEditorSession
{
	private const int TerrainColumnScanDepth = 16;
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
	public MapEditorToolMode CurrentToolMode { get; private set; } = MapEditorToolMode.Build;
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
		_ => Array.Empty<MapEditorBrush>(),
	};

	public int CurrentBrushIndex => CurrentCategory switch
	{
		MapEditorBrushCategory.Terrain => _terrainBrushIndex,
		MapEditorBrushCategory.Fixture => _fixtureBrushIndex,
		_ => -1,
	};

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
		CurrentToolMode = MapEditorToolMode.Build;
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

	public void SelectToolMode(MapEditorToolMode toolMode)
	{
		CurrentToolMode = toolMode;
	}

	public void SelectBrush(int index)
	{
		if (CurrentCategory is not (MapEditorBrushCategory.Terrain or MapEditorBrushCategory.Fixture))
			return;

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
		if (CurrentCategory is not (MapEditorBrushCategory.Terrain or MapEditorBrushCategory.Fixture))
			return;

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

	internal MapEditorHoverState? ResolveHoverState(Vector3I? hoverWorld)
	{
		var (hoverState, _) = ResolveHoverStateCore(hoverWorld);
		return hoverState;
	}

	public MapEditorBrushApplyResult ApplyBrush(int x, int y, int pickedZ)
	{
		if (_state.World == null)
			return MapEditorBrushApplyResult.None;

		var (hoverState, applyResult) = ResolveHoverStateCore(
			new Vector3I(x, y, pickedZ),
			MapEditorToolMode.Build);
		if (hoverState is not { CanApply: true, ResolvedTargetCell: { } targetCell } resolved)
			return applyResult;

		if (resolved.Kind == MapEditorHoverStateKind.Terrain)
		{
			var oldTerrain = _state.World.GetTerrain(targetCell.X, targetCell.Y, targetCell.Z).StringId;
			_history.Execute(new SetTerrainCommand(
				targetCell.X,
				targetCell.Y,
				targetCell.Z,
				resolved.BrushId,
				oldTerrain), _state.World);
			return MapEditorBrushApplyResult.Applied;
		}

		var oldFixtureId = _state.World.GetFixtureId(targetCell.X, targetCell.Y, targetCell.Z);
		var oldGlyph = EntityAccess.ResolveFixtureGlyph(oldFixtureId);
		var newGlyph = resolved.BrushGlyph ?? string.Empty;
		_history.Execute(new SetFixtureCommand(
			targetCell.X,
			targetCell.Y,
			targetCell.Z,
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

		var (hoverState, _) = ResolveHoverStateCore(
			new Vector3I(x, y, pickedZ),
			MapEditorToolMode.Demolish);
		if (hoverState is not { CanApply: true, ResolvedTargetCell: { } targetCell } resolved)
			return;

		if (resolved.Kind == MapEditorHoverStateKind.Terrain)
		{
			var oldTerrain = _state.World.GetTerrain(targetCell.X, targetCell.Y, targetCell.Z).StringId;
			if (oldTerrain is Terrains.Air or Terrains.Void) return;
			_history.Execute(new EraseTerrainCommand(targetCell.X, targetCell.Y, targetCell.Z, oldTerrain), _state.World);
			return;
		}

		var oldFixtureId = _state.World.GetFixtureId(targetCell.X, targetCell.Y, targetCell.Z);
		if (string.IsNullOrEmpty(oldFixtureId)) return;
		var oldGlyph = EntityAccess.ResolveFixtureGlyph(oldFixtureId);
		_history.Execute(new EraseFixtureCommand(targetCell.X, targetCell.Y, targetCell.Z, oldFixtureId, oldGlyph), _state.World);
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

	private (MapEditorHoverState? HoverState, MapEditorBrushApplyResult ApplyResult) ResolveHoverStateCore(
		Vector3I? hoverWorld,
		MapEditorToolMode? toolModeOverride = null)
	{
		if (_state.World == null || hoverWorld is not { } hoverCell)
			return (null, MapEditorBrushApplyResult.None);

		if (CurrentCategory == MapEditorBrushCategory.Environment)
			return (null, MapEditorBrushApplyResult.None);

		var brush = CurrentBrush;
		if (string.IsNullOrWhiteSpace(brush.Id))
			return (null, MapEditorBrushApplyResult.None);

		var toolMode = toolModeOverride ?? CurrentToolMode;
		return CurrentCategory switch
		{
			MapEditorBrushCategory.Terrain when toolMode == MapEditorToolMode.Build => ResolveTerrainBuildHoverState(hoverCell, brush),
			MapEditorBrushCategory.Terrain => ResolveTerrainOccupiedHoverState(toolMode, hoverCell, brush),
			MapEditorBrushCategory.Fixture when toolMode == MapEditorToolMode.Build => ResolveFixtureBuildHoverState(hoverCell, brush),
			MapEditorBrushCategory.Fixture => ResolveFixtureOccupiedHoverState(toolMode, hoverCell, brush),
			_ => (null, MapEditorBrushApplyResult.None),
		};
	}

	private (MapEditorHoverState HoverState, MapEditorBrushApplyResult ApplyResult) ResolveTerrainBuildHoverState(
		Vector3I hoverCell,
		MapEditorBrush brush)
	{
		var targetCell = ResolveTerrainBuildTargetCell(hoverCell);
		var previewGlyph = TerrainRegistry.Get(brush.Id)?.Glyph;
		if (targetCell is not { } resolvedTargetCell)
		{
			return (CreateHoverState(
					MapEditorToolMode.Build,
					MapEditorBrushCategory.Terrain,
					MapEditorHoverStateKind.Terrain,
					hoverCell,
					null,
					brush,
					previewGlyph,
					canApply: false,
					showGhost: false,
					showInfoOverlay: false),
				MapEditorBrushApplyResult.None);
		}

		var targetTerrain = _state.World!.GetTerrain(resolvedTargetCell.X, resolvedTargetCell.Y, resolvedTargetCell.Z).StringId;
		if (targetTerrain == brush.Id)
		{
			return (CreateHoverState(
					MapEditorToolMode.Build,
					MapEditorBrushCategory.Terrain,
					MapEditorHoverStateKind.Terrain,
					hoverCell,
					resolvedTargetCell,
					brush,
					previewGlyph,
					canApply: false,
					showGhost: false,
					showInfoOverlay: false),
				MapEditorBrushApplyResult.None);
		}

		var requiresConnectivity = brush.Id is not (Terrains.Air or Terrains.Void) &&
			!IgnoreConnectivityRequirement;
		if (requiresConnectivity && !BlockPlacementRules.HasFaceConnectedTerrain(
				_state.World!,
				resolvedTargetCell.X,
				resolvedTargetCell.Y,
				resolvedTargetCell.Z))
		{
			return (CreateHoverState(
					MapEditorToolMode.Build,
					MapEditorBrushCategory.Terrain,
					MapEditorHoverStateKind.Terrain,
					hoverCell,
					resolvedTargetCell,
					brush,
					previewGlyph,
					canApply: false,
					showGhost: false,
					showInfoOverlay: false),
				MapEditorBrushApplyResult.ConnectivityRequired);
		}

		return (CreateHoverState(
				MapEditorToolMode.Build,
				MapEditorBrushCategory.Terrain,
				MapEditorHoverStateKind.Terrain,
				hoverCell,
				resolvedTargetCell,
				brush,
				previewGlyph,
				canApply: true,
				showGhost: brush.Id is not (Terrains.Air or Terrains.Void),
				showInfoOverlay: false),
			MapEditorBrushApplyResult.Applied);
	}

	private (MapEditorHoverState HoverState, MapEditorBrushApplyResult ApplyResult) ResolveTerrainOccupiedHoverState(
		MapEditorToolMode toolMode,
		Vector3I hoverCell,
		MapEditorBrush brush)
	{
		var targetCell = ResolveTerrainOccupiedTargetCell(hoverCell);
		var previewGlyph = TerrainRegistry.Get(brush.Id)?.Glyph;
		var canApply = targetCell != null;
		return (CreateHoverState(
				toolMode,
				MapEditorBrushCategory.Terrain,
				MapEditorHoverStateKind.Terrain,
				hoverCell,
				targetCell,
				brush,
				previewGlyph,
				canApply,
				showGhost: false,
				showInfoOverlay: toolMode == MapEditorToolMode.Select && canApply),
			MapEditorBrushApplyResult.None);
	}

	private (MapEditorHoverState HoverState, MapEditorBrushApplyResult ApplyResult) ResolveFixtureBuildHoverState(
		Vector3I hoverCell,
		MapEditorBrush brush)
	{
		var existingFixtureId = _state.World!.GetFixtureId(hoverCell.X, hoverCell.Y, hoverCell.Z);
		var previewGlyph = brush.Glyph ?? EntityAccess.ResolveFixtureGlyph(brush.Id);
		var canPlace = !string.Equals(existingFixtureId, brush.Id, StringComparison.Ordinal);
		return (CreateHoverState(
				MapEditorToolMode.Build,
				MapEditorBrushCategory.Fixture,
				MapEditorHoverStateKind.Fixture,
				hoverCell,
				hoverCell,
				brush,
				previewGlyph,
				canPlace,
				showGhost: canPlace,
				showInfoOverlay: false),
			canPlace ? MapEditorBrushApplyResult.Applied : MapEditorBrushApplyResult.None);
	}

	private (MapEditorHoverState HoverState, MapEditorBrushApplyResult ApplyResult) ResolveFixtureOccupiedHoverState(
		MapEditorToolMode toolMode,
		Vector3I hoverCell,
		MapEditorBrush brush)
	{
		var existingFixtureId = _state.World!.GetFixtureId(hoverCell.X, hoverCell.Y, hoverCell.Z);
		Vector3I? targetCell = string.IsNullOrEmpty(existingFixtureId) ? null : hoverCell;
		var previewGlyph = brush.Glyph ?? EntityAccess.ResolveFixtureGlyph(brush.Id);
		var canApply = targetCell != null;
		return (CreateHoverState(
				toolMode,
				MapEditorBrushCategory.Fixture,
				MapEditorHoverStateKind.Fixture,
				hoverCell,
				targetCell,
				brush,
				previewGlyph,
				canApply,
				showGhost: false,
				showInfoOverlay: toolMode == MapEditorToolMode.Select && canApply),
			MapEditorBrushApplyResult.None);
	}

	private Vector3I? ResolveTerrainBuildTargetCell(Vector3I hoverCell)
	{
		var pickedTerrain = _state.World!.GetTerrain(hoverCell.X, hoverCell.Y, hoverCell.Z).StringId;
		if (pickedTerrain is Terrains.Air or Terrains.Void)
			return hoverCell;

		var placeZ = hoverCell.Z - 1;
		for (var scanned = 0; scanned < TerrainColumnScanDepth; scanned++, placeZ--)
		{
			var terrain = _state.World.GetTerrain(hoverCell.X, hoverCell.Y, placeZ).StringId;
			if (terrain is Terrains.Air or Terrains.Void)
				return new Vector3I(hoverCell.X, hoverCell.Y, placeZ);
		}

		return null;
	}

	private Vector3I? ResolveTerrainOccupiedTargetCell(Vector3I hoverCell)
	{
		var pickedTerrain = _state.World!.GetTerrain(hoverCell.X, hoverCell.Y, hoverCell.Z).StringId;
		if (pickedTerrain is not (Terrains.Air or Terrains.Void))
			return hoverCell;

		return null;
	}

	private static MapEditorHoverState CreateHoverState(
		MapEditorToolMode toolMode,
		MapEditorBrushCategory category,
		MapEditorHoverStateKind kind,
		Vector3I rawHoverCell,
		Vector3I? resolvedTargetCell,
		MapEditorBrush brush,
		string? brushGlyph,
		bool canApply,
		bool showGhost,
		bool showInfoOverlay)
	{
		return new MapEditorHoverState(
			toolMode,
			category,
			kind,
			rawHoverCell,
			resolvedTargetCell,
			brush.Id,
			brushGlyph ?? brush.Glyph,
			canApply,
			showGhost,
			showInfoOverlay);
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
