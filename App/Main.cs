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
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Editor;
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
	private Godot.Collections.Array? _startupThreadProgress;
	private readonly Queue<Action> _postStartupTasks = new();

	private PanelContainer _mapPanelNode = null!;
	private LogModule _log = null!;
	private InputModule _inputModule = null!;
	private InputBindingService _inputBindings = null!;
	private LineEdit _inputBar = null!;
	private TileMapRenderModule? _mapRender;
	private MapEditorSession _mapEditor = null!;
	private MapEditorBarModule _mapEditorBar = null!;
	private SaveNameDialogModule _saveNameDialog = null!;
	private CharacterCreationModule _characterCreation = null!;
	private ConfirmDialogModule _confirmDialog = null!;
	private FantasyCharacterAnimatable _playerCharacterVisual = null!;
	private Control _combatFxTextRoot = null!;
	private CombatFxRegistry _combatFxRegistry = CombatFxRegistry.Empty;
	private CombatFxPlayer? _combatFxPlayer;
	private double _watchTimer;
	private bool _watchModeEnabled;
	private bool _timelineAutoAdvancePending;
	private bool _zoomHintShown;
	private ulong _lastZoomLimitLogAtMsec;

	private bool _skillBarDirty;
	private bool _layoutResetPending;

	private GameSessionModule _session = null!;
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
	private string? _armedSkillId;
	private bool _inspectModeActive;
	private bool _skillCastCursorActive;
	private Vector3I? _inspectWorldCell;
	private string? _inspectPreviousFocusId;
	private string? _inspectActorId;
	private PlayerTargetingContext _playerTargeting = PlayerTargetingContext.Empty;
	private bool _playerRestModeActive;
	private bool _enableKeyboardTargeting;
	private bool _enableDebugPanel = true;

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
	private bool ResourcesReady => _startupState == StartupState.Ready;
	private bool RenderReady => _mapRender != null;

	private StartupState _startupState = StartupState.LoadingHeavyAssets;
	private int _startupLoadIndex = -1;
	private string? _startupLoadPath;
	private string? _startupLastLoadPath;
	private float _startupProgress;
	private string _startupStatusKey = "ui.startup.status.tileset";
	private TileSet? _loadedTileSet;
	private bool _postStartupTasksQueued;
	private int _postStartupTaskDelayFrames;
	private ulong _startupLoadStartedAtMsec;
	private bool _startupSyncFallbackUsed;
	private bool _busyOperationActive;
	private float _busyOperationProgress;
	private string _busyOperationStatusKey = "ui.loading.new_game.prepare";

	private enum StartupState
	{
		LoadingHeavyAssets,
		CompletingHeavyAssetsSynchronously,
		Finalizing,
		Ready,
		Failed,
	}

	private readonly record struct StartupHeavyLoadStep(
		string Path,
		float ProgressStart,
		float ProgressEnd,
		string StatusKey);


	// ── 懒加载低频面板 ──────────────────────────────────

	private HBoxContainer TopRow => GetNode<HBoxContainer>($"{HudRootPath}/TopRow");

	private ChestPanelModule EnsureChestPanel()
	{
		if (_chestPanel != null) return _chestPanel;
		var scene = LoadPackedSceneCached(ChestPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_chestPanel = new ChestPanelModule(node, this);
		_panels.Register(_chestPanel);
		RegisterAlwaysDirectDraggable(_chestPanel);
		RegisterCommonPanelChrome(_chestPanel, "MarginContainer/VBox/HeaderBar/Header", CloseChestPanel);
		return _chestPanel;
	}

	private DialogPanelModule EnsureDialogPanel()
	{
		if (_dialogPanel != null) return _dialogPanel;
		var scene = LoadPackedSceneCached(DialogPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_dialogPanel = new DialogPanelModule(node);
		_panels.Register(_dialogPanel);
		RegisterAlwaysDirectDraggable(_dialogPanel);
		RegisterCommonPanelChrome(_dialogPanel, "MarginContainer/VBox/HeaderBar/Header", CloseDialogPanel);
		return _dialogPanel;
	}

	private TradePanelModule EnsureTradePanel()
	{
		if (_tradePanel != null) return _tradePanel;
		var scene = LoadPackedSceneCached(TradePanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_tradePanel = new TradePanelModule(node);
		_panels.Register(_tradePanel);
		RegisterAlwaysDirectDraggable(_tradePanel);
		RegisterCommonPanelChrome(_tradePanel, "MarginContainer/VBox/HeaderBar/Header", CloseTradePanel);
		return _tradePanel;
	}

	private QuestPanelModule EnsureQuestPanel()
	{
		if (_questPanel != null) return _questPanel;
		var scene = LoadPackedSceneCached(QuestPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_questPanel = new QuestPanelModule(node);
		_panels.Register(_questPanel);
		RegisterAlwaysDirectDraggable(_questPanel);
		RegisterCommonPanelChrome(_questPanel, "MarginContainer/VBox/HeaderBar/Header", CloseQuestPanel);
		return _questPanel;
	}

	private DebugPanelModule CreateDebugPanel(DebugPanelModule.IHost host, Action closeAction)
	{
		var scene = LoadPackedSceneCached(DebugPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		var debugPanel = new DebugPanelModule(node, host);
		_panels.Register(debugPanel);
		RegisterAlwaysDirectDraggable(debugPanel);
		RegisterCommonPanelChrome(debugPanel, "MarginContainer/VBox/HeaderBar/Header", closeAction);
		return debugPanel;
	}

	private ActorInspectPanelModule EnsureActorInspectPanel()
	{
		if (_actorInspectPanel != null) return _actorInspectPanel;
		var scene = LoadPackedSceneCached(ActorInspectPanelScenePath);
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		LocalizationService.LocalizeTree(node);
		_actorInspectPanel = new ActorInspectPanelModule(node);
		_actorInspectPanel.CloseRequested += CloseActorInspectPanel;
		_panels.Register(_actorInspectPanel);
		RegisterAlwaysDirectDraggable(_actorInspectPanel);
		RegisterCommonPanelChrome(_actorInspectPanel, "MarginContainer/VBox/HeaderBar/NameInfo", CloseActorInspectPanel);
		return _actorInspectPanel;
	}

	private LimbTargetPanelModule EnsureLimbTargetPanel()
	{
		if (_limbTargetPanel != null)
			return _limbTargetPanel;

		var node = LimbTargetPanelModule.CreateControl(GetNode<Control>(HudRootPath).Theme);
		TopRow.AddChild(node);
		_limbTargetPanel = new LimbTargetPanelModule(node);
		_limbTargetPanel.CloseRequested += CloseLimbTargetPanel;
		_limbTargetPanel.TargetConfirmed += HandleLimbTargetConfirmed;
		_panels.Register(_limbTargetPanel);
		RegisterAlwaysDirectDraggable(_limbTargetPanel);
		RegisterCommonPanelChrome(_limbTargetPanel, "MarginContainer/VBox/HeaderBar/Header", CloseLimbTargetPanel);
		return _limbTargetPanel;
	}

	private TradeUIModule EnsureTradeUI()
	{
		if (_tradeUI != null) return _tradeUI;
		_tradeUI = new TradeUIModule(this, EnsureTradePanel(), _panels);
		return _tradeUI;
	}

	private DialogUIModule EnsureDialogUI()
	{
		if (_dialogUI != null) return _dialogUI;
		_dialogUI = new DialogUIModule(this, EnsureDialogPanel(), _panels);
		return _dialogUI;
	}

	private static PackedScene LoadPackedSceneCached(string path)
	{
		var scene = ResAccess.Get<PackedScene>(path);
		if (scene != null)
			return scene;

		throw new InvalidOperationException($"Failed to load PackedScene: {path}");
	}

	private Godot.Collections.Array StartupThreadProgress
		=> _startupThreadProgress ??= new Godot.Collections.Array();

	private void RegisterAlwaysDirectDraggable(IPanel panel)
	{
		_panelLayouts.RegisterPanel(panel.PanelId, panel.PanelNode);
		_panelDrag.Register(new DraggablePanelRegistration(
			panel.PanelId,
			panel.PanelNode,
			PanelDragAvailability.Always,
			[],
			DefaultFloating: true
		));
	}

	private void RegisterEditModeOnly(string panelId, PanelContainer panelNode, bool defaultFloating, params Control[] dragHandles)
	{
		_panelLayouts.RegisterPanel(panelId, panelNode);
		_panelDrag.Register(new DraggablePanelRegistration(
			panelId,
			panelNode,
			PanelDragAvailability.EditModeOnly,
			dragHandles,
			defaultFloating
		));
	}

	private void RegisterCommonPanelChrome(IPanel panel, string titleDragPath, Action closeAction)
	{
		var dragHandle = panel.PanelNode.GetNodeOrNull<Control>(titleDragPath);
		if (dragHandle == null)
		{
			GD.PushWarning($"[Main] Missing chrome drag handle '{titleDragPath}' for panel '{panel.PanelId}'.");
			return;
		}

		_panelChrome.Register(new PanelHoverChromeRegistration(
			panel.PanelId,
			panel.PanelNode,
			[dragHandle],
			closeAction));
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
	bool IGameUI.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	void InventoryPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void InventoryPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void InventoryPanelModule.IHost.SubmitPlayerAction(TimelinePlayerAction action) => SubmitPlayerAction(action);
	void InventoryPanelModule.IHost.FlushMap() => FlushMap();
	GameState InventoryPanelModule.IHost.State => _state;
	bool InventoryPanelModule.IHost.HasFocus => InventoryOpen;
	void InventoryPanelModule.IHost.OpenChestFromInventory(Item chestItem) => OpenChestPanel(chestItem);
	void InventoryPanelModule.IHost.CloseInventory() => CloseInventoryPanel();
	bool InventoryPanelModule.IHost.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	void GroundPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void GroundPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void GroundPanelModule.IHost.FlushMap() => FlushMap();
	GameState GroundPanelModule.IHost.State => _state;
	void GroundPanelModule.IHost.OpenChestPanel(Item chestItem) => OpenChestPanel(chestItem);
	void GroundPanelModule.IHost.OpenCorpseHarvest(Item corpseItem) => OpenCorpseHarvest(corpseItem);
	void GroundPanelModule.IHost.StripCorpse(Item corpseItem) => SubmitCorpseOperation("strip_corpse", corpseItem);
	void GroundPanelModule.IHost.ButcherCorpse(Item corpseItem) => SubmitCorpseOperation("butcher_corpse", corpseItem);
	void GroundPanelModule.IHost.PickupGroundItem(Item item) => PickupGroundItem(ActorModule.GetPlayer(_state)!, item);
	bool GroundPanelModule.IHost.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	void ChestPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void ChestPanelModule.IHost.FlushMap() => FlushMap();
	GameState ChestPanelModule.IHost.State => _state;
	void ChestPanelModule.IHost.CloseChestPanel() => CloseChestPanel();
	void ChestPanelModule.IHost.OpenPutIntoChestSelection(Item chestItem) => OpenPutIntoChestSelection(chestItem);
	void ChestPanelModule.IHost.PersistChestItem(Item chestItem) => PersistOpenChestState(chestItem);
	bool ChestPanelModule.IHost.TryHandleItemRightClick(Item item) => TryHandleIdentifyItemTarget(item);

	private void ProcessTimelineAutoAdvance(double delta)
	{
		if (PlayerDead || ActorModule.GetPlayer(_state) == null || (!_watchModeEnabled && !_timelineAutoAdvancePending))
			return;

		_watchTimer += delta;
		if (_watchTimer < 0.2)
			return;

		_watchTimer = 0;
		WatchModeTick();
	}

	private void ProcessPlayerRestMode()
	{
		if (!_playerRestModeActive || PlayerDead || _watchModeEnabled)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			_playerRestModeActive = false;
			return;
		}

		ActorDerivedStateUpdater.SyncPlayerUiState(_state);
		var restValue = NeedSystem.GetNeedValueSnapshot(player, NeedIds.Rest);
		if (restValue >= 85f)
		{
			_playerRestModeActive = false;
			return;
		}

		if (NeedBehaviorModule.HasNearbyThreat(_state, player))
		{
			_playerRestModeActive = false;
			var interrupted = new List<GameEvent>();
			NeedSystem.ApplyThought(player, "sleep_interrupted", _state.Turn, NeedThoughtSources.Sleep, interrupted, _state);
			Dispatch(interrupted);
			return;
		}

		if (TimelineTurnManager.IsPlayerTurn(_state))
			SubmitPlayerAction(TimelinePlayerAction.Rest());
	}

	private void TogglePlayerRestMode()
	{
		if (_playerRestModeActive)
		{
			_playerRestModeActive = false;
			return;
		}

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;
		if (!NeedActionModule.HasBedroll(player))
		{
			_log.Add(LocalizationService.T("log.rest.needs_bedroll"));
			return;
		}
		if (NeedBehaviorModule.HasNearbyThreat(_state, player))
		{
			_log.Add(LocalizationService.T("log.rest.unsafe"));
			return;
		}

		_playerRestModeActive = true;
		Dispatch(
		[
			new GameEvent("rest_started")
			{
				InitiatorId = player.Id,
				TargetId = player.Id,
				TargetActorName = IdentificationModule.GetActorDisplayName(_state, player),
			},
		]);
	}

	private bool IsTimelineInputLocked() =>
		!PlayerDead && (_timelineAutoAdvancePending || _watchModeEnabled);

	private void SubmitPlayerAction(TimelinePlayerAction action)
	{
		_ = SubmitPlayerActionWithResult(action);
	}

	private TimelineStepResult SubmitPlayerActionWithResult(TimelinePlayerAction action)
	{
		var result = TimelineTurnGateway.SubmitPlayerAction(_state, action);
		ApplyTimelineStep(result);
		return result;
	}

	private void AdvanceTimelineAutoStep()
	{
		var result = TimelineTurnGateway.AdvanceAuto(_state, _watchModeEnabled);
		ApplyTimelineStep(result);
	}

	private void ApplyTimelineStep(TimelineStepResult result)
	{
		if (result.Events.Count > 0)
			Dispatch(result.Events);

		FinalizeTimelineStepUi();
		SyncTimelineAutoAdvanceState(emitStatusLog: true);
	}

	private void SyncTimelineAutoAdvanceState(bool emitStatusLog = false)
	{
		var snapshot = TimelineTurnManager.CreateDebugSnapshot(_state, PlayerDead, _watchModeEnabled);
		_timelineAutoAdvancePending = snapshot.HasPendingAutoAdvance;
		if (!_timelineAutoAdvancePending)
			_watchTimer = 0;

		_turnPanelModule.Dirty = true;
		if (emitStatusLog)
			UpdateTimelineStatusLog(snapshot);
		else
			PrimeTimelineStatusLog(snapshot);
	}

	private void FinalizeTimelineStepUi()
	{
		var center = new WorldCoord(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_state.World?.Chunks.UpdateLoadedChunks(center, _state.Turn);
		FlushMap();
		CheckChestRange();
	}

	/// <summary>
	/// </summary>
	public void HandlePlayerDeath(string reason)
	{
		if (PlayerDead) return;
		PlayerDead = true;
		CancelLayoutEditMode();

		SetWatchModeEnabled(false, emitLog: false);

		_inputModule.CancelSelection();
		ClearArmedSkill(restoreFocus: false);
		EndInspectMode(restoreFocus: false);
		ClearPlayerTargeting();
		_playerRestModeActive = false;
		ResetThreatHud();
		_timelineAutoAdvancePending = false;
		_watchTimer = 0;
		ResetTimelineStatusLog();

		_log.Add("");
		_log.Add(reason == "incapacitated"
			? LocalizationService.T("death.incapacitated")
			: LocalizationService.T("death.killed"));
		_log.Add(LocalizationService.T("death.turn", ("turn", _state.Turn)));
		_log.Add(LocalizationService.T("death.floor", ("floor", _state.PlayerZ)));
		_log.Add(LocalizationService.T("death.kills", ("kills", _state.KillCount)));
		var player2 = ActorModule.GetPlayer(_state);
		if (player2 != null)
			_log.Add(LocalizationService.T("death.gold", ("gold", player2.Gold)));
		_log.Add(LocalizationService.T("death.separator"));
		_log.Add(LocalizationService.T("death.back_to_menu"));
	}

	// ══════════════════════════════════════════════════════
	//  Godot 生命周期
	// ══════════════════════════════════════════════════════

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	public override void _Ready()
	{
		try
		{
			BindStartupOverlayNodes();
			GameConfig.Load();
			PresetDB.Load();
			LocalizationService.Initialize();
			LocalizationService.SetLocale(AppSettingsStore.LoadLocale(), notify: false);
			_enableKeyboardTargeting = AppSettingsStore.LoadEnableKeyboardTargeting();
			_enableDebugPanel = AppSettingsStore.LoadEnableDebugPanel();
			TerrainRegistry.Load("terrains.json");
			GameLocalizer.CaptureBaseSnapshots();
			GameLocalizer.ApplyPresetTranslations();
			DialogPool.Load();
			ResAccess.Load();
			_combatFxRegistry = CombatFxRegistry.Load();
			PlayerAppearanceCatalog.LoadProjectCatalog();
			_fogTracker = new FogOfWarTracker(GameConfig.PlayerVision);

			_session = new GameSessionModule(_state, _fogTracker);
			_menu = new MenuModule(this);

			_mapPanelNode = GetNode<PanelContainer>($"{HudRootPath}/TopRow/MapPanel");
			var logPanelNode = GetNode<PanelContainer>($"{HudRootPath}/LogPanel");
			var logContent = logPanelNode.GetNode<RichTextLabel>("MarginContainer/VBox/ContentText");
			_log = new LogModule(logContent);
			_inputBar = GetNode<LineEdit>($"{HudRootPath}/InputBar");
			var lineEdit = _inputBar;
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
				_worldManager,
				_worldSettingsDialog,
				_saveNameDialog,
				_characterCreation,
				_settingsFlowModalInput,
			];
			_modalStateController = new ModalStateController(
				_panelChrome.CloseActiveSettings,
				HideSettingsPanels,
				CloseSettingsOverlayIfVisible,
				() => ExitMapEditor(silent: true),
				CancelLayoutEditMode,
				() => _mainAppFlowCoordinator.CloseConfirmDialog(),
				() => _mainAppFlowCoordinator.CloseWorldManager(),
				() => _mainAppFlowCoordinator.CloseWorldSettingsDialog(),
				CloseSaveNameDialog,
				() => _mainAppFlowCoordinator.CloseCharacterCreationDialog());
			_mainAppFlowCoordinator = new MainAppFlowCoordinator(
				_state,
				_session,
				_log,
				_menu,
				_settingsFlow,
				_worldManager,
				_worldSettingsDialog,
				_characterCreation,
				_saveNameDialog,
				_confirmDialog,
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
				RefreshPlayerCharacterVisual,
				RefreshLocalizedUi,
				SyncSettingsUiState,
				() => SyncTimelineAutoAdvanceState(),
				_weatherLabPanelController.RefreshSessionState,
				_weatherLabPanelController.Close,
				DoSave,
				DoLoad,
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
				});
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
			_settingsFlow.RenderToggleRequested += ToggleRender;
			_settingsFlow.WatchModeToggleRequested += ToggleWatchMode;
			_settingsFlow.KeyboardTargetingToggleRequested += ToggleKeyboardTargeting;
			_settingsFlow.DebugPanelToggleRequested += ToggleDebugPanelSetting;
			_settingsFlow.MapEditorToggleRequested += _mainAppFlowCoordinator.ToggleMapEditor;
			_settingsFlow.WeatherLabToggleRequested += _weatherLabPanelController.Toggle;
			_settingsFlow.LayoutEditRequested += _mainAppFlowCoordinator.OpenLayoutEditMode;
			_settingsFlow.SaveRequested += DoSaveCurrent;
			_settingsFlow.LoadRequested += () => _mainAppFlowCoordinator.OpenWorldManager(WorldManagerContext.InGame, WorldLaunchTab.Worlds);
			_settingsFlow.LanguageChangedRequested += HandleLanguageChanged;
			_settingsFlow.QuickSaveRequested += () =>
			{
				var path = _session.GetQuickSavePath();
				DoSave(path, _session.DescribeSavePath(path));
			};
			_settingsFlow.QuickLoadRequested += _mainAppFlowCoordinator.HandleQuickLoadRequested;
			_settingsFlow.ReturnToMenuRequested += _mainAppFlowCoordinator.HandleBackToMenu;
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
			_worldManager.LegacySaveRequested += _mainAppFlowCoordinator.HandleWorldManagerLegacySaveRequested;
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

			_menu.OnContinue += _mainAppFlowCoordinator.HandleMenuContinue;
			_menu.OnWorlds += _mainAppFlowCoordinator.HandleMenuWorlds;
			_menu.OnMapEditor += _mainAppFlowCoordinator.HandleMenuMapEditor;
			_menu.OnWeatherLab += HandleMenuWeatherLab;
			_menu.OnAutoTest += HandleAutoTest;
			_menu.OnQuit += () => GetTree().Quit();
			_menu.OnOpenSettings += _mainAppFlowCoordinator.OpenMenuSettingsPanel;

			LocalizationService.LocalizeTree(this);
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

	/// <summary>每帧更新：驱动异步资源加载 + 脏面板统一刷新 + 看海模式自动推进。</summary>
	public override void _Process(double delta)
	{
		if (ShouldSkipRuntimeCallbacks())
			return;

		var snapshot = CaptureRuntimeUiMode();
		ResAccess.PollAsyncLoads();
		PollHeavyStartupLoad();
		PollPostStartupTasks();
		_session.ProcessWorldStreaming();
		_panelChrome.Update(GetViewport().GetMousePosition(), enabled: snapshot.AllowPanelChrome);
		UpdateThreatHud(delta, snapshot);
		UpdateTargetSummaryHud(snapshot);
		_needsHud.Update(ActorModule.GetPlayer(_state), _state.Turn, !snapshot.InMenu);
		_healthAlerts.Update(_state, ActorModule.GetPlayer(_state), _state.Turn, !snapshot.SuppressHudAndAlerts);
		_partyHud.Update(_state);
		_incidentAlerts.Update((float)delta);
		if (snapshot.InMenu) return;

		ProcessDirtyPanels();
		_mapRender?.AdvanceAnimations(delta);
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
			|| _confirmDialog == null)
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
		var worldManagerOpen = IsWorldManagerOpen;
		var worldSettingsDialogOpen = IsWorldSettingsDialogOpen;
		var saveNameDialogOpen = IsSaveNameDialogOpen;
		var characterCreationOpen = IsCharacterCreationOpen;
		var settingsOverlayVisible = _settingsFlow.HasVisibleOverlay;
		var hasVisibleModalLayer = confirmDialogOpen
			|| worldManagerOpen
			|| worldSettingsDialogOpen
			|| saveNameDialogOpen
			|| characterCreationOpen
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
			PausesGameplayLoop: busyOperationActive || confirmDialogOpen || worldManagerOpen || worldSettingsDialogOpen || saveNameDialogOpen || mapEditorActive || layoutEditActive);
	}

	/// <summary>拦截未处理的键盘事件：优先让 PanelManager 处理（面板聚焦时），否则走 InputModule。</summary>
	private void BeginHeavyStartupLoad()
	{
		_startupState = StartupState.LoadingHeavyAssets;
		_startupLoadIndex = -1;
		_startupLoadPath = null;
		_startupLastLoadPath = null;
		_startupLoadStartedAtMsec = 0;
		_startupSyncFallbackUsed = false;
		_startupProgress = 0f;
		_loadedTileSet = null;
		StartNextHeavyStartupLoad();
	}

	private void StartNextHeavyStartupLoad()
	{
		_startupLoadIndex++;
		if (_startupLoadIndex >= StartupHeavyLoadSteps.Length)
		{
			BeginHeavyStartupFinalization();
			return;
		}

		var step = StartupHeavyLoadSteps[_startupLoadIndex];
		_startupLoadPath = step.Path;
		_startupLastLoadPath = step.Path;
		_startupStatusKey = step.StatusKey;
		_startupLoadStartedAtMsec = Time.GetTicksMsec();
		_startupProgress = Mathf.Lerp(step.ProgressStart, step.ProgressEnd, StartupVisualFloor);
		StartupThreadProgress.Clear();

		var err = ResourceLoader.LoadThreadedRequest(
			step.Path,
			string.Empty,
			useSubThreads: true,
			ResourceLoader.CacheMode.Reuse);
		if (err != Error.Ok)
		{
			FailHeavyStartupLoad(step.Path, err.ToString());
			return;
		}

		RefreshStartupUi();
	}

	private void BeginHeavyStartupFinalization()
	{
		_startupState = StartupState.Finalizing;
		_startupLoadPath = null;
		_startupLoadStartedAtMsec = 0;
		_startupProgress = 0.95f;
		_startupStatusKey = "ui.startup.status.finalizing";
		RefreshStartupUi();
	}

	private void PollHeavyStartupLoad()
	{
		switch (_startupState)
		{
			case StartupState.LoadingHeavyAssets:
				PollCurrentHeavyStartupLoad();
				break;
			case StartupState.CompletingHeavyAssetsSynchronously:
				CompleteCurrentHeavyStartupLoadSynchronously();
				break;
			case StartupState.Finalizing:
				FinalizeHeavyStartupLoad();
				break;
		}
	}

	private void PollCurrentHeavyStartupLoad()
	{
		if (string.IsNullOrEmpty(_startupLoadPath)
			|| _startupLoadIndex < 0
			|| _startupLoadIndex >= StartupHeavyLoadSteps.Length)
			return;

		var step = StartupHeavyLoadSteps[_startupLoadIndex];
		StartupThreadProgress.Clear();
		var status = ResourceLoader.LoadThreadedGetStatus(_startupLoadPath, StartupThreadProgress);
		UpdateHeavyStartupProgress(step);

		switch (status)
		{
			case ResourceLoader.ThreadLoadStatus.InProgress:
				if (ShouldSwitchToSynchronousHeavyLoad())
					BeginSynchronousHeavyStartupCompletion(step);
				return;
			case ResourceLoader.ThreadLoadStatus.Loaded:
				_startupLoadStartedAtMsec = 0;
				_startupProgress = step.ProgressEnd;
				if (!StoreLoadedHeavyResource(_startupLoadPath))
					return;

				_startupLoadPath = null;
				StartNextHeavyStartupLoad();
				return;
			case ResourceLoader.ThreadLoadStatus.Failed:
			case ResourceLoader.ThreadLoadStatus.InvalidResource:
				FailHeavyStartupLoad(_startupLoadPath, $"status={status}");
				return;
			default:
				return;
		}
	}

	private bool ShouldSwitchToSynchronousHeavyLoad()
	{
		if (_startupLoadStartedAtMsec == 0)
			return false;

		var elapsedSeconds = (float)(Time.GetTicksMsec() - _startupLoadStartedAtMsec) / 1000f;
		return elapsedSeconds >= StartupThreadedLoadTimeoutSeconds;
	}

	private void BeginSynchronousHeavyStartupCompletion(StartupHeavyLoadStep step)
	{
		if (string.IsNullOrEmpty(_startupLoadPath))
			return;

		_startupState = StartupState.CompletingHeavyAssetsSynchronously;
		_startupSyncFallbackUsed = true;
		_startupLoadStartedAtMsec = 0;
		_startupProgress = Math.Max(step.ProgressEnd, StartupSyncFallbackProgress);
		GD.Print($"[Startup] Threaded heavy load exceeded {StartupThreadedLoadTimeoutSeconds:F1}s, switching to blocking completion: {_startupLoadPath}");
		RefreshStartupUi();
	}

	private void CompleteCurrentHeavyStartupLoadSynchronously()
	{
		if (string.IsNullOrEmpty(_startupLoadPath)
			|| _startupLoadIndex < 0
			|| _startupLoadIndex >= StartupHeavyLoadSteps.Length)
			return;

		var path = _startupLoadPath;
		var step = StartupHeavyLoadSteps[_startupLoadIndex];

		try
		{
			var resource = ResourceLoader.LoadThreadedGet(path);
			_startupProgress = step.ProgressEnd;
			if (!StoreLoadedHeavyResource(path, resource))
				return;

			_startupState = StartupState.LoadingHeavyAssets;
			_startupLoadPath = null;
			StartNextHeavyStartupLoad();
		}
		catch (Exception ex)
		{
			FailHeavyStartupLoad(path, $"sync fallback failed: {ex.Message}");
		}
	}

	private void UpdateHeavyStartupProgress(StartupHeavyLoadStep step)
	{
		var progress = ReadStartupThreadProgress();

		if (_startupLoadStartedAtMsec > 0)
		{
			var elapsedSeconds = (float)(Time.GetTicksMsec() - _startupLoadStartedAtMsec) / 1000f;
			var fallbackProgress = Math.Min(
				StartupVisualPlateau,
				StartupVisualFloor + elapsedSeconds * StartupVisualProgressPerSecond);
			progress = Math.Max(progress, fallbackProgress);
		}

		_startupProgress = Mathf.Lerp(step.ProgressStart, step.ProgressEnd, progress);
		RefreshStartupUi();
	}

	private float ReadStartupThreadProgress()
	{
		if (_startupThreadProgress == null || _startupThreadProgress.Count == 0)
			return 0f;

		return ClampProgressValue(_startupThreadProgress[0]);
	}

	private static float ClampProgressValue(object? rawProgress)
	{
		if (rawProgress == null)
			return 0f;

		switch (rawProgress)
		{
			case float single:
				return Math.Clamp(single, 0f, 1f);
			case double doubleValue:
				return Math.Clamp((float)doubleValue, 0f, 1f);
			case int intValue:
				return Math.Clamp((float)intValue, 0f, 1f);
			case long longValue:
				return Math.Clamp((float)longValue, 0f, 1f);
			case decimal decimalValue:
				return Math.Clamp((float)decimalValue, 0f, 1f);
			case string text when TryParseProgressText(text, out var parsedText):
				return parsedText;
		}

		var rawType = rawProgress.GetType();
		if (string.Equals(rawType.FullName, "Godot.Variant", StringComparison.Ordinal))
		{
			var asSingleMethod = rawType.GetMethod("AsSingle", Type.EmptyTypes);
			if (asSingleMethod?.Invoke(rawProgress, null) is float variantSingle)
				return Math.Clamp(variantSingle, 0f, 1f);
		}

		return TryParseProgressText(Convert.ToString(rawProgress, CultureInfo.InvariantCulture), out var parsedFallback)
			? parsedFallback
			: 0f;
	}

	private static bool TryParseProgressText(string? text, out float progress)
	{
		if (!string.IsNullOrWhiteSpace(text)
			&& (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out progress)
				|| float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out progress)))
		{
			progress = Math.Clamp(progress, 0f, 1f);
			return true;
		}

		progress = 0f;
		return false;
	}

	private bool StoreLoadedHeavyResource(string path, Resource? loadedResource = null)
	{
		var resource = loadedResource ?? ResourceLoader.LoadThreadedGet(path);
		switch (path)
		{
			case HeavyTileSetPath:
				_loadedTileSet = resource as TileSet;
				if (_loadedTileSet != null)
					return true;
				break;
		}

		FailHeavyStartupLoad(path, "unexpected resource type");
		return false;
	}

	private void FinalizeHeavyStartupLoad()
	{
		if (_startupState != StartupState.Finalizing)
			return;

		if (_loadedTileSet == null)
		{
			FailHeavyStartupLoad("startup", "missing heavy resources during finalization");
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
			_mapRender.Init(mapRoot, _loadedTileSet, viewportContainer, subViewport, playerCharacter, camera);
			_mapRender.SetWeatherScreenFxTuning(_weatherLabPanelController?.CurrentTuningSet ?? new WeatherScreenFxTuningSet());
			_combatFxPlayer = new CombatFxPlayer(_mapRender, _mapRender.CombatFxWorldRoot, _combatFxTextRoot);

			_startupProgress = 1f;
			_startupState = StartupState.Ready;
			RefreshStartupUi();
			QueuePostStartupTasks();
		}
		catch (Exception ex)
		{
			FailHeavyStartupLoad("startup-finalize", ex.Message);
		}
	}

	private void FailHeavyStartupLoad(string path, string reason)
	{
		GD.PrintErr($"[Startup] Heavy resource load failed: {path} ({reason})");
		TransitionToStartupFailed(path);
	}

	private void FailStartupBootstrap(string path, Exception ex)
	{
		GD.PrintErr($"[Startup] Bootstrap failed: {path} ({ex.GetType().Name}: {ex.Message})");
		TransitionToStartupFailed(path);
	}

	private void TransitionToStartupFailed(string path)
	{
		_startupState = StartupState.Failed;
		_startupLastLoadPath = path;
		_startupLoadPath = null;
		_startupLoadStartedAtMsec = 0;
		_startupStatusKey = "ui.startup.status.failed";
		RefreshStartupUi();
	}

	private void BindStartupOverlayNodes()
	{
		var startupOverlay = GetNode<Control>($"{OverlayRootPath}/StartupOverlay");
		var startupStatusLabel = GetNode<Label>($"{OverlayRootPath}/StartupOverlay/Bar/Margin/VBox/Status");
		var startupProgressBar = GetNode<ProgressBar>($"{OverlayRootPath}/StartupOverlay/Bar/Margin/VBox/Progress");
		_startupOverlay = startupOverlay;
		_startupStatusLabel = startupStatusLabel;
		_startupProgressBar = startupProgressBar;
	}

	private bool ShouldSkipRuntimeCallbacks()
	{
		if (_startupState == StartupState.Failed)
			return true;

		return !IsNodeReady();
	}

	private void RefreshStartupUi()
	{
		if (_startupOverlay == null)
			return;

		var startupActive = _startupState != StartupState.Ready;
		var overlayVisible = startupActive || _busyOperationActive;
		_startupOverlay.Visible = overlayVisible;

		if (startupActive)
		{
			_startupStatusLabel.Text = LocalizationService.T(_startupStatusKey);
			_startupProgressBar.Value = Math.Round(_startupProgress * 100f);
		}
		else if (_busyOperationActive)
		{
			_startupStatusLabel.Text = LocalizationService.T(_busyOperationStatusKey);
			_startupProgressBar.Value = Math.Round(_busyOperationProgress * 100f);
		}

		_startupOverlay.MouseFilter = overlayVisible && (_busyOperationActive || _startupState != StartupState.Failed)
			? Control.MouseFilterEnum.Stop
			: Control.MouseFilterEnum.Ignore;
		if (_menu != null && _session != null)
			RefreshMainMenuContinueState();
		SyncSettingsUiState();
	}

	private SettingsUiState BuildSettingsUiState(SettingsEntryContext? context = null)
	{
		var resolvedContext = context ?? (_menu.InMenu
			? SettingsEntryContext.MainMenu
			: SettingsEntryContext.InGamePause);

		return new SettingsUiState(
			resolvedContext,
			LocalizationService.CurrentLocale,
			RenderReady,
			_watchModeEnabled,
			MapEditorActive,
			_session.GameStarted,
			_enableKeyboardTargeting,
			_enableDebugPanel,
			_weatherLabPanelController?.CanUse == true,
			_weatherLabPanelController?.Visible == true);
	}

	private void SyncSettingsUiState(SettingsEntryContext? context = null)
	{
		if (_settingsFlow == null)
			return;

		_settingsFlow.ApplyState(BuildSettingsUiState(context));
	}

	private void RefreshMainMenuContinueState()
	{
		if (_session == null || _menu == null)
			return;

		var continueTarget = _session.ResolveContinueTarget();
		_menu.RefreshMainMenuState(
			continueTarget.Kind != ContinueTargetKind.None,
			ResourcesReady,
			_session.BuildContinueButtonText(continueTarget));
	}

	private void ShowMainMenuWithCurrentContinue()
	{
		var continueTarget = _session.ResolveContinueTarget();
		_menu.ShowMainMenu(
			continueTarget.Kind != ContinueTargetKind.None,
			ResourcesReady,
			_session.BuildContinueButtonText(continueTarget));
	}

	private void ToggleKeyboardTargeting()
	{
		_enableKeyboardTargeting = !_enableKeyboardTargeting;
		AppSettingsStore.SaveEnableKeyboardTargeting(_enableKeyboardTargeting);
		SyncSettingsUiState();

		if (!_enableKeyboardTargeting && _inspectModeActive)
			EndInspectMode(restoreFocus: false);
	}

	private void ToggleDebugPanelSetting()
	{
		_enableDebugPanel = !_enableDebugPanel;
		AppSettingsStore.SaveEnableDebugPanel(_enableDebugPanel);
		if (!_enableDebugPanel)
			CloseDebugPanel();
		SyncSettingsUiState();
	}

	private static Control CreateMapOverlayRoot(Control mapPanel)
	{
		mapPanel.GetNodeOrNull<Control>("CombatFxTextRoot")?.QueueFree();
		var root = new Control
		{
			Name = "CombatFxTextRoot",
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 40,
		};
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		mapPanel.AddChild(root);
		return root;
	}

	private Node2D CreatePlayerCharacterNode()
	{
		_playerCharacterVisual = new FantasyCharacterAnimatable();
		_playerCharacterVisual.Name = "PlayerCharacter";
		RefreshPlayerCharacterVisual();
		return _playerCharacterVisual;
	}

	private void RefreshPlayerCharacterVisual()
	{
		if (_playerCharacterVisual == null)
			return;

		var appearance = PlayerAppearanceCatalog.GetOrDefault(_state.PlayerAppearanceId);
		_playerCharacterVisual.Configure(appearance.SheetDir, appearance.DefaultAnim);
		_playerCharacterVisual.Visible = false;
		_playerCharacterVisual.SetMovementDirection(1, 0);
		ResAccess.RegisterAnimatable(Factions.Player, _playerCharacterVisual);
		if (!string.IsNullOrWhiteSpace(_state.PlayerId))
			ResAccess.RegisterAnimatable(_state.PlayerId, _playerCharacterVisual);
	}

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
	}

	private void PlayCombatFx(GameEvent e)
	{
		if (_combatFxPlayer == null || _mapRender == null)
			return;

		var sourceVisible = _mapRender.IsWorldCellVisible(e.SourceX, e.SourceY, _state.PlayerZ);
		var targetVisible = _mapRender.IsWorldCellVisible(e.TargetX, e.TargetY, _state.PlayerZ);
		var commands = _combatFxRegistry.Resolve(e, sourceVisible, targetVisible);
		if (commands.Count == 0)
			return;

		_combatFxPlayer.Play(commands, _state.PlayerZ);
	}

	private void PlayWeatherLightningFx(GameEvent e)
	{
		if (_combatFxPlayer == null || _mapRender == null)
			return;

		if (!_mapRender.IsWorldCellVisible(e.TargetX, e.TargetY, _state.PlayerZ))
			return;

		_combatFxPlayer.Play(
		[
			new CombatFxCommand
			{
				Kind = CombatFxCommandKind.Sprite,
				Anchor = CombatFxAnchor.Target,
				ResourceId = "fx_lightning_strike",
				WorldX = e.TargetX,
				WorldY = e.TargetY,
				TargetWorldX = e.TargetX,
				TargetWorldY = e.TargetY,
				DurationSeconds = 0.28f,
				Scale = 1.25f,
				Layer = 7,
			},
			new CombatFxCommand
			{
				Kind = CombatFxCommandKind.Text,
				Anchor = CombatFxAnchor.Target,
				Text = e.Damage > 0 ? e.Damage.ToString() : "!",
				WorldX = e.TargetX,
				WorldY = e.TargetY,
				DurationSeconds = 0.55f,
				Tint = Colors.LightYellow,
				Layer = 8,
				RisePixels = 76f,
			},
		], _state.PlayerZ);
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

	private void QueuePostStartupTasks()
	{
		if (_postStartupTasksQueued)
			return;

		_postStartupTasksQueued = true;
		_postStartupTaskDelayFrames = 1;
		_postStartupTasks.Enqueue(PrewarmDeferredUiScenes);
	}

	private void PollPostStartupTasks()
	{
		if (_startupState != StartupState.Ready || _postStartupTasks.Count == 0)
			return;

		if (_postStartupTaskDelayFrames > 0)
		{
			_postStartupTaskDelayFrames--;
			return;
		}

		var task = _postStartupTasks.Dequeue();
		try
		{
			task();
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[Startup] Deferred task failed: {ex.Message}");
		}
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
		_mainInputCoordinator.HandleInput(@event, snapshot);
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

	private void HandleMenuMapEditor() => _mainAppFlowCoordinator.HandleMenuMapEditor();

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

	private void HandleLanguageChanged(string locale)
	{
		AppSettingsStore.SaveLocale(locale);
		var changed = LocalizationService.SetLocale(locale);
		SyncSettingsUiState();
		if (!changed)
			return;

		GameLocalizer.ApplyPresetTranslations();
		GameLocalizer.RelocalizeGameState(_state);
		DialogPool.Load();
		_mapEditor.RefreshLocalizedBrushes();
		RefreshLocalizedUi(clearLogs: _session.GameStarted);
		RefreshStartupUi();
	}

	private void RefreshLocalizedUi(bool clearLogs)
	{
		LocalizationService.LocalizeTree(this);
		_settingsFlow.RefreshTexts();
		_weatherLabPanelController.RefreshTexts();
		SyncSettingsUiState();
		_worldManager.RefreshTexts();
		_worldSettingsDialog.RefreshTexts();
		_characterCreation.RefreshTexts();
		_mapEditorBar.RefreshTexts();
		_threatHud.RefreshTexts();
		_targetSummaryHud.RefreshTexts();
		_needsHud.RefreshTexts(ActorModule.GetPlayer(_state), _state.Turn);
		_healthAlerts.RefreshTexts();
		RefreshMainMenuContinueState();
		RefreshStartupUi();

		if (MapEditorActive)
			RefreshMapEditorBar();

		RefreshVisiblePanels();
		if (IsWorldManagerOpen)
			RefreshWorldManagerContents();

		if (clearLogs)
		{
			_log.Clear();
			if (MapEditorActive)
				ShowMapEditorHints();
			else
				ShowGameHints();
		}

		MarkUIDirty();
		ProcessDirtyPanels();
		if (_session.GameStarted && !_menu.InMenu && RenderReady)
			FlushMap();
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
		var player = ActorModule.GetPlayer(_state);

		if (_statusPanelModule.PanelNode.Visible)
			_statusPanelModule.Refresh(_state, player, _state.PlayerZ, _state.Turn);
		if (_skillBar.Visible)
			_skillBar.Refresh(player);
		if (_skillMgr.Visible)
			_skillMgr.State = _state;
		if (_skillMgr.Visible)
			_skillMgr.Refresh();
		if (_inventoryPanel.Visible)
			_inventoryPanel.Refresh();
		if (_groundPanel.Visible)
			_groundPanel.Refresh();
		if (_chestPanel?.Visible == true)
			_chestPanel.Refresh();
		if (_questPanel?.Visible == true)
			_questPanel.Refresh();
		_debugPanelController.MarkDirty();
		_debugPanelController.FlushIfDirty();
		if (_actorInspectPanel?.Visible == true)
			RefreshActorInspectPanel();
		if (_tradeUI?.InTrade == true)
			_tradeUI.Refresh();
		if (_dialogUI?.InDialog == true)
			_dialogUI.RefreshCurrentEntry();
		if (_worldManager.Visible)
			RefreshWorldManagerContents();
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

	private void BeginLayoutEditMode()
	{
		if (_menu.InMenu || !_session.GameStarted || LayoutEditActive || MapEditorActive)
			return;

		_layoutResetPending = false;
		_modalStateController.Prepare(RuntimeUiResetReason.EnterLayoutEdit);
		_inputModule.CancelSelection();
		_inputModule.EnterActionMode();
		_panels.ClearFocus();
		_panelDrag.BeginEditSession();
		_layoutEditBar.Open();
	}

	private void ApplyLayoutEditMode()
	{
		if (!LayoutEditActive)
			return;

		if (_layoutResetPending)
		{
			foreach (var panelId in LayoutEditablePanelIds)
				_panelDrag.RemovePersistedPosition(panelId);
		}

		_panelDrag.ApplyEditSession();
		_layoutResetPending = false;
		_layoutEditBar.Close();
		FlushMap();
	}

	private void CancelLayoutEditMode()
	{
		if (!LayoutEditActive)
			return;

		_panelDrag.CancelEditSession();
		_layoutResetPending = false;
		_layoutEditBar.Close();
		FlushMap();
	}

	private void ResetLayoutEditMode()
	{
		if (!LayoutEditActive)
			return;

		_layoutResetPending = true;
		_panelDrag.ResetToDefaults();
		FlushMap();
	}

	private void EnterMapEditor(MapEditorEntryMode entryMode) => _mainAppFlowCoordinator.EnterMapEditor(entryMode);

	private void EnterMapEditorCore(MapEditorEntryMode entryMode)
	{
		if (!ResourcesReady || !_session.GameStarted || _state.World == null)
			return;

		_modalStateController.Prepare(RuntimeUiResetReason.EnterMapEditor);
		_inputModule.CancelSelection();
		_inputModule.EnterActionMode();
		_panels.ClearFocus();
		_mapEditor.Enter(entryMode, entryMode == MapEditorEntryMode.MenuBlank ? null : _session.CurrentSavePath);
		_mapEditorBar.Open(_mapEditor.CanCenterOnPlayer);
		_weatherLabPanelController.RefreshSessionState(autoOpen: false);
		SyncSettingsUiState();
		RefreshMapEditorBar();
		FlushMap();
	}

	private void ExitMapEditor(bool silent = false)
	{
		if (!MapEditorActive)
			return;

		var startedFromMenu = _mapEditor.StartedFromMenu;
		_mapEditor.Exit();
		_mapEditorBar.Close();
		CloseSaveNameDialog();
		_weatherLabPanelController.RefreshSessionState(autoOpen: true);
		SyncSettingsUiState();
		if (startedFromMenu)
		{
			if (!silent)
				_log.Add(LocalizationService.T("log.map_editor.exit_to_menu"));
			ShowMainMenuWithCurrentContinue();
			return;
		}

		if (!silent)
			_log.Add(LocalizationService.T("log.map_editor.exit"));
		FlushMap();
	}

	private void ToggleMapEditor() => _mainAppFlowCoordinator.ToggleMapEditor();

	private void RefreshMapEditorBar()
	{
		if (!MapEditorActive)
			return;

		_mapEditorBar.Render(_mapEditor.CurrentCategory, _mapEditor.CurrentBrushes, _mapEditor.CurrentBrushIndex);
	}

	private bool HandleMapEditorKeyInput(InputEventKey key)
	{
		if (!key.Pressed)
			return false;

		switch (key.Keycode)
		{
			case Key.Escape:
				ExitMapEditor();
				return true;
			case Key.Tab:
				_mapEditor.ToggleCategory();
				RefreshMapEditorBar();
				FlushMap();
				return true;
			case Key.W:
			case Key.Up:
				_mapEditor.MoveCamera(0, -1);
				FlushMap();
				return true;
			case Key.S:
			case Key.Down:
				_mapEditor.MoveCamera(0, 1);
				FlushMap();
				return true;
			case Key.A:
			case Key.Left:
				_mapEditor.MoveCamera(-1, 0);
				FlushMap();
				return true;
			case Key.D:
			case Key.Right:
				_mapEditor.MoveCamera(1, 0);
				FlushMap();
				return true;
		}

		return false;
	}

	private bool HandleMapEditorMouseInput(InputEvent @event)
	{
		if (_mapRender == null)
			return false;

		if (@event is InputEventMouseMotion motion)
		{
			if (_mapRender.TryGetWorldCellFromGlobalPosition(motion.GlobalPosition, out var hovered))
			{
				if (_mapEditor.SetHover(new Vector2I(hovered.X, hovered.Y)))
					FlushMap();
				return true;
			}

			if (_mapEditor.SetHover(null))
				FlushMap();
			return false;
		}

		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return false;

		if (mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
		{
			_mapEditor.CycleBrush(mb.ButtonIndex == MouseButton.WheelUp ? -1 : 1);
			RefreshMapEditorBar();
			FlushMap();
			return true;
		}

		if (mb.ButtonIndex is not MouseButton.Left and not MouseButton.Right)
			return false;

		if (!_mapRender.TryGetWorldCellFromGlobalPosition(mb.GlobalPosition, out var worldCell))
			return false;

		_mapEditor.SetHover(new Vector2I(worldCell.X, worldCell.Y));
		if (mb.ButtonIndex == MouseButton.Left)
			_mapEditor.ApplyBrush(worldCell.X, worldCell.Y);
		else
			_mapEditor.EraseBrush(worldCell.X, worldCell.Y);

		FlushMap();
		return true;
	}

	private void HandleMapEditorSaveRequested()
	{
		if (!MapEditorActive)
			return;

		if (_mapEditor.StartedFromMenu && string.IsNullOrEmpty(_mapEditor.SavePath))
		{
			OpenSaveNameDialog();
			return;
		}

		var path = _mapEditor.SavePath ?? _session.GetPreferredSavePath();
		DoSave(path, _session.DescribeSavePath(path));
		_mapEditor.UpdateSavePath(path);
	}

	private void OpenSaveNameDialog()
	{
		if (!MapEditorActive)
			return;

		_panelChrome.CloseActiveSettings();
		_saveNameDialog.Open("editor_map");
	}

	private void CloseSaveNameDialog()
	{
		_saveNameDialog.Close();
	}

	private void HandleSaveNameConfirmed(string rawName)
	{
		if (!MapEditorActive)
			return;

		var path = _session.BuildNamedSavePath(rawName);
		CloseSaveNameDialog();
		DoSave(path, _session.DescribeSavePath(path));
		_mapEditor.UpdateSavePath(path);
	}

	private void HandleSkillConfirmRequested(InteractionDef skill)
	{
		if (!SkillQuery.IsUnifiedCastSkill(skill))
		{
			_log.Add(LocalizationService.T("log.skill_cast_failed.unsupported", ("skill", skill.Name)));
			return;
		}

		if (string.Equals(_armedSkillId, skill.Id, StringComparison.Ordinal))
		{
			ClearArmedSkill();
			return;
		}

		if (IsTimelineInputLocked())
			return;

		ArmSkill(skill.Id);
	}

	private void ArmSkill(string skillId)
	{
		if (string.IsNullOrWhiteSpace(skillId))
		{
			ClearArmedSkill();
			return;
		}

		_armedSkillId = skillId;
		CloseLimbTargetPanel();
		if (_skillCastCursorActive)
			EndInspectMode();

		RefreshArmedSkillUi();
	}

	private void ClearArmedSkill(bool restoreFocus = true)
	{
		_armedSkillId = null;
		CloseLimbTargetPanel();
		RefreshArmedSkillUi();
		if (_skillCastCursorActive)
			EndInspectMode(restoreFocus);
	}

	private void RefreshArmedSkillUi()
	{
		_skillBar.ArmedSkillId = _armedSkillId;
		_skillMgr.ArmedSkillId = _armedSkillId;
		_skillBarDirty = true;
		_skillMgr.Dirty = true;
	}

	private void OpenLimbTargetPanel(Actor target, InteractionDef skill) =>
		OpenLimbTargetPanel(new LimbTargetPanelModule.LimbTargetRequest
		{
			TargetActor = target,
			Skill = skill,
			TargetName = IdentificationModule.GetActorDisplayName(_state, target),
			Options = target.Limbs
				.Select(limb => new LimbTargetPanelModule.LimbTargetOption
				{
					LimbId = limb.Id,
					Label = limb.Name,
					CurrentDurability = limb.Durability,
					MaxDurability = limb.MaxDurability,
					IsVital = CombatModule.IsVitalLimb(limb),
					IsMissing = limb.Durability <= 0,
				})
				.ToList(),
		});

	private void OpenLimbTargetPanel(LimbTargetPanelModule.LimbTargetRequest request)
	{
		var panel = EnsureLimbTargetPanel();
		panel.Open(_state, request);
		_panels.PushFocus(panel);
	}

	private void OpenOperationTargetPanel(Actor surgeon, Actor target, InteractionDef skill)
	{
		var limbIds = SurgeryModule.GetLiveOperationLimbIds(surgeon, target);
		if (limbIds.Count == 0)
		{
			_log.Add(LocalizationService.TOrFallback("log.surgery.no_operation_targets", "No valid operation is available for that target."));
			return;
		}

		var request = new LimbTargetPanelModule.LimbTargetRequest
		{
			TargetActor = target,
			Skill = skill,
			TargetName = IdentificationModule.GetActorDisplayName(_state, target),
			Options = limbIds
				.Select(limbId => CreateOperationOption(target, limbId))
				.ToList(),
		};
		OpenLimbTargetPanel(request);
	}

	private void OpenCorpseHarvest(Item corpseItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		var skill = InteractionDefs.Get("harvest_corpse");
		if (skill == null)
			return;

		var limbIds = SurgeryModule.GetCorpseHarvestableLimbIds(corpseItem);
		if (limbIds.Count == 0)
		{
			_log.Add(LocalizationService.TOrFallback("log.corpse.no_harvest_targets", "Nothing useful remains to harvest."));
			return;
		}

		var request = new LimbTargetPanelModule.LimbTargetRequest
		{
			TargetItem = corpseItem,
			Skill = skill,
			TargetName = ItemFormatHelper.GetDisplayName(_state, corpseItem),
			Options = limbIds
				.Select(limbId => CreateCorpseHarvestOption(limbId))
				.ToList(),
		};
		OpenLimbTargetPanel(request);
	}

	private LimbTargetPanelModule.LimbTargetOption CreateOperationOption(Actor target, string limbId)
	{
		var current = target.Limbs.FirstOrDefault(limb => string.Equals(limb.Id, limbId, StringComparison.Ordinal));
		var preset = PresetDB.Limbs.GetValueOrDefault(limbId);
		return new LimbTargetPanelModule.LimbTargetOption
		{
			LimbId = limbId,
			Label = current?.Name ?? preset?.Name ?? limbId,
			CurrentDurability = current?.Durability ?? 0,
			MaxDurability = current?.MaxDurability ?? preset?.MaxDurability ?? 0,
			IsVital = current != null
				? CombatModule.IsVitalLimb(current)
				: (preset?.Tags.ContainsKey(CombatModule.VitalTag) ?? false) || (preset?.Tags.ContainsKey("要害") ?? false),
			IsMissing = current == null || current.Durability <= 0,
		};
	}

	private static LimbTargetPanelModule.LimbTargetOption CreateCorpseHarvestOption(string limbId)
	{
		var preset = PresetDB.Limbs.GetValueOrDefault(limbId);
		return new LimbTargetPanelModule.LimbTargetOption
		{
			LimbId = limbId,
			Label = preset?.Name ?? limbId,
			CurrentDurability = preset?.MaxDurability ?? 0,
			MaxDurability = preset?.MaxDurability ?? 0,
			IsVital = (preset?.Tags.ContainsKey(CombatModule.VitalTag) ?? false) || (preset?.Tags.ContainsKey("要害") ?? false),
			IsMissing = false,
		};
	}

	private void SubmitCorpseOperation(string skillId, Item corpseItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		SubmitPlayerAction(TimelinePlayerAction.CastSkill(
			skillId,
			SkillTargetType.Item,
			targetItemId: corpseItem.InstanceId,
			targetX: player.X,
			targetY: player.Y,
			targetZ: player.Z));
	}

	private void CloseLimbTargetPanel()
	{
		if (_limbTargetPanel == null || !_limbTargetPanel.Visible)
			return;

		_limbTargetPanel.Close();
		_panels.OnPanelClosed(_limbTargetPanel);
	}

	private void HandleLimbTargetConfirmed(LimbTargetPanelModule.LimbTargetRequest request, LimbTargetPanelModule.LimbTargetOption option)
	{
		if (request.TargetItem != null)
		{
			CloseLimbTargetPanel();
			var player = ActorModule.GetPlayer(_state);
			if (player == null)
				return;

			SubmitPlayerAction(TimelinePlayerAction.CastSkill(
				request.Skill.Id,
				SkillTargetType.Item,
				targetLimbId: option.LimbId,
				targetItemId: request.TargetItem.InstanceId,
				targetX: player.X,
				targetY: player.Y,
				targetZ: player.Z));
			return;
		}

		var target = request.TargetActor;
		var limb = target?.Limbs.Find(candidate => string.Equals(candidate.Id, option.LimbId, StringComparison.Ordinal));
		if (target == null)
		{
			CloseLimbTargetPanel();
			return;
		}

		CloseLimbTargetPanel();
		SubmitPlayerAction(TimelinePlayerAction.CastSkill(
			request.Skill.Id,
			SkillTargetType.Actor,
			targetActorId: target.Id,
			targetLimbId: limb?.Id ?? option.LimbId,
			targetX: target.X,
			targetY: target.Y,
			targetZ: target.Z));
	}

	private InteractionDef? GetArmedSkill()
	{
		if (string.IsNullOrWhiteSpace(_armedSkillId))
			return null;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			ClearArmedSkill(restoreFocus: false);
			return null;
		}

		foreach (var skill in SkillQuery.GetAll(player))
		{
			if (!string.Equals(skill.Id, _armedSkillId, StringComparison.Ordinal))
				continue;
			if (SkillQuery.IsUnifiedCastSkill(skill))
				return skill;
			break;
		}

		ClearArmedSkill();
		return null;
	}

	private void HandleInspectToggleCommand()
	{
		if (_inspectModeActive)
		{
			if (_skillCastCursorActive)
				_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
			EndInspectMode();
			return;
		}

		if (IsTimelineInputLocked())
			return;

		if (!_enableKeyboardTargeting)
			return;

		if (GetArmedSkill() != null)
		{
			StartSkillCastCursorMode();
			return;
		}

		StartInspectMode();
	}

	private void StartSkillCastCursorMode()
	{
		var skill = GetArmedSkill();
		if (skill == null || !_session.GameStarted || _menu.InMenu || _mapRender == null)
			return;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return;

		_inspectModeActive = true;
		_skillCastCursorActive = true;
		_inspectWorldCell = ResolveSkillCursorOriginCell(player);
		_inspectPreviousFocusId = _panels.FocusedId;
		CloseActorInspectPanel();
		_panels.SetFocus("map");
		_log.Add(LocalizationService.T("ui.skill.targeting.entered", ("skill", skill.Name)));
		FlushMap();
	}

	private bool TryCastArmedSkillAtMouse(Vector2 globalPosition)
	{
		if (_mapRender == null || !(_mapRender.TryGetWorldCellFromGlobalPosition(globalPosition, out var worldCell)))
			return false;

		return TryCastArmedSkillAtWorldCell(worldCell);
	}

	private bool TryOpenActorInspectPanelAtMouse(Vector2 globalPosition)
	{
		if (_mapRender == null || !(_mapRender.TryGetWorldCellFromGlobalPosition(globalPosition, out var worldCell)))
			return false;

		var actor = LookModule.TryGetInspectableActor(_state, _fogTracker, worldCell.X, worldCell.Y, worldCell.Z);
		if (actor == null)
			return false;

		OpenActorInspectPanel(actor);
		return true;
	}

	private bool TryCastArmedSkillAtWorldCell(Vector3I worldCell)
	{
		var skill = GetArmedSkill();
		if (skill == null || IsTimelineInputLocked())
			return false;

		var player = ActorModule.GetPlayer(_state);
		if (player == null)
			return false;

		var targetType = ActionModule.ResolveSkillTargetType(skill);
		var targetActor = targetType == SkillTargetType.Actor
			? ActorModule.GetAt(_state, worldCell.X, worldCell.Y, worldCell.Z)
			: null;
		var restoreFocus = _skillCastCursorActive;
		if (_inspectModeActive)
			EndInspectMode(restoreFocus);

		if (IsIdentifySkill(skill))
			return TryHandleIdentifyActorTarget(targetActor);

		switch (targetType)
		{
			case SkillTargetType.Self:
				SubmitPlayerAction(TimelinePlayerAction.CastSkill(skill.Id, SkillTargetType.Self));
				return true;

			case SkillTargetType.Cell:
				SubmitPlayerAction(TimelinePlayerAction.CastSkill(
					skill.Id,
					SkillTargetType.Cell,
					targetX: worldCell.X,
					targetY: worldCell.Y,
					targetZ: worldCell.Z));
				return true;

			case SkillTargetType.Actor:
				if (targetActor != null && string.Equals(skill.EffectType, "operate", StringComparison.Ordinal))
				{
					SetCurrentTarget(targetActor, PlayerTargetSource.Explicit);
					OpenOperationTargetPanel(player, targetActor, skill);
					return true;
				}

				if (targetActor != null
					&& SkillQuery.IsAttackSkill(skill)
					&& ActionModule.CanCastSkill(
						_state,
						player,
						skill.Id,
						SkillTargetType.Actor,
						targetActor: targetActor,
						targetX: targetActor.X,
						targetY: targetActor.Y,
						targetZ: targetActor.Z))
				{
					SetCurrentTarget(targetActor, PlayerTargetSource.Explicit);
					OpenLimbTargetPanel(targetActor, skill);
					return true;
				}

				SubmitPlayerAction(TimelinePlayerAction.CastSkill(
					skill.Id,
					SkillTargetType.Actor,
					targetActorId: targetActor?.Id,
					targetX: worldCell.X,
					targetY: worldCell.Y,
					targetZ: worldCell.Z));
				return true;

			default:
				return false;
		}
	}

	private static bool IsIdentifySkill(InteractionDef skill) =>
		string.Equals(skill.EffectType, "identify", StringComparison.Ordinal);

	private bool TryHandleIdentifyActorTarget(Actor? targetActor)
	{
		if (targetActor == null)
		{
			_log.Add(LocalizationService.T("ui.inspect.no_actor"));
			return true;
		}

		var identified = IdentificationModule.IdentifyActor(_state, targetActor);
		var actorName = IdentificationModule.GetActorDisplayName(_state, targetActor);
		_log.Add(LocalizationService.T(
			identified ? "log.identify.actor_identified" : "log.identify.actor_known",
			("target", actorName)));

		OpenActorInspectPanel(targetActor);
		RefreshVisiblePanels();
		FlushMap();
		return true;
	}

	private bool TryHandleIdentifyItemTarget(Item item)
	{
		var skill = GetArmedSkill();
		if (skill == null || !IsIdentifySkill(skill))
			return false;

		IdentificationModule.IdentifyItem(_state, item);
		RefreshVisiblePanels();
		FlushMap();
		return true;
	}

	private bool HandleInspectModeKey(InputEventKey key)
	{
		if (!_inspectModeActive || !key.Pressed)
			return false;

		if (_actorInspectPanel?.Visible == true && _panels.FocusedId == _actorInspectPanel.PanelId)
			return false;

		if (key.Keycode is Key.Enter or Key.KpEnter)
		{
			if (_skillCastCursorActive)
			{
				if (_inspectWorldCell is { } worldCell)
					TryCastArmedSkillAtWorldCell(worldCell);
			}
			else
			{
				OpenInspectActorPanelAtCursor();
			}
			return true;
		}

		if (key.Keycode == Key.Escape)
		{
			if (_skillCastCursorActive)
				_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
			EndInspectMode();
			return true;
		}

		if (_inputBindings.Resolve(InputBindingContext.Action, key, out var actionId, out _))
		{
			switch (actionId)
			{
				case "move_north":
					MoveInspectCursor(0, -1);
					return true;
				case "move_south":
					MoveInspectCursor(0, 1);
					return true;
				case "move_west":
					MoveInspectCursor(-1, 0);
					return true;
				case "move_east":
					MoveInspectCursor(1, 0);
					return true;
				case "inspect_mode":
					if (_skillCastCursorActive)
						_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
					EndInspectMode();
					return true;
			}
		}

		if (key.Unicode == '*')
		{
			if (_skillCastCursorActive)
				_log.Add(LocalizationService.T("ui.skill.targeting.canceled"));
			EndInspectMode();
			return true;
		}

		return true;
	}

	private void StartInspectMode()
	{
		if (!_session.GameStarted || _menu.InMenu || _mapRender == null)
			return;

		_inspectModeActive = true;
		_skillCastCursorActive = false;
		_inspectWorldCell = new Vector3I(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_inspectPreviousFocusId = _panels.FocusedId;
		_panels.SetFocus("map");
		_log.Add(LocalizationService.T("ui.inspect.entered"));
		LogInspectCellInfo();
		FlushMap();
	}

	private void EndInspectMode(bool restoreFocus = true)
	{
		if (!_inspectModeActive)
			return;

		CloseActorInspectPanel();
		_inspectModeActive = false;
		_skillCastCursorActive = false;
		_inspectWorldCell = null;
		_mapRender!.InspectWorldCell = null;

		var focusId = restoreFocus ? _inspectPreviousFocusId : null;
		_inspectPreviousFocusId = null;
		if (restoreFocus)
		{
			if (string.IsNullOrEmpty(focusId))
				_panels.ClearFocus();
			else
				_panels.SetFocus(focusId);
		}

		FlushMap();
	}

	private void MoveInspectCursor(int dx, int dy)
	{
		if (_inspectWorldCell is not { } current)
			return;

		var halfW = ViewW / 2;
		var halfH = ViewH / 2;
		var nextX = Math.Clamp(current.X + dx, _state.PlayerX - halfW, _state.PlayerX - halfW + ViewW - 1);
		var nextY = Math.Clamp(current.Y + dy, _state.PlayerY - halfH, _state.PlayerY - halfH + ViewH - 1);
		if (nextX == current.X && nextY == current.Y)
			return;

		_inspectWorldCell = new Vector3I(nextX, nextY, _state.PlayerZ);
		if (!_skillCastCursorActive)
			LogInspectCellInfo();
		FlushMap();
	}

	private void LogInspectCellInfo()
	{
		if (_inspectWorldCell is not { } cell)
			return;

		var info = LookModule.DescribeCell(_state, _fogTracker, cell.X, cell.Y, cell.Z);
		_log.Add(info.Text);
	}

	private void OpenInspectActorPanelAtCursor()
	{
		if (_inspectWorldCell is not { } cell)
			return;

		var info = LookModule.DescribeCell(_state, _fogTracker, cell.X, cell.Y, cell.Z);
		if (info.InspectableActor == null)
		{
			_log.Add(LocalizationService.T("ui.inspect.no_actor"));
			return;
		}

		OpenActorInspectPanel(info.InspectableActor);
	}

	private void RefreshActorInspectPanel()
	{
		if (_actorInspectPanel == null || !_actorInspectPanel.Visible)
			return;

		if (string.IsNullOrEmpty(_inspectActorId))
		{
			CloseActorInspectPanel();
			return;
		}

		var actor = ActorModule.GetById(_state, _inspectActorId);
		if (actor == null)
		{
			CloseActorInspectPanel();
			return;
		}

		ActorDerivedStateUpdater.SyncInspectActor(_state, actor);
		_actorInspectPanel.Refresh(_state, actor);
	}

	private void OpenActorInspectPanel(Actor actor)
	{
		var panel = EnsureActorInspectPanel();
		_inspectActorId = actor.Id;
		ActorDerivedStateUpdater.SyncInspectActor(_state, actor);
		panel.Open(_state, actor);
		_panels.PushFocus(panel);
	}

	private void CloseActorInspectPanel()
	{
		_inspectActorId = null;
		if (_actorInspectPanel == null || !_actorInspectPanel.Visible)
			return;

		_actorInspectPanel.Close();
		_panels.OnPanelClosed(_actorInspectPanel);
	}

	// ══════════════════════════════════════════════════════
	//  命令分发（InputModule 产出命令字符串 → 此处路由到具体逻辑）

	/// <summary>

	// ══════════════════════════════════════════════════════
	//  命令分发（InputModule 产出命令字符串 → 此处路由到具体逻辑）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 核心命令路由。命令来源：InputModule 的键盘映射或文本输入。
	/// 前缀 ":" 的是快捷键命令，无前缀的是文本命令。
	/// </summary>
	private void OnCommand(string cmd)
	{
		var fogMapVisible = _mapRender?.FogMapVisible == true;

		if (PlayerDead)
		{
			PlayerDead = false;
			ShowMainMenuWithCurrentContinue();
			return;
		}

		if (_playerRestModeActive && cmd != ":rest")
			_playerRestModeActive = false;

		if (MapEditorActive)
			return;

		if (cmd.StartsWith(":dig_") && cmd != ":dig")
		{
			HandleDigDirection(cmd[":dig_".Length..]);
			return;
		}
		if (cmd == ":dir_cancel")
		{
			_log.Add(LocalizationService.T("ui.selection.canceled"));
			return;
		}

		if (cmd is ":settings" or "settings")
		{
			if (fogMapVisible && _mapRender != null)
			{
				_mapRender.FogMapVisible = false;
				_log.Add(LocalizationService.T("log.fog_map.closed"));
				FlushMap();
				return;
			}

			ToggleSettingsPanel();
			return;
		}

		if (cmd == ":inspect_mode")
		{
			HandleInspectToggleCommand();
			return;
		}

		if (cmd == ":debug_panel")
		{
			ToggleDebugPanel();
			return;
		}

		if (IsTimelineInputLocked())
			return;

		switch (cmd)
		{
			case ":quicksave":
			{
				var path = _session.GetQuickSavePath();
				DoSave(path, _session.DescribeSavePath(path));
				return;
			}
			case ":quickload":
			{
				var path = _session.GetQuickSavePath();
				DoLoad(path, _session.DescribeSavePath(path));
				return;
			}
			case ":interact" or "interact": DoInteract(); return;
			case ":dig": StartDig(); return;
			case ":inventory": ToggleInventory(); return;
			case ":rest": TogglePlayerRestMode(); return;
			case ":skills": ToggleSkillManager(); return;
			case ":skillbar": ToggleSkillBarPanel(); return;
			case ":skill_prev": if (_skillBar.Visible) _skillBar.StepSelection(-1); return;
			case ":skill_next": if (_skillBar.Visible) _skillBar.StepSelection(1); return;
			case ":toggle_status": ToggleStatusPanel(); return;
			case ":cycle_party": _partyHud.CycleActive(); FlushMap(); return;
			case ":quests": ToggleQuestPanel(); return;
			case ":render" or "render": ToggleRender(); return;
			case ":status_prev": _statusPanelModule.CycleTab(-1); return;
			case ":status_next": _statusPanelModule.CycleTab(1); return;
			case ":minimap": ToggleMinimap(); return;
			case ":fogmap": ToggleFogMap(); return;
			case ":fogmap_center": CenterFogMap(); return;
		}

		if (_settingsFlow.SettingsVisible) return;

		if (fogMapVisible && _mapRender != null)
		{
			switch (cmd)
			{
				case "w": _mapRender.ScrollFogMap(0, -1); FlushMap(); break;
				case "s": _mapRender.ScrollFogMap(0, 1); FlushMap(); break;
				case "a": _mapRender.ScrollFogMap(-1, 0); FlushMap(); break;
				case "d": _mapRender.ScrollFogMap(1, 0); FlushMap(); break;
			}
			return;
		}

		switch (cmd)
		{
			case "w": DoMove(0, -1); break;
			case "s": DoMove(0, 1); break;
			case "a": DoMove(-1, 0); break;
			case "d": DoMove(1, 0); break;
			case "look": DoLook(); break;
			case "enter": DoEnterStairs(); break;
			case "save": DoSaveCurrent(); break;
			case "load": OpenWorldManager(WorldManagerContext.InGame, WorldLaunchTab.Worlds); break;
			case "newmap":
				_session.NewGame(PlayerCreationOptions.CreateDefault());
				ClearPlayerTargeting();
				RefreshPlayerCharacterVisual();
				SyncTimelineAutoAdvanceState();
				_log.Add(LocalizationService.T("log.game.new_map_generated"));
				FlushMap();
				break;
			default:
				if (cmd.StartsWith('/'))
					_debugPanelController.HandleCommand(cmd);
				else
					_log.Add(LocalizationService.T("ui.command.unknown"));
				break;
		}
	}

	// ══════════════════════════════════════════════════════
	//  交互流程
	// ══════════════════════════════════════════════════════

	/// <summary>F 键智能交互：有相邻 Actor → 交互菜单；否则 → 脚下面板交互。</summary>
	private void DoInteract()
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var targets = InteractionModule
			.GetAvailableTargets(_state, player)
			.FindAll(target => GetNonCombatInteractions(player, target).Count > 0);

		if (targets.Count > 0)
		{
			if (targets.Count == 1)
			{
				ShowInteractionsFor(player, targets[0]);
				return;
			}

			var options = new List<(string Name, Action Execute)>();
			foreach (var t in targets)
			{
				var target = t;
				options.Add((IdentificationModule.GetActorDisplayName(_state, target), () => ShowInteractionsFor(player, target)));
			}

			var sb = new StringBuilder(LocalizationService.T("ui.interaction.choose_target"));
			for (var i = 0; i < options.Count; i++)
				sb.Append($"  [{i + 1}] {options[i].Name}");
			_log.Add(sb.ToString());

			_inputModule.EnterSelection(n =>
			{
			if (n < 1 || n > options.Count) { _log.Add(LocalizationService.T("ui.selection.invalid")); return; }
				options[n - 1].Execute();
			}, () => _log.Add(LocalizationService.T("ui.selection.canceled")));
			return;
		}

		var groundItems = MapModule.PeekGroundItems(_state, _state.PlayerX, _state.PlayerY);
		if (groundItems.Count > 0)
		{
			_groundPanel.Refresh();
			_groundPanel.DoInteractSelected();
		}
		else
		{
			_log.Add(LocalizationService.T("ui.interaction.none_nearby"));
		}
	}

	/// <summary>G 键：进入地形破坏方向选择。</summary>
	private void StartDig()
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var cellSkills = SkillQuery.GetCellSkills(player);
		if (cellSkills.Count == 0)
		{
			_log.Add(LocalizationService.T("dig.no_skills"));
			return;
		}

		var hasTargets = false;
		foreach (var skill in cellSkills)
		{
			if (InteractionModule.GetBreakableNeighbors(_state, player, skill).Count > 0)
			{ hasTargets = true; break; }
		}

		if (!hasTargets)
		{
			_log.Add(LocalizationService.T("dig.no_targets"));
			return;
		}

		_log.Add(LocalizationService.T("dig.choose_direction"));
		_inputModule.EnterDirectionMode("dig");
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void HandleDigDirection(string dir)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var (dx, dy) = dir switch
		{
			"n" => (0, -1),
			"s" => (0, 1),
			"w" => (-1, 0),
			"e" => (1, 0),
			_ => (0, 0),
		};
		if (dx == 0 && dy == 0) return;

		var tx = player.X + dx;
		var ty = player.Y + dy;
		var tz = player.Z;

		if (_state.World == null) return;
		var terrain = _state.World.GetTerrain(tx, ty, tz);
		var hardness = _state.World.GetHardness(tx, ty, tz);

		if (!terrain.Solid || hardness == 0)
		{
			_log.Add(LocalizationService.T("dig.invalid_target"));
			return;
		}

		var cellSkills = SkillQuery.GetCellSkills(player);
		InteractionDef? bestSkill = null;
		foreach (var skill in cellSkills)
		{
			if (MiniRPG.Core.World.DigModule.CanApply(skill, terrain, hardness))
			{
				if (bestSkill == null || skill.TerrainMaterial.Length > 0)
					bestSkill = skill;
			}
		}

		if (bestSkill == null)
		{
			_log.Add(LocalizationService.T("dig.cannot_break", ("terrain", GameLocalizer.LocalizeTerrainName(terrain.StringId))));
			return;
		}

		SubmitPlayerAction(TimelinePlayerAction.Dig(dx, dy, bestSkill.Id));
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void PickupGroundItem(Actor player, Item itemInfo)
	{
		var events = InteractionModule.PickupItem(_state, player, itemInfo.InstanceId);
		Dispatch(events);
		_groundPanel.Invalidate();
		_groundPanel.Refresh();
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void OpenChestPanel(Item chestItem)
	{
		_openChestPos = (_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		var chest = EnsureChestPanel();
		chest.Open(chestItem);
		_panels.PushFocus(chest);
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void CloseChestPanel()
	{
		if (_chestPanel?.CurrentChest != null)
			PersistOpenChestState(_chestPanel.CurrentChest);

		_openChestPos = null;
		if (_chestPanel != null)
		{
			_chestPanel.Close();
			_panels.OnPanelClosed(_chestPanel);
		}
		_groundPanel.Invalidate();
		_groundPanel.Refresh();
		FlushMap();
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void CheckChestRange()
	{
		if (_openChestPos == null || _chestPanel == null || !_chestPanel.Visible) return;
		var (cx, cy, _) = _openChestPos.Value;
		var dist = Math.Max(Math.Abs(_state.PlayerX - cx), Math.Abs(_state.PlayerY - cy));
		if (dist > 1)
		{
			_log.Add(LocalizationService.T("log.chest.out_of_range"));
			CloseChestPanel();
		}
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void OpenPutIntoChestSelection(Item chestItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var inv = InventoryModule.List(player);
		if (inv.Count == 0)
		{
			_log.Add(LocalizationService.T("ui.inventory.empty"));
			return;
		}

		var sb = new StringBuilder(LocalizationService.T("ui.chest.choose_put_item"));
		for (var i = 0; i < inv.Count; i++)
		{
			var (_, item) = inv[i];
			var eqMark = item.Equipped ? "[E]" : "";
			sb.Append($"  [{i + 1}] {eqMark}{ItemFormatHelper.GetDisplayName(_state, item)}");
		}
		sb.Append(LocalizationService.T("ui.selection.cancel_option"));
		_log.Add(sb.ToString());

		var chest = EnsureChestPanel();
		_inputModule.EnterSelection(n =>
		{
			if (n == 0) { _log.Add(LocalizationService.T("ui.selection.canceled")); _panels.SetFocus(chest); return; }
			if (n < 1 || n > inv.Count) { _log.Add(LocalizationService.T("ui.selection.invalid")); _panels.SetFocus(chest); return; }
			var (invIdx, item) = inv[n - 1];
			if (item.Equipped)
			{
				_log.Add(LocalizationService.T("log.inventory.unequip_first", ("item", ItemFormatHelper.GetDisplayName(_state, item))));
				_panels.SetFocus(chest);
				return;
			}
			var removed = InventoryModule.RemoveAt(player, invIdx);
			if (removed != null)
			{
				chestItem.Contents!.Add(removed);
				PersistOpenChestState(chestItem);
				_log.Add(LocalizationService.T(
					"log.chest.put_item",
					("item", ItemFormatHelper.GetDisplayName(_state, removed)),
					("chest", ItemFormatHelper.GetDisplayName(_state, chestItem))));
			}
			_panels.SetFocus(chest);
			chest.Refresh();
			FlushMap();
		}, () => { _log.Add(LocalizationService.T("ui.selection.canceled")); _panels.SetFocus(chest); });
	}

	private void PersistOpenChestState(Item chestItem)
	{
		if (_state.World == null || _openChestPos == null)
			return;

		var (x, y, z) = _openChestPos.Value;
		_state.World.UpdateGroundItem(x, y, z, chestItem);
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	// REVIEW: Selection blocks turn progression today, so capturing player/target here is acceptable.
	private void ShowInteractionsFor(Actor player, Actor target)
	{
		var interactions = GetNonCombatInteractions(player, target);
		if (interactions.Count == 0)
		{
			_log.Add(LocalizationService.T("ui.interaction.none_nearby"));
			return;
		}

		var options = new List<(string Name, Action Execute)>();
		foreach (var def in interactions)
		{
			var d = def;
			options.Add((d.Name, () =>
			{
				var events = InteractionModule.Execute(_state, player, target, d);
				Dispatch(events);
				FlushMap();
			}));
		}
		options.Add((LocalizationService.T("ui.interaction.nothing"), () => _log.Add(LocalizationService.T("ui.interaction.walk_away"))));

		if (options.Count == 1)
		{
			options[0].Execute();
			return;
		}

		var sb = new StringBuilder($"{IdentificationModule.GetActorDisplayName(_state, target)}：");
		for (var i = 0; i < options.Count; i++)
			sb.Append($"  [{i + 1}] {options[i].Name}");
		_log.Add(sb.ToString());

		_inputModule.EnterSelection(n =>
		{
			if (n < 1 || n > options.Count) { _log.Add(LocalizationService.T("ui.selection.invalid")); return; }
			options[n - 1].Execute();
		}, () => _log.Add(LocalizationService.T("ui.selection.canceled")));
	}

private static List<InteractionDef> GetNonCombatInteractions(Actor player, Actor target) =>
	InteractionModule
		.GetInteractions(player, target, InteractionDefs.All)
		.FindAll(def => !string.Equals(def.Category, "combat", StringComparison.Ordinal));

	// ══════════════════════════════════════════════════════
	//  看海模式（AI 自动控制玩家）

	/// <summary>切换看海模式：设置玩家 BrainId 为 "simple" 或 null。</summary>
	// REVIEW: Watch mode still toggles BrainId directly on the player actor.
	private void ToggleWatchMode()
	{
		SetWatchModeEnabled(!_watchModeEnabled);
	}

	private void SetWatchModeEnabled(bool enabled, bool emitLog = true)
	{
		_watchModeEnabled = enabled;
		_watchTimer = 0;

		var player = ActorModule.GetPlayer(_state);
		if (player != null)
			player.BrainId = enabled ? "simple" : null;

		SyncSettingsUiState();
		SyncTimelineAutoAdvanceState(emitStatusLog: emitLog);
		if (!emitLog)
			return;

		_log.Add(enabled
			? LocalizationService.T("log.watch_mode.on")
			: LocalizationService.T("log.watch_mode.off"));
	}


	/// <summary>看海模式每 0.15 秒推进一次回合。</summary>
	private void WatchModeTick()
	{
		AdvanceTimelineAutoStep();
	}

	// ══════════════════════════════════════════════════════
	//  移动
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 处理方向键移动：玩家已死时按方向键返回主菜单，
	/// 否则执行 TryMove → Tick → Dispatch → FlushMap。
	/// </summary>
	private void DoMove(int dx, int dy)
	{
		SubmitPlayerAction(TimelinePlayerAction.Move(dx, dy));
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
		foreach (var e in events)
		{
			_log.DispatchEvent(e, _state);

			switch (e.Type)
			{
				case "combat_attack":
					if (e.InitiatorId == _state.PlayerId && e.TargetId != null)
					{
						var attackedTarget = ActorModule.GetById(_state, e.TargetId);
						if (attackedTarget != null)
							SetCurrentTarget(attackedTarget, PlayerTargetSource.Explicit);
					}
					PlayCombatFx(e);
					if (e.InitiatorId == _state.PlayerId)
						ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Attack_1");
					if (e.TargetId == _state.PlayerId)
						ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Pain");
					break;
				case "combat_block":
					PlayCombatFx(e);
					break;
				case "weather_lightning_strike":
					PlayWeatherLightningFx(e);
					if (e.TargetId == _state.PlayerId)
						ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Pain");
					break;
				case "actor_killed" when e.TargetId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
					HandlePlayerDeath("killed");
					break;
				case "death_blood_loss" when e.TargetId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
					HandlePlayerDeath("blood_loss");
					break;
				case "death_infection" when e.TargetId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
					HandlePlayerDeath("infection");
					break;
				case "actor_killed":
					_state.KillCount++;
					_combatUI.HandleActorKilled(e);
					break;
				case "actor_incapacitated" when e.TargetId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
					HandlePlayerDeath("incapacitated");
					break;
				case "actor_moved" when e.InitiatorId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Walk");
					break;
				case "interaction":
					DispatchInteraction(e);
					break;
				case "rest_completed" when e.TargetId == _state.PlayerId:
					_playerRestModeActive = false;
					break;
			case "item_picked_up" or "item_dropped":
				_groundPanel.Invalidate();
				break;
			}
		}

		_incidentAlerts.ProcessEvents(events);
	}

	/// <summary>交互事件路由：流程类（trade/combat）转发到 UI Module，日志类由 LogModule 处理。</summary>
	private void DispatchInteraction(GameEvent e)
	{
		switch (e.EffectType)
		{
			case "trade":
				if (e.TargetId != null && ActorModule.GetById(_state, e.TargetId) != null)
					CloseDialogPanel();
				EnsureTradeUI().OpenTradeMenu(e);
				break;
			case "talk":
				if (e.TargetId != null)
				{
					var talkTarget = ActorModule.GetById(_state, e.TargetId);
					if (talkTarget != null)
					{
						CloseTradePanel();
						EnsureDialogUI().OpenDialog(talkTarget);
						break;
					}
				}
				_log.Add(LocalizationService.T("dialog.fallback.line", ("target", e.TargetActorName)));
				break;
			case "tame":
				_log.Add(LocalizationService.T("log.interaction.tame_success", ("target", e.TargetActorName)));
				break;
			default:
				_log.Add(LocalizationService.T("log.interaction.default", ("name", e.InteractionName), ("target", e.TargetActorName)));
				break;
		}
	}


	// ══════════════════════════════════════════════════════
	//  楼梯（上行 / 下行）
	// ══════════════════════════════════════════════════════

	private async void DoEnterStairs()
	{
		if (_busyOperationActive)
			return;

		if (!TryFindNearbyStairs(out var goDown))
		{
			_log.Add(LocalizationService.T("ui.interaction.none_nearby"));
			return;
		}

		BeginBusyOperation("ui.loading.floor.prepare", 18f / 100f);
		try
		{
			await ShowBusyOperationStageAsync("ui.loading.floor.prepare", 0.18f);
			if (ChestOpen) CloseChestPanel();

			await ShowBusyOperationStageAsync("ui.loading.floor.load", 0.76f);
			_session.ChangeFloor(goDown);

			await ShowBusyOperationStageAsync("ui.loading.floor.finalize", 0.95f);
			_log.Add(LocalizationService.T(
				goDown ? "log.floor.enter_down" : "log.floor.enter_up",
				("floor", _state.PlayerZ)));
			FlushMap();
		}
		finally
		{
			EndBusyOperation();
		}
	}

	private bool TryFindNearbyStairs(out bool goDown)
	{
		var px = _state.PlayerX;
		var py = _state.PlayerY;
		var dirs = new (int Dx, int Dy)[] { (0, 0), (0, -1), (0, 1), (-1, 0), (1, 0) };

		foreach (var (dx, dy) in dirs)
		{
			if (MapModule.HasFixture(_state, px + dx, py + dy, Entities.StairDown))
			{
				goDown = true;
				return true;
			}

			if (MapModule.HasFixture(_state, px + dx, py + dy, Entities.StairUp))
			{
				goDown = false;
				return true;
			}
		}

		goDown = false;
		return false;
	}

	private void DoLook() => _log.Add(LookModule.BuildLookText(_state, _fogTracker));

	// ══════════════════════════════════════════════════════
	//  存档 / 读档
	// ══════════════════════════════════════════════════════

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void DoSave(string path, string label)
	{
		_session.SaveGame(path);
		_log.Add(LocalizationService.T("log.save.saved", ("label", label)));
	}

	/// <summary>从指定路径加载存档。加载后重建世界并确保玩家 Actor 存在。</summary>
	private async void DoLoad(string path, string label)
	{
		if (_busyOperationActive)
			return;

		BeginBusyOperation("ui.loading.save.prepare", 16f / 100f);
		try
		{
			await ShowBusyOperationStageAsync("ui.loading.save.prepare", 0.16f);
			ExitMapEditor(silent: true);
			CloseSaveNameDialog();

			await ShowBusyOperationStageAsync("ui.loading.save.load", 0.76f);
			var status = _session.LoadGame(path);
			if (status != SaveLoadStatus.Success)
			{
				LogLoadFailure(status, label);
				return;
			}

			await ShowBusyOperationStageAsync("ui.loading.save.finalize", 0.95f);
			ClearArmedSkill(restoreFocus: false);
			ClearPlayerTargeting();
			_playerRestModeActive = false;
			ResetThreatHud();
			RefreshPlayerCharacterVisual();
			SyncSettingsUiState();
			SyncTimelineAutoAdvanceState();
			RefreshLocalizedUi(clearLogs: false);
			_log.Add(LocalizationService.T("log.save.loaded", ("label", label), ("floor", _state.PlayerZ)));
			FlushMap();
		}
		finally
		{
			EndBusyOperation();
		}
	}

	private void LogLoadFailure(SaveLoadStatus status, string label)
	{
		_log.Add(BuildLoadFailureMessage(status, label));
	}

	private string BuildLoadFailureMessage(SaveLoadStatus status, string label)
	{
		var key = status switch
		{
			SaveLoadStatus.Incompatible => "log.save.incompatible",
			_ => "log.save.not_found",
		};
		return LocalizationService.T(key, ("label", label));
	}

	private void SetWorldManagerStatus(string message, bool isError)
	{
	}

	private void ClearWorldManagerStatus()
	{
	}

	private (string? WorldId, string? CharacterId) ResolvePreferredWorldManagerSelection()
	{
		return (null, null);
	}

	private void SaveCurrentSessionAndLoadFromWorldManager(string label, Func<SaveLoadStatus> loadAction)
	{
		_mainAppFlowCoordinator.LoadFromWorldManager(label, loadAction);
	}

	private void OpenSwitchConfirmation(
		string title,
		string message,
		IReadOnlyList<ConfirmDialogAction> actions,
		int defaultActionIndex,
		Action? onSaveAndSwitch = null,
		Action? onSwitch = null)
	{
		throw new NotSupportedException("OpenSwitchConfirmation is handled by MainAppFlowCoordinator.");
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

	private void RequestWorldManagerLoad(string targetLabel, Func<SaveLoadStatus> loadAction)
	{
		_mainAppFlowCoordinator.LoadFromWorldManager(targetLabel, loadAction);
	}

	private async void LoadFromWorldManager(string label, Func<SaveLoadStatus> loadAction)
	{
		_mainAppFlowCoordinator.LoadFromWorldManager(label, loadAction);
		await Task.CompletedTask;
	}

	private void HandleWorldSettingsConfirmed(string worldName, WorldSettings settings)
		=> _mainAppFlowCoordinator.HandleWorldSettingsConfirmed(worldName, settings);

	private void ToggleRender()
	{
		if (_mapRender == null)
			return;

		var msg = _mapRender.ToggleRenderMode();
		if (_session.GameStarted && !_menu.InMenu && msg != null)
			_log.Add(msg);
		if (!_menu.InMenu) FlushMap();
	}

	/// <summary>立即刷新地图，标记 UI 面板为脏（由 _Process 统一驱动刷新）。</summary>
	private void FlushMap()
	{
		if (_mapRender == null)
			return;

		_mapRender.InspectWorldCell = _inspectModeActive ? _inspectWorldCell : null;
		_mapRender.SetEditorView(
			MapEditorActive,
			MapEditorActive ? _mapEditor.CameraX : _state.PlayerX,
			MapEditorActive ? _mapEditor.CameraY : _state.PlayerY,
			_state.PlayerZ,
			MapEditorActive ? _mapEditor.HoverWorld : null);
		_mapRender.Flush();
		MarkUIDirty();
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void MarkUIDirty()
	{
		_statusPanelModule.Dirty = true;
		_skillBarDirty = true;
		_skillMgr.Dirty = true;
		if (_inventoryPanel.Visible) _inventoryPanel.Dirty = true;
		_groundPanel.Dirty = true;
		_turnPanelModule.Dirty = true;
		_debugPanelController.MarkDirty();
		_weatherLabPanelController?.MarkDirty();
		if (_actorInspectPanel?.Visible == true) _actorInspectPanel.Dirty = true;
	}

	/// <summary>在 _Process 中统一驱动脏面板刷新，避免单帧重复刷新。</summary>
	private void ProcessDirtyPanels()
	{
		if (_statusPanelModule.Dirty && _statusPanelModule.PanelNode.Visible)
		{
			var player = ActorModule.GetPlayer(_state);
			_statusPanelModule.Refresh(_state, player, _state.PlayerZ, _state.Turn);
		}
		if (_turnPanelModule.Dirty && _turnPanelModule.PanelNode.Visible)
			_turnPanelModule.FlushIfDirty(_state, PlayerDead, _watchModeEnabled, _mapRender?.IsIsometricMode ?? true);
		if (_skillBarDirty && _skillBar.Visible)
		{
			_skillBar.Refresh(ActorModule.GetPlayer(_state));
			_skillBarDirty = false;
		}
		if (_skillMgr.Visible)
			_skillMgr.FlushIfDirty();
		if (_inventoryPanel.Visible && _inventoryPanel.Dirty)
			_inventoryPanel.FlushIfDirty();
		if (_groundPanel.Dirty)
			_groundPanel.FlushIfDirty();
		_debugPanelController.FlushIfDirty();
		_weatherLabPanelController?.FlushIfDirty();
		if (_actorInspectPanel?.Visible == true && _actorInspectPanel.Dirty)
			RefreshActorInspectPanel();
	}

	// ══════════════════════════════════════════════════════
	//  状态面板 toggle
	// ══════════════════════════════════════════════════════

	private void ToggleStatusPanel()
	{
		var node = _statusPanelModule.PanelNode;
		if (node.Visible)
		{
			CloseStatusPanel();
		}
		else
		{
			node.Visible = true;
			_panels.PushFocus(_statusPanelModule);
		}

		if (node.Visible && _statusPanelModule.Dirty)
		{
			var player = ActorModule.GetPlayer(_state);
			_statusPanelModule.Refresh(_state, player, _state.PlayerZ, _state.Turn);
		}
	}

	private void CloseStatusPanel()
	{
		if (!_statusPanelModule.PanelNode.Visible)
			return;

		_statusPanelModule.PanelNode.Visible = false;
		_panels.OnPanelClosed(_statusPanelModule);
	}

	// ══════════════════════════════════════════════════════
	//  技能管理面板

	private void ToggleSkillBarPanel()
	{
		if (_skillBar.Visible)
		{
			CloseSkillBarPanel();
			return;
		}

		_skillBar.Open(ActorModule.GetPlayer(_state));
		_panels.PushFocus(_skillBar);
	}

	private void CloseSkillBarPanel()
	{
		if (!_skillBar.Visible)
			return;

		_skillBar.Close();
		_panels.OnPanelClosed(_skillBar);
	}

	private void ToggleSkillManager()
	{
		if (_skillMgr.Visible)
		{
			CloseSkillManagerPanel();
		}
		else
		{
			var player = ActorModule.GetPlayer(_state);
			_skillMgr.State = _state;
			_skillMgr.Open(player);
			_panels.PushFocus(_skillMgr);
		}
	}

	private void CloseSkillManagerPanel()
	{
		if (!_skillMgr.Visible)
			return;

		_skillMgr.Close();
		_panels.OnPanelClosed(_skillMgr);
	}


	// ══════════════════════════════════════════════════════
	//  任务面板
	// ══════════════════════════════════════════════════════

	private void ToggleQuestPanel()
	{
		var quest = EnsureQuestPanel();
		if (quest.Visible)
		{
			CloseQuestPanel();
		}
		else
		{
			quest.Open(_state);
			_panels.PushFocus(quest);
		}
	}

	private void CloseQuestPanel()
	{
		if (_questPanel == null || !_questPanel.Visible)
			return;

		_questPanel.Close();
		_panels.OnPanelClosed(_questPanel);
	}

	private void ToggleDebugPanel() => _debugPanelController.Toggle();

	private void CloseDebugPanel() => _debugPanelController.Close();


	// ══════════════════════════════════════════════════════
	//  背包面板
	// ══════════════════════════════════════════════════════

	private void ToggleInventory()
	{
		if (_inventoryPanel.Visible)
		{
			CloseInventoryPanel();
		}
		else
		{
			_inventoryPanel.Visible = true;
			_panels.PushFocus(_inventoryPanel);
		}
		FlushMap();
	}

	private void CloseInventoryPanel()
	{
		if (!_inventoryPanel.Visible)
			return;

		_inventoryPanel.Visible = false;
		_panels.OnPanelClosed(_inventoryPanel);
	}

	private void CloseTradePanel()
	{
		if (_tradeUI != null && _tradeUI.InTrade)
		{
			_tradeUI.CloseTrade();
			return;
		}

		if (_tradePanel == null || !_tradePanel.Visible)
			return;

		_tradePanel.Close();
		_panels.OnPanelClosed(_tradePanel);
	}

	private void CloseDialogPanel()
	{
		if (_dialogUI != null && _dialogUI.InDialog)
		{
			_dialogUI.CloseDialog();
			return;
		}

		if (_dialogPanel == null || !_dialogPanel.Visible)
			return;

		_dialogPanel.Close();
		_panels.OnPanelClosed(_dialogPanel);
	}


	// ══════════════════════════════════════════════════════
	//  小地图 / 大地图
	// ══════════════════════════════════════════════════════

	private void ToggleMinimap()
	{
		if (_mapRender == null)
			return;

		var msg = _mapRender.ToggleMinimap();
		if (msg.Length > 0) _log.Add(msg);
		FlushMap();
	}

	private void ToggleFogMap()
	{
		if (_mapRender == null)
			return;

		_log.Add(_mapRender.ToggleFogMap());
		FlushMap();
	}

	private void CenterFogMap()
	{
		if (_mapRender == null)
			return;

		_mapRender.CenterFogMap();
		FlushMap();
	}

	private void RefreshAllBorders() => _panels.RefreshBorders();

	private void PrimeTimelineStatusLog(TimelineDebugSnapshot snapshot)
	{
		_timelineStatusLogPrimed = true;
		_lastTimelineLockReason = snapshot.InputLockedReason;
	}

	private void ResetTimelineStatusLog()
	{
		_timelineStatusLogPrimed = false;
		_lastTimelineLockReason = TimelineInputLockReason.None;
	}

	private void UpdateTimelineStatusLog(TimelineDebugSnapshot snapshot)
	{
		if (!_timelineStatusLogPrimed)
		{
			PrimeTimelineStatusLog(snapshot);
			return;
		}

		var nextReason = snapshot.InputLockedReason;
		if (nextReason == _lastTimelineLockReason)
			return;

		if (nextReason == TimelineInputLockReason.None)
		{
			if (_lastTimelineLockReason is TimelineInputLockReason.OtherActorsActing or TimelineInputLockReason.WatchMode)
				_log.Add(LocalizationService.T("log.timeline.player_turn"));
			_lastTimelineLockReason = nextReason;
			return;
		}

		var logKey = nextReason switch
		{
			TimelineInputLockReason.OtherActorsActing => "log.timeline.locked.auto",
			TimelineInputLockReason.WatchMode => "log.timeline.locked.watch",
			_ => null,
		};
		if (logKey != null)
			_log.Add(LocalizationService.T(logKey));

		_lastTimelineLockReason = nextReason;
	}
}
