using Godot;
using MiniRPG.Module.Editor;

namespace MiniRPG;

public partial class Main
{
	private void BeginLayoutEditMode()
	{
		if (_menu.InMenu || !_session.GameStarted || LayoutEditActive || MapEditorActive)
			return;

		_layoutResetPending = false;
		_modalStateController.Prepare(RuntimeUiResetReason.EnterLayoutEdit);
		_inputModule.CancelSelection();
		_inputModule.EnterActionMode();
		_panels.ClearFocus();
		_panelDrag.BeginEditSession();
		_layoutEditBar.Open();
	}

	private void ApplyLayoutEditMode()
	{
		if (!LayoutEditActive)
			return;

		if (_layoutResetPending)
		{
			foreach (var panelId in LayoutEditablePanelIds)
				_panelDrag.RemovePersistedPosition(panelId);
		}

		_panelDrag.ApplyEditSession();
		_layoutResetPending = false;
		_layoutEditBar.Close();
		FlushMap();
	}

	private void CancelLayoutEditMode()
	{
		if (!LayoutEditActive)
			return;

		_panelDrag.CancelEditSession();
		_layoutResetPending = false;
		_layoutEditBar.Close();
		FlushMap();
	}

	private void ResetLayoutEditMode()
	{
		if (!LayoutEditActive)
			return;

		_layoutResetPending = true;
		_panelDrag.ResetToDefaults();
		FlushMap();
	}

	private void EnterMapEditorCore(MapEditorEntryMode entryMode)
	{
		if (!ResourcesReady || !_session.GameStarted || _state.World == null)
			return;

		_modalStateController.Prepare(RuntimeUiResetReason.EnterMapEditor);
		_inputModule.CancelSelection();
		_inputModule.EnterActionMode();
		_panels.ClearFocus();
		_mapEditor.Enter(entryMode, entryMode == MapEditorEntryMode.MenuBlank ? null : _session.CurrentSavePath);
		_mapEditorBar.Open(_mapEditor.CanCenterOnPlayer);
		_weatherLabPanelController.RefreshSessionState(autoOpen: false);
		SyncSettingsUiState();
		RefreshMapEditorBar();
		FlushMap();
	}

	private void ExitMapEditor(bool silent = false)
	{
		if (!MapEditorActive)
			return;

		var startedFromMenu = _mapEditor.StartedFromMenu;
		_mapEditor.Exit();
		_mapEditorBar.Close();
		CloseSaveNameDialog();
		_weatherLabPanelController.RefreshSessionState(autoOpen: true);
		SyncSettingsUiState();
		if (startedFromMenu)
		{
			if (!silent)
				_log.Add(LocalizationService.T("log.map_editor.exit_to_menu"));
			ShowMainMenuWithCurrentContinue();
			return;
		}

		if (!silent)
			_log.Add(LocalizationService.T("log.map_editor.exit"));
		FlushMap();
	}

	private void ToggleMapEditor()
	{
		if (IsMultiplayerSession)
		{
			_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.map_editor", "Map editor is disabled in multiplayer sessions."));
			return;
		}
		_mainAppFlowCoordinator.ToggleMapEditor();
	}

	private void RefreshMapEditorBar()
	{
		if (!MapEditorActive)
			return;

		_mapEditorBar.Render(_mapEditor.CurrentCategory, _mapEditor.CurrentBrushes, _mapEditor.CurrentBrushIndex);
	}

	private bool HandleMapEditorKeyInput(InputEventKey key)
	{
		if (!key.Pressed)
			return false;

		switch (key.Keycode)
		{
			case Key.Escape:
				ExitMapEditor();
				return true;
			case Key.Tab:
				_mapEditor.ToggleCategory();
				RefreshMapEditorBar();
				FlushMap();
				return true;
			case Key.W:
			case Key.Up:
				_mapEditor.MoveCamera(0, -1);
				FlushMap();
				return true;
			case Key.S:
			case Key.Down:
				_mapEditor.MoveCamera(0, 1);
				FlushMap();
				return true;
			case Key.A:
			case Key.Left:
				_mapEditor.MoveCamera(-1, 0);
				FlushMap();
				return true;
			case Key.D:
			case Key.Right:
				_mapEditor.MoveCamera(1, 0);
				FlushMap();
				return true;
		}

		return false;
	}

	private bool HandleMapEditorMouseInput(InputEvent @event)
	{
		if (_mapRender == null)
			return false;

		if (@event is InputEventMouseMotion motion)
		{
			if (_mapRender.TryGetWorldCellFromGlobalPosition(motion.GlobalPosition, out var hovered))
			{
				if (_mapEditor.SetHover(new Vector2I(hovered.X, hovered.Y)))
					FlushMap();
				return true;
			}

			if (_mapEditor.SetHover(null))
				FlushMap();
			return false;
		}

		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return false;

		if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{
			_mapEditor.CycleBrush(mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
			RefreshMapEditorBar();
			FlushMap();
			return true;
		}

		if (mb.ButtonIndex is not MouseButton.Left and not MouseButton.Right)
			return false;

		if (!_mapRender.TryGetWorldCellFromGlobalPosition(mb.GlobalPosition, out var worldCell))
			return false;

		_mapEditor.SetHover(new Vector2I(worldCell.X, worldCell.Y));
		if (mb.ButtonIndex == MouseButton.Left)
			_mapEditor.ApplyBrush(worldCell.X, worldCell.Y);
		else
			_mapEditor.EraseBrush(worldCell.X, worldCell.Y);

		FlushMap();
		return true;
	}

	private void HandleMapEditorSaveRequested()
	{
		if (!MapEditorActive)
			return;

		if (_mapEditor.StartedFromMenu && string.IsNullOrEmpty(_mapEditor.SavePath))
		{
			OpenSaveNameDialog();
			return;
		}

		var path = _mapEditor.SavePath ?? _session.GetPreferredSavePath();
		DoSave(path, _session.DescribeSavePath(path));
		_mapEditor.UpdateSavePath(path);
	}

	private void OpenSaveNameDialog()
	{
		if (!MapEditorActive)
			return;

		_panelChrome.CloseActiveSettings();
		_saveNameDialog.Open("editor_map");
	}

	private void CloseSaveNameDialog()
	{
		_saveNameDialog.Close();
	}

	private void HandleSaveNameConfirmed(string rawName)
	{
		if (!MapEditorActive)
			return;

		var path = _session.BuildNamedSavePath(rawName);
		CloseSaveNameDialog();
		DoSave(path, _session.DescribeSavePath(path));
		_mapEditor.UpdateSavePath(path);
	}
}
