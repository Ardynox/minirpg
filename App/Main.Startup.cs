using Godot;
using System;
using System.Globalization;
using System.Threading.Tasks;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Network;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;
using MiniRPG.Module.Session;

namespace MiniRPG;

public partial class Main
{
	private const float StartupVisualFloor = 0.06f;
	private const float StartupVisualPlateau = 0.92f;
	private const float StartupVisualProgressPerSecond = 0.18f;
	private const float StartupThreadedLoadTimeoutSeconds = 10f;
	private const float StartupSyncFallbackProgress = 0.90f;
	private static readonly string[] DeferredUiScenePaths =
		[ChestPanelScenePath, DialogPanelScenePath, TradePanelScenePath, QuestPanelScenePath, DebugPanelScenePath, StatusPanelScenePath];
	private static readonly StartupHeavyLoadStep[] StartupHeavyLoadSteps = [];

	private Control _startupOverlay = null!;
	private Label _startupStatusLabel = null!;
	private ProgressBar _startupProgressBar = null!;
	private bool _busyOperationActive;
	private float _busyOperationProgress;
	private string _busyOperationStatusKey = "ui.loading.new_game.prepare";

	private void BindStartupOverlayNodes()
	{
		var startupOverlay = GetNode<Control>($"{OverlayRootPath}/StartupOverlay");
		var startupStatusLabel = GetNode<Label>($"{OverlayRootPath}/StartupOverlay/Bar/Margin/VBox/Status");
		var startupProgressBar = GetNode<ProgressBar>($"{OverlayRootPath}/StartupOverlay/Bar/Margin/VBox/Progress");
		_startupOverlay = startupOverlay;
		_startupStatusLabel = startupStatusLabel;
		_startupProgressBar = startupProgressBar;
	}

	private bool ShouldSkipRuntimeCallbacks() => _startupCoordinator != null && _startupCoordinator.State == StartupState.Failed;

	private void BeginHeavyStartupLoad() => _startupCoordinator?.BeginHeavyStartupLoad();

	private void PollHeavyStartupLoad() => _startupCoordinator?.PollHeavyStartupLoad(StoreLoadedHeavyResource, FinalizeHeavyStartupLoad);

	private void PollPostStartupTasks() => _startupCoordinator?.PollPostStartupTasks();

	private void RefreshStartupUi()
	{
		if (_startupOverlay == null || _startupCoordinator == null)
			return;

		var startupActive = _startupCoordinator.State != StartupState.Ready;
		var overlayVisible = startupActive || _busyOperationActive;
		_startupOverlay.Visible = overlayVisible;

		if (startupActive)
		{
			_startupStatusLabel.Text = LocalizationService.T(_startupCoordinator.StartupStatusKey);
			_startupProgressBar.Value = Math.Round(_startupCoordinator.StartupProgress * 100f);
		}
		else if (_busyOperationActive)
		{
			_startupStatusLabel.Text = LocalizationService.T(_busyOperationStatusKey);
			_startupProgressBar.Value = Math.Round(_busyOperationProgress * 100f);
		}

		_startupOverlay.MouseFilter = overlayVisible && (_busyOperationActive || _startupCoordinator.State != StartupState.Failed)
			? Control.MouseFilterEnum.Stop
			: Control.MouseFilterEnum.Ignore;
		if (_menu != null && _session != null)
			RefreshMainMenuContinueState();
		SyncSettingsUiState();
		RefreshPanelLauncherState();
	}

	private bool StoreLoadedHeavyResource(string path)
	{
		_ = path;
		return true;
	}

	private void FinalizeHeavyStartupLoad()
	{
		if (_startupCoordinator == null || _startupCoordinator.State != StartupState.Finalizing)
			return;

		try
		{
			var viewportContainer = GetNode<SubViewportContainer>($"{HudRootPath}/TopRow/MapPanel/SubViewportContainer");
			var subViewport = viewportContainer.GetNode<SubViewport>("SubViewport");
			var mapRoot = subViewport.GetNode<Node2D>("MapRoot");
			var camera = subViewport.GetNode<Camera2D>("Camera2D");

			mapRoot.GetNodeOrNull<Node>("PlayerSpine")?.QueueFree();
			mapRoot.GetNodeOrNull<Node>("PlayerCharacter")?.QueueFree();
			var playerCharacter = CreatePlayerCharacterNode();
			mapRoot.AddChild(playerCharacter);

			_mapRender = new IsometricVoxelRenderer(_state, _fogTracker, ViewW, ViewH);
			_mapRender.Init(mapRoot, tileSet: null, viewportContainer, subViewport, playerCharacter, camera);
			_mapRender.SetZoomRange(_mapZoomMin, _mapZoomMax);
			_mapRender.SetWeatherScreenFxTuning(new WeatherScreenFxTuningSet());
			_combatFxPlayer = new CombatFxPlayer(_mapRender, _mapRender.CombatFxWorldRoot, _combatFxTextRoot);
			if (_runtime != null)
				_runtime.UiRefs.MapRender = _mapRender;
			_startupCoordinator.MarkReady();
		}
		catch (Exception ex)
		{
			_startupCoordinator.FailHeavyStartupLoad("startup-finalize", ex.Message);
		}
	}

