using System;
using Godot;
using MiniRPG.Core.Config;
using MiniRPG.Core.Weather;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Render;

namespace MiniRPG;

/// <summary>
/// Coordinates all map editor interactions: lifecycle, input handling, save flow.
/// Extracted from Main.MapEditor.cs to decouple editor logic from the Main class.
/// </summary>
internal sealed class MapEditorCoordinator
{
	private readonly GameState _state;
	private readonly MapEditorSession _session;
	private readonly MapEditorBarModule _bar;
	private readonly Func<IsometricVoxelRenderer?> _getRenderer;
	private readonly GameSessionModule _gameSession;
	private readonly LogModule _log;
	private readonly Action _flushMap;
	private readonly Action<string, string> _doSave;
	private readonly Action _openSaveNameDialog;
	private readonly Action _closeSaveNameDialog;
	private readonly ModalStateController _modalStateController;
	private readonly InputModule _inputModule;
	private readonly PanelManager _panels;
	private readonly TurnControllerPanelController _turnController;
	private readonly Action _syncSettingsUiState;
	private readonly Action _showMainMenuWithCurrentContinue;
	private readonly Action _closeAllInGamePanels;
	private readonly Action<Vector3I?> _setWorldHoverCell;
	private readonly Action<Vector2> _positionWorldHoverOverlay;
	private Vector2 _lastPointerGlobalPosition;
	private bool _hasLastPointerGlobalPosition;
	private bool _leftMouseHeld;
	private Vector3I? _lastPaintedCell;
	private bool _moveUp, _moveDown, _moveLeft, _moveRight;
	private float _cameraFloatX, _cameraFloatY;
	private const float CameraMoveSpeed = 8f;

	public MapEditorCoordinator(
		GameState state,
		MapEditorSession session,
		MapEditorBarModule bar,
		Func<IsometricVoxelRenderer?> getRenderer,
		GameSessionModule gameSession,
		LogModule log,
		Action flushMap,
		Action<string, string> doSave,
		Action openSaveNameDialog,
		Action closeSaveNameDialog,
		ModalStateController modalStateController,
		InputModule inputModule,
		PanelManager panels,
		TurnControllerPanelController turnController,
		Action syncSettingsUiState,
		Action showMainMenuWithCurrentContinue,
		Action closeAllInGamePanels,
		Action<Vector3I?> setWorldHoverCell,
		Action<Vector2> positionWorldHoverOverlay)
	{
		_state = state;
		_session = session;
		_bar = bar;
		_getRenderer = getRenderer;
		_gameSession = gameSession;
		_log = log;
		_flushMap = flushMap;
		_doSave = doSave;
		_openSaveNameDialog = openSaveNameDialog;
		_closeSaveNameDialog = closeSaveNameDialog;
		_modalStateController = modalStateController;
		_inputModule = inputModule;
		_panels = panels;
		_turnController = turnController;
		_syncSettingsUiState = syncSettingsUiState;
		_showMainMenuWithCurrentContinue = showMainMenuWithCurrentContinue;
		_closeAllInGamePanels = closeAllInGamePanels;
		_setWorldHoverCell = setWorldHoverCell;
		_positionWorldHoverOverlay = positionWorldHoverOverlay;
	}

	public bool Active => _session.Active;

	// ── Lifecycle ──

	public void Enter(MapEditorEntryMode entryMode, string? savePath)
	{
		_modalStateController.Prepare(RuntimeUiResetReason.EnterMapEditor);
		_inputModule.CancelSelection();
		_inputModule.EnterActionMode();
		_panels.ClearFocus();
		_closeAllInGamePanels();
		_session.Enter(entryMode, savePath);
		ClearEditorHover(flushMap: false);
		InitCameraSmoothing();

		// Default to daytime if current turn is in night/dawn/dusk range
		var phase = _state.Turn % 120;
		if (phase < 24 || phase > 84)
			_state.Turn = 36;

		_bar.Open(_session.CanCenterOnPlayer);
		_syncSettingsUiState();
		RefreshBar();
		_flushMap();
	}

	public void Exit(bool silent = false)
	{
		if (!Active) return;

		var startedFromMenu = _session.StartedFromMenu;
		_session.Exit();
		ClearEditorHover(flushMap: false);
		_bar.Close();
		_closeSaveNameDialog();
		_turnController.Close();
		_syncSettingsUiState();
		_moveUp = _moveDown = _moveLeft = _moveRight = false;
		_getRenderer()?.ClearEditorCameraSmoothing();

		if (startedFromMenu)
		{
			if (!silent)
				_log.Add(LocalizationService.T("log.map_editor.exit_to_menu"));
			_showMainMenuWithCurrentContinue();
			return;
		}

		if (!silent)
			_log.Add(LocalizationService.T("log.map_editor.exit"));
		_flushMap();
	}

