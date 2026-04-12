namespace MiniRPG;

public partial class Main
{
	private RuntimeUiModeSnapshot CaptureRuntimeUiMode()
	{
		if (_menu == null
			|| _session == null
			|| _settingsFlow == null
			|| _worldManager == null
			|| _worldSettingsDialog == null
			|| _saveNameDialog == null
			|| _characterCreation == null
			|| _confirmDialog == null
			|| _loadRecoveryDialog == null)
		{
			return new RuntimeUiModeSnapshot(
				BusyOperationActive: true,
				InMenu: true,
				SessionStarted: false,
				LayoutEditActive: false,
				MapEditorActive: false,
				SettingsOverlayVisible: false,
				HasVisibleModalLayer: false,
				AllowPanelChrome: false,
				AllowPanelDrag: false,
				BlocksGameplayInput: true,
				SuppressHudAndAlerts: true,
				PausesGameplayLoop: true);
		}

		var busyOperationActive = _busyOperationActive;
		var inMenu = _menu.InMenu;
		var sessionStarted = _session.GameStarted;
		var layoutEditActive = LayoutEditActive;
		var mapEditorActive = MapEditorActive;
		var confirmDialogOpen = IsConfirmDialogOpen;
		var loadRecoveryDialogOpen = IsLoadRecoveryDialogOpen;
		var worldManagerOpen = IsWorldManagerOpen;
		var worldSettingsDialogOpen = IsWorldSettingsDialogOpen;
		var saveNameDialogOpen = IsSaveNameDialogOpen;
		var characterCreationOpen = IsCharacterCreationOpen;
		var multiplayerRoomPanelOpen = IsMultiplayerRoomPanelOpen;
		var settingsOverlayVisible = _settingsFlow.HasVisibleOverlay;
		var hasVisibleModalLayer = confirmDialogOpen
			|| loadRecoveryDialogOpen
			|| worldManagerOpen
			|| worldSettingsDialogOpen
			|| saveNameDialogOpen
			|| characterCreationOpen
			|| multiplayerRoomPanelOpen
			|| settingsOverlayVisible;
		return new RuntimeUiModeSnapshot(
			BusyOperationActive: busyOperationActive,
			InMenu: inMenu,
			SessionStarted: sessionStarted,
			LayoutEditActive: layoutEditActive,
			MapEditorActive: mapEditorActive,
			SettingsOverlayVisible: settingsOverlayVisible,
			HasVisibleModalLayer: hasVisibleModalLayer,
			AllowPanelChrome: !busyOperationActive && !layoutEditActive && !mapEditorActive && !hasVisibleModalLayer,
			AllowPanelDrag: !busyOperationActive && !mapEditorActive && !hasVisibleModalLayer,
			BlocksGameplayInput: busyOperationActive || inMenu || layoutEditActive || mapEditorActive || hasVisibleModalLayer,
			SuppressHudAndAlerts: !sessionStarted || inMenu || PlayerDead || busyOperationActive || layoutEditActive || mapEditorActive || hasVisibleModalLayer,
			PausesGameplayLoop: busyOperationActive || confirmDialogOpen || loadRecoveryDialogOpen || worldManagerOpen || worldSettingsDialogOpen || saveNameDialogOpen || multiplayerRoomPanelOpen || mapEditorActive || layoutEditActive || settingsOverlayVisible);
	}
}
