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

		_mapEditorCoordinator.Enter(entryMode, entryMode == MapEditorEntryMode.MenuBlank ? null : _session.CurrentSavePath);
	}

	private void ExitMapEditor(bool silent = false) =>
		_mapEditorCoordinator.Exit(silent);

	private void ToggleMapEditor()
	{
		if (IsMultiplayerSession)
		{
			_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.map_editor", "Map editor is disabled in multiplayer sessions."));
			return;
		}
		_mainAppFlowCoordinator.ToggleMapEditor();
	}

	private void RefreshMapEditorBar() => _mapEditorCoordinator.RefreshBar();

	private bool HandleMapEditorKeyInput(InputEventKey key) =>
		_mapEditorCoordinator.HandleKeyInput(key);

	private bool HandleMapEditorMouseInput(InputEvent @event) =>
		_mapEditorCoordinator.HandleMouseInput(@event);

	private void HandleMapEditorSaveRequested() =>
		_mapEditorCoordinator.HandleSaveRequested();

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

	private void HandleSaveNameConfirmed(string rawName) =>
		_mapEditorCoordinator.HandleSaveNameConfirmed(rawName);
}
