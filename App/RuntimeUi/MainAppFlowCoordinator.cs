using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Module.Session;
using MiniRPG.Module;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Panel;

namespace MiniRPG;

internal sealed class MainAppFlowCoordinator
{
	private readonly GameState _state;
	private readonly GameSessionModule _session;
	private readonly IGameSessionBackend _sessionBackend;
	private readonly LogModule _log;
	private readonly MenuModule _menu;
	private readonly SettingsFlowCoordinator _settingsFlow;
	private readonly WorldManagerModule _worldManager;
	private readonly WorldSettingsDialogModule _worldSettingsDialog;
	private readonly CharacterCreationModule _characterCreation;
	private readonly SaveNameDialogModule _saveNameDialog;
	private readonly ConfirmDialogModule _confirmDialog;
	private readonly LoadRecoveryDialogModule _loadRecoveryDialog;
	private readonly InputModule _inputModule;
	private readonly PanelManager _panels;
	private readonly ModalStateController _modalStateController;
	private readonly Func<bool> _resourcesReady;
	private readonly Func<bool> _busyOperationActive;
	private readonly Func<bool> _layoutEditActive;
	private readonly Func<bool> _mapEditorActive;
	private readonly Func<bool> _worldReadyForMapEditor;
	private readonly Action _showMainMenuWithCurrentContinue;
	private readonly Action _refreshMainMenuContinueState;
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
	private readonly Action _closeInGamePanels;
	private readonly Action _refreshPlayerCharacterVisual;
	private readonly Action<bool> _refreshLocalizedUi;
	private readonly Action<SettingsEntryContext?> _syncSettingsUiState;
	private readonly Action _syncTimelineAutoAdvanceState;
	private readonly Action<string, string> _doSave;
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
	private readonly WorldManagerStatusBanner _statusBanner;
	private readonly WorldManagerDeletionRouter _deletionRouter;
	private readonly Dictionary<string, Action> _confirmDialogActions = new(StringComparer.Ordinal);
	private PendingPreparedLoad? _pendingPreparedLoad;

	private static readonly BusyLoadStages ContinueLoadStages = new(
		"ui.loading.continue.prepare",
		0.15f,
		"ui.loading.continue.load",
		0.70f,
		"ui.loading.continue.finalize",
		0.95f);

	private static readonly BusyLoadStages SaveLoadStages = new(
		"ui.loading.save.prepare",
		0.16f,
		"ui.loading.save.load",
		0.76f,
		"ui.loading.save.finalize",
		0.95f);

