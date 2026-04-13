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
		Action closeAllInGamePanels)
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
		_bar.Close();
		_closeSaveNameDialog();
		_turnController.Close();
		_syncSettingsUiState();

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
		if (!key.Pressed) return false;

		switch (key.Keycode)
		{
			case Key.Escape:
				Exit();
				return true;
			case Key.Tab:
				_session.ToggleCategory();
				RefreshBar();
				_flushMap();
				return true;
			case Key.W or Key.Up:
				_session.MoveCamera(0, -1);
				RefreshBar();
				_flushMap();
				return true;
			case Key.S or Key.Down:
				_session.MoveCamera(0, 1);
				RefreshBar();
				_flushMap();
				return true;
			case Key.A or Key.Left:
				_session.MoveCamera(-1, 0);
				RefreshBar();
				_flushMap();
				return true;
			case Key.D or Key.Right:
				_session.MoveCamera(1, 0);
				RefreshBar();
				_flushMap();
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

		return false;
	}

	public bool HandleMouseInput(InputEvent @event)
	{
		if (@event is InputEventMouse mouse &&
			(_bar.IsPointerOver(mouse.GlobalPosition) || _turnController.IsPointerOver(mouse.GlobalPosition)))
			return false;

		var renderer = _getRenderer();
		if (renderer == null) return false;

		if (@event is InputEventMouseMotion motion)
		{
			if (renderer.TryGetWorldCellFromGlobalPosition(motion.GlobalPosition, out var hovered))
			{
				if (_session.SetHover(new Vector2I(hovered.X, hovered.Y)))
					_flushMap();
				return true;
			}

			if (_session.SetHover(null))
				_flushMap();
			return false;
		}

		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return false;

		if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{
			_session.CycleBrush(mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
			RefreshBar();
			_flushMap();
			return true;
		}

		if (mb.ButtonIndex is not MouseButton.Left and not MouseButton.Right)
			return false;

		if (!renderer.TryGetWorldCellFromGlobalPosition(mb.GlobalPosition, out var worldCell))
			return false;

		_session.SetHover(new Vector2I(worldCell.X, worldCell.Y));
		if (mb.ButtonIndex == MouseButton.Left)
			_session.ApplyBrush(worldCell.X, worldCell.Y, worldCell.Z);
		else
			_session.EraseBrush(worldCell.X, worldCell.Y, worldCell.Z);

		RefreshBar();
		_flushMap();
		return true;
	}

	// ── UI ──

	public void RefreshBar()
	{
		if (!Active) return;
		_bar.Render(_session.CurrentCategory, _session.CurrentBrushes, _session.CurrentBrushIndex);
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
		_flushMap();
	}

	public void SelectBrush(int index)
	{
		_session.SelectBrush(index);
		RefreshBar();
		_flushMap();
	}

	public void CenterOnPlayer()
	{
		_session.CenterOnPlayer();
		RefreshBar();
		_flushMap();
	}

	public void HandleHeightChanged(int delta)
	{
		_session.AdjustZ(delta);
		RefreshBar();
		_flushMap();
	}

	public void HandleUndo()
	{
		if (_session.Undo())
		{
			RefreshBar();
			_flushMap();
		}
	}

	public void HandleRedo()
	{
		if (_session.Redo())
		{
			RefreshBar();
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
}
