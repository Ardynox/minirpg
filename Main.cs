using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using MiniRPG.Core;
using MiniRPG.Core.World;
using MiniRPG.Module;

namespace MiniRPG;

/// <summary>
/// 游戏入口节点，实现 IGameUI 接口，同时承担以下职责：
///   1. 输入路由：接收键盘事件 → 转换为命令字符串 → 分派到对应逻辑
///   2. 事件分发：Core 层产出 GameEvent 列表 → 翻译为日志/UI 动作
///   3. 渲染调度：以 MapFps 帧率定时刷新地图 + 状态面板
///   4. 菜单管理：主菜单、设置面板、选择模式的状态切换
///   5. 楼层切换：上下楼梯的流程编排
///   6. 看海模式：定时驱动 TurnModule.TickWatchMode
///
/// 设计约束：
///   - Main 只做「胶水」和「副作用」，不包含游戏核心逻辑。
///   - 核心逻辑在 Core/ 层的各 Module 中，Main 通过调用 Module 静态方法驱动。
///   - Main 持有唯一的 GameState 实例，所有 Module 共享同一数据源。
/// </summary>
// REVIEW: Main 类承担了太多职责（约 850 行），违反单一职责原则。
//         输入路由、事件分发、楼层切换、菜单管理、渲染等可以进一步拆分。
//         虽然作为 Godot 入口节点有一定集中的必要性，但内部逻辑可以委托给更多子模块。
public partial class Main : Node, IGameUI, InventoryPanelModule.IHost,
	GroundPanelModule.IHost, ChestPanelModule.IHost
{
	private const int MaxLogLines = 30;
	private const double MapFps = 10.0;
	private const int ViewW = 21;
	private const int ViewH = 11;

	private static readonly string SaveDir =
		System.IO.Path.Combine(OS.GetUserDataDir(), "save");
	private static string QuickSavePath =>
		System.IO.Path.Combine(SaveDir, "quicksave.json");
	private static string ManualSavePath =>
		System.IO.Path.Combine(SaveDir, "save.json");

	private readonly GameState _state = new();
	private readonly List<string> _logLines = [];
	private bool _settingsOpen;
	private bool _inMenu = true;
	private bool _gameStarted;
	private Action<int>? _selectionCallback;

	private VBoxContainer _ui = null!;
	private PanelContainer _mapPanelNode = null!;
	private RichTextLabel _mapText = null!;
	private RichTextLabel _logPanel = null!;
	private PanelContainer _settingsPanel = null!;
	private PanelContainer _mainMenu = null!;
	private Button _continueBtn = null!;
	private Button _settingSaveBtn = null!;
	private Button _settingLoadBtn = null!;
	private Button _settingBackToMenuBtn = null!;
	private Button _watchModeBtn = null!;
	private InputModule _inputModule = null!;
	private RenderModule _renderModule = null!;
	private IViewMode _viewMode = null!;
	private double _watchTimer;

	private FogOfWarTracker _fogTracker = null!;
	private MinimapModule _minimapModule = null!;
	private FogMapModule _fogMapModule = null!;

	private StatusPanelModule _statusPanelModule = null!;

	private SkillPanelModule _skillPanel = null!;
	private InventoryPanelModule _inventoryPanel = null!;
	private GroundPanelModule _groundPanel = null!;
	private ChestPanelModule _chestPanel = null!;
	private PanelContainer _invPanelNode = null!;
	private PanelContainer _chestPanelNode = null!;
	private PanelContainer _statusPanelNode = null!;
	private PanelContainer _skillPanelNode = null!;
	private PanelContainer _groundPanelNode = null!;
	private PanelContainer? _focusedPanelNode;
	private (int x, int y)? _openChestPos;

	private bool StatusOpen => _inputModule?.Focus == InputFocus.Status;
	private bool InventoryOpen => _inputModule?.Focus == InputFocus.Inventory;
	private bool ChestOpen => _inputModule?.Focus == InputFocus.Chest;

	private CombatUIModule _combatUI = null!;
	private TradeUIModule _tradeUI = null!;
	private InventoryUIModule _inventoryUI = null!;

	// ══════════════════════════════════════════════════════
	//  IGameUI 接口实现
	// ══════════════════════════════════════════════════════

	public GameState State => _state;
	public bool PlayerDead { get; set; }
	private int _killCount;

	void IGameUI.AddLog(string msg) => AddLog(msg);
	void IGameUI.EnterSelection(Action<int> callback) => EnterSelection(callback);
	void IGameUI.CancelSelection() => CancelSelection();
	void IGameUI.FlushMap() => FlushMap();
	void IGameUI.Dispatch(List<GameEvent> events) => Dispatch(events);

	void InventoryPanelModule.IHost.AddLog(string msg) => AddLog(msg);
	void InventoryPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void InventoryPanelModule.IHost.FlushMap() => FlushMap();
	GameState InventoryPanelModule.IHost.State => _state;
	bool InventoryPanelModule.IHost.HasFocus => InventoryOpen;
	void InventoryPanelModule.IHost.OpenChestFromInventory(Item chestItem) => OpenChestPanel(chestItem);

	void GroundPanelModule.IHost.AddLog(string msg) => AddLog(msg);
	void GroundPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void GroundPanelModule.IHost.FlushMap() => FlushMap();
	GameState GroundPanelModule.IHost.State => _state;
	void GroundPanelModule.IHost.OpenChestPanel(Item chestItem) => OpenChestPanel(chestItem);
	void GroundPanelModule.IHost.PickupGroundItem(Item item) => PickupGroundItem(ActorModule.GetPlayer(_state)!, item);

	void ChestPanelModule.IHost.AddLog(string msg) => AddLog(msg);
	void ChestPanelModule.IHost.FlushMap() => FlushMap();
	GameState ChestPanelModule.IHost.State => _state;
	void ChestPanelModule.IHost.CloseChestPanel() => CloseChestPanel();
	void ChestPanelModule.IHost.OpenPutIntoChestSelection(Item chestItem) => OpenPutIntoChestSelection(chestItem);

	/// <summary>
	/// 统一的玩家死亡处理：显示死亡战绩 → 冻结输入 → 等待任意键返回主菜单。
	/// </summary>
	public void HandlePlayerDeath(string reason)
	{
		if (PlayerDead) return;
		PlayerDead = true;

		if (_state.WatchMode)
		{
			_state.WatchMode = false;
			var player = ActorModule.GetPlayer(_state);
			if (player != null) player.BrainId = null;
			_watchModeBtn.Text = "看海模式：关闭";
		}

		CancelSelection();

		AddLog("");
		AddLog(reason == "incapacitated"
			? "══════ 😵 你失去了意识 ══════"
			: "══════ 💀 你死了 ══════");
		AddLog($"  回合: {_state.Turn}");
		AddLog($"  到达: 第 {_state.PlayerZ} 层");
		AddLog($"  击杀: {_killCount}");
		var player2 = ActorModule.GetPlayer(_state);
		if (player2 != null) AddLog($"  金币: {player2.Gold}G");
		AddLog("════════════════════════════");
		AddLog("按任意键返回主菜单……");
	}

	// ══════════════════════════════════════════════════════
	//  Godot 生命周期
	// ══════════════════════════════════════════════════════

	/// <summary>节点就绪：加载预设数据、获取 UI 节点引用、注册信号、显示主菜单。</summary>
	public override void _Ready()
	{
		PresetDB.Load();
		TerrainRegistry.Load("res://Data/terrains.json");
		_viewMode = new SingleLayerViewMode();
		_fogTracker = new FogOfWarTracker();
		_minimapModule = new MinimapModule(_fogTracker);
		_fogMapModule = new FogMapModule(_fogTracker);

		_ui = GetNode<VBoxContainer>("UI");
		_mapPanelNode = GetNode<PanelContainer>("UI/TopRow/MapPanel");
		_mapText = GetNode<RichTextLabel>("UI/TopRow/MapPanel/MarginContainer/MapText");
		_logPanel = GetNode<RichTextLabel>("UI/LogPanel");
		_settingsPanel = GetNode<PanelContainer>("SettingsPanel");
		_mainMenu = GetNode<PanelContainer>("MainMenu");
		_continueBtn = GetNode<Button>("MainMenu/Center/VBox/ContinueBtn");
		var lineEdit = GetNode<LineEdit>("UI/InputBar");

		_statusPanelNode = GetNode<PanelContainer>("UI/TopRow/StatusPanel");
		_statusPanelModule = new StatusPanelModule(_statusPanelNode);

		_skillPanelNode = GetNode<PanelContainer>("UI/TopRow/SkillPanel");
		_skillPanel = new SkillPanelModule(_skillPanelNode);
		_invPanelNode = GetNode<PanelContainer>("UI/TopRow/InventoryPanel");
		_inventoryPanel = new InventoryPanelModule(_invPanelNode, this);
		_groundPanelNode = GetNode<PanelContainer>("UI/GroundPanel");
		_groundPanel = new GroundPanelModule(_groundPanelNode, this);
		_chestPanelNode = GetNode<PanelContainer>("UI/TopRow/ChestPanel");
		_chestPanel = new ChestPanelModule(_chestPanelNode, this);


		_renderModule = new RenderModule();
		_renderModule.ApplyFont(_mapText);

		_inputModule = new InputModule(lineEdit);
		_inputModule.CommandReceived += OnCommand;

		_combatUI = new CombatUIModule(this);
		_tradeUI = new TradeUIModule(this);
		_inventoryUI = new InventoryUIModule(this);

		_settingSaveBtn = GetNode<Button>("SettingsPanel/VBox/SaveBtn");
		_settingLoadBtn = GetNode<Button>("SettingsPanel/VBox/LoadBtn");
		_settingBackToMenuBtn = GetNode<Button>("SettingsPanel/VBox/BackToMenuBtn");

		GetNode<Button>("SettingsPanel/VBox/RenderToggle").Pressed += ToggleRender;
		_watchModeBtn = GetNode<Button>("SettingsPanel/VBox/WatchModeToggle");
		_watchModeBtn.Pressed += ToggleWatchMode;
		_settingSaveBtn.Pressed += () => DoSave(ManualSavePath);
		_settingLoadBtn.Pressed += () => DoLoad(ManualSavePath);
		_settingBackToMenuBtn.Pressed += BackToMenu;
		GetNode<Button>("SettingsPanel/VBox/CloseBtn").Pressed += CloseSettings;

		_continueBtn.Pressed += MenuContinue;
		GetNode<Button>("MainMenu/Center/VBox/NewGameBtn").Pressed += MenuNewGame;
		GetNode<Button>("MainMenu/Center/VBox/LoadGameBtn").Pressed += MenuLoadGame;
		GetNode<Button>("MainMenu/Center/VBox/SettingsBtn").Pressed += MenuSettings;
		GetNode<Button>("MainMenu/Center/VBox/QuitBtn").Pressed += MenuQuit;

		ShowMainMenu();
	}

	/// <summary>每帧更新：驱动看海模式自动推进。</summary>
	public override void _Process(double delta)
	{
		if (_inMenu) return;

		if (_state.WatchMode && !PlayerDead)
		{
			_watchTimer += delta;
			if (_watchTimer >= 0.15)
			{
				_watchTimer = 0;
				WatchModeTick();
			}
		}
	}

	/// <summary>拦截未处理的键盘事件，转发给 InputModule 处理。</summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (_inMenu) return;
		if (@event is InputEventKey key && _inputModule.HandleKeyInput(key))
			GetViewport().SetInputAsHandled();
	}

	/// <summary>全局输入：检测鼠标点击落在哪个面板内，切换焦点。</summary>
	public override void _Input(InputEvent @event)
	{
		if (_inMenu || !_gameStarted) return;
		if (@event is not InputEventMouseButton mb || !mb.Pressed) return;
		if (mb.ButtonIndex != MouseButton.Left) return;

		var pos = mb.GlobalPosition;
		var hit = HitTestPanel(pos);
		if (hit == null) return;

		var cur = _inputModule.Focus;

		if (hit == _invPanelNode)
		{
			if (cur == InputFocus.Inventory) return;
			if (!_inventoryPanel.Visible)
			{
				_inventoryPanel.Visible = true;
				_inputModule.EnterInventoryMode();
				FlushMap();
			}
			else
			{
				_inputModule.EnterInventoryMode();
				_inventoryPanel.Refresh();
			}
			_focusedPanelNode = _invPanelNode;
		}
		else if (hit == _chestPanelNode)
		{
			if (cur == InputFocus.Chest) return;
			if (_chestPanel.Visible)
			{
				_inputModule.EnterChestMode();
				_chestPanel.Refresh();
			}
			_focusedPanelNode = _chestPanelNode;
		}
		else if (hit == _statusPanelNode)
		{
			if (cur != InputFocus.Status)
				_inputModule.EnterStatusMode();
			_focusedPanelNode = _statusPanelNode;
		}
		else
		{
			_focusedPanelNode = hit;
			if (cur != InputFocus.Action)
			{
				_inputModule.EnterActionMode();
				FlushMap();
			}
		}
		RefreshAllBorders();
	}

	private PanelContainer? HitTestPanel(Vector2 globalPos)
	{
		PanelContainer?[] panels =
			[_mapPanelNode, _statusPanelNode, _skillPanelNode, _invPanelNode, _groundPanelNode, _chestPanelNode];
		foreach (var p in panels)
		{
			if (p == null || !p.Visible) continue;
			var rect = p.GetGlobalRect();
			if (rect.HasPoint(globalPos)) return p;
		}
		return null;
	}

	// ══════════════════════════════════════════════════════
	//  主菜单
	// ══════════════════════════════════════════════════════

	/// <summary>显示主菜单，隐藏游戏 UI。检查是否存在存档来决定「继续」按钮可见性。</summary>
	private void ShowMainMenu()
	{
		_inMenu = true;
		_mainMenu.Visible = true;
		_ui.Visible = false;
		_settingsPanel.Visible = false;
		_settingsOpen = false;
		CancelSelection();

		var hasSave = System.IO.File.Exists(QuickSavePath)
			|| System.IO.File.Exists(ManualSavePath);
		_continueBtn.Visible = hasSave;
	}

	/// <summary>从主菜单切换到游戏界面。</summary>
	private void EnterGame()
	{
		_inMenu = false;
		_mainMenu.Visible = false;
		_ui.Visible = true;
		_inputModule.EnterActionMode();
		FlushMap();
	}

	/// <summary>「继续」按钮：优先加载快速存档，其次手动存档，都失败则新建游戏。</summary>
	private void MenuContinue()
	{
		if (SaveModule.LoadGame(_state, QuickSavePath)
			|| SaveModule.LoadGame(_state, ManualSavePath))
		{
			MapGenModule.InitializeWorld(_state);
			EnsurePlayerActor();
			SyncViewMode();
			_logLines.Clear();
			AddLog("存档已加载 📂");
		}
		else
		{
			StartNewGame();
		}
		ShowGameHints();
		EnterGame();
	}

	/// <summary>「新游戏」按钮。</summary>
	private void MenuNewGame()
	{
		StartNewGame();
		ShowGameHints();
		EnterGame();
	}

	/// <summary>「加载游戏」按钮：优先手动存档，其次快速存档，都失败则新建游戏。</summary>
	private void MenuLoadGame()
	{
		if (SaveModule.LoadGame(_state, ManualSavePath))
		{
			MapGenModule.InitializeWorld(_state);
			EnsurePlayerActor();
			SyncViewMode();
			_logLines.Clear();
			AddLog("存档已加载 📂");
		}
		else if (SaveModule.LoadGame(_state, QuickSavePath))
		{
			MapGenModule.InitializeWorld(_state);
			EnsurePlayerActor();
			SyncViewMode();
			_logLines.Clear();
			AddLog("快速存档已加载 📂");
		}
		else
		{
			_logLines.Clear();
			AddLog("未找到存档，已创建新游戏");
			StartNewGame();
		}
		ShowGameHints();
		EnterGame();
	}

	private bool _settingsFromMenu;

	/// <summary>从主菜单打开设置面板。</summary>
	private void MenuSettings()
	{
		_settingsFromMenu = true;
		_settingsPanel.Visible = true;
		_mainMenu.Visible = false;
		UpdateSettingsContext();
	}

	private void MenuQuit() => GetTree().Quit();

	/// <summary>「返回主菜单」按钮：自动快速存档后返回主菜单。</summary>
	private void BackToMenu()
	{
		_settingsOpen = false;
		_settingsPanel.Visible = false;
		if (!_gameStarted) { ShowMainMenu(); return; }
		DoSave(QuickSavePath, "快速存档");
		ShowMainMenu();
	}

	/// <summary>重置状态并初始化无限世界。</summary>
	private void StartNewGame()
	{
		_state.Reset();
		_state.WorldSeed = System.Environment.TickCount;
		_logLines.Clear();
		_killCount = 0;
		PlayerDead = false;
		_fogTracker.Clear();
		_fogMapModule.Visible = false;
		_minimapModule.Visible = false;
		_skillPanel.Visible = false;
		_inventoryPanel.Visible = false;
		_chestPanel.Visible = false;
		InitializeWorld();
		_gameStarted = true;
		RefreshAllBorders();
		AddLog("新游戏开始 🗺️");
	}

	/// <summary>在日志中显示操作提示。</summary>
	private void ShowGameHints()
	{
		_gameStarted = true;
		AddLog("WASD 移动 | L 查看 | R 渲染 | ESC 设置");
		AddLog("空格 上下楼 | F 交互 | I 背包 | F5 快存 | F9 快读");
	}

	// ══════════════════════════════════════════════════════
	//  选择模式（数字键选择列表项）
	// ══════════════════════════════════════════════════════

	/// <summary>进入选择模式：InputModule 切换为数字键监听，选中后回调 callback(n)。</summary>
	private void EnterSelection(Action<int> callback)
	{
		_selectionCallback = callback;
		_inputModule.EnterSelectionMode();
	}

	/// <summary>取消选择模式，恢复正常输入。</summary>
	private void CancelSelection()
	{
		_selectionCallback = null;
		if (_inputModule != null)
			_inputModule.EnterActionMode();
	}

	// ══════════════════════════════════════════════════════
	//  命令分发（InputModule 产出命令字符串 → 此处路由到具体逻辑）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 核心命令路由。命令来源：InputModule 的键盘映射或文本输入。
	/// 前缀 ":" 的是快捷键命令，无前缀的是文本命令。
	/// </summary>
	private void OnCommand(string cmd)
	{
		if (PlayerDead)
		{
			PlayerDead = false;
			_killCount = 0;
			ShowMainMenu();
			return;
		}

		if (StatusOpen && cmd.StartsWith(":status_"))
		{
			HandleStatusInput(cmd);
			return;
		}

		if (InventoryOpen && cmd.StartsWith(":inv_"))
		{
			HandleInventoryInput(cmd);
			return;
		}

		if (ChestOpen && cmd.StartsWith(":chest_"))
		{
			HandleChestInput(cmd);
			return;
		}

		if (cmd == ":select_cancel")
		{
			CancelSelection();
			AddLog("已取消");
			return;
		}

		if (cmd.StartsWith(":select_") && _selectionCallback != null)
		{
			if (int.TryParse(cmd[":select_".Length..], out var n))
			{
				var cb = _selectionCallback;
				_selectionCallback = null;
				_inputModule.EnterActionMode();
				cb(n);
			}
			return;
		}

		if (cmd.StartsWith(":dig_") && cmd != ":dig")
		{
			HandleDigDirection(cmd[":dig_".Length..]);
			return;
		}
		if (cmd == ":dir_cancel")
		{
			AddLog("已取消");
			return;
		}

		switch (cmd)
		{
			case ":settings" or "settings":
				if (_fogMapModule.Visible) { _fogMapModule.Visible = false; AddLog("大地图: 关闭"); FlushMap(); return; }
				ToggleSettings(); return;
			case ":quicksave": DoSave(QuickSavePath, "快速存档"); return;
			case ":quickload": DoLoad(QuickSavePath, "快速存档"); return;
			case ":interact" or "interact": DoInteract(); return;
			case ":dig": StartDig(); return;
			case ":inventory": ToggleInventory(); return;
			case ":skills": ToggleSkillPanel(); return;
			case ":render" or "render": ToggleRender(); return;
			case ":status_prev": _statusPanelModule.CycleTab(-1); return;
			case ":status_next": _statusPanelModule.CycleTab(1); return;
			case ":minimap": ToggleMinimap(); return;
			case ":fogmap": ToggleFogMap(); return;
			case ":fogmap_center": CenterFogMap(); return;
		}

		if (_settingsOpen) return;

		if (_fogMapModule.Visible)
		{
			switch (cmd)
			{
				case "w": _fogMapModule.Scroll(0, -1); FlushMap(); break;
				case "s": _fogMapModule.Scroll(0, 1); FlushMap(); break;
				case "a": _fogMapModule.Scroll(-1, 0); FlushMap(); break;
				case "d": _fogMapModule.Scroll(1, 0); FlushMap(); break;
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
			case "save": DoSave(ManualSavePath); break;
			case "load": DoLoad(ManualSavePath); break;
			case "newmap":
				_state.Reset();
				_state.WorldSeed = System.Environment.TickCount;
				InitializeWorld();
				AddLog("新地图已生成 🗺️");
				FlushMap();
				break;
			default:
				if (cmd.StartsWith('/'))
					HandleDebugCommand(cmd);
				else
					AddLog("未知指令 ❓");
				break;
		}
	}

	/// <summary>处理 / 前缀的 debug 文本命令。</summary>
	private void HandleDebugCommand(string cmd)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) { AddLog("[debug] 无玩家"); return; }

		var parts = cmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
		var verb = parts[0].ToLowerInvariant();
		var arg = parts.Length > 1 ? parts[1].Trim() : "";

		switch (verb)
		{
			case "/chest":
				var count = DebugModule.SpawnChest(_state, _state.PlayerX, _state.PlayerY);
				AddLog($"[debug] 宝箱已生成，含 {count} 件装备。按 F 打开");
				FlushMap();
				break;

			case "/gold":
				var gold = int.TryParse(arg, out var g) ? g : 1000;
				DebugModule.GiveGold(player, gold);
				AddLog($"[debug] +{gold}G (总计: {player.Gold}G)");
				break;

			case "/heal":
				DebugModule.HealAll(player);
				AddLog("[debug] 所有肢体已恢复满耐久");
				break;

			case "/spawn":
				if (string.IsNullOrEmpty(arg))
				{
					var ids = DebugModule.GetMonsterTemplateIds();
					AddLog($"[debug] 可用模板: {string.Join(", ", ids)}");
					break;
				}
				var sx = _state.PlayerX + player.FacingX;
				var sy = _state.PlayerY + player.FacingY;
				var spawned = DebugModule.SpawnEnemy(_state, arg, sx, sy);
				if (spawned != null)
				{
					AddLog($"[debug] 已生成 {spawned.DisplayName} 在 ({sx},{sy})");
					FlushMap();
				}
				else
				{
					AddLog($"[debug] 未知模板: {arg}");
				}
				break;

			case "/god":
				var on = DebugModule.ToggleGodMode(player);
				AddLog($"[debug] 无敌模式: {(on ? "开启" : "关闭")}");
				break;

			case "/down":
				DebugModule.SkipToNextFloor(_state);
				AddLog($"[debug] 已传送到第 {_state.PlayerZ} 层");
				FlushMap();
				break;

			default:
				AddLog("[debug] 可用命令: /chest /gold /heal /spawn /god /down");
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

		var targets = InteractionModule.GetAvailableTargets(_state, player);

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
				options.Add((target.DisplayName, () => ShowInteractionsFor(player, target)));
			}

			var sb = new StringBuilder("选择目标：");
			for (var i = 0; i < options.Count; i++)
				sb.Append($"  [{i + 1}] {options[i].Name}");
			AddLog(sb.ToString());

			EnterSelection(n =>
			{
				if (n < 1 || n > options.Count) { AddLog("无效选择"); return; }
				options[n - 1].Execute();
			});
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
			AddLog("附近没有可交互的对象 🤷");
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
			AddLog("你没有任何地形破坏技能");
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
			AddLog("周围没有可破坏的地形");
			return;
		}

		AddLog("选择方向 (WASD/方向键)...");
		_inputModule.EnterDirectionMode("dig");
	}

	/// <summary>收到方向后，自动匹配最合适的地形破坏技能并执行。</summary>
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
			AddLog("那个方向没有可破坏的地形");
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
			AddLog($"你没有能破坏 {terrain.StringId} 的技能");
			return;
		}

		var events = InteractionModule.ExecuteDig(_state, player, tx, ty, tz, bestSkill);
		Dispatch(events);
		var turnEvents = TurnModule.Tick(_state);
		Dispatch(turnEvents);
		FlushMap();
	}

	private static string GetDirectionName(int fx, int fy, int tx, int ty)
	{
		var dx = tx - fx;
		var dy = ty - fy;
		return (dx, dy) switch
		{
			(0, -1) => "北",
			(0, 1) => "南",
			(-1, 0) => "西",
			(1, 0) => "东",
			_ => $"{dx},{dy}",
		};
	}

	/// <summary>从地面拾取一个物品放入背包。</summary>
	private void PickupGroundItem(Actor player, Item itemInfo)
	{
		var events = InteractionModule.PickupItem(_state, player, itemInfo.Id);
		Dispatch(events);
		_groundPanel.Refresh();
	}

	/// <summary>打开宝箱面板。</summary>
	private void OpenChestPanel(Item chestItem)
	{
		_openChestPos = (_state.PlayerX, _state.PlayerY);
		_chestPanel.Open(chestItem);
		_inputModule.EnterChestMode();
		RefreshAllBorders();
	}

	/// <summary>关闭宝箱面板。</summary>
	private void CloseChestPanel()
	{
		_openChestPos = null;
		_chestPanel.Close();
		_inputModule.EnterActionMode();
		RefreshAllBorders();
		_groundPanel.Refresh();
		FlushMap();
	}

	/// <summary>玩家移动后检查是否离开宝箱范围，超过1格自动关闭。</summary>
	private void CheckChestRange()
	{
		if (_openChestPos == null || !_chestPanel.Visible) return;
		var (cx, cy) = _openChestPos.Value;
		var dist = Math.Max(Math.Abs(_state.PlayerX - cx), Math.Abs(_state.PlayerY - cy));
		if (dist > 1)
		{
			AddLog("你离开了宝箱范围，宝箱已关闭。");
			CloseChestPanel();
		}
	}

	/// <summary>从背包选择物品放入宝箱。</summary>
	private void OpenPutIntoChestSelection(Item chestItem)
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var inv = InventoryModule.List(player);
		if (inv.Count == 0)
		{
			AddLog("背包是空的");
			return;
		}

		var sb = new StringBuilder("选择要放入的物品：");
		for (var i = 0; i < inv.Count; i++)
		{
			var (_, item) = inv[i];
			var eqMark = item.Equipped ? "[E]" : "";
			sb.Append($"  [{i + 1}] {eqMark}{item.Name}");
		}
		sb.Append("  [0] 取消");
		AddLog(sb.ToString());

		_inputModule.EnterSelectionMode();
		EnterSelection(n =>
		{
			if (n == 0) { AddLog("取消"); _inputModule.EnterChestMode(); return; }
			if (n < 1 || n > inv.Count) { AddLog("无效选择"); _inputModule.EnterChestMode(); return; }
			var (invIdx, item) = inv[n - 1];
			if (item.Equipped)
			{
				AddLog($"请先卸下 {item.Name}");
				_inputModule.EnterChestMode();
				return;
			}
			var removed = InventoryModule.RemoveAt(player, invIdx);
			if (removed != null)
			{
				chestItem.Contents!.Add(removed);
				AddLog($"将 {removed.Name} 放入了 {chestItem.Name}");
			}
			_inputModule.EnterChestMode();
			_chestPanel.Refresh();
			FlushMap();
		});
	}

	/// <summary>显示对特定目标可用的交互选项列表。</summary>
	// REVIEW: 闭包捕获了 player 和 target 引用。
	//         如果在选择期间 player/target 状态被其他逻辑（如 AI 回合）修改，
	//         执行时可能基于过期状态。当前流程中选择模式会阻塞 AI 回合，暂无问题。
	private void ShowInteractionsFor(Actor player, Actor target)
	{
		var interactions = InteractionModule.GetInteractions(player, target, InteractionDefs.All);

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
		options.Add(("没什么", () => AddLog("你转身离开")));

		if (options.Count == 1)
		{
			options[0].Execute();
			return;
		}

		var sb = new StringBuilder($"{target.DisplayName}：");
		for (var i = 0; i < options.Count; i++)
			sb.Append($"  [{i + 1}] {options[i].Name}");
		AddLog(sb.ToString());

		EnterSelection(n =>
		{
			if (n < 1 || n > options.Count) { AddLog("无效选择"); return; }
			options[n - 1].Execute();
		});
	}

	// ══════════════════════════════════════════════════════
	//  看海模式（AI 自动控制玩家）
	// ══════════════════════════════════════════════════════

	/// <summary>切换看海模式：设置玩家 BrainId 为 "simple" 或 null。</summary>
	// REVIEW: BrainId 修改直接操作 Actor 字段，不产出事件，
	//         与「状态 → 事件 → 副作用」的设计理念不一致。
	//         且 BrainId 未被 SaveModule.CopyActor 拷贝，楼层切换后会丢失。
	private void ToggleWatchMode()
	{
		_state.WatchMode = !_state.WatchMode;
		_watchTimer = 0;

		var player = ActorModule.GetPlayer(_state);
		if (player != null)
			player.BrainId = _state.WatchMode ? "simple" : null;

		_watchModeBtn.Text = _state.WatchMode ? "看海模式：开启 🌊" : "看海模式：关闭";
		AddLog(_state.WatchMode ? "看海模式已开启 🌊 世界将自动推进" : "看海模式已关闭 🎮 恢复手动控制");
	}

	/// <summary>看海模式每 0.15 秒推进一次回合。</summary>
	private void WatchModeTick()
	{
		var events = TurnModule.TickWatchMode(_state);
		Dispatch(events);
		FlushMap();
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
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var events = ActionModule.TryMove(_state, player, dx, dy);
		events.AddRange(TurnModule.Tick(_state));
		Dispatch(events);

		var center = new WorldCoord(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_state.World?.Chunks.UpdateLoadedChunks(center, _state.Turn);

		FlushMap();
		CheckChestRange();
	}

	// ══════════════════════════════════════════════════════
	//  事件分发（GameEvent → 日志 / UI 动作）
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 遍历事件列表，按 Type 分发到对应的处理方法。
	/// 这是 Core 层事件到 UI 层副作用的唯一翻译层。
	/// </summary>
	// REVIEW: 事件 Type 使用字符串匹配，编译器无法检查完整性。
	//         新增事件类型时容易遗漏 case 而静默忽略。
	//         建议改为枚举或至少增加 default 日志警告。
	private void Dispatch(List<GameEvent> events)
	{
		foreach (var e in events)
		{
			switch (e.Type)
			{
				case "hit_wall":
					AddLog("撞墙了 🚧");
					break;
				case "actor_moved":
					break;
				case "monster_spawned":
					AddLog($"巢穴刷出怪物 👾 ({e.TargetX},{e.TargetY})");
					break;
				case "interaction":
					DispatchInteraction(e);
					break;
				case "combat_bump":
					_combatUI.HandleCombatBump(e);
					break;
				case "combat_attack":
					DispatchCombatAttack(e);
					break;
				case "combat_block":
					AddLog($"🛡️ {e.TargetActorName}使用了{e.ActionName}！防御+5 (1回合)");
					break;
				case "limb_destroyed":
					DispatchLimbDestroyed(e);
					break;
				case "actor_killed":
					DispatchActorKilled(e);
					break;
				case "actor_incapacitated":
					DispatchIncapacitated(e);
					break;
				case "item_picked_up":
					AddLog($"📦 拾取了 {e.ItemName}");
					break;
				case "item_dropped":
					AddLog($"📦 丢弃了 {e.ItemName}");
					break;
				case "drop_failed":
					AddLog($"⚠️ 请先卸下 {e.ItemName} 再丢弃");
					break;
				case "pickup_failed":
					AddLog("物品已经不在了");
					break;
				case "dig_success":
					AddLog($"{e.ActionName ?? "挖掘"}成功！地形被破坏了 ⛏️");
					break;
				case "dig_progress":
					AddLog($"{e.ActionName ?? "挖掘"}中... 造成 {e.Damage} 点破坏 ⛏️");
					break;
				case "dig_failed":
					AddLog($"无法执行：{e.ItemName}");
					break;
			}
		}
	}

	/// <summary>处理攻击事件：区分「被攻击」和「攻击他人」的日志格式。</summary>
	private void DispatchCombatAttack(GameEvent e)
	{
		if (e.TargetId == _state.PlayerId)
		{
			var attackerName = e.InitiatorId != null
				? ActorModule.GetById(_state, e.InitiatorId)?.DisplayName ?? "???"
				: "???";
			AddLog($"🩸 {attackerName}攻击了你的{e.LimbName}，造成{e.Damage}点伤害");
		}
		else
		{
			AddLog($"⚔️ {e.ActionName} → {e.TargetActorName}的{e.LimbName}，造成{e.Damage}点伤害");
		}

		var hitTarget = e.TargetId != null ? ActorModule.GetById(_state, e.TargetId) : null;
		if (hitTarget != null)
		{
			var hl = hitTarget.Limbs.Find(l => l.Name == e.LimbName);
			if (hl != null)
				AddLog($"   {e.LimbName} ({hl.Durability}/{hl.MaxDurability})");
		}
	}

	private void DispatchLimbDestroyed(GameEvent e)
	{
		if (e.TargetId == _state.PlayerId)
			AddLog($"💥 你的{e.LimbName}被摧毁了！");
		else
			AddLog($"💥 {e.TargetActorName}的{e.LimbName}被摧毁了！");
	}

	private void DispatchActorKilled(GameEvent e)
	{
		if (e.TargetId == _state.PlayerId)
		{
			HandlePlayerDeath("killed");
		}
		else
		{
			_killCount++;
			_combatUI.HandleActorKilled(e);
		}
	}

	private void DispatchIncapacitated(GameEvent e)
	{
		if (e.TargetId == _state.PlayerId)
		{
			HandlePlayerDeath("incapacitated");
		}
		else
		{
			AddLog($"😵 {e.TargetActorName}失去了意识！");
		}
	}

	/// <summary>处理交互事件：根据 EffectType 分派到对话/交易/战斗/驯服。</summary>
	private void DispatchInteraction(GameEvent e)
	{
		switch (e.EffectType)
		{
			case "talk":
				AddLog($"{e.TargetActorName}: 「你好，旅行者。」");
				break;
			case "trade":
				_tradeUI.OpenTradeMenu(e);
				break;
			case "combat":
				_combatUI.OpenCombatMenu(e);
				break;
			case "tame":
				AddLog($"你成功驯服了 {e.TargetActorName}！它现在是友方了。");
				break;
			default:
				AddLog($"[{e.InteractionName}] {e.TargetActorName}");
				break;
		}
	}

	// ══════════════════════════════════════════════════════
	//  楼梯（上行 / 下行）
	// ══════════════════════════════════════════════════════

	/// <summary>尝试使用楼梯：扫描脚下 + 四方向是否有楼梯 Fixture。</summary>
	private void DoEnterStairs()
	{
		var px = _state.PlayerX;
		var py = _state.PlayerY;
		var dirs = new (int Dx, int Dy)[] { (0, 0), (0, -1), (0, 1), (-1, 0), (1, 0) };

		foreach (var (dx, dy) in dirs)
		{
			var nx = px + dx;
			var ny = py + dy;
			if (MapModule.HasFixture(_state, nx, ny, Entities.StairDown)) { GoDown(); return; }
			if (MapModule.HasFixture(_state, nx, ny, Entities.StairUp)) { GoUp(); return; }
		}
		AddLog("附近没有楼梯 🤷");
	}

	/// <summary>下楼：Z++ 并传送到下层楼梯附近。</summary>
	private void GoDown()
	{
		if (ChestOpen) CloseChestPanel();
		MapModule.GoDown(_state);
		var center = new WorldCoord(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_state.World?.Chunks.UpdateLoadedChunks(center, _state.Turn);
		MapModule.PlacePlayerAtFixture(_state, Entities.StairUp);
		AddLog($"你进入了第 {_state.PlayerZ} 层 ⬇️");
		FlushMap();
	}

	/// <summary>上楼：Z-- 并传送到上层楼梯附近。</summary>
	private void GoUp()
	{
		if (ChestOpen) CloseChestPanel();
		MapModule.GoUp(_state);
		var center = new WorldCoord(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_state.World?.Chunks.UpdateLoadedChunks(center, _state.Turn);
		MapModule.PlacePlayerAtFixture(_state, Entities.StairDown);
		AddLog($"你回到了第 {_state.PlayerZ} 层 ⬆️");
		FlushMap();
	}

	// ══════════════════════════════════════════════════════
	//  查看（L 键）
	// ══════════════════════════════════════════════════════

	/// <summary>构建环境信息文本：位置、肢体状态、脚下设施、同格 Actor、四方向概览。</summary>
	private void DoLook()
	{
		var sb = new StringBuilder();
		var player = ActorModule.GetPlayer(_state);
		sb.Append($"📍 Z{_state.PlayerZ} ({_state.PlayerX}, {_state.PlayerY})  回合: {_state.Turn}");
		if (player != null) sb.Append($"  💰{player.Gold}G");

		if (player != null && player.Limbs.Count > 0)
		{
			sb.Append("\n  肢体: ");
			var parts = new List<string>();
			foreach (var l in player.Limbs)
			{
				var vital = l.Tags.ContainsKey("要害") ? "*" : "";
				parts.Add($"{l.Name}{vital}({l.Durability}/{l.MaxDurability})");
			}
			sb.Append(string.Join(" ", parts));
		}

		var standingOn = MapModule.GetFixtureId(_state, _state.PlayerX, _state.PlayerY);
		if (!string.IsNullOrEmpty(standingOn))
			sb.Append($"  脚下: {FixtureLabel(standingOn)}");

		var groundItems = MapModule.PeekGroundItems(_state, _state.PlayerX, _state.PlayerY);
		if (groundItems.Count > 0)
		{
			var names = groundItems.ConvertAll(i => i.Name);
			sb.Append($"\n  📦 地上: {string.Join(", ", names)}  (F 键拾取)");
		}

		var coActors = ActorModule.GetAllAt(_state, _state.PlayerX, _state.PlayerY);
		foreach (var a in coActors)
		{
			if (a.Id == _state.PlayerId) continue;
			sb.Append($"  同格: {a.DisplayName}");
		}

		var dirs = new (string Name, int Dx, int Dy)[]
		{
			("上", 0, -1), ("下", 0, 1), ("左", -1, 0), ("右", 1, 0),
		};
		foreach (var (name, dx, dy) in dirs)
		{
			var tx = _state.PlayerX + dx;
			var ty = _state.PlayerY + dy;
			sb.Append($"  {name}: {CellLabel(tx, ty)}");
		}
		AddLog(sb.ToString());
	}

	/// <summary>将格子内容转化为人类可读文本标签。</summary>
	private string CellLabel(int x, int y)
	{
		if (MapModule.IsWall(_state, x, y)) return "墙 🚧";
		var actors = ActorModule.GetAllAt(_state, x, y);
		if (actors.Count > 0)
		{
			var names = actors.ConvertAll(a => a.DisplayName);
			return string.Join("+", names);
		}
		var items = MapModule.GetGroundItems(_state, x, y);
		if (items.Count > 0) return $"📦{items.Count}个物品";
		var f = MapModule.GetFixtureId(_state, x, y);
		if (!string.IsNullOrEmpty(f)) return FixtureLabel(f);
		return "空地";
	}

	/// <summary>Fixture EntityId → 中文标签。</summary>
	private static string FixtureLabel(string id) => id switch
	{
		Entities.StairDown => "下行楼梯 ⬇️",
		Entities.StairUp => "上行楼梯 ⬆️",
		Entities.Nest => "巢穴 🕳️",
		Entities.House => "房屋 🏠",
		Entities.Item => "道具 📦",
		Entities.Door => "门 🚪",
		_ => id,
	};

	// ══════════════════════════════════════════════════════
	//  存档 / 读档
	// ══════════════════════════════════════════════════════

	/// <summary>保存游戏到指定路径。</summary>
	private void DoSave(string path, string label = "存档")
	{
		SaveModule.SaveGame(_state, path);
		AddLog($"{label}已保存 💾");
	}

	/// <summary>从指定路径加载存档。加载后重建世界并确保玩家 Actor 存在。</summary>
	private void DoLoad(string path, string label = "存档")
	{
		if (SaveModule.LoadGame(_state, path))
		{
			MapGenModule.InitializeWorld(_state);
			EnsurePlayerActor();
			SyncViewMode();
			AddLog($"{label}已加载 📂 (Z{_state.PlayerZ})");
			FlushMap();
		}
		else
		{
			AddLog($"未找到{label} ❌");
		}
	}

	/// <summary>初始化无限世界：创建 WorldMap、找出生点、生成玩家。</summary>
	private void InitializeWorld()
	{
		MapGenModule.InitializeWorld(_state);
		MapGenModule.FindSpawnPoint(_state);
		MapGenModule.SpawnPlayer(_state);
		SyncViewMode();
	}

	/// <summary>
	/// 防御性保障：确保玩家 Actor 存在于 Actors 字典中。
	/// 1. PlayerId 已在 Actors 中 → 直接返回
	/// 2. PlayerId 不在，但 Actors 中有 Player 阵营的 Actor → 修正 PlayerId 指向它
	/// 3. 完全找不到 → 创建最小 fallback（仅保证不崩溃）
	/// </summary>
	private void EnsurePlayerActor()
	{
		if (_state.Actors.ContainsKey(_state.PlayerId))
			return;

		foreach (var a in _state.Actors.Values)
		{
			if (a.Faction != Factions.Player) continue;
			GD.PushWarning($"EnsurePlayerActor: PlayerId '{_state.PlayerId}' missing, recovered existing player Actor '{a.Id}'");
			_state.PlayerId = a.Id;
			_state.PlayerX = a.X;
			_state.PlayerY = a.Y;
			_state.PlayerZ = a.Z;
			return;
		}

		GD.PushWarning($"EnsurePlayerActor: no player Actor found at all, creating minimal fallback");
		var player = ActorTemplates.Spawn("player", _state.PlayerId);
		player.X = _state.PlayerX;
		player.Y = _state.PlayerY;
		player.Z = _state.PlayerZ;
		_state.Actors[player.Id] = player;
	}

	// ══════════════════════════════════════════════════════
	//  渲染
	// ══════════════════════════════════════════════════════

	/// <summary>切换 ASCII / Emoji 渲染模式。</summary>
	private void ToggleRender()
	{
		var mode = _renderModule.ToggleMode();
		_renderModule.ApplyFont(_mapText);
		if (_gameStarted && !_inMenu)
			AddLog(mode == RenderMode.Emoji ? "渲染模式: Emoji 🎨" : "渲染模式: ASCII ⌨️");
		if (!_inMenu) FlushMap();
	}

	/// <summary>立即刷新地图面板和状态面板。</summary>
	private void FlushMap()
	{
		_fogTracker.Update(_state);

		if (_fogMapModule.Visible)
		{
			_mapText.BbcodeEnabled = true;
			_mapText.Clear();
			_mapText.AppendText(_fogMapModule.Render(_state));
			RefreshStatus();
			return;
		}

		var displayMap = BuildDisplayMap();
		ApplyFOV(displayMap);
		var text = _renderModule.RenderMap(displayMap);

		if (_minimapModule.Visible)
			text += "\n" + _minimapModule.Render(_state);

		if (_renderModule.UsesBBCode || _minimapModule.Visible)
		{
			_mapText.BbcodeEnabled = true;
			_mapText.Clear();
			_mapText.AppendText(text);
		}
		else
		{
			_mapText.BbcodeEnabled = false;
			_mapText.Text = text;
		}
		RefreshStatus();
	}

	/// <summary>刷新右侧状态面板（分页式）+ 技能/背包/脚下面板。</summary>
	private void RefreshStatus()
	{
		var player = ActorModule.GetPlayer(_state);
		_statusPanelModule.Refresh(player, _state.PlayerZ, _state.Turn, true);

		if (player != null)
			_skillPanel.Refresh(player);
		if (InventoryOpen) _inventoryPanel.Refresh();
		_groundPanel.Refresh();
		RefreshAllBorders();
	}

	/// <summary>委托给当前 IViewMode 构建显示地图。</summary>
	private List<List<string>> BuildDisplayMap() =>
		_viewMode.BuildDisplayMap(_state, ViewW, ViewH);

	/// <summary>
	/// 根据 FOV 四态给 displayMap 中的格子加前缀：
	/// 1. 朝向可见 → 原样（正常亮色）
	/// 2. 周边感知 → "per:" + 原始glyph（灰色但显示实时内容含怪物）
	/// 3. 已探索 → "mem:" + 地形glyph（暗色只显示地形）
	/// 4. 未探索 → "fog:"（黑色迷雾）
	/// </summary>
	private void ApplyFOV(List<List<string>> displayMap)
	{
		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var cz = _state.PlayerZ;
		var halfW = ViewW / 2;
		var halfH = ViewH / 2;

		for (var vy = 0; vy < displayMap.Count; vy++)
		{
			var row = displayMap[vy];
			var wy = cy - halfH + vy;
			for (var vx = 0; vx < row.Count; vx++)
			{
				var wx = cx - halfW + vx;

				if (_fogTracker.IsVisible(wx, wy, cz))
					continue;

				if (_fogTracker.IsPeripheral(wx, wy, cz))
				{
					row[vx] = "per:" + row[vx];
					continue;
				}

				if (_fogTracker.HasSeen(wx, wy, cz))
				{
					var terrain = _state.World?.GetTerrain(wx, wy, cz);
					row[vx] = "mem:" + (terrain?.Glyph ?? " ");
				}
				else
				{
					row[vx] = "fog:";
				}
			}
		}
	}

	/// <summary>根据 GameState.ViewModeId 同步视图模式实例。</summary>
	private void SyncViewMode()
	{
		_viewMode = _state.ViewModeId switch
		{
			"multi_layer" => new MultiLayerViewMode(),
			_ => new SingleLayerViewMode(),
		};
	}

	// ══════════════════════════════════════════════════════
	//  技能面板
	// ══════════════════════════════════════════════════════

	private void ToggleSkillPanel()
	{
		_skillPanel.Visible = !_skillPanel.Visible;
		AddLog(_skillPanel.Visible ? "技能面板: 开启 (K 关闭)" : "技能面板: 关闭");
		FlushMap();
	}

	// ══════════════════════════════════════════════════════
	//  状态面板
	// ══════════════════════════════════════════════════════

	private void HandleStatusInput(string cmd)
	{
		switch (cmd)
		{
			case ":status_up": _statusPanelModule.MoveCursor(-1); break;
			case ":status_down": _statusPanelModule.MoveCursor(1); break;
			case ":status_prev": _statusPanelModule.CycleTab(-1); break;
			case ":status_next": _statusPanelModule.CycleTab(1); break;
			case ":status_close":
				_inputModule.EnterActionMode();
				RefreshAllBorders();
				break;
		}
	}

	// ══════════════════════════════════════════════════════
	//  背包面板
	// ══════════════════════════════════════════════════════

	private void ToggleInventory()
	{
		if (InventoryOpen)
		{
			_inventoryPanel.Visible = false;
			_inputModule.EnterActionMode();
		}
		else
		{
			_inventoryPanel.Visible = true;
			_inputModule.EnterInventoryMode();
		}
		RefreshAllBorders();
		FlushMap();
	}

	private void HandleInventoryInput(string cmd)
	{
		switch (cmd)
		{
			case ":inv_up": _inventoryPanel.MoveCursor(-1); break;
			case ":inv_down": _inventoryPanel.MoveCursor(1); break;
			case ":inv_equip": _inventoryPanel.TryEquip(); break;
			case ":inv_use": _inventoryPanel.TryUse(); break;
			case ":inv_drop": _inventoryPanel.TryDrop(); break;
			case ":inv_filter_next": _inventoryPanel.CycleFilter(1); break;
			case ":inv_filter_prev": _inventoryPanel.CycleFilter(-1); break;
			case ":inv_sort": _inventoryPanel.CycleSort(); break;
			case ":inv_close": ToggleInventory(); break;
		}
	}

	private void HandleChestInput(string cmd)
	{
		switch (cmd)
		{
			case ":chest_up": _chestPanel.MoveCursor(-1); break;
			case ":chest_down": _chestPanel.MoveCursor(1); break;
			case ":chest_take": _chestPanel.TryTake(); break;
			case ":chest_put": _chestPanel.TryPut(); break;
			case ":chest_close": CloseChestPanel(); break;
		}
	}

	// ══════════════════════════════════════════════════════
	//  小地图 / 大地图
	// ══════════════════════════════════════════════════════

	private void ToggleMinimap()
	{
		if (_fogMapModule.Visible) return;
		_minimapModule.Visible = !_minimapModule.Visible;
		AddLog(_minimapModule.Visible ? "小地图: 开启 (Tab 关闭)" : "小地图: 关闭");
		FlushMap();
	}

	private void ToggleFogMap()
	{
		_fogMapModule.Visible = !_fogMapModule.Visible;
		if (_fogMapModule.Visible)
		{
			_fogMapModule.CenterOnPlayer(_state);
			AddLog("大地图: 开启 (WASD 滚动 / C 回中心 / M 关闭)");
		}
		else
		{
			AddLog("大地图: 关闭");
		}
		FlushMap();
	}

	private void CenterFogMap()
	{
		if (!_fogMapModule.Visible) return;
		_fogMapModule.CenterOnPlayer(_state);
		FlushMap();
	}

	// ══════════════════════════════════════════════════════
	//  设置面板
	// ══════════════════════════════════════════════════════

	/// <summary>根据进入来源（主菜单/游戏中）决定设置面板中哪些按钮可见。</summary>
	private void UpdateSettingsContext()
	{
		var inGame = _gameStarted && !_settingsFromMenu;
		_settingSaveBtn.Visible = inGame;
		_settingLoadBtn.Visible = inGame;
		_settingBackToMenuBtn.Visible = inGame;
	}

	/// <summary>切换设置面板的显示/隐藏。</summary>
	private void ToggleSettings()
	{
		_settingsFromMenu = false;
		_settingsOpen = !_settingsOpen;
		_settingsPanel.Visible = _settingsOpen;
		if (_settingsOpen) UpdateSettingsContext();
	}

	/// <summary>关闭设置面板。如果从主菜单进入则返回主菜单。</summary>
	private void CloseSettings()
	{
		if (_settingsFromMenu)
		{
			_settingsFromMenu = false;
			_settingsPanel.Visible = false;
			ShowMainMenu();
		}
		else
		{
			ToggleSettings();
		}
	}

	// ══════════════════════════════════════════════════════
	//  日志
	// ══════════════════════════════════════════════════════

	/// <summary>追加日志消息，超出 MaxLogLines 时裁剪最旧的。</summary>
	private void AddLog(string msg)
	{
		_logLines.Add(msg);
		if (_logLines.Count > MaxLogLines)
			_logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
		_logPanel.Text = string.Join("\n", _logLines);
	}

	private void RefreshAllBorders()
	{
		var focus = _inputModule?.Focus ?? InputFocus.Action;
		if (focus == InputFocus.Status)
			_focusedPanelNode = _statusPanelNode;
		else if (focus == InputFocus.Inventory)
			_focusedPanelNode = _invPanelNode;
		else if (focus == InputFocus.Chest)
			_focusedPanelNode = _chestPanelNode;
		else if (_focusedPanelNode != null && !_focusedPanelNode.Visible)
			_focusedPanelNode = null;

		PanelContainer?[] all =
			[_mapPanelNode, _statusPanelNode, _skillPanelNode, _invPanelNode, _groundPanelNode, _chestPanelNode];
		foreach (var p in all)
			PanelBorderHelper.Apply(p!, p == _focusedPanelNode);
	}
}