	// ── Input ──

	public bool HandleKeyInput(InputEventKey key)
	{
		if (key.Pressed)
		{
			switch (key.Keycode)
			{
				case Key.Ctrl:
					RefreshBuildReverseStackModifier(key.CtrlPressed);
					return true;
				case Key.Escape:
					Exit();
					return true;
				case Key.Tab:
					_session.ToggleCategory();
					RefreshBar();
					SyncEditorHoverPresentation(_session.HoverWorld);
					_flushMap();
					return true;
				case Key.S when key.ShiftPressed:
					SelectToolMode(MapEditorToolMode.Select);
					return true;
				case Key.B when key.ShiftPressed:
					SelectToolMode(MapEditorToolMode.Build);
					return true;
				case Key.D when key.ShiftPressed:
					SelectToolMode(MapEditorToolMode.Demolish);
					return true;
				case Key.W or Key.Up:
					SetMoveFlag(key.Keycode, true);
					return true;
				case Key.S or Key.Down:
					SetMoveFlag(key.Keycode, true);
					return true;
				case Key.A or Key.Left:
					SetMoveFlag(key.Keycode, true);
					return true;
				case Key.D or Key.Right:
					SetMoveFlag(key.Keycode, true);
					return true;
				case Key.Z when key.CtrlPressed:
					if (_session.Undo())
					{
						RefreshBar();
						_flushMap();
					}
					return true;
				case Key.Y when key.CtrlPressed:
					if (_session.Redo())
					{
						RefreshBar();
						_flushMap();
					}
					return true;
			}
		}
		else
		{
			switch (key.Keycode)
			{
				case Key.Ctrl:
					RefreshBuildReverseStackModifier(key.CtrlPressed);
					return true;
				case Key.W or Key.Up:
				case Key.S or Key.Down:
				case Key.A or Key.Left:
				case Key.D or Key.Right:
					SetMoveFlag(key.Keycode, false);
					return true;
			}
		}

		return false;
	}

	private void SetMoveFlag(Key keycode, bool pressed)
	{
		switch (keycode)
		{
			case Key.W or Key.Up:
				_moveUp = pressed;
				break;
			case Key.S or Key.Down:
				_moveDown = pressed;
				break;
			case Key.A or Key.Left:
				_moveLeft = pressed;
				break;
			case Key.D or Key.Right:
				_moveRight = pressed;
				break;
		}
	}

	public void InitCameraSmoothing()
	{
		_cameraFloatX = _session.CameraX;
		_cameraFloatY = _session.CameraY;
		_moveUp = _moveDown = _moveLeft = _moveRight = false;
	}

	public void Tick(float delta)
	{
		if (!Active) return;

		var dx = (_moveRight ? 1f : 0f) - (_moveLeft ? 1f : 0f);
		var dy = (_moveDown ? 1f : 0f) - (_moveUp ? 1f : 0f);
		if (dx == 0f && dy == 0f)
			return;

		_cameraFloatX += dx * CameraMoveSpeed * delta;
		_cameraFloatY += dy * CameraMoveSpeed * delta;

		var cellX = Mathf.RoundToInt(_cameraFloatX);
		var cellY = Mathf.RoundToInt(_cameraFloatY);

		if (cellX != _session.CameraX || cellY != _session.CameraY)
		{
			_session.MoveCamera(cellX - _session.CameraX, cellY - _session.CameraY);
			RefreshBar();
			_flushMap();
		}

		var renderer = _getRenderer();
		renderer?.SetEditorCameraScreenTarget(
			IsoCoordUtil.WorldToScreen(_cameraFloatX, _cameraFloatY, _session.CameraZ));
	}

