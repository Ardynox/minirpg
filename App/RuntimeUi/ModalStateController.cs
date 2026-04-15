using System;

namespace MiniRPG;

internal sealed class ModalStateController(
	Action closePanelChromeSettings,
	Action hideSettingsPanels,
	Action closeSettingsOverlayIfVisible,
	Action closeMultiplayerHub,
	Action closeMultiplayerRoomPanel,
	Action exitMapEditor,
	Action cancelLayoutEdit,
	Action closeConfirmDialog,
	Action closeLoadRecoveryDialog,
	Action closeWorldManager,
	Action closeWorldSettingsDialog,
	Action closeSaveNameDialog,
	Action closeCharacterCreationDialog)
{
	[Flags]
	private enum CloseFlags
	{
		None = 0,
		PanelChromeSettings = 1 << 0,
		SettingsPanels = 1 << 1,
		SettingsOverlay = 1 << 2,
		MultiplayerHub = 1 << 3,
		MultiplayerRoomPanel = 1 << 4,
		MapEditor = 1 << 5,
		LayoutEdit = 1 << 6,
		ConfirmDialog = 1 << 7,
		LoadRecoveryDialog = 1 << 8,
		WorldManager = 1 << 9,
		WorldSettingsDialog = 1 << 10,
		SaveNameDialog = 1 << 11,
		CharacterCreationDialog = 1 << 12,

		AllDialogs = ConfirmDialog | LoadRecoveryDialog | WorldSettingsDialog | SaveNameDialog | CharacterCreationDialog,
		AllMultiplayer = MultiplayerHub | MultiplayerRoomPanel,
		CommonBase = PanelChromeSettings | AllMultiplayer | AllDialogs | WorldManager,
	}

	private static CloseFlags ResolveFlags(RuntimeUiResetReason reason) => reason switch
	{
		RuntimeUiResetReason.SessionTransition =>
			CloseFlags.CommonBase | CloseFlags.MapEditor | CloseFlags.LayoutEdit | CloseFlags.SettingsPanels,
		RuntimeUiResetReason.OpenWorldManager =>
			CloseFlags.PanelChromeSettings | CloseFlags.SettingsOverlay | CloseFlags.AllMultiplayer
			| CloseFlags.MapEditor | CloseFlags.LayoutEdit
			| CloseFlags.ConfirmDialog | CloseFlags.LoadRecoveryDialog
			| CloseFlags.WorldSettingsDialog | CloseFlags.SaveNameDialog | CloseFlags.CharacterCreationDialog,
		RuntimeUiResetReason.OpenMenuSettings =>
			CloseFlags.CommonBase | CloseFlags.SettingsPanels,
		RuntimeUiResetReason.OpenMultiplayerRoomPanel =>
			CloseFlags.CommonBase | CloseFlags.SettingsPanels,
		RuntimeUiResetReason.OpenWorldSettings =>
			CloseFlags.PanelChromeSettings | CloseFlags.AllMultiplayer | CloseFlags.SettingsPanels
			| CloseFlags.ConfirmDialog | CloseFlags.LoadRecoveryDialog
			| CloseFlags.WorldManager | CloseFlags.CharacterCreationDialog | CloseFlags.SaveNameDialog,
		RuntimeUiResetReason.OpenCharacterCreation =>
			CloseFlags.PanelChromeSettings | CloseFlags.AllMultiplayer | CloseFlags.SettingsPanels
			| CloseFlags.ConfirmDialog | CloseFlags.LoadRecoveryDialog
			| CloseFlags.WorldManager | CloseFlags.WorldSettingsDialog | CloseFlags.SaveNameDialog,
		RuntimeUiResetReason.EnterMapEditor =>
			CloseFlags.CommonBase | CloseFlags.LayoutEdit | CloseFlags.SettingsPanels,
		RuntimeUiResetReason.EnterLayoutEdit =>
			CloseFlags.PanelChromeSettings | CloseFlags.AllMultiplayer | CloseFlags.SettingsPanels,
		_ => CloseFlags.None,
	};

	public void Prepare(RuntimeUiResetReason reason)
	{
		var flags = ResolveFlags(reason);
		if (flags == CloseFlags.None)
			return;

		if (flags.HasFlag(CloseFlags.PanelChromeSettings)) closePanelChromeSettings();
		if (flags.HasFlag(CloseFlags.SettingsPanels)) hideSettingsPanels();
		if (flags.HasFlag(CloseFlags.SettingsOverlay)) closeSettingsOverlayIfVisible();
		if (flags.HasFlag(CloseFlags.MultiplayerHub)) closeMultiplayerHub();
		if (flags.HasFlag(CloseFlags.MultiplayerRoomPanel)) closeMultiplayerRoomPanel();
		if (flags.HasFlag(CloseFlags.MapEditor)) exitMapEditor();
		if (flags.HasFlag(CloseFlags.LayoutEdit)) cancelLayoutEdit();
		if (flags.HasFlag(CloseFlags.ConfirmDialog)) closeConfirmDialog();
		if (flags.HasFlag(CloseFlags.LoadRecoveryDialog)) closeLoadRecoveryDialog();
		if (flags.HasFlag(CloseFlags.WorldManager)) closeWorldManager();
		if (flags.HasFlag(CloseFlags.WorldSettingsDialog)) closeWorldSettingsDialog();
		if (flags.HasFlag(CloseFlags.SaveNameDialog)) closeSaveNameDialog();
		if (flags.HasFlag(CloseFlags.CharacterCreationDialog)) closeCharacterCreationDialog();
	}
}