	public MainAppFlowCoordinator(
		GameState state,
		GameSessionModule session,
		IGameSessionBackend sessionBackend,
		LogModule log,
		MenuModule menu,
		SettingsFlowCoordinator settingsFlow,
		WorldManagerModule worldManager,
		WorldSettingsDialogModule worldSettingsDialog,
		CharacterCreationModule characterCreation,
		SaveNameDialogModule saveNameDialog,
		ConfirmDialogModule confirmDialog,
		LoadRecoveryDialogModule loadRecoveryDialog,
		InputModule inputModule,
		PanelManager panels,
		ModalStateController modalStateController,
		Func<bool> resourcesReady,
		Func<bool> busyOperationActive,
		Func<bool> layoutEditActive,
		Func<bool> mapEditorActive,
		Func<bool> worldReadyForMapEditor,
		Action showMainMenuWithCurrentContinue,
		Action refreshMainMenuContinueState,
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
		Action closeInGamePanels,
		Action refreshPlayerCharacterVisual,
		Action<bool> refreshLocalizedUi,
		Action<SettingsEntryContext?> syncSettingsUiState,
		Action syncTimelineAutoAdvanceState,
		Action<string, string> doSave,
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
		_sessionBackend = sessionBackend;
		_log = log;
		_menu = menu;
		_settingsFlow = settingsFlow;
		_worldManager = worldManager;
		_worldSettingsDialog = worldSettingsDialog;
		_characterCreation = characterCreation;
		_saveNameDialog = saveNameDialog;
		_confirmDialog = confirmDialog;
		_loadRecoveryDialog = loadRecoveryDialog;
		_inputModule = inputModule;
		_panels = panels;
		_modalStateController = modalStateController;
		_resourcesReady = resourcesReady;
		_busyOperationActive = busyOperationActive;
		_layoutEditActive = layoutEditActive;
		_mapEditorActive = mapEditorActive;
		_worldReadyForMapEditor = worldReadyForMapEditor;
		_showMainMenuWithCurrentContinue = showMainMenuWithCurrentContinue;
		_refreshMainMenuContinueState = refreshMainMenuContinueState;
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
		_closeInGamePanels = closeInGamePanels;
		_refreshPlayerCharacterVisual = refreshPlayerCharacterVisual;
		_refreshLocalizedUi = refreshLocalizedUi;
		_syncSettingsUiState = syncSettingsUiState;
		_syncTimelineAutoAdvanceState = syncTimelineAutoAdvanceState;
		_doSave = doSave;
		_beginLayoutEditMode = beginLayoutEditMode;
		_enterMapEditorCore = enterMapEditorCore;
		_exitMapEditor = exitMapEditor;
		_flushMap = flushMap;
		_beginBusyOperation = beginBusyOperation;
		_showBusyOperationStageAsync = showBusyOperationStageAsync;
		_endBusyOperation = endBusyOperation;
		_statusBanner = new WorldManagerStatusBanner(worldManager, session);
		_deletionRouter = new WorldManagerDeletionRouter(
			session,
			worldManager,
			_statusBanner,
			refreshMainMenuContinueState,
			(selectedWorldId, selectedCharacterId) => RefreshWorldManagerContents(selectedWorldId, selectedCharacterId));
	}

	public void HandleBackToMenu()
	{
		_clearArmedSkill();
		_endInspectMode();
		_clearPlayerTargeting();
		_resetThreatHud();
		_modalStateController.Prepare(RuntimeUiResetReason.SessionTransition);

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
		_closeInGamePanels();
		_showMainMenuWithCurrentContinue();
	}

	public void PrepareSessionTransition(bool clearLogs)
	{
		_clearArmedSkill();
		_endInspectMode();
		_clearPlayerTargeting();
		_resetThreatHud();
		_modalStateController.Prepare(RuntimeUiResetReason.SessionTransition);

		if (clearLogs)
			_clearLog();
		_setPlayerDead(false);
		_disableWatchMode();
		_resetTimelineStatusLog();
	}