	public bool HandleMouseInput(InputEvent @event)
	{
		if (@event is InputEventMouse mouse &&
			(_bar.IsPointerOver(mouse.GlobalPosition) || _turnController.IsPointerOver(mouse.GlobalPosition)))
		{
			ClearEditorHover();
			_leftMouseHeld = false;
			_lastPaintedCell = null;
			return false;
		}

		var renderer = _getRenderer();
		if (renderer == null)
		{
			if (@event is InputEventMouseMotion)
				ClearEditorHover();
			_leftMouseHeld = false;
			_lastPaintedCell = null;
			return false;
		}

		if (@event is InputEventMouseMotion motion)
		{
			var reverseStackChanged = _session.SetBuildReverseStack(motion.CtrlPressed);
			if (renderer.TryGetEditorWorldCellFromGlobalPosition(motion.GlobalPosition, _session.CameraZ, out var hovered))
			{
				UpdateEditorHover(hovered, motion.GlobalPosition, forceRefresh: reverseStackChanged);
				if (_leftMouseHeld && hovered != _lastPaintedCell)
					TryPaintCell(hovered);
				return true;
			}

			ClearEditorHover();
			return false;
		}

		if (@event is InputEventMouseButton mb)
		{
			_session.SetBuildReverseStack(mb.CtrlPressed);
			if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed)
			{
				_leftMouseHeld = false;
				_lastPaintedCell = null;
				return false;
			}

			if (!mb.Pressed)
				return false;

			if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
			{
				_session.CycleBrush(mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
				RefreshBar();
				SyncEditorHoverPresentation(_session.HoverWorld);
				_flushMap();
				return true;
			}

			if (mb.ButtonIndex is not MouseButton.Left and not MouseButton.Right)
				return false;

			if (!renderer.TryGetEditorWorldCellFromGlobalPosition(mb.GlobalPosition, _session.CameraZ, out var worldCell))
			{
				ClearEditorHover();
				return false;
			}

			SetEditorHoverCell(worldCell, mb.GlobalPosition);
			if (mb.ButtonIndex == MouseButton.Right)
				return true;

			_leftMouseHeld = true;
			TryPaintCell(worldCell);
			return true;
		}

		return false;
	}

	private void TryPaintCell(Vector3I worldCell)
	{
		if (_session.CurrentCategory == MapEditorBrushCategory.Environment)
			return;
		if (_session.CurrentToolMode == MapEditorToolMode.Select)
			return;

		if (_session.CurrentToolMode == MapEditorToolMode.Build)
		{
			var result = _session.ApplyBrush(worldCell.X, worldCell.Y, worldCell.Z);
			if (result == MapEditorBrushApplyResult.ConnectivityRequired)
			{
				_log.Add(LocalizationService.T(
					"log.map_editor.place_requires_support"));
			}
		}
		else
			_session.EraseBrush(worldCell.X, worldCell.Y, worldCell.Z);

		_lastPaintedCell = worldCell;
		RefreshBar();
		_flushMap();
	}

	private void RefreshBuildReverseStackModifier(bool reverseStack)
	{
		if (!_session.SetBuildReverseStack(reverseStack))
			return;

		if (_session.HoverWorld is { } hoverCell)
			SyncEditorHoverPresentation(hoverCell);

		_flushMap();
	}

	// ── UI ──

	public void RefreshBar()
	{
		if (!Active) return;
		_bar.Render(
			_session.CurrentCategory,
			_session.CurrentToolMode,
			_session.CurrentBrushes,
			_session.CurrentBrushIndex);
		_bar.SetIgnoreConnectivityRequirement(_session.IgnoreConnectivityRequirement);
		_bar.UpdateInfo(_session.CameraX, _session.CameraY, _session.CameraZ,
			_session.CanUndo, _session.CanRedo, _session.UndoCount);
		_bar.UpdateHeight(_session.CameraZ);

		var renderer = _getRenderer();
		var weatherOverride = _state.Weather.DebugOverride;
		_bar.SetEnvironmentState(
			_state.Turn,
			weatherOverride != null ? (int)weatherOverride.Type : 0,
			weatherOverride != null ? (int)weatherOverride.Intensity : 1,
			renderer?.LightingProfileIndex ?? 0);
	}

	public void SelectCategory(MapEditorBrushCategory category)
	{
		_session.SelectCategory(category);
		RefreshBar();
		SyncEditorHoverPresentation(_session.HoverWorld);
		_flushMap();
	}

	public void SelectToolMode(MapEditorToolMode toolMode)
	{
		_session.SelectToolMode(toolMode);
		RefreshBar();
		SyncEditorHoverPresentation(_session.HoverWorld);
		_flushMap();
	}

	public void SelectBrush(int index)
	{
		_session.SelectBrush(index);
		RefreshBar();
		SyncEditorHoverPresentation(_session.HoverWorld);
		_flushMap();
	}

	public void CenterOnPlayer()
	{
		_session.CenterOnPlayer();
		ClearEditorHover(flushMap: false);
		InitCameraSmoothing();
		RefreshBar();
		_flushMap();
	}

	public void HandleHeightChanged(int delta)
	{
		_session.AdjustZ(delta);
		ClearEditorHover(flushMap: false);
		RefreshBar();
		_flushMap();
	}

