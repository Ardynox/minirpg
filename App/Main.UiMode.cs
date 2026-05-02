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
			|| _characterCustomization == null
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
		var characterCustomizationOpen = IsCharacterCustomizationOpen;
		var multiplayerRoomPanelOpen = IsMultiplayerRoomPanelOpen;
		var settingsOverlayVisible = _settingsFlow.HasVisibleOverlay;
		// 仪式感第 1 条：死亡演出 1.5s 内屏蔽玩法输入，避免误操作；演出结束自动放开。
		var deathSequenceActive = _deathSequenceOverlay != null && _deathSequenceOverlay.IsActive;
		var hasVisibleModalLayer = confirmDialogOpen
			|| loadRecoveryDialogOpen
			|| worldManagerOpen
			|| worldSettingsDialogOpen
			|| saveNameDialogOpen
			|| characterCreationOpen
			|| characterCustomizationOpen
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
			BlocksGameplayInput: busyOperationActive || inMenu || layoutEditActive || mapEditorActive || hasVisibleModalLayer || deathSequenceActive || (_conversationPanel?.Visible == true),
			SuppressHudAndAlerts: !sessionStarted || inMenu || PlayerDead || busyOperationActive || layoutEditActive || mapEditorActive || hasVisibleModalLayer || (_conversationPanel?.Visible == true),
			PausesGameplayLoop: busyOperationActive || confirmDialogOpen || loadRecoveryDialogOpen || worldManagerOpen || worldSettingsDialogOpen || saveNameDialogOpen || multiplayerRoomPanelOpen || mapEditorActive || layoutEditActive || settingsOverlayVisible || deathSequenceActive || (_conversationPanel?.Visible == true));
	}
}