	public void HandleMenuContinue()
	{
		if (!_resourcesReady() || _busyOperationActive())
			return;

		var continueTarget = _session.ResolveContinueTarget();
		if (continueTarget.Kind == ContinueTargetKind.None)
			return;

		var prepared = continueTarget.Kind switch
		{
			ContinueTargetKind.WorldCharacter => _session.PrepareLoadWorldCharacter(continueTarget.WorldId!, continueTarget.CharacterId!),
			ContinueTargetKind.LegacySave => _session.PrepareLoadGame(continueTarget.SavePath!),
			_ => PreparedSessionLoad.FromFailure(SaveLoadStatus.NotFound),
		};
		StartPreparedLoad(
			prepared,
			selectedCandidateActorId => CommitPreparedLoadAsync(
				prepared,
				selectedCandidateActorId,
				continueTarget.Label,
				ContinueLoadStages,
				clearLogs: true,
				onSuccess: recoveredCandidate =>
				{
					_refreshPlayerCharacterVisual();
					_clearLog();
					LogRecoverySuccess(recoveredCandidate);
					_log.Add(LocalizationService.T(
						"log.save.loaded",
						("label", continueTarget.Label),
						("floor", _state.PlayerZ)));
					_refreshLocalizedUi(false);
					_showGameHints();
					_doEnterGame();
				},
				onFailure: _ => { }),
			onPreviewFailure: _ => { });
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
			var startResult = await _sessionBackend.StartAsync(new GameSessionStartRequest
			{
				Kind = GameSessionStartKind.WorldCharacter,
				WorldId = worldId,
				PlayerCreationOptions = options,
			});
			if (!startResult.Success)
			{
				_log.Add(startResult.FailureReason ?? LocalizationService.T("ui.command.unknown"));
				return;
			}

			await _showBusyOperationStageAsync("ui.loading.new_game.finalize", 0.95f);
			FinalizeNewGameStart();
			if (startResult.WorldCharacterEntry != null)
				_showWorldCharacterEntryHint(startResult.WorldCharacterEntry.WorldName, startResult.WorldCharacterEntry.CharacterName);
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
			var startResult = await _sessionBackend.StartAsync(new GameSessionStartRequest
			{
				Kind = GameSessionStartKind.BlankEditor,
			});
			if (!startResult.Success)
			{
				_log.Add(startResult.FailureReason ?? LocalizationService.T("ui.command.unknown"));
				return;
			}

			await _showBusyOperationStageAsync("ui.loading.blank_editor.finalize", 0.95f);
			FinalizeBlankEditorStart();
			_doEnterGame();
		}
		finally
		{
			_endBusyOperation();
		}
	}

	public async void DoStartNewGame(PlayerCreationOptions? options = null)
	{
		PrepareSessionTransition(clearLogs: true);
		var startResult = await _sessionBackend.StartAsync(new GameSessionStartRequest
		{
			Kind = GameSessionStartKind.NewGame,
			PlayerCreationOptions = options ?? PlayerCreationOptions.CreateDefault(),
		});
		if (!startResult.Success)
		{
			_log.Add(startResult.FailureReason ?? LocalizationService.T("ui.command.unknown"));
			return;
		}
		FinalizeNewGameStart();
	}

	public async void DoStartBlankEditor()
	{
		PrepareSessionTransition(clearLogs: true);
		var startResult = await _sessionBackend.StartAsync(new GameSessionStartRequest
		{
			Kind = GameSessionStartKind.BlankEditor,
		});
		if (!startResult.Success)
		{
			_log.Add(startResult.FailureReason ?? LocalizationService.T("ui.command.unknown"));
			return;
		}
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
		var path = _session.GetQuickSavePath();
		var label = _session.DescribeSavePath(path);
		var prepared = _session.PrepareLoadGame(path);
		StartPreparedLoad(
			prepared,
			selectedCandidateActorId => CommitPreparedLoadAsync(
				prepared,
				selectedCandidateActorId,
				label,
				SaveLoadStages,
				clearLogs: false,
				onSuccess: recoveredCandidate =>
				{
					_refreshPlayerCharacterVisual();
					_syncSettingsUiState(null);
					_refreshLocalizedUi(false);
					LogRecoverySuccess(recoveredCandidate);
					_log.Add(LocalizationService.T("log.save.loaded", ("label", label), ("floor", _state.PlayerZ)));

					_syncTimelineAutoAdvanceState();
					_flushMap();
				},
				onFailure: status => LogLoadFailure(status, label)),
			onPreviewFailure: status => LogLoadFailure(status, label));
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
		_statusBanner.Clear();
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
		_statusBanner.Apply();
	}

	public void CloseWorldManager()
	{
		CloseConfirmDialog();
		CloseLoadRecoveryDialog();
		_worldManager.Close();
		if (_worldManagerContext == WorldManagerContext.MainMenu)
			_refreshMainMenuContinueState();
	}

	public void HandleWorldManagerCreateCharacterRequested(string worldId)
	{
		var world = _session.ListWorlds()
			.FirstOrDefault(entry => string.Equals(entry.WorldId, worldId, StringComparison.Ordinal));
		if (world != null)
			OpenCharacterCreationDialog(world.WorldId, world.DisplayName);
	}

	public void HandleWorldManagerDeleteWorldRequested(string worldId)
	{
		if (_busyOperationActive())
			return;

		var world = _session.ListWorlds()
			.FirstOrDefault(entry => string.Equals(entry.WorldId, worldId, StringComparison.Ordinal));
		if (world == null)
		{
			_statusBanner.Set(LocalizationService.T("ui.world_manager.status.delete_not_found"), isError: true);
			RefreshWorldManagerContents();
			return;
		}

		_confirmDialogActions.Clear();
		_confirmDialogActions["delete_world"] = () => _deletionRouter.DeleteWorld(world.WorldId, world.DisplayName);
		_confirmDialog.Open(
			LocalizationService.T("ui.confirm_world_delete.title"),
			LocalizationService.T(
				"ui.confirm_world_delete.message",
				("world", world.DisplayName),
				("characters", world.CharacterCount)),
			[
				new ConfirmDialogAction("delete_world", LocalizationService.T("ui.confirm_world_delete.action.delete")),
				new ConfirmDialogAction("cancel", LocalizationService.T("ui.confirm_switch.action.cancel")),
			],
			defaultActionIndex: 1);
	}

	public void HandleWorldManagerDeleteSaveDataRequested(string worldId)
	{
		if (_busyOperationActive())
			return;

		var world = _session.ListWorlds()
			.FirstOrDefault(entry => string.Equals(entry.WorldId, worldId, StringComparison.Ordinal));
		if (world == null)
		{
			_statusBanner.Set(LocalizationService.T("ui.world_manager.status.delete_save_data_not_found"), isError: true);
			RefreshWorldManagerContents();
			return;
		}

		_confirmDialogActions.Clear();
		_confirmDialogActions["delete_save_data"] = () => _deletionRouter.DeleteWorldSaveData(world.WorldId, world.DisplayName);
		_confirmDialog.Open(
			LocalizationService.T("ui.confirm_world_delete_save_data.title"),
			LocalizationService.T(
				"ui.confirm_world_delete_save_data.message",
				("world", world.DisplayName),
				("characters", world.CharacterCount)),
			[
				new ConfirmDialogAction("delete_save_data", LocalizationService.T("ui.confirm_world_delete_save_data.action.delete")),
				new ConfirmDialogAction("cancel", LocalizationService.T("ui.confirm_switch.action.cancel")),
			],
			defaultActionIndex: 1);
	}

	public void HandleWorldManagerCleanAssetsRequested(string worldId)
	{
		if (_busyOperationActive())
			return;

		var world = _session.ListWorlds()
			.FirstOrDefault(entry => string.Equals(entry.WorldId, worldId, StringComparison.Ordinal));
		if (world == null)
		{
			_statusBanner.Set(LocalizationService.T("ui.world_manager.status.clean_assets_not_found"), isError: true);
			RefreshWorldManagerContents();
			return;
		}

		_confirmDialogActions.Clear();
		_confirmDialogActions["clean_assets"] = () => _deletionRouter.CleanWorldAssets(world.WorldId, world.DisplayName);
		_confirmDialog.Open(
			LocalizationService.T("ui.confirm_world_clean_assets.title"),
			LocalizationService.T(
				"ui.confirm_world_clean_assets.message",
				("world", world.DisplayName)),
			[
				new ConfirmDialogAction("clean_assets", LocalizationService.T("ui.confirm_world_clean_assets.action.delete")),
				new ConfirmDialogAction("cancel", LocalizationService.T("ui.confirm_switch.action.cancel")),
			],
			defaultActionIndex: 1);
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
		RequestWorldManagerLoad(targetLabel, () => _session.PrepareLoadWorldCharacter(worldId, characterId));
	}

	public void HandleWorldManagerScenarioRequested(string scenarioId)
	{
		var scenario = _session.ListScenarioEntries()
			.FirstOrDefault(entry => string.Equals(entry.Id, scenarioId, StringComparison.Ordinal));
		if (scenario == null)
			return;

		var targetLabel = LocalizationService.T("ui.session_label.scenario", ("name", scenario.DisplayName));
		RequestWorldManagerLoad(targetLabel, () => _session.PrepareLoadPresetScenario(scenarioId));
	}

	public void HandleWorldManagerLegacySaveRequested(SaveSlotInfo slot) =>
		RequestWorldManagerLoad(
			LocalizationService.T("ui.session_label.legacy_save", ("name", slot.DisplayName)),
			() => _session.PrepareLoadGame(slot.SourcePath));

	public void LoadFromMainMenu(string label, Func<PreparedSessionLoad> prepareAction)
	{
		if (_busyOperationActive())
			return;

		_worldManagerContext = WorldManagerContext.MainMenu;
		_statusBanner.Clear();
		var prepared = prepareAction();
		StartPreparedLoad(
			prepared,
			selectedCandidateActorId => CommitPreparedLoadAsync(
				prepared,
				selectedCandidateActorId,
				label,
				SaveLoadStages,
				clearLogs: true,
				onSuccess: recoveredCandidate =>
				{
					_refreshPlayerCharacterVisual();
					_syncSettingsUiState(null);
					_refreshLocalizedUi(false);
					_clearLog();
					LogRecoverySuccess(recoveredCandidate);
					_log.Add(LocalizationService.T("log.save.loaded", ("label", label), ("floor", _state.PlayerZ)));
					_showGameHints();
					_doEnterGame();
				},
				onFailure: status => LogLoadFailure(status, label)),
			onPreviewFailure: status => LogLoadFailure(status, label));
	}

	public void LoadFromWorldManager(string label, Func<PreparedSessionLoad> prepareAction)
	{
		if (_busyOperationActive())
			return;

		var prepared = prepareAction();
		StartPreparedLoad(
			prepared,
			selectedCandidateActorId => CommitPreparedLoadAsync(
				prepared,
				selectedCandidateActorId,
				label,
				SaveLoadStages,
				clearLogs: _worldManagerContext == WorldManagerContext.MainMenu,
				onSuccess: recoveredCandidate =>
				{
					_statusBanner.Clear();
					_refreshPlayerCharacterVisual();
					_syncSettingsUiState(null);
					_refreshLocalizedUi(false);

					if (_worldManagerContext == WorldManagerContext.MainMenu)
					{
						_clearLog();
						LogRecoverySuccess(recoveredCandidate);
						_log.Add(LocalizationService.T("log.save.loaded", ("label", label), ("floor", _state.PlayerZ)));
						_showGameHints();
						_doEnterGame();
						return;
					}

					LogRecoverySuccess(recoveredCandidate);
					_log.Add(LocalizationService.T("log.save.loaded", ("label", label), ("floor", _state.PlayerZ)));

					_syncTimelineAutoAdvanceState();
					_flushMap();
				},
				onFailure: status =>
				{
					_statusBanner.Set(BuildLoadFailureMessage(status, label), isError: true);
					LogLoadFailure(status, label);
				}),
			onPreviewFailure: status =>
			{
				_statusBanner.Set(BuildLoadFailureMessage(status, label), isError: true);
				LogLoadFailure(status, label);
				RefreshWorldManagerContents();
			});
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

	public async void HandleLoadRecoveryConfirmed(string actorId)
	{
		if (_busyOperationActive())
			return;

		var pending = _pendingPreparedLoad;
		if (pending == null)
			return;

		CloseLoadRecoveryDialog();
		await pending.CommitAsync(actorId);
	}

	public void CloseLoadRecoveryDialog()
	{
		_pendingPreparedLoad = null;
		if (_loadRecoveryDialog.Visible)
			_loadRecoveryDialog.Close();
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

	private void StartPreparedLoad(
		PreparedSessionLoad preparedLoad,
		Func<string?, Task> commitAsync,
		Action<SaveLoadStatus> onPreviewFailure)
	{
		if (preparedLoad.Status != SaveLoadStatus.Success)
		{
			onPreviewFailure(preparedLoad.Status);
			return;
		}

		if (preparedLoad.Recovery == null)
		{
			_ = commitAsync(null);
			return;
		}

		CloseLoadRecoveryDialog();
		_pendingPreparedLoad = new PendingPreparedLoad
		{
			CommitAsync = commitAsync,
		};
		CloseConfirmDialog();
		_loadRecoveryDialog.Open(preparedLoad.Recovery);
	}

	private async Task CommitPreparedLoadAsync(
		PreparedSessionLoad preparedLoad,
		string? selectedCandidateActorId,
		string label,
		BusyLoadStages stages,
		bool clearLogs,
		Action<PreparedLoadCandidate?> onSuccess,
		Action<SaveLoadStatus> onFailure)
	{
		if (_busyOperationActive())
			return;

		var recoveredCandidate = ResolveRecoveredCandidate(preparedLoad, selectedCandidateActorId);
		_beginBusyOperation(stages.PrepareKey, stages.PrepareProgress);
		try
		{
			await _showBusyOperationStageAsync(stages.PrepareKey, stages.PrepareProgress);
			PrepareSessionTransition(clearLogs);

			await _showBusyOperationStageAsync(stages.LoadKey, stages.LoadProgress);
			var status = _session.CommitPreparedLoad(preparedLoad, selectedCandidateActorId);
			if (status != SaveLoadStatus.Success)
			{
				onFailure(status);
				return;
			}

			await _showBusyOperationStageAsync(stages.FinalizeKey, stages.FinalizeProgress);
			onSuccess(recoveredCandidate);
		}
		finally
		{
			_endBusyOperation();
		}
	}

	private static PreparedLoadCandidate? ResolveRecoveredCandidate(
		PreparedSessionLoad preparedLoad,
		string? selectedCandidateActorId)
	{
		if (preparedLoad.Recovery == null)
			return null;

		foreach (var candidate in preparedLoad.Recovery.Candidates)
		{
			if (string.Equals(candidate.ActorId, selectedCandidateActorId, StringComparison.Ordinal))
				return candidate;
		}

		return null;
	}

	private void LogRecoverySuccess(PreparedLoadCandidate? recoveredCandidate)
	{
		if (recoveredCandidate == null)
			return;

		_log.Add(LocalizationService.T(
			"log.save.recovered_player_binding",
			("name", recoveredCandidate.DisplayName),
			("actorId", recoveredCandidate.ActorId),
			("x", recoveredCandidate.X),
			("y", recoveredCandidate.Y),
			("z", recoveredCandidate.Z)));
	}

	private void RequestWorldManagerLoad(string targetLabel, Func<PreparedSessionLoad> prepareAction)
	{
		_statusBanner.Clear();
		if (_worldManagerContext != WorldManagerContext.InGame || !_session.RequiresSwitchConfirmation)
		{
			LoadFromWorldManager(targetLabel, prepareAction);
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
				onSaveAndSwitch: () => SaveCurrentSessionAndLoadFromWorldManager(targetLabel, prepareAction),
				onSwitch: () => LoadFromWorldManager(targetLabel, prepareAction));
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
			onSwitch: () => LoadFromWorldManager(targetLabel, prepareAction));
	}

	private void SaveCurrentSessionAndLoadFromWorldManager(string label, Func<PreparedSessionLoad> prepareAction)
	{
		var path = _session.GetPreferredSavePath();
		_doSave(path, _session.DescribeSavePath(path));
		LoadFromWorldManager(label, prepareAction);
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

	private sealed class PendingPreparedLoad
	{
		public required Func<string?, Task> CommitAsync { get; init; }
	}

	private readonly record struct BusyLoadStages(
		string PrepareKey,
		float PrepareProgress,
		string LoadKey,
		float LoadProgress,
		string FinalizeKey,
		float FinalizeProgress);
}
