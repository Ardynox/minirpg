using System;

namespace MiniRPG;

internal sealed class ModalStateController(
	Action closePanelChromeSettings,
	Action hideSettingsPanels,
	Action closeSettingsOverlayIfVisible,
	Action exitMapEditor,
	Action cancelLayoutEdit,
	Action closeConfirmDialog,
	Action closeWorldManager,
	Action closeWorldSettingsDialog,
	Action closeSaveNameDialog,
	Action closeCharacterCreationDialog)
{
	private readonly Action _closePanelChromeSettings = closePanelChromeSettings;
	private readonly Action _hideSettingsPanels = hideSettingsPanels;
	private readonly Action _closeSettingsOverlayIfVisible = closeSettingsOverlayIfVisible;
	private readonly Action _exitMapEditor = exitMapEditor;
	private readonly Action _cancelLayoutEdit = cancelLayoutEdit;
	private readonly Action _closeConfirmDialog = closeConfirmDialog;
	private readonly Action _closeWorldManager = closeWorldManager;
	private readonly Action _closeWorldSettingsDialog = closeWorldSettingsDialog;
	private readonly Action _closeSaveNameDialog = closeSaveNameDialog;
	private readonly Action _closeCharacterCreationDialog = closeCharacterCreationDialog;

	public void Prepare(RuntimeUiResetReason reason)
	{
		switch (reason)
		{
			case RuntimeUiResetReason.SessionTransition:
				_closePanelChromeSettings();
				_exitMapEditor();
				_cancelLayoutEdit();
				_closeConfirmDialog();
				_closeCharacterCreationDialog();
				_closeWorldManager();
				_closeWorldSettingsDialog();
				_closeSaveNameDialog();
				_hideSettingsPanels();
				break;
			case RuntimeUiResetReason.OpenWorldManager:
				_closePanelChromeSettings();
				_closeSettingsOverlayIfVisible();
				_exitMapEditor();
				_cancelLayoutEdit();
				_closeConfirmDialog();
				_closeCharacterCreationDialog();
				_closeWorldSettingsDialog();
				_closeSaveNameDialog();
				break;
			case RuntimeUiResetReason.OpenMenuSettings:
				_closePanelChromeSettings();
				_hideSettingsPanels();
				_closeConfirmDialog();
				_closeCharacterCreationDialog();
				_closeWorldManager();
				_closeWorldSettingsDialog();
				_closeSaveNameDialog();
				break;
			case RuntimeUiResetReason.OpenWorldSettings:
				_closePanelChromeSettings();
				_hideSettingsPanels();
				_closeConfirmDialog();
				_closeWorldManager();
				_closeCharacterCreationDialog();
				_closeSaveNameDialog();
				break;
			case RuntimeUiResetReason.OpenCharacterCreation:
				_closePanelChromeSettings();
				_hideSettingsPanels();
				_closeConfirmDialog();
				_closeWorldManager();
				_closeWorldSettingsDialog();
				_closeSaveNameDialog();
				break;
			case RuntimeUiResetReason.EnterMapEditor:
				_closePanelChromeSettings();
				_cancelLayoutEdit();
				_hideSettingsPanels();
				_closeConfirmDialog();
				_closeCharacterCreationDialog();
				_closeWorldManager();
				_closeWorldSettingsDialog();
				_closeSaveNameDialog();
				break;
			case RuntimeUiResetReason.EnterLayoutEdit:
				_closePanelChromeSettings();
				_hideSettingsPanels();
				break;
		}
	}
}
