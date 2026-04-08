using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Module;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Panel;

namespace MiniRPG;

internal sealed class MainAppFlowCoordinator
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly LogModule _log;
	private readonly MenuModule _menu;
	private readonly SettingsFlowCoordinator _settingsFlow;
	private readonly WorldManagerModule _worldManager;
	private readonly WorldSettingsDialogModule _worldSettingsDialog;
	private readonly CharacterCreationModule _characterCreation;
	private readonly SaveNameDialogModule _saveNameDialog;
	private readonly ConfirmDialogModule _confirmDialog;
	private readonly InputModule _inputModule;
	private readonly PanelManager _panels;
	private readonly ModalStateController _modalStateController;
	private readonly Func<bool> _resourcesReady;
	private readonly Func<bool> _busyOperationActive;
	private readonly Func<bool> _layoutEditActive;
	private readonly Func<bool> _mapEditorActive;
	private readonly Func<bool> _worldReadyForMapEditor;
	private readonly Action _showMainMenuWithCurrentContinue;
	private readonly Action _showGameHints;
	private readonly Action<string, string> _showWorldCharacterEntryHint;
	private readonly Action _showMapEditorHints;
	private readonly Action<bool> _finalizeSessionPanels;
	private readonly Action _doEnterGame;
	private readonly Action _hideSettingsPanels;
	private readonly Action _clearArmedSkill;
	private readonly Action _endInspectMode;
	private readonly Action _clearPlayerTargeting;
	private readonly Action _resetThreatHud;
	private readonly Action _clearLog;
	private readonly Action<bool> _setPlayerDead;
	private readonly Action _disableWatchMode;
	private readonly Action _stopPlayerRestMode;
	private readonly Action _resetTimelineStatusLog;
	private readonly Action _closeDialogUi;
	private readonly Action _closeTradeUi;
	private readonly Action _closeSkillBar;
	private readonly Action _closeDebugPanel;
	private readonly Action _refreshPlayerCharacterVisual;
	private readonly Action<bool> _refreshLocalizedUi;
	private readonly Action<SettingsEntryContext?> _syncSettingsUiState;
	private readonly Action _syncTimelineAutoAdvanceState;
	private readonly Action<bool> _refreshWeatherLabSessionState;
	private readonly Action<bool> _closeWeatherLabPanel;
	private readonly Action<string, string> _doSave;
	private readonly Action<string, string> _doLoad;
	private readonly Action _beginLayoutEditMode;
	private readonly Action<MapEditorEntryMode> _enterMapEditorCore;
	private readonly Action<bool> _exitMapEditor;
	private readonly Action _flushMap;
	private readonly Action<string, float> _beginBusyOperation;
	private readonly Func<string, float, Task> _showBusyOperationStageAsync;
	private readonly Action _endBusyOperation;

	private WorldManagerContext _worldManagerContext = WorldManagerContext.MainMenu;
	private WorldLaunchTab _worldManagerTab = WorldLaunchTab.Worlds;
	private string? _pendingCharacterCreationWorldId;
	private string? _pendingCharacterCreationWorldName;
	private string? _worldManagerStatusMessage;
	private bool _worldManagerStatusIsError;
	private readonly Dictionary<string, Action> _confirmDialogActions = new(StringComparer.Ordinal);

	public MainAppFlowCoordinator(
		GameState state,
		GameSessionModule session,
		LogModule log,
		MenuModule menu,
		SettingsFlowCoordinator settingsFlow,
		WorldManagerModule worldManager,
		WorldSettingsDialogModule worldSettingsDialog,
		CharacterCreationModule characterCreation,
		SaveNameDialogModule saveNameDialog,
		ConfirmDialogModule confirmDialog,
		InputModule inputModule,
		PanelManager panels,
		ModalStateController modalStateController,
		Func<bool> resourcesReady,
		Func<bool> busyOperationActive,
		Func<bool> layoutEditActive,
		Func<bool> mapEditorActive,
		Func<bool> worldReadyForMapEditor,
		Action showMainMenuWithCurrentContinue,
		Action showGameHints,
		Action<string, string> showWorldCharacterEntryHint,
		Action showMapEditorHints,
		Action<bool> finalizeSessionPanels,
		Action doEnterGame,
		Action hideSettingsPanels,
		Action clearArmedSkill,
		Action endInspectMode,
		Action clearPlayerTargeting,
		Action resetThreatHud,
		Action clearLog,
		Action<bool> setPlayerDead,
		Action disableWatchMode,
		Action stopPlayerRestMode,
		Action resetTimelineStatusLog,
		Action closeDialogUi,
		Action closeTradeUi,
		Action closeSkillBar,
		Action closeDebugPanel,
		Action refreshPlayerCharacterVisual,
		Action<bool> refreshLocalizedUi,
		Action<SettingsEntryContext?> syncSettingsUiState,
		Action syncTimelineAutoAdvanceState,
		Action<bool> refreshWeatherLabSessionState,
		Action<bool> closeWeatherLabPanel,
		Action<string, string> doSave,
		Action<string, string> doLoad,
		Action beginLayoutEditMode,
		Action<MapEditorEntryMode> enterMapEditorCore,
		Action<bool> exitMapEditor,
		Action flushMap,
		Action<string, float> beginBusyOperation,
		Func<string, float, Task> showBusyOperationStageAsync,
		Action endBusyOperation)
	{
		_state = state;
		_session = session;
		_log = log;
		_menu = menu;
		_settingsFlow = settingsFlow;
		_worldManager = worldManager;
		_worldSettingsDialog = worldSettingsDialog;
		_characterCreation = characterCreation;
		_saveNameDialog = saveNameDialog;
		_confirmDialog = confirmDialog;
		_inputModule = inputModule;
		_panels = panels;
		_modalStateController = modalStateController;
		_resourcesReady = resourcesReady;
		_busyOperationActive = busyOperationActive;
		_layoutEditActive = layoutEditActive;
		_mapEditorActive = mapEditorActive;
		_worldReadyForMapEditor = worldReadyForMapEditor;
		_showMainMenuWithCurrentContinue = showMainMenuWithCurrentContinue;
		_showGameHints = showGameHints;
		_showWorldCharacterEntryHint = showWorldCharacterEntryHint;
		_showMapEditorHints = showMapEditorHints;
		_finalizeSessionPanels = finalizeSessionPanels;
		_doEnterGame = doEnterGame;
		_hideSettingsPanels = hideSettingsPanels;
		_clearArmedSkill = clearArmedSkill;
		_endInspectMode = endInspectMode;
		_clearPlayerTargeting = clearPlayerTargeting;
		_resetThreatHud = resetThreatHud;
		_clearLog = clearLog;
		_setPlayerDead = setPlayerDead;
		_disableWatchMode = disableWatchMode;
		_stopPlayerRestMode = stopPlayerRestMode;
		_resetTimelineStatusLog = resetTimelineStatusLog;
		_closeDialogUi = closeDialogUi;
		_closeTradeUi = closeTradeUi;
		_closeSkillBar = closeSkillBar;
		_closeDebugPanel = closeDebugPanel;
		_refreshPlayerCharacterVisual = refreshPlayerCharacterVisual;
		_refreshLocalizedUi = refreshLocalizedUi;
		_syncSettingsUiState = syncSettingsUiState;
		_syncTimelineAutoAdvanceState = syncTimelineAutoAdvanceState;
		_refreshWeatherLabSessionState = refreshWeatherLabSessionState;
		_closeWeatherLabPanel = closeWeatherLabPanel;
		_doSave = doSave;
		_doLoad = doLoad;
		_beginLayoutEditMode = beginLayoutEditMode;
		_enterMapEditorCore = enterMapEditorCore;
		_exitMapEditor = exitMapEditor;
		_flushMap = flushMap;
		_beginBusyOperation = beginBusyOperation;
		_showBusyOperationStageAsync = showBusyOperationStageAsync;
		_endBusyOperation = endBusyOperation;
	}

	public void HandleBackToMenu()
	{
		_clearArmedSkill();
		_endInspectMode();
		_clearPlayerTargeting();
		_resetThreatHud();
		_modalStateController.Prepare(RuntimeUiResetReason.SessionTransition);
		_closeWeatherLabPanel(true);
		if (_session.GameStarted)
		{
			var path = _session.GetQuickSavePath();
			_doSave(path, _session.DescribeSavePath(path));
		}

		_hideSettingsPanels();
		_inputModule.CancelSelection();
		_panels.ClearFocus();
		_closeDialogUi();
		_closeTradeUi();
		_closeSkillBar();
		_closeDebugPanel();
		_showMainMenuWithCurrentContinue();
	}

	public void PrepareSessionTransition(bool clearLogs)
	{
		_clearArmedSkill();
		_endInspectMode();
		_clearPlayerTargeting();
		_resetThreatHud();
		_modalStateController.Prepare(RuntimeUiResetReason.SessionTransition);
		_closeWeatherLabPanel(true);
		if (clearLogs)
			_clearLog();
		_setPlayerDead(false);
		_disableWatchMode();
		_resetTimelineStatusLog();
	}

	public async void HandleMenuContinue()
	{
		if (!_resourcesReady() || _busyOperationActive())
			return;

		var continueTarget = _session.ResolveContinueTarget();
		if (continueTarget.Kind == ContinueTargetKind.None)
			return;

		_beginBusyOperation("ui.loading.continue.prepare", 0.15f);
		try
		{
			await _showBusyOperationStageAsync("ui.loading.continue.prepare", 0.15f);
			PrepareSessionTransition(true);

			await _showBusyOperationStageAsync("ui.loading.continue.load", 0.70f);
			var loaded = continueTarget.Kind switch
			{
				ContinueTargetKind.WorldCharacter => _session.LoadWorldCharacter(continueTarget.WorldId!, continueTarget.CharacterId!),
				ContinueTargetKind.LegacySave => _session.LoadGame(continueTarget.SavePath!),
				_ => SaveLoadStatus.NotFound,
			};
			if (loaded != SaveLoadStatus.Success)
				return;

			await _showBusyOperationStageAsync("ui.loading.continue.finalize", 0.95f);
			_refreshPlayerCharacterVisual();
			_clearLog();
			_log.Add(LocalizationService.T(
				"log.save.loaded",
				("label", continueTarget.Label),
				("floor", _state.PlayerZ)));
			_refreshLocalizedUi(false);
			_showGameHints();
			_doEnterGame();
		}
		finally
		{
			_endBusyOperation();
		}
	}

	public void HandleMenuWorlds()
	{
		if (!_resourcesReady() || _busyOperationActive())
			return;

		OpenWorldManager(WorldManagerContext.MainMenu, WorldLaunchTab.Worlds);
	}

	public async void HandleCharacterCreationConfirmed(PlayerCreationOptions options)
	{
		if (_busyOperationActive() || string.IsNullOrWhiteSpace(_pendingCharacterCreationWorldId))
			return;

		var worldId = _pendingCharacterCreationWorldId!;
		CloseCharacterCreationDialog();
		_beginBusyOperation("ui.loading.new_game.prepare", 0.15f);
		try
		{
			await _showBusyOperationStageAsync("ui.loading.new_game.prepare", 0.15f);
			PrepareSessionTransition(clearLogs: true);

			await _showBusyOperationStageAsync("ui.loading.new_game.generate", 0.72f);
			var entry = _session.StartWorldCharacter(worldId, options);

			await _showBusyOperationStageAsync("ui.loading.new_game.finalize", 0.95f);
			FinalizeNewGameStart();
			_showWorldCharacterEntryHint(entry.WorldName, entry.CharacterName);
			_showGameHints();
			_doEnterGame();
		}
		finally
		{
			_pendingCharacterCreationWorldId = null;
			_pendingCharacterCreationWorldName = null;
			_endBusyOperation();
		}
	}

	public async void HandleMenuMapEditor()
	{
		if (!_resourcesReady() || _busyOperationActive())
			return;

		_beginBusyOperation("ui.loading.blank_editor.prepare", 0.15f);
		try
		{
			await _showBusyOperationStageAsync("ui.loading.blank_editor.prepare", 0.15f);
			PrepareSessionTransition(clearLogs: true);

			await _showBusyOperationStageAsync("ui.loading.blank_editor.generate", 0.72f);
			_session.NewBlankEditorMap();

			await _showBusyOperationStageAsync("ui.loading.blank_editor.finalize", 0.95f);
			FinalizeBlankEditorStart();
			_doEnterGame();
		}
		finally
		{
			_endBusyOperation();
		}
	}

	public void DoStartNewGame(PlayerCreationOptions? options = null)
	{
		PrepareSessionTransition(clearLogs: true);
		_session.NewGame(options ?? PlayerCreationOptions.CreateDefault());
		FinalizeNewGameStart();
	}

	public void DoStartBlankEditor()
	{
		PrepareSessionTransition(clearLogs: true);
		_session.NewBlankEditorMap();
		FinalizeBlankEditorStart();
	}

	public void OpenCharacterCreationDialog(string worldId, string worldName)
	{
		_modalStateController.Prepare(RuntimeUiResetReason.OpenCharacterCreation);
		_pendingCharacterCreationWorldId = worldId;
		_pendingCharacterCreationWorldName = worldName;
		_characterCreation.Open(PlayerCreationOptions.CreateDefault(), worldName);
	}

	public void CloseCharacterCreationDialog()
	{
		if (!_characterCreation.Visible)
			return;

		_characterCreation.Close();
		_pendingCharacterCreationWorldId = null;
		_pendingCharacterCreationWorldName = null;
	}

	public void HandleCharacterCreationCanceled()
	{
		var selectedWorldId = _pendingCharacterCreationWorldId;
		CloseCharacterCreationDialog();
		OpenWorldManager(_worldManagerContext, WorldLaunchTab.Worlds, selectedWorldId);
	}

	public void OpenWorldSettingsDialog()
	{
		_modalStateController.Prepare(RuntimeUiResetReason.OpenWorldSettings);
		_worldSettingsDialog.Open(LocalizationService.T("ui.world_settings.default_name"));
	}

	public void CloseWorldSettingsDialog()
	{
		if (_worldSettingsDialog.Visible)
			_worldSettingsDialog.Close();
	}

	public void HandleWorldSettingsConfirmed(string worldName, WorldSettings settings)
	{
		var manifest = _session.CreateWorld(worldName, settings);
		CloseWorldSettingsDialog();
		OpenCharacterCreationDialog(manifest.WorldId, manifest.DisplayName);
	}

	public void HandleWorldSettingsCanceled()
	{
		CloseWorldSettingsDialog();
		OpenWorldManager(_worldManagerContext, WorldLaunchTab.Worlds, _worldManager.SelectedWorldId, _worldManager.SelectedCharacterId);
	}

	public void OpenMenuSettingsPanel()
	{
		_modalStateController.Prepare(RuntimeUiResetReason.OpenMenuSettings);
		_syncSettingsUiState(SettingsEntryContext.MainMenu);
		_settingsFlow.OpenSettings(SettingsEntryContext.MainMenu, SettingsTab.General);
		_menu.ShowSettingsFromMenu();
	}

	public void ToggleSettingsPanel()
	{
		if (_settingsFlow.HasVisibleOverlay)
		{
			_settingsFlow.CloseActiveOverlay();
			return;
		}

		if (_menu.InMenu)
		{
			OpenMenuSettingsPanel();
			return;
		}

		_modalStateController.Prepare(RuntimeUiResetReason.OpenMenuSettings);
		_syncSettingsUiState(SettingsEntryContext.InGamePause);
		_settingsFlow.OpenPauseMenu();
	}

	public void OpenLayoutEditMode()
	{
		if (_menu.InMenu || !_session.GameStarted || _layoutEditActive() || _mapEditorActive())
			return;

		_modalStateController.Prepare(RuntimeUiResetReason.EnterLayoutEdit);
		_beginLayoutEditMode();
	}

	public void EnterMapEditor(MapEditorEntryMode entryMode)
	{
		if (!_resourcesReady() || !_session.GameStarted || !_worldReadyForMapEditor())
			return;

		_modalStateController.Prepare(RuntimeUiResetReason.EnterMapEditor);
		_enterMapEditorCore(entryMode);
	}

	public void ToggleMapEditor()
	{
		if (!_resourcesReady())
			return;

		if (_mapEditorActive())
		{
			_exitMapEditor(false);
			return;
		}

		if (_menu.InMenu || !_session.GameStarted)
			return;

		EnterMapEditor(MapEditorEntryMode.InGame);
		_log.Add(LocalizationService.T("log.map_editor.enter"));
		_showMapEditorHints();
	}

	public void HandleQuickLoadRequested()
	{
		_hideSettingsPanels();
		var path = _session.GetQuickSavePath();
		_doLoad(path, _session.DescribeSavePath(path));
	}

	public void OpenWorldManager(
		WorldManagerContext context,
		WorldLaunchTab initialTab,
		string? selectedWorldId = null,
		string? selectedCharacterId = null)
	{
		_modalStateController.Prepare(RuntimeUiResetReason.OpenWorldManager);
		_worldManagerContext = context;
		_worldManagerTab = initialTab;
		ClearWorldManagerStatus();
		if (initialTab == WorldLaunchTab.Worlds && string.IsNullOrWhiteSpace(selectedWorldId))
			(selectedWorldId, selectedCharacterId) = ResolvePreferredWorldManagerSelection();
		RefreshWorldManagerContents(selectedWorldId, selectedCharacterId);
	}

	public void RefreshWorldManagerContents(string? selectedWorldId = null, string? selectedCharacterId = null)
	{
		var worlds = _session.ListWorlds();
		var scenarios = _session.ListScenarioEntries();
		var legacySaves = _session.ListLegacySaveEntries();
		var title = _worldManagerContext switch
		{
			WorldManagerContext.MainMenu => LocalizationService.T("ui.world_manager.title.main_menu"),
			_ => LocalizationService.T("ui.world_manager.title.in_game"),
		};
		var subtitle = _worldManagerContext switch
		{
			WorldManagerContext.MainMenu => LocalizationService.T("ui.world_manager.subtitle.main_menu"),
			_ => LocalizationService.T("ui.world_manager.subtitle.in_game"),
		};

		_worldManager.Open(
			worlds,
			scenarios,
			legacySaves,
			title,
			subtitle,
			_worldManagerTab,
			selectedWorldId ?? _worldManager.SelectedWorldId,
			selectedCharacterId ?? _worldManager.SelectedCharacterId);
		_worldManager.SetStatusMessage(_worldManagerStatusMessage, _worldManagerStatusIsError);
	}

	public void CloseWorldManager()
	{
		CloseConfirmDialog();
		_worldManager.Close();
	}

	public void HandleWorldManagerCreateCharacterRequested(string worldId)
	{
		var world = _session.ListWorlds()
			.FirstOrDefault(entry => string.Equals(entry.WorldId, worldId, StringComparison.Ordinal));
		if (world != null)
			OpenCharacterCreationDialog(world.WorldId, world.DisplayName);
	}

	public void HandleWorldManagerContinueCharacterRequested(string worldId, string characterId)
	{
		var world = _session.ListWorlds()
			.FirstOrDefault(entry => string.Equals(entry.WorldId, worldId, StringComparison.Ordinal));
		var character = world?.Characters
			.FirstOrDefault(entry => string.Equals(entry.CharacterId, characterId, StringComparison.Ordinal));
		if (character == null)
			return;

		var targetLabel = LocalizationService.T(
			"ui.continue_target.world_character",
			("character", character.CharacterName),
			("world", world?.DisplayName ?? character.WorldName));
		RequestWorldManagerLoad(targetLabel, () => _session.LoadWorldCharacter(worldId, characterId));
	}

	public void HandleWorldManagerScenarioRequested(string scenarioId)
	{
		var scenario = _session.ListScenarioEntries()
			.FirstOrDefault(entry => string.Equals(entry.Id, scenarioId, StringComparison.Ordinal));
		if (scenario == null)
			return;

		var targetLabel = LocalizationService.T("ui.session_label.scenario", ("name", scenario.DisplayName));
		RequestWorldManagerLoad(targetLabel, () => _session.LoadPresetScenario(scenarioId));
	}

	public void HandleWorldManagerLegacySaveRequested(SaveSlotInfo slot) =>
		RequestWorldManagerLoad(
			LocalizationService.T("ui.session_label.legacy_save", ("name", slot.DisplayName)),
			() => _session.LoadGame(slot.SourcePath));

	public void LoadFromMainMenu(string label, Func<SaveLoadStatus> loadAction)
	{
		_worldManagerContext = WorldManagerContext.MainMenu;
		ClearWorldManagerStatus();
		LoadFromWorldManager(label, loadAction);
	}

	public async void LoadFromWorldManager(string label, Func<SaveLoadStatus> loadAction)
	{
		if (_busyOperationActive())
			return;

		_beginBusyOperation("ui.loading.save.prepare", 0.16f);
		try
		{
			await _showBusyOperationStageAsync("ui.loading.save.prepare", 0.16f);
			_exitMapEditor(true);
			_closeWeatherLabPanel(true);

			await _showBusyOperationStageAsync("ui.loading.save.load", 0.76f);
			var status = loadAction();
			if (status != SaveLoadStatus.Success)
			{
				SetWorldManagerStatus(BuildLoadFailureMessage(status, label), isError: true);
				LogLoadFailure(status, label);
				RefreshWorldManagerContents();
				return;
			}

			await _showBusyOperationStageAsync("ui.loading.save.finalize", 0.95f);
			ClearWorldManagerStatus();
			CloseWorldManager();
			_clearArmedSkill();
			_clearPlayerTargeting();
			_stopPlayerRestMode();
			_resetThreatHud();
			_refreshPlayerCharacterVisual();
			_syncSettingsUiState(null);
			_refreshLocalizedUi(false);

			_log.Add(LocalizationService.T("log.save.loaded", ("label", label), ("floor", _state.PlayerZ)));
			if (_worldManagerContext == WorldManagerContext.MainMenu)
			{
				_clearLog();
				_log.Add(LocalizationService.T("log.save.loaded", ("label", label), ("floor", _state.PlayerZ)));
				_showGameHints();
				_doEnterGame();
				return;
			}

			_refreshWeatherLabSessionState(true);
			_hideSettingsPanels();
			_syncTimelineAutoAdvanceState();
			_flushMap();
		}
		finally
		{
			_endBusyOperation();
		}
	}

	public void HandleConfirmDialogActionSelected(string actionId)
	{
		var action = _confirmDialogActions.GetValueOrDefault(actionId);
		CloseConfirmDialog();
		action?.Invoke();
	}

	public void CloseConfirmDialog()
	{
		if (_confirmDialog.Visible)
			_confirmDialog.Close();
		_confirmDialogActions.Clear();
	}

	private void FinalizeNewGameStart()
	{
		_finalizeSessionPanels(true);
		_log.Add(LocalizationService.T("log.game.new_game_started"));
	}

	private void FinalizeBlankEditorStart()
	{
		_finalizeSessionPanels(false);
		_log.Add(LocalizationService.T("log.map_editor.blank_started"));
		EnterMapEditor(MapEditorEntryMode.MenuBlank);
		_showMapEditorHints();
	}

	private void RequestWorldManagerLoad(string targetLabel, Func<SaveLoadStatus> loadAction)
	{
		ClearWorldManagerStatus();
		if (_worldManagerContext != WorldManagerContext.InGame || !_session.RequiresSwitchConfirmation)
		{
			LoadFromWorldManager(targetLabel, loadAction);
			return;
		}

		var sourceLabel = _session.DescribeCurrentSessionLabel();
		if (_session.CanSaveAndSwitchCurrentSession)
		{
			OpenSwitchConfirmation(
				LocalizationService.T("ui.confirm_switch.title.world_character"),
				LocalizationService.T(
					"ui.confirm_switch.message.world_character",
					("source", sourceLabel),
					("target", targetLabel)),
				[
					new ConfirmDialogAction("save_switch", LocalizationService.T("ui.confirm_switch.action.save_and_switch")),
					new ConfirmDialogAction("switch", LocalizationService.T("ui.confirm_switch.action.switch_without_saving")),
					new ConfirmDialogAction("cancel", LocalizationService.T("ui.confirm_switch.action.cancel")),
				],
				defaultActionIndex: 0,
				onSaveAndSwitch: () => SaveCurrentSessionAndLoadFromWorldManager(targetLabel, loadAction),
				onSwitch: () => LoadFromWorldManager(targetLabel, loadAction));
			return;
		}

		OpenSwitchConfirmation(
			LocalizationService.T("ui.confirm_switch.title.simple"),
			LocalizationService.T(
				"ui.confirm_switch.message.simple",
				("source", sourceLabel),
				("target", targetLabel)),
			[
				new ConfirmDialogAction("switch", LocalizationService.T("ui.confirm_switch.action.switch")),
				new ConfirmDialogAction("cancel", LocalizationService.T("ui.confirm_switch.action.cancel")),
			],
			defaultActionIndex: 0,
			onSwitch: () => LoadFromWorldManager(targetLabel, loadAction));
	}

	private void SaveCurrentSessionAndLoadFromWorldManager(string label, Func<SaveLoadStatus> loadAction)
	{
		var path = _session.GetPreferredSavePath();
		_doSave(path, _session.DescribeSavePath(path));
		LoadFromWorldManager(label, loadAction);
	}

	private void OpenSwitchConfirmation(
		string title,
		string message,
		IReadOnlyList<ConfirmDialogAction> actions,
		int defaultActionIndex,
		Action? onSaveAndSwitch = null,
		Action? onSwitch = null)
	{
		_confirmDialogActions.Clear();
		if (onSaveAndSwitch != null)
			_confirmDialogActions["save_switch"] = onSaveAndSwitch;
		if (onSwitch != null)
			_confirmDialogActions["switch"] = onSwitch;
		_confirmDialog.Open(title, message, actions, defaultActionIndex);
	}

	private void SetWorldManagerStatus(string message, bool isError)
	{
		_worldManagerStatusMessage = message;
		_worldManagerStatusIsError = isError;
		_worldManager.SetStatusMessage(message, isError);
	}

	private void ClearWorldManagerStatus()
	{
		_worldManagerStatusMessage = null;
		_worldManagerStatusIsError = false;
		_worldManager.SetStatusMessage(null, isError: false);
	}

	private (string? WorldId, string? CharacterId) ResolvePreferredWorldManagerSelection()
	{
		var continueTarget = _session.ResolveContinueTarget();
		if (continueTarget.Kind == ContinueTargetKind.WorldCharacter)
			return (continueTarget.WorldId, continueTarget.CharacterId);

		var recentWorldCharacter = _session.ListWorlds()
			.SelectMany(static world => world.Characters)
			.OrderByDescending(static character => character.ModifiedAtUtc)
			.ThenByDescending(static character => character.SavedAtUtc)
			.FirstOrDefault();
		if (recentWorldCharacter != null)
			return (recentWorldCharacter.WorldId, recentWorldCharacter.CharacterId);

		return (null, null);
	}

	private void LogLoadFailure(SaveLoadStatus status, string label)
	{
		_log.Add(BuildLoadFailureMessage(status, label));
	}

	private static string BuildLoadFailureMessage(SaveLoadStatus status, string label)
	{
		var key = status switch
		{
			SaveLoadStatus.Incompatible => "log.save.incompatible",
			_ => "log.save.not_found",
		};
		return LocalizationService.T(key, ("label", label));
	}
}