	public void HandleIgnoreConnectivityRequirementChanged(bool ignore)
	{
		_session.SetIgnoreConnectivityRequirement(ignore);
		RefreshBar();
		SyncEditorHoverPresentation(_session.HoverWorld);
		_flushMap();
	}

	public void HandleUndo()
	{
		if (_session.Undo())
		{
			RefreshBar();
			SyncEditorHoverPresentation(_session.HoverWorld);
			_flushMap();
		}
	}

	public void HandleRedo()
	{
		if (_session.Redo())
		{
			RefreshBar();
			SyncEditorHoverPresentation(_session.HoverWorld);
			_flushMap();
		}
	}

	public void HandleTimeOfDayChanged(int turn)
	{
		_state.Turn = turn;
		RefreshBar();
		_flushMap();
	}

	public void HandleWeatherTypeChanged(int index)
	{
		var weatherType = (WeatherType)index;
		_state.Weather.DebugOverride ??= new WeatherDebugOverride();
		_state.Weather.DebugOverride.Type = weatherType;
		_flushMap();
	}

	public void HandleWeatherIntensityChanged(int index)
	{
		var intensity = (WeatherIntensity)index;
		_state.Weather.DebugOverride ??= new WeatherDebugOverride();
		_state.Weather.DebugOverride.Intensity = intensity;
		_flushMap();
	}

	public void HandleLightingProfileChanged(int index)
	{
		var renderer = _getRenderer();
		renderer?.SetLightingProfile(index);
		_flushMap();
	}

	public void HandleTurnControllerRequested()
	{
		_turnController.Toggle();
	}

	// ── Save ──

	public void HandleSaveRequested()
	{
		if (!Active) return;

		if (_session.StartedFromMenu && string.IsNullOrEmpty(_session.SavePath))
		{
			_openSaveNameDialog();
			return;
		}

		var path = _session.SavePath ?? _gameSession.GetPreferredSavePath();
		_doSave(path, _gameSession.DescribeSavePath(path));
		_session.UpdateSavePath(path);
	}

	public void HandleSaveNameConfirmed(string rawName)
	{
		if (!Active) return;

		var path = _gameSession.BuildNamedSavePath(rawName);
		_closeSaveNameDialog();
		_doSave(path, _gameSession.DescribeSavePath(path));
		_session.UpdateSavePath(path);
	}

	private void UpdateEditorHover(Vector3I hoveredCell, Vector2 pointerGlobalPosition, bool forceRefresh = false)
	{
		_lastPointerGlobalPosition = pointerGlobalPosition;
		_hasLastPointerGlobalPosition = true;
		var changed = _session.SetHover(hoveredCell);
		SyncEditorHoverPresentation(hoveredCell, pointerGlobalPosition);
		if (changed || forceRefresh)
			_flushMap();
	}

	private void SetEditorHoverCell(Vector3I hoveredCell, Vector2 pointerGlobalPosition)
	{
		_lastPointerGlobalPosition = pointerGlobalPosition;
		_hasLastPointerGlobalPosition = true;
		_session.SetHover(hoveredCell);
		SyncEditorHoverPresentation(hoveredCell, pointerGlobalPosition);
	}

	private void ClearEditorHover(bool flushMap = true)
	{
		_hasLastPointerGlobalPosition = false;
		var changed = _session.SetHover(null);
		_setWorldHoverCell(null);
		if (flushMap && changed)
			_flushMap();
	}

	private void SyncEditorHoverPresentation(Vector3I? hoveredCell, Vector2? pointerGlobalPosition = null)
	{
		var overlayCell = ResolveWorldHoverOverlayCell(hoveredCell);
		_setWorldHoverCell(overlayCell);

		if (overlayCell is not { })
			return;

		var resolvedPointerPosition = pointerGlobalPosition
			?? (_hasLastPointerGlobalPosition ? _lastPointerGlobalPosition : (Vector2?)null);
		if (resolvedPointerPosition is { } overlayPosition)
			_positionWorldHoverOverlay(overlayPosition);
	}

	private Vector3I? ResolveWorldHoverOverlayCell(Vector3I? hoveredCell)
	{
		if (hoveredCell is not { } hoverCell)
			return null;

		if (_session.CurrentCategory == MapEditorBrushCategory.Environment)
			return hoverCell;

		var hoverState = _session.ResolveHoverState(hoverCell);
		return hoverState is { ShowInfoOverlay: true, ResolvedTargetCell: { } targetCell }
			? targetCell
			: null;
	}
}