	private void FailStartupBootstrap(string path, Exception ex) => _startupCoordinator?.FailStartupBootstrap(path, ex);

	private void BeginBusyOperation(string statusKey, float progress)
	{
		_busyOperationActive = true;
		_busyOperationStatusKey = statusKey;
		_busyOperationProgress = Math.Clamp(progress, 0f, 1f);
		RefreshStartupUi();
	}

	private void UpdateBusyOperation(string statusKey, float progress)
	{
		_busyOperationStatusKey = statusKey;
		_busyOperationProgress = Math.Clamp(progress, 0f, 1f);
		RefreshStartupUi();
	}

	private void EndBusyOperation()
	{
		_busyOperationActive = false;
		_busyOperationProgress = 0f;
		RefreshStartupUi();
		RefreshPanelLauncherState();
	}

	private async Task ShowBusyOperationStageAsync(string statusKey, float progress)
	{
		if (!_busyOperationActive)
			BeginBusyOperation(statusKey, progress);
		else
			UpdateBusyOperation(statusKey, progress);

		if (IsInsideTree())
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private void PrewarmDeferredUiScenes()
	{
		foreach (var path in DeferredUiScenePaths)
			ResAccess.RequestAsync(path, _ => { });
	}

	// ── _Ready() 分阶段初始化 ────────────────────────────

	private Theme? _uiTheme;
	private Control _floatingRoot = null!;
	private CanvasLayer _overlayLayer = null!;
	private PanelContainer _logPanelNode = null!;
	private RichTextLabel _logContent = null!;

	private void InitializeStartupAndConfig()
	{
		BindStartupOverlayNodes();
		_startupCoordinator = new MainStartupCoordinator(
			StartupHeavyLoadSteps,
			StartupVisualFloor,
			StartupVisualPlateau,
			StartupVisualProgressPerSecond,
			StartupThreadedLoadTimeoutSeconds,
			StartupSyncFallbackProgress,
			onStartupReady: () => { },
			onStartupFailed: () => HandleAutoTestCliStartupFailed(_startupCoordinator.LastFailedPath ?? "startup"),
			onWarning: GD.PushWarning,
			refreshStartupUi: RefreshStartupUi,
			prewarmDeferredUiScenes: PrewarmDeferredUiScenes);
		ConfigureGodotBridge();
		GameConfig.Load();
		FinalizeAutoTestCliConfig();
		PresetDB.Load();
		LocalizationService.Initialize();
		LocalizationService.SetLocale(AppSettingsStore.LoadLocale(), notify: false);
		_enableKeyboardTargeting = AppSettingsStore.LoadEnableKeyboardTargeting();
		_fastTurnModeEnabled = AppSettingsStore.LoadFastTurnMode();
		_enableDebugPanel = AppSettingsStore.LoadEnableDebugPanel();
		_mapZoomMin = AppSettingsStore.LoadMapZoomMin();
		_mapZoomMax = AppSettingsStore.LoadMapZoomMax();
		_predictionConfig = LoadPredictionConfigFromEnvironment();
		_predictionCorrectionSmoothingSeconds = LoadPredictionSmoothingSecondsFromEnvironment();
		if (_mapZoomMin > _mapZoomMax)
			(_mapZoomMin, _mapZoomMax) = (_mapZoomMax, _mapZoomMin);
		TerrainRegistry.Load("terrains.json");
		GameLocalizer.CaptureBaseSnapshots();
		GameLocalizer.ApplyPresetTranslations();
		MiniRPG.Core.Conversation.ConversationDefLoader.EnsureLoaded();
		ResAccess.Load();
		_combatFxRegistry = CombatFxRegistry.Load();
		PlayerAppearanceCatalog.LoadProjectCatalog();
		FacePartCatalog.LoadProjectCatalog();
		MapSpriteTemplateCatalog.LoadProjectCatalog();
		_fogTracker = new FogOfWarTracker(GameConfig.PlayerVision);
	}

	private void InitializeCoreServices()
	{
		_session = new GameSessionModule(_state, _fogTracker);
		_incidentStatistics = new MiniRPG.Core.Events.IncidentStatistics();
		_relationships = new MiniRPG.Core.Social.RelationshipModule();
		_actorMemories = new MiniRPG.Core.Social.ActorMemoryModule();
		_rumorBus = new MiniRPG.Core.Social.RumorBus();
		_consequenceRouter = new MiniRPG.Core.Events.GameEventConsequenceRouter(
			errorSink: message => _log?.Add($"[Consequence] {message}"));
		_consequenceRouter.Register(_incidentStatistics);
		_consequenceRouter.Register(_relationships);
		_consequenceRouter.Register(_actorMemories);
		_consequenceRouter.Register(_rumorBus);
		_consequenceRouter.Register(new MiniRPG.Core.Demographics.KinshipLossCapturer());
		_deathReportRecorder = new MiniRPG.Core.Combat.DeathReportRecorder();
		_consequenceRouter.Register(_deathReportRecorder);
		// Inject the same simulation-side modules into the AIDispatcher
		// ambient context so social InputResolvers can read live state;
		// without these the AIBehaviorContext.Relationships/ActorMemories/Rumors
		// stay null and every relationship-aware Consideration scores 0.
		MiniRPG.Core.AI.AIDispatcher.Relationships = _relationships;
		MiniRPG.Core.AI.AIDispatcher.ActorMemories = _actorMemories;
		MiniRPG.Core.AI.AIDispatcher.Rumors = _rumorBus;
		var localBackend = new LocalSessionBackend(_session, _state, Dispatch);
		localBackend.AttachConsequenceRouter(_consequenceRouter);
		_sessionBackend = localBackend;
		_localServerLauncher = new LocalProcessServerLauncher(ProjectSettings.GlobalizePath("res://"));
		_menu = new MenuModule(this);
	}

	private void BindSceneTreeNodes()
	{
		_mapPanelNode = GetNode<PanelContainer>($"{HudRootPath}/TopRow/MapPanel");
		_logPanelNode = GetNode<PanelContainer>($"{HudRootPath}/LogPanel");
		_logContent = _logPanelNode.GetNode<RichTextLabel>("MarginContainer/VBox/ContentText");
		_log = new LogModule(_logContent);
		_inputBar = GetNode<LineEdit>($"{HudRootPath}/InputBar");
		BindWorldHoverOverlay();
		_altLabelOverlayController = new AltLabelOverlayController(
			_state,
			_fogTracker,
			() => _mapRender,
			() => _session,
			() => _menu,
			() => RenderReady,
			GetCurrentVisibleWorldHalfExtents,
			() => GetNode<Control>(HudRootPath),
			GetNode<CanvasLayer>(OverlayRootPath));
		BindAltLabelOverlay();
		BindPanelLauncherBar();
		_mapEditor = new MapEditorSession(_state);

		_combatFxTextRoot = CreateMapOverlayRoot(_mapPanelNode);

		_uiTheme = GetNode<Control>(HudRootPath).Theme;
		UIScaleService.Apply(_uiTheme, AppSettingsStore.LoadUiFontScale());
		UIAccessibilityService.Apply(
			_uiTheme,
			ParseContrastMode(AppSettingsStore.LoadUiContrastMode()),
			ParseColorBlindMode(AppSettingsStore.LoadUiColorBlindMode()));
		_floatingRoot = new Control { Name = "FloatingPanels", MouseFilter = Control.MouseFilterEnum.Ignore };
		_floatingRoot.Theme = _uiTheme;
		_overlayLayer = GetNode<CanvasLayer>(OverlayRootPath);
		_overlayLayer.AddChild(_floatingRoot);
		var threatHudNode = ThreatHudModule.CreateControl(_uiTheme);
		_overlayLayer.AddChild(threatHudNode);
		_threatHud = new ThreatHudModule(threatHudNode);
		var targetSummaryHudNode = TargetSummaryHudModule.CreateControl(_uiTheme);
		_overlayLayer.AddChild(targetSummaryHudNode);
		_targetSummaryHud = new TargetSummaryHudModule(targetSummaryHudNode);
		var needsHudNode = NeedsHudModule.CreateControl(_uiTheme);
		_overlayLayer.AddChild(needsHudNode);
		_needsHud = new NeedsHudModule(needsHudNode);
		var healthAlertsNode = HealthAlertsModule.CreateControl(_uiTheme);
		_overlayLayer.AddChild(healthAlertsNode);
		_healthAlerts = new HealthAlertsModule(healthAlertsNode);
		var partyHudNode = PartyHudModule.CreateControl(_uiTheme);
		_overlayLayer.AddChild(partyHudNode);
		_partyHud = new PartyHudModule(partyHudNode);
		var incidentAlertNode = IncidentAlertModule.CreateControl(_uiTheme);
		_overlayLayer.AddChild(incidentAlertNode);
		_incidentAlerts = new IncidentAlertModule(incidentAlertNode);

		_toastOverlay = new ToastOverlay();
		_overlayLayer.AddChild(_toastOverlay);

		_playerDeathReportPanel = new MiniRPG.Module.Panel.PlayerDeathReportPanel(
			onBackToMenu: ShowMainMenuWithCurrentContinue);
		_overlayLayer.AddChild(_playerDeathReportPanel);
		_playerDeathPresenter = new PlayerDeathPresenter(
			getReport: (actorId, currentTurn) => _deathReportRecorder?.GetOrBuildReport(_state, actorId, currentTurn),
			showReportPanel: (report, isPartyWipe) => _playerDeathReportPanel.ShowReport(report, isPartyWipe),
			addLog: text => _log?.Add(text));

		_richTooltips = new RichTooltipLayer();
		_overlayLayer.AddChild(_richTooltips);
	}

	private void InitializePanelsAndChrome()
	{
		var layoutStore = new PanelLayoutStore();
		var buttonScaleService = new PanelButtonScaleService();
		PanelButtonScaleRegistry.Bind(buttonScaleService);
		_panelLayouts = new PanelLayoutService(layoutStore, buttonScaleService);
		_panelLayouts.Initialize();
		_panelDrag = new PanelDragService(layoutStore, _floatingRoot);
		_panelChrome = new PanelHoverChromeService(_floatingRoot, _panelLayouts, _panelDrag);
		_inputBindings = new InputBindingService(ProjectSettings.GlobalizePath("user://keybindings.json"));
		_inputBindings.Changed += () =>
		{
			ApplyPanelLauncherTooltips();
			RefreshPanelLauncherState();
		};

		_statusPanelModule = new StatusPanelModule(GetNode<PanelContainer>($"{HudRootPath}/TopRow/StatusPanel"));
		_turnPanelModule = new TurnPanelModule(GetNode<PanelContainer>($"{HudRootPath}/TurnPanel"));
		var pauseMenuNode = GetNode<PanelContainer>($"{OverlayRootPath}/PauseMenuPanel");
		pauseMenuNode.Theme = _uiTheme;
		_pauseMenuPanelModule = new PauseMenuPanelModule(pauseMenuNode);
		var settingsPanelNode = GetNode<PanelContainer>($"{OverlayRootPath}/SettingsPanel");
		settingsPanelNode.Theme = _uiTheme;
		_settingsPanelModule = new SettingsPanelModule(settingsPanelNode, _inputBindings);
		var layoutEditBarNode = GetNode<PanelContainer>($"{OverlayRootPath}/LayoutEditBar");
		layoutEditBarNode.Theme = _uiTheme;
		_layoutEditBar = new LayoutEditBarModule(layoutEditBarNode);
		var worldManagerNode = GetNode<PanelContainer>($"{OverlayRootPath}/WorldManager");
		worldManagerNode.Theme = _uiTheme;
		_worldManager = new WorldManagerModule(worldManagerNode);
		var mapEditorBarNode = GetNode<PanelContainer>($"{OverlayRootPath}/MapEditorBar");
		mapEditorBarNode.Theme = _uiTheme;
		_mapEditorBar = new MapEditorBarModule(mapEditorBarNode);
		InitializeRuntimeWorldToolUi();
		var turnControllerNode = GetNode<PanelContainer>($"{OverlayRootPath}/TurnControllerPanel");
		turnControllerNode.Theme = _uiTheme;
		var turnControllerModule = new TurnControllerPanelModule(turnControllerNode);
		_turnControllerPanelController = new TurnControllerPanelController(
			turnControllerModule,
			AdvanceTimelineAutoStep,
			FlushMap,
			() => _state.Turn);

		var saveNameDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/SaveNameDialog");
		saveNameDialogNode.Theme = _uiTheme;
		_saveNameDialog = new SaveNameDialogModule(saveNameDialogNode);
		var characterCreationNode = GetNode<PanelContainer>($"{OverlayRootPath}/CharacterCreationDialog");
		characterCreationNode.Theme = _uiTheme;
		_characterCreation = new CharacterCreationModule(characterCreationNode);
		var characterCustomizationNode = GetNode<PanelContainer>($"{OverlayRootPath}/CharacterCustomizationPanel");
		characterCustomizationNode.Theme = _uiTheme;
		_characterCustomization = new CharacterCustomizationPanelModule(characterCustomizationNode);
		var worldSettingsDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/WorldSettingsDialog");
		worldSettingsDialogNode.Theme = _uiTheme;
		_worldSettingsDialog = new WorldSettingsDialogModule(worldSettingsDialogNode);
		var confirmDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/ConfirmDialog");
		confirmDialogNode.Theme = _uiTheme;
		_confirmDialog = new ConfirmDialogModule(confirmDialogNode);
		var loadRecoveryDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/LoadRecoveryDialog");
		loadRecoveryDialogNode.Theme = _uiTheme;
		_loadRecoveryDialog = new LoadRecoveryDialogModule(loadRecoveryDialogNode);
		var multiplayerHubNode = GetNode<PanelContainer>($"{OverlayRootPath}/MultiplayerHub");
		multiplayerHubNode.Theme = _uiTheme;
		_multiplayerHub = new MultiplayerHubModule(multiplayerHubNode);
		var multiplayerRoomPanelNode = GetNode<PanelContainer>($"{OverlayRootPath}/MultiplayerRoomPanel");
		multiplayerRoomPanelNode.Theme = _uiTheme;
		_multiplayerRoomPanel = new MultiplayerRoomPanelModule(multiplayerRoomPanelNode);

		var skillBarNode = GetNode<PanelContainer>($"{OverlayRootPath}/SkillBar");
		skillBarNode.Theme = _uiTheme;
		_skillBar = new SkillBarModule(skillBarNode);
		_skillBar.CloseRequested += CloseSkillBarPanel;
		_skillBar.ConfirmRequested += HandleSkillConfirmRequested;
		var skillManagerNode = GetNode<PanelContainer>($"{HudRootPath}/TopRow/SkillManager");
		_skillMgr = new SkillManagerModule(skillManagerNode);
		var inventoryNode = GetNode<PanelContainer>($"{HudRootPath}/TopRow/InventoryPanel");
		_inventoryPanel = new InventoryGridPanelModule(inventoryNode, this);
		var groundNode = GetNode<PanelContainer>($"{HudRootPath}/GroundPanel");
		_groundPanel = new GroundPanelModule(groundNode, this);

		_panels = new PanelManager(_inputBindings);
		_panels.SetFloatingCheck(_panelDrag.IsFloating);
		_panelDrag.LayoutChanged += RefreshAllBorders;
		_panels.RegisterPassive(_mapPanelNode, "map", canFocus: true, consumeUnhandledKeys: false, allowGlobalClose: false);
		_panels.Register(_skillBar);
		_panels.Register(_skillMgr);
		_panels.Register(_inventoryPanel);
		_panels.Register(_groundPanel);
		_panels.RegisterPassive(_logPanelNode, "log", canFocus: false);

		RegisterAlwaysDirectDraggable(_skillBar);
		RegisterAlwaysDirectDraggable(_skillMgr);
		RegisterAlwaysDirectDraggable(_inventoryPanel);
		RegisterEditModeOnly("ground", groundNode, defaultFloating: false, groundNode.GetNode<Control>("MarginContainer/VBox/Header"));
		RegisterEditModeOnly("log", _logPanelNode, defaultFloating: false, _logContent);
		RegisterCommonPanelChrome(_skillBar, "MarginContainer/VBox/HeaderBar/Header", CloseSkillBarPanel);
		RegisterCommonPanelChrome(_skillMgr, "MarginContainer/VBox/HeaderBar/Header", CloseSkillManagerPanel);
		RegisterCommonPanelChrome(_inventoryPanel, "MarginContainer/VBox/HeaderBar/Header", CloseInventoryPanel);
		_statusPanelController = new RuntimeStatusPanelController(
			_state,
			_statusPanelModule.PanelNode,
			_uiTheme,
			TopRow,
			_panels,
			_panelLayouts,
			_panelDrag,
			_panelChrome);
		_statusPanelController.PanelsChanged += RefreshPanelLauncherState;

		_inputModule = new InputModule(_inputBar, _inputBindings);
		_panels.Register(_pauseMenuPanelModule);
		_panels.Register(_settingsPanelModule);
		_settingsFlow = new SettingsFlowCoordinator(
			new PanelManagerSettingsFlowFocusHost(_panels),
			_pauseMenuPanelModule,
			_settingsPanelModule);
		_settingsFlowModalInput = new SettingsFlowModalInputAdapter(_settingsFlow, _panels, FlushMap);
		_modalInputLayers =
		[
			_confirmDialog,
			_loadRecoveryDialog,
			_worldManager,
			_worldSettingsDialog,
			_saveNameDialog,
			_characterCreation,
			_characterCustomization,
			_multiplayerRoomPanel,
			_settingsFlowModalInput,
		];

		RegisterPanelTooltips();
	}

	/// <summary>
	/// 把 <c>_richTooltips</c> 注入所有实现 <c>ITooltipRegistrar</c> 的常驻面板/HUD。
	/// 懒加载面板（chest / trade / conversation / quest / debug / actor_inspect / limb_target）
	/// 由各自的 <c>EnsureXxxPanel</c> 在创建后单独 <c>TryRegisterPanelTooltip</c>。
	/// </summary>
	private void RegisterPanelTooltips()
	{
		var layer = _richTooltips;
		if (layer == null)
			return;

		TryRegisterPanelTooltip(_skillBar, layer);
		TryRegisterPanelTooltip(_skillMgr, layer);
		TryRegisterPanelTooltip(_inventoryPanel, layer);
		TryRegisterPanelTooltip(_groundPanel, layer);
		TryRegisterPanelTooltip(_partyHud, layer);
		TryRegisterPanelTooltip(_needsHud, layer);
		TryRegisterPanelTooltip(_statusPanelModule, layer);
	}

	private static void TryRegisterPanelTooltip(object? target, RichTooltipLayer layer)
	{
		if (target is ITooltipRegistrar registrar)
			registrar.RegisterTooltips(layer);
	}

	private void InitializeCoordinators()
	{
		_modalStateController = new ModalStateController(
			_panelChrome.CloseActiveSettings,
			HideSettingsPanels,
			CloseSettingsOverlayIfVisible,
			() => _multiplayerHub.Close(),
			() => CloseMultiplayerRoomPanel(),
			() => ExitMapEditor(silent: true),
			CancelLayoutEditMode,
			() => _mainAppFlowCoordinator.CloseConfirmDialog(),
			() => _mainAppFlowCoordinator.CloseLoadRecoveryDialog(),
			() => _mainAppFlowCoordinator.CloseWorldManager(),
			() => _mainAppFlowCoordinator.CloseWorldSettingsDialog(),
			CloseSaveNameDialog,
			() => _mainAppFlowCoordinator.CloseCharacterCreationDialog());
		_mainAppFlowCoordinator = new MainAppFlowCoordinator(
			_state,
			_session,
			_sessionBackend,
			_log,
			_menu,
			_settingsFlow,
			_worldManager,
			_worldSettingsDialog,
			_characterCreation,
			_saveNameDialog,
			_confirmDialog,
			_loadRecoveryDialog,
			_inputModule,
			_panels,
			_modalStateController,
			() => ResourcesReady,
			() => _busyOperationActive,
			() => LayoutEditActive,
			() => MapEditorActive,
			() => _state.World != null,
			ShowMainMenuWithCurrentContinue,
			RefreshMainMenuContinueState,
			ShowGameHints,
			ShowWorldCharacterEntryHint,
			ShowMapEditorHints,
			FinalizeSessionPanels,
			DoEnterGame,
			OnSessionEndedAudio,
			HideSettingsPanels,
			() => ClearArmedSkill(restoreFocus: false),
			() => EndSkillTargetCursorMode(restoreFocus: false),
			ClearPlayerTargeting,
			ResetThreatHud,
			() => _log.Clear(),
			value => PlayerDead = value,
			() => SetWatchModeEnabled(false, emitLog: false),
			() => _playerRestModeActive = false,
			ResetTimelineStatusLog,
			() =>
			{
				if (_conversationUI != null && _conversationUI.InConversation)
					_conversationUI.CloseConversation();
			},
			() =>
			{
				if (_tradeUI != null && _tradeUI.InTrade)
					_tradeUI.CloseTrade();
			},
			() => _skillBar.Close(),
			CloseDebugPanel,
			CloseAllInGamePanels,
			RefreshPlayerCharacterVisual,
			RefreshLocalizedUi,
			SyncSettingsUiState,
			() => SyncTimelineAutoAdvanceState(),

			DoSave,
			BeginLayoutEditMode,
			EnterMapEditorCore,
			silent => ExitMapEditor(silent),
			FlushMap,
			BeginBusyOperation,
			ShowBusyOperationStageAsync,
			EndBusyOperation);
		_debugPanelController = new DebugPanelController(
			_state,
			_session,
			_log,
			_panels,
			CreateDebugPanel,
			MarkUIDirty,
			FlushMap,
			SubmitPlayerActionWithResult,
			() => _session.GameStarted,
			() => _menu.InMenu,
			() => _enableDebugPanel,
			() =>
			{
				if (_mapRender == null)
					return (0, 0, 0d);
				var snapshot = _mapRender.LastPerfSnapshot;
				return (snapshot.ActiveSpriteCount, snapshot.DrawCommandCount, snapshot.FrameTimeAvgMs);
			},
			() => _mapRender?.ToggleRevealAll() ?? false,
			() =>
			{
				_runtimeWorldToolSession.RefreshBrushes();
				RefreshRuntimeWorldHoverPresentation(_runtimeWorldToolSession.HoverWorld);
				RefreshRuntimeWorldToolBar();
			});
		_runtime = BuildRuntimeComposition();
		_playerTargetingCoordinator = new PlayerTargetingCoordinator(
			_state,
			_session,
			_log,
			_menu,
			() => _mapRender,
			() => PlayerDead)
		{
			ThreatHud = _threatHud,
			TargetSummaryHud = _targetSummaryHud,
			HealthAlerts = _healthAlerts,
		};
		_multiplayerFlowCoordinator = new MultiplayerFlowCoordinator(
			AppSettingsStore.LoadMultiplayerSettings,
			AppSettingsStore.SaveMultiplayerSettings,
			ShowMainMenuWithCurrentContinue,
			_localServerLauncher,
			baseUrl => new LobbyHttpClient(baseUrl),
			() => new MultiplayerSessionBackend());
		_multiplayerHubCoordinator = new MultiplayerHubCoordinator(
			_state,
			_session,
			_multiplayerFlowCoordinator,
			() => IsMultiplayerSession,
			() => MapEditorActive,
			id => ResolveActorDisplayName(id),
			id => ResolvePlayerDisplayName(id),
			id => TryGetCurrentPlayerPrimaryActorId(id, out _),
			ResolveCurrentMultiplayerRoomDisplayName,
			ResolveCurrentRoomOwnerDisplayName);
		_multiplayerRuntimeCoordinator = new MultiplayerRuntimeCoordinator(
			_state,
			_session,
			_multiplayerFlowCoordinator,
			_log,
			events => Dispatch(events),
			MarkUIDirty,
			RefreshVisiblePanels,
			RefreshPlayerCharacterVisual,
			FlushMap,
			request => _mapRender?.PresentActorMotion(request),
			() => _mapRender?.ResetActorMotionState(),
			FinalizeSessionPanels,
			DoEnterGame,
			(status, isError) =>
			{
				_menu.ShowMultiplayerHub();
				_multiplayerHub.Open(_multiplayerHubCoordinator.BuildHubViewState(busy: false, statusMessage: status, statusIsError: isError));
			},
			CloseMultiplayerRoomPanel,
			() => _predictionConfig,
			() => _predictionCorrectionSmoothingSeconds);
		_runtimeViewCoordinator = new RuntimeViewCoordinator(_state, _runtime!.UiRefs, _runtime.Services);
		_autoNav = new AutoNavigationCoordinator(
			_state,
			_session,
			_log,
			() => _menu,
			() => PlayerDead,
			() => MapEditorActive,
			() => LayoutEditActive,
			() => RenderReady,
			() => IsMultiplayerSession,
			() => _watchModeEnabled,
			() => _skillTargetCursorActive,
			() => _skillTargetWorldCell,
			DoMove,
			SubmitPlayerAction,
			FlushMap,
			() => SyncSettingsUiState(),
			() => _multiplayerRuntimeCoordinator?.ActivityVersion ?? 0L,
			() => _multiplayerRuntimeCoordinator?.PendingPredictionCount ?? 0)
		{
			SetPlayerRestModeActive = value => _playerRestModeActive = value,
		};
		_autoNav.LoadInterruptPolicy(AppSettingsStore.LoadAutoNavigationInterruptPolicy());
		_mapEditorCoordinator = new MapEditorCoordinator(
			_state,
			_mapEditor,
			_mapEditorBar,
			() => _mapRender,
			_session,
			_log,
			FlushMap,
			DoSave,
			OpenSaveNameDialog,
			CloseSaveNameDialog,
			_modalStateController,
			_inputModule,
			_panels,
			_turnControllerPanelController,
			() => SyncSettingsUiState(),
			ShowMainMenuWithCurrentContinue,
			CloseAllInGamePanels,
			SetWorldHoverCellFromMapEditor,
			PositionWorldHoverOverlay);
		_mainInputCoordinator = new MainInputCoordinator(
			_modalInputLayers,
			() => GetViewport().SetInputAsHandled(),
			new InputHandlerSet(
				HandlePanelChromeInput: @event => _panelChrome.HandleInput(@event, enabled: true),
				HandlePanelDragInput: _panelDrag.HandleGlobalInput,
				HandleLayoutEditKeyInput: HandleLayoutEditKeyInput,
				HandleLayoutEditInput: HandleLayoutEditInput,
				HandleMapEditorKeyInput: _mapEditorCoordinator.HandleKeyInput,
				HandleMapEditorInput: _mapEditorCoordinator.HandleMouseInput,
				HandleInspectModeKeyInput: HandleRuntimeWorldToolKey,
				HandlePanelManagerKeyInput: _panels.HandleKey,
				HandleInputModuleKeyInput: _inputModule.HandleKeyInput,
				HandleGameplayMouseInput: HandleGameplayMouseInput));
		_inputModule.CommandReceived += OnCommand;

		_combatUI = new CombatUIModule(this);
		_gameEventPresentationRouter = new GameEventPresentationRouter(
			_state,
			_log,
			_combatUI,
			_groundPanel,
			_incidentAlerts,
			PlayCombatFx,
			PlayWeatherLightningFx,
			PresentActorMotion,
			HandlePlayerDeath,
			SetCurrentTarget,
			CloseConversationPanel,
			CloseTradePanel,
			EnsureTradeUI,
			npcId => EnsureConversationUI().OpenForNpc(npcId),
			() => _conversationUI?.RefreshFromState(),
			() => _playerRestModeActive = false,
			showInfoToast: text => _toastOverlay.Show(text),
			showWarningToast: text => _toastOverlay.ShowWarning(text),
			flushMap: FlushMap);
		_limbTargetCoordinator = new LimbTargetCoordinator(
			_state,
			_log,
			_panels,
			EnsureLimbTargetPanel,
			SubmitPlayerAction);
		_chestCoordinator = new ChestCoordinator(
			_state,
			_log,
			_inputModule,
			_panels,
			_groundPanel,
			() => IsMultiplayerSession,
			EnsureChestPanel,
			SubmitClientCommand,
			FlushMap);
		_gameplayCommandCoordinator = new GameplayCommandCoordinator(
			_state,
			_inputModule,
			_log,
			() => IsMultiplayerSession,
			SubmitClientCommand,
			SubmitPlayerAction,
			Dispatch,
			FlushMap,
			() => _groundPanel.Invalidate(),
			() => _groundPanel.Refresh(),
			() => _groundPanel.DoInteractSelected());
	}

	private void FinalizeReadyState()
	{
		LocalizationService.LocalizeTree(this);
		_multiplayerHub.RefreshTexts();
		_multiplayerRoomPanel.RefreshTexts();
		_multiplayerHubCoordinator.SetTemplates(BuildMultiplayerHubTemplates());
		_settingsFlow.RefreshTexts();
		SyncSettingsUiState();
		InitAudioSystem();
		ShowMainMenuWithCurrentContinue();
		RefreshStartupUi();
		BeginHeavyStartupLoad();
	}

	// ── 懒加载低频面板 ──────────────────────────────────

	private HBoxContainer TopRow => GetNode<HBoxContainer>($"{HudRootPath}/TopRow");

	private MainRuntimeComposition BuildRuntimeComposition()
	{
		var services = new RuntimeServices
		{
			State = _state,
			Session = _session,
			Log = _log,
			Input = _inputModule,
			SessionBackend = _sessionBackend,
			LocalServerLauncher = _localServerLauncher,
			FogTracker = _fogTracker,
			Menu = _menu,
			Panels = _panels,
		};

		var uiRefs = new RuntimeUiRefs
		{
			MapRender = _mapRender,
			WorldManager = _worldManager,
			SettingsPanel = _settingsPanelModule,
			MultiplayerHub = _multiplayerHub,
			MultiplayerRoomPanel = _multiplayerRoomPanel,
			StatusPanel = _statusPanelModule,
			SkillBar = _skillBar,
			SkillManager = _skillMgr,
			Inventory = _inventoryPanel,
			Ground = _groundPanel,
			TurnPanel = _turnPanelModule,
			ChestPanel = _chestPanel,
			ConversationPanel = _conversationPanel,
			TradePanel = _tradePanel,
			QuestPanel = _questPanel,
			ActorInspectPanel = _actorInspectPanel,
			LimbTargetPanel = _limbTargetPanel,
		};

		var hooks = new RuntimeHooks
		{
			Quit = () => GetTree().Quit(),
			ShowMainMenuWithCurrentContinue = ShowMainMenuWithCurrentContinue,
			SetInputHandled = () => GetViewport().SetInputAsHandled(),
		};

		return MainRuntimeComposition.Create(services, uiRefs, hooks);
	}
}
