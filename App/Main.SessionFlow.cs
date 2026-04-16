using MiniRPG.Core.Data;

namespace MiniRPG;

public partial class Main
{
	private void FinalizeSessionPanels(bool openSkillBar)
	{
		ClearArmedSkill(restoreFocus: false);
		EndSkillTargetCursorMode(restoreFocus: false);
		ClearPlayerTargeting();
		CancelAutoNavigation(AutoNavigationStopReason.SessionReset, emitLog: false);
		ResetRuntimeWorldToolSession();
		ResetThreatHud();
		RefreshPlayerCharacterVisual();
		SyncSettingsUiState();
		SyncTimelineAutoAdvanceState();
		OnSessionStartedAudio();
		_incidentStatistics?.Reset();
		_mapRender?.ResetActorMotionState();
		_mapRender?.ResetOverlays();
		if (openSkillBar)
			_skillBar.Open(ActorModule.GetPlayer(_state));
		else
			_skillBar.Close();
		_skillMgr.Close();
		_inventoryPanel.Visible = false;
		if (_chestPanel != null) _chestPanel.Visible = false;
		if (_dialogPanel != null) _dialogPanel.Close();
		if (_tradePanel != null) _tradePanel.Close();
		if (_questPanel != null) _questPanel.Close();
		_debugPanelController.Close();
		_statusPanelController.CloseAll();
		_panels.ClearFocus();
	}

	private void DoEnterGame()
	{
		if (!ResourcesReady)
			return;

		_multiplayerHub.Close();
		_menu.EnterGame();
		_inputModule.EnterActionMode();
		_mapRender?.ResetActorMotionState();
		SyncTimelineAutoAdvanceState();
		FlushMap();
	}

	private void ShowGameHints()
	{
		_log.Add(LocalizationService.T("hint.game.line1"));
		_log.Add(LocalizationService.T("hint.game.line2"));
	}

	private void ShowWorldCharacterEntryHint(string worldName, string characterName)
	{
		if (string.IsNullOrWhiteSpace(worldName) || string.IsNullOrWhiteSpace(characterName))
			return;

		_log.Add(LocalizationService.T(
			"hint.game.world_entry",
			("world", worldName),
			("character", characterName)));
	}

	private void ShowMapEditorHints()
	{
		_log.Add(LocalizationService.T("hint.map_editor.line1"));
		_log.Add(LocalizationService.T("hint.map_editor.line2"));
	}
}
