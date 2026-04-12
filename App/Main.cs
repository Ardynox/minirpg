using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Dialog;
using MiniRPG.Core.Facility;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Session;
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Network;
using MiniRPG.Module.Panel;
using MiniRPG.Module.Render;

namespace MiniRPG;

/// <summary>
/// 游戏入口节点（Godot 胶水层），实现 IGameUI 接口。
/// 职责：Godot 生命周期、输入路由、事件分发路由、渲染调度。
/// 菜单 UI → MenuModule，会话生命周期 → GameSessionModule，
/// 事件日志翻译 → LogModule，战斗/交易 UI → CombatUIModule/TradeUIModule。
/// </summary>
public partial class Main : Node, IGameUI, InventoryPanelModule.IHost,
	GroundPanelModule.IHost, ChestPanelModule.IHost
{
	private const int ViewW = 27;
	private const int ViewH = 15;
	private const string HudRootPath = "HudLayer/UI";
	private const string OverlayRootPath = "OverlayLayer";
	private const string HeavyTileSetPath = "res://Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres";
	private const string ChestPanelScenePath = "res://Scene/ChestPanel.tscn";
	private const string DialogPanelScenePath = "res://Scene/DialogPanel.tscn";
	private const string TradePanelScenePath = "res://Scene/TradePanel.tscn";
	private const string QuestPanelScenePath = "res://Scene/QuestPanel.tscn";
	private const string DebugPanelScenePath = "res://Scene/DebugPanel.tscn";
	private const string ActorInspectPanelScenePath = "res://Scene/ActorInspectPanel.tscn";
	private const float StartupVisualFloor = 0.06f;
	private const float StartupVisualPlateau = 0.92f;
	private const float StartupVisualProgressPerSecond = 0.18f;
	private const float StartupThreadedLoadTimeoutSeconds = 10f;
	private const float StartupSyncFallbackProgress = 0.90f;
	private static readonly string[] LayoutEditablePanelIds =
		["status", "skill_bar", "skill_mgr", "inventory", "ground", "log", "chest", "dialog", "trade", "quest", "debug", "actor_inspect", "limb_target"];
	private static readonly string[] DeferredUiScenePaths =
		[ChestPanelScenePath, DialogPanelScenePath, TradePanelScenePath, QuestPanelScenePath, DebugPanelScenePath, ActorInspectPanelScenePath];
	private static readonly StartupHeavyLoadStep[] StartupHeavyLoadSteps =
	[
		new(HeavyTileSetPath, 0.00f, 0.80f, "ui.startup.status.tileset"),
	];

	private readonly GameState _state = new();
	private MainRuntimeComposition? _runtime;

	private PanelContainer _mapPanelNode = null!;
	private LogModule _log = null!;
	private InputModule _inputModule = null!;
	private InputBindingService _inputBindings = null!;
	private LineEdit _inputBar = null!;
	private HBoxContainer _panelLauncherBar = null!;
	private Button _statusLauncherBtn = null!;
	private Button _skillBarLauncherBtn = null!;
	private Button _skillMgrLauncherBtn = null!;
	private Button _inventoryLauncherBtn = null!;
	private Button _questLauncherBtn = null!;
	private Button _debugLauncherBtn = null!;
	private Button _settingsLauncherBtn = null!;
	private TileMapRenderModule? _mapRender;
	private MapEditorSession _mapEditor = null!;
	private MapEditorBarModule _mapEditorBar = null!;
	private SaveNameDialogModule _saveNameDialog = null!;
	private CharacterCreationModule _characterCreation = null!;
	private ConfirmDialogModule _confirmDialog = null!;
	private LoadRecoveryDialogModule _loadRecoveryDialog = null!;
	private FantasyCharacterAnimatable _playerCharacterVisual = null!;
	private Control _combatFxTextRoot = null!;
	private CombatFxRegistry _combatFxRegistry = CombatFxRegistry.Empty;
	private CombatFxPlayer? _combatFxPlayer;
	private double _watchTimer;
	private bool _watchModeEnabled;
	private bool _fastTurnModeEnabled = true;
	private bool _timelineAutoAdvancePending;
	private bool _zoomHintShown;
	private ulong _lastZoomLimitLogAtMsec;

	private bool _skillBarDirty;
	private bool _layoutResetPending;

	private GameSessionModule _session = null!;
	private IGameSessionBackend _sessionBackend = null!;
	private ILocalServerLauncher _localServerLauncher = null!;
	private MenuModule _menu = null!;

	private FogOfWarTracker _fogTracker = null!;

	private PanelManager _panels = null!;
	private PanelDragService _panelDrag = null!;
	private PanelLayoutService _panelLayouts = null!;
	private PanelHoverChromeService _panelChrome = null!;
	private PauseMenuPanelModule _pauseMenuPanelModule = null!;
	private SettingsPanelModule _settingsPanelModule = null!;
	private SettingsFlowCoordinator _settingsFlow = null!;
	private SettingsFlowModalInputAdapter _settingsFlowModalInput = null!;
	private IModalInputLayer[] _modalInputLayers = [];
	private ModalStateController _modalStateController = null!;
	private MainInputCoordinator _mainInputCoordinator = null!;
	private MainAppFlowCoordinator _mainAppFlowCoordinator = null!;
	private MultiplayerFlowCoordinator _multiplayerFlowCoordinator = null!;
	private MultiplayerHubCoordinator _multiplayerHubCoordinator = null!;
	private MultiplayerRuntimeCoordinator _multiplayerRuntimeCoordinator = null!;
	private MainStartupCoordinator _startupCoordinator = null!;
	private RuntimeViewCoordinator _runtimeViewCoordinator = null!;
	private GameEventPresentationRouter _gameEventPresentationRouter = null!;
	private GameplayCommandCoordinator _gameplayCommandCoordinator = null!;
	private MultiplayerHubModule _multiplayerHub = null!;
	private MultiplayerRoomPanelModule _multiplayerRoomPanel = null!;
	private DebugPanelController _debugPanelController = null!;
	private WeatherLabPanelController _weatherLabPanelController = null!;
	private LayoutEditBarModule _layoutEditBar = null!;
	private WorldManagerModule _worldManager = null!;
	private WorldSettingsDialogModule _worldSettingsDialog = null!;
	private StatusPanelModule _statusPanelModule = null!;
	private SkillBarModule _skillBar = null!;
	private SkillManagerModule _skillMgr = null!;
	private InventoryPanelModule _inventoryPanel = null!;
	private GroundPanelModule _groundPanel = null!;
	private TurnPanelModule _turnPanelModule = null!;
	private ThreatHudModule _threatHud = null!;
	private TargetSummaryHudModule _targetSummaryHud = null!;
	private NeedsHudModule _needsHud = null!;
	private HealthAlertsModule _healthAlerts = null!;
	private PartyHudModule _partyHud = null!;
	private IncidentAlertModule _incidentAlerts = null!;
	private Control _startupOverlay = null!;
	private Label _startupStatusLabel = null!;
	private ProgressBar _startupProgressBar = null!;
	private bool _timelineStatusLogPrimed;
	private TimelineInputLockReason _lastTimelineLockReason;
	private ThreatHudMode _lastActiveThreatMode;

	private ChestPanelModule? _chestPanel;
	private DialogPanelModule? _dialogPanel;
	private TradePanelModule? _tradePanel;
	private QuestPanelModule? _questPanel;
	private ActorInspectPanelModule? _actorInspectPanel;
	private LimbTargetPanelModule? _limbTargetPanel;

	private (int x, int y, int z)? _openChestPos;
	private OpenContainerContext? _openChestContext;
	private string? _armedSkillId;
	private bool _inspectModeActive;
	private bool _skillCastCursorActive;
	private Vector3I? _inspectWorldCell;
	private Vector3I? _hoverWorldCell;
	private RichTextLabel _worldHoverRtl = null!;
	private PanelContainer _worldHoverRoot = null!;
	private float _hoverDwell;
	private Vector2 _hoverLastMousePos;
	private string? _inspectPreviousFocusId;
	private string? _inspectActorId;
	private PlayerTargetingContext _playerTargeting = PlayerTargetingContext.Empty;
	private bool _playerRestModeActive;
	private bool _enableKeyboardTargeting;
	private bool _enableDebugPanel = true;
	private float _mapZoomMin = 0.6f;
	private float _mapZoomMax = 2.4f;
	private bool _suppressMultiplayerDisconnectHandling;
	private PredictionConfig _predictionConfig = PredictionConfig.Default;
	private float _predictionCorrectionSmoothingSeconds = 0.10f;

	private bool StatusOpen => _panels?.FocusedId == "status";
	private bool InventoryOpen => _panels?.FocusedId == "inventory";
	private bool ChestOpen => _panels?.FocusedId == "chest";
	private bool MapPanelFocused => _panels?.FocusedId == "map";
	private bool LayoutEditActive => _panelDrag?.EditModeActive == true;
	private bool MapEditorActive => _mapEditor?.Active == true;

	private CombatUIModule _combatUI = null!;
	private TradeUIModule? _tradeUI;
	private DialogUIModule? _dialogUI;

	private bool IsWorldManagerOpen => _worldManager != null && _worldManager.Visible;
	private bool IsWorldSettingsDialogOpen => _worldSettingsDialog != null && _worldSettingsDialog.Visible;
	private bool IsSaveNameDialogOpen => _saveNameDialog != null && _saveNameDialog.Visible;
	private bool IsCharacterCreationOpen => _characterCreation != null && _characterCreation.Visible;
	private bool IsConfirmDialogOpen => _confirmDialog != null && _confirmDialog.Visible;
	private bool IsLoadRecoveryDialogOpen => _loadRecoveryDialog != null && _loadRecoveryDialog.Visible;
	private bool IsMultiplayerRoomPanelOpen => _multiplayerRoomPanel != null && _multiplayerRoomPanel.Visible;
	private bool ResourcesReady => _startupCoordinator != null && _startupCoordinator.ResourcesReady;
	private bool RenderReady => _mapRender != null;
	private bool IsMultiplayerSession => _session != null && _session.IsMultiplayerRoomSession;

	private bool _busyOperationActive;
	private float _busyOperationProgress;
	private string _busyOperationStatusKey = "ui.loading.new_game.prepare";

	private readonly record struct OpenContainerContext(
		ContainerSourceKind Source,
		string ContainerInstanceId,
		string? OwnerActorId,
		int X,
		int Y,
		int Z);

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
			DialogPanel = _dialogPanel,
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

	// ══════════════════════════════════════════════════════
	//  IGameUI 接口实现

	// ══════════════════════════════════════════════════════
	//  IGameUI 接口实现
	// ══════════════════════════════════════════════════════

	public GameState State => _state;
	public bool PlayerDead { get; set; }

	void IGameUI.AddLog(string msg) => _log.Add(msg);
	void IGameUI.EnterSelection(Action<int> callback) => _inputModule.EnterSelection(callback);
	void IGameUI.CancelSelection() => _inputModule.CancelSelection();
	void IGameUI.FlushMap() => FlushMap();
	void IGameUI.Dispatch(List<GameEvent> events) => Dispatch(events);
	void IGameUI.SubmitPlayerAction(TimelinePlayerAction action) => SubmitPlayerAction(action);
	bool IGameUI.TrySubmitClientCommand(ClientCommand command) => TrySubmitClientCommand(command);
	bool IGameUI.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	void InventoryPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void InventoryPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void InventoryPanelModule.IHost.SubmitPlayerAction(TimelinePlayerAction action) => SubmitPlayerAction(action);
	void InventoryPanelModule.IHost.FlushMap() => FlushMap();
	GameState InventoryPanelModule.IHost.State => _state;
	bool InventoryPanelModule.IHost.HasFocus => InventoryOpen;
	bool InventoryPanelModule.IHost.TrySubmitClientCommand(ClientCommand command) => TrySubmitClientCommand(command);
	void InventoryPanelModule.IHost.OpenChestFromInventory(Item chestItem) => OpenChestPanel(
		chestItem,
		ContainerSourceKind.Inventory,
		ActorModule.GetPlayer(_state)?.Id);
	void InventoryPanelModule.IHost.CloseInventory() => CloseInventoryPanel();
	bool InventoryPanelModule.IHost.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	void GroundPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void GroundPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void GroundPanelModule.IHost.FlushMap() => FlushMap();
	GameState GroundPanelModule.IHost.State => _state;
	void GroundPanelModule.IHost.OpenChestPanel(Item chestItem) => OpenChestPanel(chestItem, ContainerSourceKind.Ground);
	void GroundPanelModule.IHost.OpenCorpseHarvest(Item corpseItem) => OpenCorpseHarvest(corpseItem);
	void GroundPanelModule.IHost.StripCorpse(Item corpseItem) => SubmitCorpseOperation("strip_corpse", corpseItem);
	void GroundPanelModule.IHost.ButcherCorpse(Item corpseItem) => SubmitCorpseOperation("butcher_corpse", corpseItem);
	void GroundPanelModule.IHost.PickupGroundItem(Item item) => PickupGroundItem(ActorModule.GetPlayer(_state)!, item);
	bool GroundPanelModule.IHost.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	void ChestPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void ChestPanelModule.IHost.FlushMap() => FlushMap();
	void ChestPanelModule.IHost.TakeChestItem(Item chestItem, int itemIndex) => TakeChestItem(chestItem, itemIndex);
	void ChestPanelModule.IHost.TakeAllChestItems(Item chestItem) => TakeAllChestItems(chestItem);
	GameState ChestPanelModule.IHost.State => _state;
	void ChestPanelModule.IHost.CloseChestPanel() => CloseChestPanel();
	void ChestPanelModule.IHost.OpenPutIntoChestSelection(Item chestItem) => OpenPutIntoChestSelection(chestItem);
	void ChestPanelModule.IHost.PersistChestItem(Item chestItem) => PersistOpenChestState(chestItem);
	bool ChestPanelModule.IHost.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	// ══════════════════════════════════════════════════════
	//  Godot 生命周期
	// ══════════════════════════════════════════════════════

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	public override void _Ready()
	{
		try
		{
			ParseAutoTestCliOptions();
			if (_autoTestCliExitRequested)
				return;

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
			DialogPool.Load();
			ResAccess.Load();
			_combatFxRegistry = CombatFxRegistry.Load();
			PlayerAppearanceCatalog.LoadProjectCatalog();
			_fogTracker = new FogOfWarTracker(GameConfig.PlayerVision);

			_session = new GameSessionModule(_state, _fogTracker);
			_sessionBackend = new LocalSessionBackend(_session, _state, Dispatch);
			_localServerLauncher = new LocalProcessServerLauncher(ProjectSettings.GlobalizePath("res://"));
			_menu = new MenuModule(this);

			_mapPanelNode = GetNode<PanelContainer>($"{HudRootPath}/TopRow/MapPanel");
			var logPanelNode = GetNode<PanelContainer>($"{HudRootPath}/LogPanel");
			var logContent = logPanelNode.GetNode<RichTextLabel>("MarginContainer/VBox/ContentText");
			_log = new LogModule(logContent);
			_inputBar = GetNode<LineEdit>($"{HudRootPath}/InputBar");
			var lineEdit = _inputBar;
			BindWorldHoverOverlay();
			BindAltLabelOverlay();
			BindPanelLauncherBar();
			_mapEditor = new MapEditorSession(_state);

			_combatFxTextRoot = CreateMapOverlayRoot(_mapPanelNode);

			var uiTheme = GetNode<Control>(HudRootPath).Theme;
			var floatingRoot = new Control { Name = "FloatingPanels", MouseFilter = Control.MouseFilterEnum.Ignore };
			floatingRoot.Theme = uiTheme;
			var overlayLayer = GetNode<CanvasLayer>(OverlayRootPath);
			overlayLayer.AddChild(floatingRoot);
			var threatHudNode = ThreatHudModule.CreateControl(uiTheme);
			overlayLayer.AddChild(threatHudNode);
			_threatHud = new ThreatHudModule(threatHudNode);
			var targetSummaryHudNode = TargetSummaryHudModule.CreateControl(uiTheme);
			overlayLayer.AddChild(targetSummaryHudNode);
			_targetSummaryHud = new TargetSummaryHudModule(targetSummaryHudNode);
			var needsHudNode = NeedsHudModule.CreateControl(uiTheme);
			overlayLayer.AddChild(needsHudNode);
			_needsHud = new NeedsHudModule(needsHudNode);
			var healthAlertsNode = HealthAlertsModule.CreateControl(uiTheme);
			overlayLayer.AddChild(healthAlertsNode);
			_healthAlerts = new HealthAlertsModule(healthAlertsNode);
			var partyHudNode = PartyHudModule.CreateControl(uiTheme);
			overlayLayer.AddChild(partyHudNode);
			_partyHud = new PartyHudModule(partyHudNode);
			var incidentAlertNode = IncidentAlertModule.CreateControl(uiTheme);
			overlayLayer.AddChild(incidentAlertNode);
			_incidentAlerts = new IncidentAlertModule(incidentAlertNode);
			_lastActiveThreatMode = ThreatHudMode.Hidden;

			var layoutStore = new PanelLayoutStore();
			var buttonScaleService = new PanelButtonScaleService();
			PanelButtonScaleRegistry.Bind(buttonScaleService);
			_panelLayouts = new PanelLayoutService(layoutStore, buttonScaleService);
			_panelLayouts.Initialize();
			_panelDrag = new PanelDragService(layoutStore, floatingRoot);
			_panelChrome = new PanelHoverChromeService(floatingRoot, _panelLayouts, _panelDrag);
			_inputBindings = new InputBindingService(ProjectSettings.GlobalizePath("user://keybindings.json"));
			_inputBindings.Changed += () =>
			{
				ApplyPanelLauncherTooltips();
				RefreshPanelLauncherState();
			};

			_statusPanelModule = new StatusPanelModule(GetNode<PanelContainer>($"{HudRootPath}/TopRow/StatusPanel"));
			_turnPanelModule = new TurnPanelModule(GetNode<PanelContainer>($"{HudRootPath}/TurnPanel"));
			var pauseMenuNode = GetNode<PanelContainer>($"{OverlayRootPath}/PauseMenuPanel");
			pauseMenuNode.Theme = uiTheme;
			_pauseMenuPanelModule = new PauseMenuPanelModule(pauseMenuNode);
			var settingsPanelNode = GetNode<PanelContainer>($"{OverlayRootPath}/SettingsPanel");
			settingsPanelNode.Theme = uiTheme;
			_settingsPanelModule = new SettingsPanelModule(settingsPanelNode, _inputBindings);
			var layoutEditBarNode = GetNode<PanelContainer>($"{OverlayRootPath}/LayoutEditBar");
			layoutEditBarNode.Theme = uiTheme;
			_layoutEditBar = new LayoutEditBarModule(layoutEditBarNode);
			var worldManagerNode = GetNode<PanelContainer>($"{OverlayRootPath}/WorldManager");
			worldManagerNode.Theme = uiTheme;
			_worldManager = new WorldManagerModule(worldManagerNode);
			var mapEditorBarNode = GetNode<PanelContainer>($"{OverlayRootPath}/MapEditorBar");
			mapEditorBarNode.Theme = uiTheme;
			_mapEditorBar = new MapEditorBarModule(mapEditorBarNode);
			InitializeWeatherLabPanel(uiTheme);
			var saveNameDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/SaveNameDialog");
			saveNameDialogNode.Theme = uiTheme;
			_saveNameDialog = new SaveNameDialogModule(saveNameDialogNode);
			var characterCreationNode = GetNode<PanelContainer>($"{OverlayRootPath}/CharacterCreationDialog");
			characterCreationNode.Theme = uiTheme;
			_characterCreation = new CharacterCreationModule(characterCreationNode);
			var worldSettingsDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/WorldSettingsDialog");
			worldSettingsDialogNode.Theme = uiTheme;
			_worldSettingsDialog = new WorldSettingsDialogModule(worldSettingsDialogNode);
			var confirmDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/ConfirmDialog");
			confirmDialogNode.Theme = uiTheme;
			_confirmDialog = new ConfirmDialogModule(confirmDialogNode);
			var loadRecoveryDialogNode = GetNode<PanelContainer>($"{OverlayRootPath}/LoadRecoveryDialog");
			loadRecoveryDialogNode.Theme = uiTheme;
			_loadRecoveryDialog = new LoadRecoveryDialogModule(loadRecoveryDialogNode);
			var multiplayerHubNode = GetNode<PanelContainer>($"{OverlayRootPath}/MultiplayerHub");
			multiplayerHubNode.Theme = uiTheme;
			_multiplayerHub = new MultiplayerHubModule(multiplayerHubNode);
			var multiplayerRoomPanelNode = GetNode<PanelContainer>($"{OverlayRootPath}/MultiplayerRoomPanel");
			multiplayerRoomPanelNode.Theme = uiTheme;
			_multiplayerRoomPanel = new MultiplayerRoomPanelModule(multiplayerRoomPanelNode);

			var skillBarNode = GetNode<PanelContainer>($"{OverlayRootPath}/SkillBar");
			skillBarNode.Theme = uiTheme;
			_skillBar = new SkillBarModule(skillBarNode);
			_skillBar.CloseRequested += CloseSkillBarPanel;
			_skillBar.ConfirmRequested += HandleSkillConfirmRequested;
			var skillManagerNode = GetNode<PanelContainer>($"{HudRootPath}/TopRow/SkillManager");
			_skillMgr = new SkillManagerModule(skillManagerNode);
			var inventoryNode = GetNode<PanelContainer>($"{HudRootPath}/TopRow/InventoryPanel");
			_inventoryPanel = new InventoryPanelModule(inventoryNode, this);
			var groundNode = GetNode<PanelContainer>($"{HudRootPath}/GroundPanel");
			_groundPanel = new GroundPanelModule(groundNode, this);

			_panels = new PanelManager();
			_panels.SetFloatingCheck(_panelDrag.IsFloating);
			_panelDrag.LayoutChanged += RefreshAllBorders;
			_panels.RegisterPassive(_mapPanelNode, "map", canFocus: true, consumeUnhandledKeys: false, allowGlobalClose: false);
			_panels.Register(_statusPanelModule);
			_panels.Register(_skillBar);
			_panels.Register(_skillMgr);
			_panels.Register(_inventoryPanel);
			_panels.Register(_groundPanel);
			_panels.RegisterPassive(logPanelNode, "log", canFocus: false);

			RegisterAlwaysDirectDraggable(_statusPanelModule);
			RegisterAlwaysDirectDraggable(_skillBar);
			RegisterAlwaysDirectDraggable(_skillMgr);
			RegisterAlwaysDirectDraggable(_inventoryPanel);
			RegisterEditModeOnly("ground", groundNode, defaultFloating: false, groundNode.GetNode<Control>("MarginContainer/VBox/Header"));
			RegisterEditModeOnly("log", logPanelNode, defaultFloating: false, logContent);
			RegisterCommonPanelChrome(_statusPanelModule, "MarginContainer/VBox/HeaderBar/NameInfo", CloseStatusPanel);
			RegisterCommonPanelChrome(_skillBar, "MarginContainer/VBox/HeaderBar/Header", CloseSkillBarPanel);
			RegisterCommonPanelChrome(_skillMgr, "MarginContainer/VBox/HeaderBar/Header", CloseSkillManagerPanel);
			RegisterCommonPanelChrome(_inventoryPanel, "MarginContainer/VBox/HeaderBar/Header", CloseInventoryPanel);

			_inputModule = new InputModule(lineEdit, _inputBindings);
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
				_multiplayerRoomPanel,
				_settingsFlowModalInput,
			];
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
				HideSettingsPanels,
				() => ClearArmedSkill(restoreFocus: false),
				() => EndInspectMode(restoreFocus: false),
				ClearPlayerTargeting,
				ResetThreatHud,
				() => _log.Clear(),
				value => PlayerDead = value,
				() => SetWatchModeEnabled(false, emitLog: false),
				() => _playerRestModeActive = false,
				ResetTimelineStatusLog,
				() =>
				{
					if (_dialogUI != null && _dialogUI.InDialog)
						_dialogUI.CloseDialog();
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
				RefreshWeatherLabSessionStateForCoordinator,
				CloseWeatherLabPanelForCoordinator,
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
				() => _mapRender?.ToggleRevealAll() ?? false);
			_runtime = BuildRuntimeComposition();
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
			_mainInputCoordinator = new MainInputCoordinator(
				_modalInputLayers,
				() => GetViewport().SetInputAsHandled(),
				@event => _panelChrome.HandleInput(@event, enabled: true),
				_panelDrag.HandleGlobalInput,
				HandleLayoutEditKeyInput,
				HandleLayoutEditInput,
				HandleMapEditorKeyInput,
				HandleMapEditorMouseInput,
				HandleInspectModeKey,
				_panels.HandleKey,
				_inputModule.HandleKeyInput,
				HandleGameplayMouseInput);
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
				HandlePlayerDeath,
				SetCurrentTarget,
				CloseDialogPanel,
				CloseTradePanel,
				EnsureTradeUI,
				EnsureDialogUI,
				() => _playerRestModeActive = false);
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
			_settingsFlow.RenderToggleRequested += ToggleRender;
			_settingsFlow.WatchModeToggleRequested += ToggleWatchMode;
			_settingsFlow.FastTurnModeToggleRequested += ToggleFastTurnMode;
			_settingsFlow.KeyboardTargetingToggleRequested += ToggleKeyboardTargeting;
			_settingsFlow.DebugPanelToggleRequested += ToggleDebugPanelSetting;
			_settingsFlow.MapZoomMinDecreaseRequested += DecreaseMapZoomMin;
			_settingsFlow.MapZoomMinIncreaseRequested += IncreaseMapZoomMin;
			_settingsFlow.MapZoomMaxDecreaseRequested += DecreaseMapZoomMax;
			_settingsFlow.MapZoomMaxIncreaseRequested += IncreaseMapZoomMax;
			_settingsFlow.MapEditorToggleRequested += () =>
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.map_editor", "Map editor is disabled in multiplayer sessions."));
					return;
				}
				_mainAppFlowCoordinator.ToggleMapEditor();
			};
			_settingsFlow.WeatherLabToggleRequested += _weatherLabPanelController.Toggle;
			_settingsFlow.LayoutEditRequested += _mainAppFlowCoordinator.OpenLayoutEditMode;
			_settingsFlow.SaveRequested += () =>
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.save", "Saving local files is disabled in multiplayer sessions."));
					return;
				}
				DoSaveCurrent();
			};
			_settingsFlow.LoadRequested += () =>
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.load", "Loading local saves is disabled in multiplayer sessions."));
					return;
				}
				_mainAppFlowCoordinator.OpenWorldManager(WorldManagerContext.InGame, WorldLaunchTab.Worlds);
			};
			_settingsFlow.LanguageChangedRequested += HandleLanguageChanged;
			_settingsFlow.QuickSaveRequested += () =>
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.save", "Saving local files is disabled in multiplayer sessions."));
					return;
				}
				var path = _session.GetQuickSavePath();
				DoSave(path, _session.DescribeSavePath(path));
			};
			_settingsFlow.QuickLoadRequested += () =>
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.load", "Loading local saves is disabled in multiplayer sessions."));
					return;
				}
				_mainAppFlowCoordinator.HandleQuickLoadRequested();
			};
			_settingsFlow.ReturnToMenuRequested += () =>
			{
				if (IsMultiplayerSession)
				{
					HandleMultiplayerReturnToMenu();
					return;
				}

				_mainAppFlowCoordinator.HandleBackToMenu();
			};
			_settingsFlow.MultiplayerRoomRequested += HandleMultiplayerRoomRequested;
			_settingsFlow.MainMenuRestoreRequested += ShowMainMenuWithCurrentContinue;
			_layoutEditBar.ApplyRequested += ApplyLayoutEditMode;
			_layoutEditBar.CancelRequested += CancelLayoutEditMode;
			_layoutEditBar.ResetRequested += ResetLayoutEditMode;
			_worldManager.CloseRequested += _mainAppFlowCoordinator.CloseWorldManager;
			_worldManager.CreateWorldRequested += _mainAppFlowCoordinator.OpenWorldSettingsDialog;
			_worldManager.DeleteSaveDataRequested += _mainAppFlowCoordinator.HandleWorldManagerDeleteSaveDataRequested;
			_worldManager.CleanAssetsRequested += _mainAppFlowCoordinator.HandleWorldManagerCleanAssetsRequested;
			_worldManager.CreateCharacterRequested += _mainAppFlowCoordinator.HandleWorldManagerCreateCharacterRequested;
			_worldManager.ContinueCharacterRequested += _mainAppFlowCoordinator.HandleWorldManagerContinueCharacterRequested;
			_worldManager.ScenarioRequested += _mainAppFlowCoordinator.HandleWorldManagerScenarioRequested;
			_worldManager.LegacySaveRequested += slot =>
			{
				if (IsMultiplayerSession)
				{
					_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.load", "Loading local saves is disabled in multiplayer sessions."));
					return;
				}
				_mainAppFlowCoordinator.HandleWorldManagerLegacySaveRequested(slot);
			};
			_mapEditorBar.CategorySelected += category =>
			{
				_mapEditor.SelectCategory(category);
				RefreshMapEditorBar();
				FlushMap();
			};
			_mapEditorBar.BrushSelected += index =>
			{
				_mapEditor.SelectBrush(index);
				RefreshMapEditorBar();
				FlushMap();
			};
			_mapEditorBar.SaveRequested += HandleMapEditorSaveRequested;
			_mapEditorBar.ExitRequested += () => ExitMapEditor();
			_mapEditorBar.CenterRequested += () =>
			{
				_mapEditor.CenterOnPlayer();
				FlushMap();
			};
			_saveNameDialog.ConfirmRequested += HandleSaveNameConfirmed;
			_saveNameDialog.CancelRequested += CloseSaveNameDialog;
			_characterCreation.ConfirmRequested += _mainAppFlowCoordinator.HandleCharacterCreationConfirmed;
			_characterCreation.CancelRequested += _mainAppFlowCoordinator.HandleCharacterCreationCanceled;
			_worldSettingsDialog.ConfirmRequested += _mainAppFlowCoordinator.HandleWorldSettingsConfirmed;
			_worldSettingsDialog.CancelRequested += _mainAppFlowCoordinator.HandleWorldSettingsCanceled;
			_confirmDialog.ActionSelected += _mainAppFlowCoordinator.HandleConfirmDialogActionSelected;
			_confirmDialog.CancelRequested += _mainAppFlowCoordinator.CloseConfirmDialog;
			_loadRecoveryDialog.RecoveryConfirmed += _mainAppFlowCoordinator.HandleLoadRecoveryConfirmed;
			_loadRecoveryDialog.CancelRequested += _mainAppFlowCoordinator.CloseLoadRecoveryDialog;

			_menu.OnContinue += _mainAppFlowCoordinator.HandleMenuContinue;
			_menu.OnWorlds += _mainAppFlowCoordinator.HandleMenuWorlds;
			_menu.OnMapEditor += HandleMenuMapEditor;
			_menu.OnMultiplayer += HandleMenuMultiplayer;
			_menu.OnWeatherLab += HandleMenuWeatherLab;
			_menu.OnAutoTest += HandleAutoTest;
			_menu.OnQuit += () => GetTree().Quit();
			_menu.OnOpenSettings += _mainAppFlowCoordinator.OpenMenuSettingsPanel;
			_multiplayerHub.BackRequested += HandleMultiplayerHubBackRequested;
			_multiplayerHub.SaveSettingsRequested += HandleMultiplayerHubSaveSettingsRequested;
			_multiplayerHub.RefreshRoomsRequested += HandleMultiplayerHubRefreshRequested;
			_multiplayerHub.JoinRoomRequested += HandleMultiplayerHubJoinRoomRequested;
			_multiplayerHub.JoinByCodeRequested += HandleMultiplayerHubJoinByCodeRequested;
			_multiplayerHub.CreateRoomRequested += HandleMultiplayerHubCreateRequested;
			_multiplayerHub.ReconnectRequested += HandleMultiplayerHubReconnectRequested;
			_multiplayerRoomPanel.CloseRequested += CloseMultiplayerRoomPanel;
			_multiplayerRoomPanel.HostCurrentSessionRequested += HandleMultiplayerRoomHostCurrentSessionRequested;
			_multiplayerRoomPanel.AssignPrimaryActorRequested += HandleMultiplayerRoomAssignPrimaryActorRequested;
			_multiplayerRoomPanel.KickPlayerRequested += HandleMultiplayerRoomKickPlayerRequested;
			_multiplayerRoomPanel.ReclaimPrimaryActorRequested += HandleMultiplayerRoomReclaimPrimaryActorRequested;

			LocalizationService.LocalizeTree(this);
			_multiplayerHub.RefreshTexts();
			_multiplayerRoomPanel.RefreshTexts();
			_multiplayerHubCoordinator.SetTemplates(BuildMultiplayerHubTemplates());
			_settingsFlow.RefreshTexts();
			SyncSettingsUiState();
			ShowMainMenuWithCurrentContinue();
			RefreshStartupUi();
			BeginHeavyStartupLoad();
		}
		catch (Exception ex)
		{
			FailStartupBootstrap("startup-bootstrap", ex);
		}
	}

	public override void _ExitTree()
	{
		CloseMultiplayerBackendAsync(suppressDisconnectHandling: true).GetAwaiter().GetResult();
		_localServerLauncher?.DisposeAsync().AsTask().GetAwaiter().GetResult();
	}

	/// <summary>每帧更新：驱动异步资源加载 + 脏面板统一刷新 + 看海模式自动推进。</summary>
	public override void _Process(double delta)
	{
		if (ShouldSkipRuntimeCallbacks())
			return;

		_multiplayerRuntimeCoordinator?.Poll();
		var snapshot = CaptureRuntimeUiMode();
		ResAccess.PollAsyncLoads();
		PollHeavyStartupLoad();
		PollPostStartupTasks();
		TryStartAutoTestCli();
		_session.ProcessWorldStreaming();
		_panelChrome.Update(GetViewport().GetMousePosition(), enabled: snapshot.AllowPanelChrome);
		UpdateThreatHud(delta, snapshot);
		UpdateTargetSummaryHud(snapshot);
		_needsHud.Update(ActorModule.GetPlayer(_state), _state.Turn, !snapshot.SuppressHudAndAlerts);
		_healthAlerts.Update(_state, ActorModule.GetPlayer(_state), _state.Turn, !snapshot.SuppressHudAndAlerts);
		_partyHud.Update(_state, !snapshot.SuppressHudAndAlerts);
		_incidentAlerts.Update((float)delta, !snapshot.SuppressHudAndAlerts);
		TickWorldHoverOverlay((float)delta);
		TickAltLabelOverlay(snapshot);
		if (snapshot.InMenu) return;

		ProcessDirtyPanels();
		_mapRender?.AdvanceAnimations(delta);
		EmitPredictionMetricsIfDue();
		if (snapshot.PausesGameplayLoop) return;

		ProcessTimelineAutoAdvance(delta);
		ProcessPlayerRestMode();
	}

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

	/// <summary>拦截未处理的键盘事件：优先让 PanelManager 处理（面板聚焦时），否则走 InputModule。</summary>
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
		var resource = ResourceLoader.LoadThreadedGet(path);
		switch (path)
		{
			case HeavyTileSetPath:
				if (resource is TileSet tileSet)
				{
					_loadedTileSetForFinalization = tileSet;
					return true;
				}
				break;
		}

		_startupCoordinator.FailHeavyStartupLoad(path, "unexpected resource type");
		return false;
	}

	private TileSet? _loadedTileSetForFinalization;

	private void FinalizeHeavyStartupLoad()
	{
		if (_startupCoordinator == null || _startupCoordinator.State != StartupState.Finalizing)
			return;

		if (_loadedTileSetForFinalization == null)
		{
			_startupCoordinator.FailHeavyStartupLoad("startup", "missing heavy resources during finalization");
			return;
		}

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

			_mapRender = new TileMapRenderModule(_state, _fogTracker, ViewW, ViewH);
			_mapRender.Init(mapRoot, _loadedTileSetForFinalization, viewportContainer, subViewport, playerCharacter, camera);
			_mapRender.SetZoomRange(_mapZoomMin, _mapZoomMax);
			_mapRender.SetWeatherScreenFxTuning(_weatherLabPanelController?.CurrentTuningSet ?? new WeatherScreenFxTuningSet());
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

	public override void _UnhandledInput(InputEvent @event)
	{
		if (ShouldSkipRuntimeCallbacks())
			return;

		var snapshot = CaptureRuntimeUiMode();
		if (@event is not InputEventKey key)
		{
			if (snapshot.BusyOperationActive)
				GetViewport().SetInputAsHandled();
			return;
		}

		_mainInputCoordinator.HandleUnhandledKey(key, snapshot);
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	public override void _Input(InputEvent @event)
	{
		if (ShouldSkipRuntimeCallbacks())
			return;

		var snapshot = CaptureRuntimeUiMode();
		if (_mainInputCoordinator.HandleInput(@event, snapshot))
			GetViewport().SetInputAsHandled();
	}

	// ══════════════════════════════════════════════════════
	//  菜单事件处理（MenuModule 回调）

	private bool HandleLayoutEditKeyInput(InputEventKey key)
	{
		if (key.Pressed && key.Keycode == Key.Escape)
			CancelLayoutEditMode();

		return false;
	}

	private static bool HandleLayoutEditInput(InputEvent @event) =>
		@event is InputEventMouseButton editMouse
		&& editMouse.Pressed
		&& editMouse.ButtonIndex == MouseButton.Right;

	private bool HandleGameplayMouseInput(InputEvent @event, RuntimeUiModeSnapshot snapshot)
	{
		if (HandleWorldHoverInput(@event, snapshot))
			return true;

		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return false;
		if (!snapshot.AllowGameplayInput)
			return false;

		if (HandleGameplayMouseWheelInput(mb, snapshot))
			return true;

		if (snapshot.AllowPanelChrome && _panelChrome.IsPointerOverInteractiveChrome(mb.GlobalPosition))
			return false;

		if (mb.ButtonIndex == MouseButton.Right)
		{
			if (GetArmedSkill() != null)
			{
				if (TryCastArmedSkillAtMouse(mb.GlobalPosition))
					return true;
				return false;
			}

			if (TryOpenActorInspectPanelAtMouse(mb.GlobalPosition))
				return true;

			if (_panels.CloseFocused())
			{
				FlushMap();
				return true;
			}

			return false;
		}

		if (mb.ButtonIndex != MouseButton.Left)
			return false;

		var hit = _panels.HitTest(mb.GlobalPosition);
		if (hit == null)
			return false;

		var clearFocusStack = hit.PanelId == "map";
		if (hit == _panels.Focused && !clearFocusStack)
			return false;

		if (hit.PanelId == "inventory" && !_inventoryPanel.Visible)
		{
			_inventoryPanel.Visible = true;
			FlushMap();
		}

		_panels.FocusFromPointer(hit, clearFocusStack);
		return false;
	}

	private bool HandleGameplayMouseWheelInput(InputEventMouseButton mb, RuntimeUiModeSnapshot snapshot)
	{
		if (!snapshot.AllowGameplayInput
			|| mb.ButtonIndex is not MouseButton.WheelUp and not MouseButton.WheelDown)
		{
			return false;
		}

		if (MapPanelFocused)
		{
			if (mb.AltPressed && !mb.CtrlPressed && !mb.ShiftPressed)
			{
				OnCommand(mb.ButtonIndex == MouseButton.WheelUp ? ":skill_prev" : ":skill_next");
				return true;
			}

			if (_mapRender != null)
			{
				if (!_zoomHintShown)
				{
					_log.Add(LocalizationService.T("ui.zoom.hint"));
					_zoomHintShown = true;
				}

				var zoomed = _mapRender.StepZoom(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
				if (zoomed)
				{
					FlushMap();
				}
				else
				{
					var now = Time.GetTicksMsec();
					if (now - _lastZoomLimitLogAtMsec > 1000)
					{
						_log.Add(LocalizationService.T("ui.zoom.limit_reached"));
						_lastZoomLimitLogAtMsec = now;
					}
				}
				return true;
			}
		}

		return _inputModule.HandleMouseButtonInput(mb);
	}

	private void HandleMenuContinue() => _mainAppFlowCoordinator.HandleMenuContinue();

	private void HandleMenuWorlds() => _mainAppFlowCoordinator.HandleMenuWorlds();

	private void HandleCharacterCreationConfirmed(PlayerCreationOptions options) =>
		_mainAppFlowCoordinator.HandleCharacterCreationConfirmed(options);

	private void HandleMenuMapEditor()
	{
		if (IsMultiplayerSession)
		{
			_log.Add(LocalizationService.TOrFallback("ui.multiplayer.disabled.map_editor", "Map editor is disabled in multiplayer sessions."));
			return;
		}
		_mainAppFlowCoordinator.HandleMenuMapEditor();
	}

	
	private void HandleAutoTest()
	{
		if (!ResourcesReady || _mapRender == null)
			return;

		var test = new AutoTestModule();
		test.RunAll(this);
	}

	/// <summary>供 AutoTestModule 转发命令到 OnCommand。</summary>
	public void RunTestCommand(string cmd) => OnCommand(cmd);

	private void HandleBackToMenu() => _mainAppFlowCoordinator.HandleBackToMenu();

	private void DoStartNewGame(PlayerCreationOptions? options = null) => _mainAppFlowCoordinator.DoStartNewGame(options);

	private void DoStartBlankEditor() => _mainAppFlowCoordinator.DoStartBlankEditor();

	private void PrepareSessionTransition(bool clearLogs) => _mainAppFlowCoordinator.PrepareSessionTransition(clearLogs);

	private void FinalizeSessionPanels(bool openSkillBar)
	{
		ClearArmedSkill(restoreFocus: false);
		EndInspectMode(restoreFocus: false);
		ClearPlayerTargeting();
		ResetThreatHud();
		RefreshPlayerCharacterVisual();
		SyncSettingsUiState();
		SyncTimelineAutoAdvanceState();
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
		if (_actorInspectPanel != null) _actorInspectPanel.Close();
		_panels.ClearFocus();
	}

	private void DoEnterGame()
	{
		if (!ResourcesReady)
			return;

		_multiplayerHub.Close();
		_menu.EnterGame();
		_inputModule.EnterActionMode();
		_weatherLabPanelController.RefreshSessionState(autoOpen: true);
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

	private void UpdateThreatHud(double delta, RuntimeUiModeSnapshot uiMode)
	{
		if (_threatHud == null)
			return;

		var snapshot = _session.GameStarted
			? ThreatHudModule.BuildSnapshot(_state)
			: ThreatHudSnapshot.Hidden;
		if (_lastActiveThreatMode != ThreatHudMode.Hidden
			&& snapshot.Mode == ThreatHudMode.Hidden
			&& !PlayerDead
			&& _session.GameStarted
			&& !_menu.InMenu)
		{
			_log.Add(LocalizationService.T("log.awareness.disengaged"));
		}

		_lastActiveThreatMode = snapshot.Mode;
		_threatHud.Update(snapshot, delta, uiMode.SuppressHudAndAlerts);
	}

	private void UpdateTargetSummaryHud(RuntimeUiModeSnapshot uiMode)
	{
		if (_targetSummaryHud == null)
			return;

		var snapshot = _session.GameStarted
			? BuildTargetSummarySnapshot()
			: TargetSummarySnapshot.Hidden;
		_targetSummaryHud.Update(snapshot, uiMode.SuppressHudAndAlerts);
	}

	private TargetSummarySnapshot BuildTargetSummarySnapshot()
	{
		var resolution = PlayerTargetingModule.Resolve(_state, _playerTargeting);
		_playerTargeting = resolution.Context;

		var selection = PlayerTargetingModule.ResolveSummaryTarget(_state, _playerTargeting, IsHudTargetVisible);
		return TargetSummaryHudModule.BuildSnapshot(_state, selection.Target, selection.MarkCurrentTarget);
	}

	private bool IsHudTargetVisible(Actor actor) =>
		_mapRender != null && _mapRender.IsWorldCellVisible(actor.X, actor.Y, actor.Z);

	private void ClearPlayerTargeting()
	{
		_playerTargeting = PlayerTargetingContext.Empty;
	}

	private void SetCurrentTarget(Actor target, PlayerTargetSource source)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null || !PlayerTargetingModule.IsTargetValid(player, target))
			return;

		_playerTargeting = PlayerTargetingModule.SetTarget(target, source);
	}

	private PlayerTargetResolution ResolveCurrentTarget()
	{
		var resolution = PlayerTargetingModule.Resolve(_state, _playerTargeting);
		_playerTargeting = resolution.Context;
		return resolution;
	}

	private bool TrySubmitSkillOnCurrentTarget(Actor player, InteractionDef skill)
	{
		if (!PlayerTargetingModule.TryResolveDirectSkillCast(
				_state,
				player,
				_playerTargeting,
				skill,
				out var action,
				out var normalizedContext))
		{
			return false;
		}

		_playerTargeting = normalizedContext;
		SubmitPlayerAction(action!);
		return true;
	}

	private Vector3I ResolveSkillCursorOriginCell(Actor player)
	{
		var origin = PlayerTargetingModule.ResolveCursorOrigin(_state, player, _playerTargeting);
		return new Vector3I(origin.X, origin.Y, origin.Z);
	}

	private void ResetThreatHud()
	{
		if (_threatHud == null)
			return;

		_lastActiveThreatMode = ThreatHudMode.Hidden;
		_threatHud.HideImmediate();
		_targetSummaryHud.HideImmediate();
		_healthAlerts.HideImmediate();
	}

	private void RefreshVisiblePanels()
	{
		_runtimeViewCoordinator.RefreshVisiblePanels(
			_debugPanelController,
			RefreshActorInspectPanel,
			() => RefreshWorldManagerContents(),
			RefreshMultiplayerRoomPanelState);

		if (_tradeUI?.InTrade == true)
			_tradeUI.Refresh();
		if (_dialogUI?.InDialog == true)
			_dialogUI.RefreshCurrentEntry();
	}

	private void OpenCharacterCreationDialog(string worldId, string worldName) =>
		_mainAppFlowCoordinator.OpenCharacterCreationDialog(worldId, worldName);

	private void CloseCharacterCreationDialog() => _mainAppFlowCoordinator.CloseCharacterCreationDialog();

	private void HandleCharacterCreationCanceled() => _mainAppFlowCoordinator.HandleCharacterCreationCanceled();

	private void OpenWorldSettingsDialog() => _mainAppFlowCoordinator.OpenWorldSettingsDialog();

	private void CloseWorldSettingsDialog() => _mainAppFlowCoordinator.CloseWorldSettingsDialog();

	private void HandleWorldSettingsCanceled() => _mainAppFlowCoordinator.HandleWorldSettingsCanceled();

	private void OpenMenuSettingsPanel() => _mainAppFlowCoordinator.OpenMenuSettingsPanel();

	private void ToggleSettingsPanel() => _mainAppFlowCoordinator.ToggleSettingsPanel();

	private void HideSettingsPanels()
	{
		_panelChrome.CloseActiveSettings();
		while (_settingsFlow.HasVisibleOverlay)
			_settingsFlow.CloseActiveOverlay();
	}

	private void CloseSettingsOverlayIfVisible()
	{
		if (_settingsFlow.SettingsVisible)
			_settingsFlow.CloseActiveOverlay();
	}

	private void OpenLayoutEditMode() => _mainAppFlowCoordinator.OpenLayoutEditMode();

	private void EnterMapEditor(MapEditorEntryMode entryMode) => _mainAppFlowCoordinator.EnterMapEditor(entryMode);


	// ══════════════════════════════════════════════════════
	//  移动
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 处理方向键移动：玩家已死时按方向键返回主菜单，
	/// 否则执行 TryMove → Tick → Dispatch → FlushMap。
	/// </summary>
	private void DoMove(int dx, int dy) => _gameplayCommandCoordinator.DoMove(dx, dy, TrySubmitPredictedMove);

	private bool TrySubmitPredictedMove(int dx, int dy) =>
		_multiplayerRuntimeCoordinator != null
		&& _multiplayerRuntimeCoordinator.TrySubmitPredictedMove(dx, dy, IsMultiplayerSession);

	private void EmitPredictionMetricsIfDue() => _multiplayerRuntimeCoordinator?.EmitPredictionMetricsIfDue();

	private static PredictionConfig LoadPredictionConfigFromEnvironment()
	{
		var threshold = ParseIntEnvironment("MINIRPG_PREDICTION_ROLLBACK_THRESHOLD", PredictionConfig.Default.RollbackThresholdManhattan, 0, 8);
		var maxPending = ParseIntEnvironment("MINIRPG_PREDICTION_MAX_PENDING", PredictionConfig.Default.MaxPendingCommands, 8, 256);
		return new PredictionConfig(threshold, maxPending);
	}

	private static float LoadPredictionSmoothingSecondsFromEnvironment()
	{
		var raw = System.Environment.GetEnvironmentVariable("MINIRPG_PREDICTION_SMOOTHING_SECONDS");
		if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
			return 0.10f;
		return Math.Clamp(parsed, 0.02f, 0.35f);
	}

	private static int ParseIntEnvironment(string name, int fallback, int min, int max)
	{
		var raw = System.Environment.GetEnvironmentVariable(name);
		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
			return fallback;
		return Math.Clamp(parsed, min, max);
	}

	// ══════════════════════════════════════════════════════
	//  事件分发（薄路由层：日志翻译 + 流程触发）

	/// <summary>

	// ══════════════════════════════════════════════════════
	//  事件分发（薄路由层：日志翻译 + 流程触发）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// Core 层事件到 UI 层副作用的唯一入口。
	/// 日志翻译委托给 LogModule.DispatchEvent，
	/// 流程触发路由到对应的 UI Module。
	/// </summary>
	private void Dispatch(List<GameEvent> events)
	{
		_gameEventPresentationRouter.Dispatch(events);
	}

	// ══════════════════════════════════════════════════════
	//  存档 / 读档
	// ══════════════════════════════════════════════════════

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void DoSave(string path, string label)
	{
		_session.SaveGame(path);
		_log.Add(LocalizationService.T("log.save.saved", ("label", label)));
	}

	private void HandleConfirmDialogActionSelected(string actionId)
		=> _mainAppFlowCoordinator.HandleConfirmDialogActionSelected(actionId);

	private void CloseConfirmDialog() => _mainAppFlowCoordinator.CloseConfirmDialog();

	// ══════════════════════════════════════════════════════
	//  渲染
	// ══════════════════════════════════════════════════════

	private void DoSaveCurrent()
	{
		var path = _session.GetPreferredSavePath();
		DoSave(path, _session.DescribeSavePath(path));
	}

	private void RefreshWeatherLabSessionStateForCoordinator(bool autoOpen) =>
		_weatherLabPanelController.RefreshSessionState(autoOpen);

	private void CloseWeatherLabPanelForCoordinator(bool resetRuntime) =>
		_weatherLabPanelController.Close(resetRuntime);

	private void HandleQuickLoadRequested() => _mainAppFlowCoordinator.HandleQuickLoadRequested();

	private void OpenWorldManager(
		WorldManagerContext context,
		WorldLaunchTab initialTab,
		string? selectedWorldId = null,
		string? selectedCharacterId = null)
		=> _mainAppFlowCoordinator.OpenWorldManager(context, initialTab, selectedWorldId, selectedCharacterId);

	private void RefreshWorldManagerContents(string? selectedWorldId = null, string? selectedCharacterId = null)
		=> _mainAppFlowCoordinator.RefreshWorldManagerContents(selectedWorldId, selectedCharacterId);

	private void CloseWorldManager() => _mainAppFlowCoordinator.CloseWorldManager();

	private void HandleWorldManagerCreateCharacterRequested(string worldId)
		=> _mainAppFlowCoordinator.HandleWorldManagerCreateCharacterRequested(worldId);

	private void HandleWorldManagerDeleteSaveDataRequested(string worldId)
		=> _mainAppFlowCoordinator.HandleWorldManagerDeleteSaveDataRequested(worldId);

	private void HandleWorldManagerCleanAssetsRequested(string worldId)
		=> _mainAppFlowCoordinator.HandleWorldManagerCleanAssetsRequested(worldId);

	private void HandleWorldManagerContinueCharacterRequested(string worldId, string characterId)
		=> _mainAppFlowCoordinator.HandleWorldManagerContinueCharacterRequested(worldId, characterId);

	private void HandleWorldManagerScenarioRequested(string scenarioId)
		=> _mainAppFlowCoordinator.HandleWorldManagerScenarioRequested(scenarioId);

	private void HandleWorldManagerLegacySaveRequested(SaveSlotInfo slot) =>
		_mainAppFlowCoordinator.HandleWorldManagerLegacySaveRequested(slot);

	private void RequestWorldManagerLoad(string targetLabel, Func<PreparedSessionLoad> loadAction)
	{
		_mainAppFlowCoordinator.LoadFromWorldManager(targetLabel, loadAction);
	}

	private async void LoadFromWorldManager(string label, Func<PreparedSessionLoad> loadAction)
	{
		_mainAppFlowCoordinator.LoadFromWorldManager(label, loadAction);
		await Task.CompletedTask;
	}

	private void HandleWorldSettingsConfirmed(string worldName, WorldSettings settings)
		=> _mainAppFlowCoordinator.HandleWorldSettingsConfirmed(worldName, settings);

}
