namespace MiniRPG;

internal readonly record struct RuntimeUiModeSnapshot(
	bool BusyOperationActive,
	bool InMenu,
	bool SessionStarted,
	bool LayoutEditActive,
	bool MapEditorActive,
	bool SettingsOverlayVisible,
	bool HasVisibleModalLayer,
	bool AllowPanelChrome,
	bool AllowPanelDrag,
	bool BlocksGameplayInput,
	bool SuppressHudAndAlerts,
	bool PausesGameplayLoop)
{
	public bool AllowGameplayInput => SessionStarted && !InMenu && !BlocksGameplayInput;
}
