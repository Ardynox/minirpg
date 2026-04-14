using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MiniRPG.Core.Facility;
using MiniRPG.Module.Editor;
using MiniRPG.Module.Network;
using MiniRPG.Module.WorldTool;

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
	private const string StatusPanelScenePath = "res://Scene/StatusPanel.tscn";
	private const string ActorInspectPanelScenePath = "res://Scene/ActorInspectPanel.tscn";
	private static readonly string[] LayoutEditablePanelIds =
		["status", "skill_bar", "skill_mgr", "inventory", "ground", "log", "chest", "dialog", "trade", "quest", "debug", "actor_inspect", "limb_target"];

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
	private IsometricVoxelRenderer? _mapRender;
	private MapEditorSession _mapEditor = null!;
	private MapEditorBarModule _mapEditorBar = null!;
	private MapEditorCoordinator _mapEditorCoordinator = null!;
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
	private TurnControllerPanelController _turnControllerPanelController = null!;

	private LayoutEditBarModule _layoutEditBar = null!;
	private WorldManagerModule _worldManager = null!;
	private WorldSettingsDialogModule _worldSettingsDialog = null!;
	private StatusPanelModule _statusPanelModule = null!;
	private RuntimeStatusPanelController _statusPanelController = null!;
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
	private bool _timelineStatusLogPrimed;
	private TimelineInputLockReason _lastTimelineLockReason;

	private ChestPanelModule? _chestPanel;
	private DialogPanelModule? _dialogPanel;
	private TradePanelModule? _tradePanel;
	private QuestPanelModule? _questPanel;
	private ActorInspectPanelModule? _actorInspectPanel;
	private LimbTargetPanelModule? _limbTargetPanel;

	private (int x, int y, int z)? _openChestPos;
	private OpenContainerContext? _openChestContext;
	private string? _armedSkillId;
	private RuntimeWorldToolSession _runtimeWorldToolSession = null!;
	private RuntimeWorldToolBarModule _runtimeWorldToolBar = null!;
	private RuntimeWorldToolHeightPanelModule _runtimeWorldToolHeightPanel = null!;
	private bool _runtimeWorldToolDragActive;
	private Vector3I? _runtimeWorldToolLastDraggedHoverCell;
	private bool _runtimeWorldToolHasLastPointerGlobalPosition;
	private Vector2 _runtimeWorldToolLastPointerGlobalPosition;
	private bool _skillTargetCursorActive;
	private Vector3I? _skillTargetWorldCell;
	private Vector3I? _hoverWorldCell;
	private RichTextLabel _worldHoverRtl = null!;
	private PanelContainer _worldHoverRoot = null!;
	private float _hoverDwell;
	private Vector2 _hoverLastMousePos;
	private string? _skillTargetPreviousFocusId;
	private bool _playerRestModeActive;
	private bool _enableKeyboardTargeting;
	private bool _enableDebugPanel = true;
	private float _mapZoomMin = 0.6f;
	private float _mapZoomMax = 2.4f;
	private bool _suppressMultiplayerDisconnectHandling;
	private PredictionConfig _predictionConfig = PredictionConfig.Default;
	private float _predictionCorrectionSmoothingSeconds = 0.10f;

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

	private readonly record struct OpenContainerContext(
		ContainerSourceKind Source,
		string ContainerInstanceId,
		string? OwnerActorId,
		int X,
		int Y,
		int Z);

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

			InitializeStartupAndConfig();
			InitializeCoreServices();
			BindSceneTreeNodes();
			InitializePanelsAndChrome();
			InitializeCoordinators();
			WireEventHandlers();
			FinalizeReadyState();
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
		RefreshRuntimeWorldToolBar(snapshot);
		if (snapshot.InMenu) return;

		ProcessDirtyPanels();
		_mapRender?.AdvanceAnimations(delta);
		if (MapEditorActive) _mapEditorCoordinator.Tick((float)delta);
		EmitPredictionMetricsIfDue();
		if (snapshot.PausesGameplayLoop) return;

		ProcessTimelineAutoAdvance(delta);
		ProcessPlayerRestMode();
		_turnControllerPanelController?.Process(delta);
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
}
