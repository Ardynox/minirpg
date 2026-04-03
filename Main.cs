using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using MiniRPG.Core.World;
using MiniRPG.Module;
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
	private const int ViewW = 21;
	private const int ViewH = 11;

	private readonly GameState _state = new();

	private PanelContainer _mapPanelNode = null!;
	private LogModule _log = null!;
	private Button _watchModeBtn = null!;
	private InputModule _inputModule = null!;
	private InputBindingService _inputBindings = null!;
	private KeyBindingsUIModule _keyBindingsUI = null!;
	private TileMapRenderModule _mapRender = null!;
	private double _watchTimer;

	private bool _skillBarDirty;

	private GameSessionModule _session = null!;
	private MenuModule _menu = null!;

	private FogOfWarTracker _fogTracker = null!;

	private PanelManager _panels = null!;
	private StatusPanelModule _statusPanelModule = null!;
	private SkillBarModule _skillBar = null!;
	private SkillManagerModule _skillMgr = null!;
	private InventoryPanelModule _inventoryPanel = null!;
	private GroundPanelModule _groundPanel = null!;

	private ChestPanelModule? _chestPanel;
	private DialogPanelModule? _dialogPanel;
	private TradePanelModule? _tradePanel;
	private QuestPanelModule? _questPanel;

	private (int x, int y)? _openChestPos;

	private bool StatusOpen => _panels?.FocusedId == "status";
	private bool InventoryOpen => _panels?.FocusedId == "inventory";
	private bool ChestOpen => _panels?.FocusedId == "chest";

	private CombatUIModule _combatUI = null!;
	private TradeUIModule? _tradeUI;
	private DialogUIModule? _dialogUI;

	// ── 懒加载低频面板 ──────────────────────────────────

	private HBoxContainer TopRow => GetNode<HBoxContainer>("UI/TopRow");

	private ChestPanelModule EnsureChestPanel()
	{
		if (_chestPanel != null) return _chestPanel;
		var scene = GD.Load<PackedScene>("res://Scene/ChestPanel.tscn");
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		_chestPanel = new ChestPanelModule(node, this);
		_panels.Register(_chestPanel);
		return _chestPanel;
	}

	private DialogPanelModule EnsureDialogPanel()
	{
		if (_dialogPanel != null) return _dialogPanel;
		var scene = GD.Load<PackedScene>("res://Scene/DialogPanel.tscn");
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		_dialogPanel = new DialogPanelModule(node);
		_panels.Register(_dialogPanel);
		return _dialogPanel;
	}

	private TradePanelModule EnsureTradePanel()
	{
		if (_tradePanel != null) return _tradePanel;
		var scene = GD.Load<PackedScene>("res://Scene/TradePanel.tscn");
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		_tradePanel = new TradePanelModule(node);
		_panels.Register(_tradePanel);
		return _tradePanel;
	}

	private QuestPanelModule EnsureQuestPanel()
	{
		if (_questPanel != null) return _questPanel;
		var scene = GD.Load<PackedScene>("res://Scene/QuestPanel.tscn");
		var node = scene.Instantiate<PanelContainer>();
		TopRow.AddChild(node);
		_questPanel = new QuestPanelModule(node);
		_panels.Register(_questPanel);
		return _questPanel;
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

	void InventoryPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void InventoryPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void InventoryPanelModule.IHost.FlushMap() => FlushMap();
	GameState InventoryPanelModule.IHost.State => _state;
	bool InventoryPanelModule.IHost.HasFocus => InventoryOpen;
	void InventoryPanelModule.IHost.OpenChestFromInventory(Item chestItem) => OpenChestPanel(chestItem);
	void InventoryPanelModule.IHost.CloseInventory() => ToggleInventory();

	void GroundPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
	void GroundPanelModule.IHost.Dispatch(List<GameEvent> events) => Dispatch(events);
	void GroundPanelModule.IHost.FlushMap() => FlushMap();
	GameState GroundPanelModule.IHost.State => _state;
	void GroundPanelModule.IHost.OpenChestPanel(Item chestItem) => OpenChestPanel(chestItem);
	void GroundPanelModule.IHost.PickupGroundItem(Item item) => PickupGroundItem(ActorModule.GetPlayer(_state)!, item);

	void ChestPanelModule.IHost.AddLog(string msg) => _log.Add(msg);
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

		_inputModule.CancelSelection();

		_log.Add("");
		_log.Add(reason == "incapacitated"
			? "══════ 😵 你失去了意识 ══════"
			: "══════ 💀 你死了 ══════");
		_log.Add($"  回合: {_state.Turn}");
		_log.Add($"  到达: 第 {_state.PlayerZ} 层");
		_log.Add($"  击杀: {_state.KillCount}");
		var player2 = ActorModule.GetPlayer(_state);
		if (player2 != null) _log.Add($"  金币: {player2.Gold}G");
		_log.Add("════════════════════════════");
		_log.Add("按任意键返回主菜单……");
	}

	// ══════════════════════════════════════════════════════
	//  Godot 生命周期
	// ══════════════════════════════════════════════════════

	/// <summary>节点就绪：加载预设数据、创建模块、注册信号、显示主菜单。</summary>
	public override void _Ready()
	{
		PresetDB.Load();
		TerrainRegistry.Load("res://Data/terrains.json");
		DialogPool.Load();
		ResAccess.Load();
		_fogTracker = new FogOfWarTracker();

		_session = new GameSessionModule(_state, _fogTracker);
		_menu = new MenuModule(this);

		_mapPanelNode = GetNode<PanelContainer>("UI/TopRow/MapPanel");
		_log = new LogModule(GetNode<RichTextLabel>("UI/LogPanel"));
		var lineEdit = GetNode<LineEdit>("UI/InputBar");

		var mapRoot = GetNode<Node2D>("UI/TopRow/MapPanel/SubViewportContainer/SubViewport/MapRoot");
		var tileSet = GD.Load<TileSet>("res://FantasyKingdomTileSet.tres");
		var playerSpine = mapRoot.GetNodeOrNull<Node2D>("PlayerSpine");
		var camera = GetNode<Camera2D>("UI/TopRow/MapPanel/SubViewportContainer/SubViewport/Camera2D");
		_mapRender = new TileMapRenderModule(_state, _fogTracker, ViewW, ViewH);
		_mapRender.Init(mapRoot, tileSet, playerSpine, camera);

		_statusPanelModule = new StatusPanelModule(GetNode<PanelContainer>("UI/TopRow/StatusPanel"));

		var skillBarNode = GetNode<PanelContainer>("SkillBar");
		skillBarNode.Theme = GD.Load<Theme>("res://UITheme.tres");
		_skillBar = new SkillBarModule(skillBarNode);
		_skillMgr = new SkillManagerModule(GetNode<PanelContainer>("UI/TopRow/SkillManager"));
		_inventoryPanel = new InventoryPanelModule(GetNode<PanelContainer>("UI/TopRow/InventoryPanel"), this);
		_groundPanel = new GroundPanelModule(GetNode<PanelContainer>("UI/GroundPanel"), this);

		_panels = new PanelManager();
		_panels.RegisterPassive(_mapPanelNode, "map", canFocus: true, consumeUnhandledKeys: false);
		_panels.Register(_statusPanelModule);
		_panels.Register(_skillMgr);
		_panels.Register(_inventoryPanel);
		_panels.Register(_groundPanel);

		_inputBindings = new InputBindingService();
		_inputModule = new InputModule(lineEdit, _inputBindings);
		_keyBindingsUI = new KeyBindingsUIModule(this, _inputBindings);
		_inputModule.CommandReceived += OnCommand;

		_combatUI = new CombatUIModule(this);

		GetNode<Button>("SettingsPanel/VBox/RenderToggle").Pressed += ToggleRender;
		_watchModeBtn = GetNode<Button>("SettingsPanel/VBox/WatchModeToggle");
		_watchModeBtn.Pressed += ToggleWatchMode;
		GetNode<Button>("SettingsPanel/VBox/SaveBtn").Pressed += () => DoSave(GameSessionModule.ManualSavePath);
		GetNode<Button>("SettingsPanel/VBox/LoadBtn").Pressed += () => DoLoad(GameSessionModule.ManualSavePath);

		_menu.OnContinue += HandleMenuContinue;
		_menu.OnNewGame += HandleMenuNewGame;
		_menu.OnLoadGame += HandleMenuLoadGame;
		_menu.OnAutoTest += HandleAutoTest;
		_menu.OnQuit += () => GetTree().Quit();
		_menu.OnBackToMenu += HandleBackToMenu;
		_menu.OnOpenKeyBindings += () => _keyBindingsUI.Open();
		_menu.OnSettingsClosed += () => _keyBindingsUI.Close();

		_menu.ShowMainMenu(_session.HasAnySave());
	}

	/// <summary>每帧更新：驱动异步资源加载 + 脏面板统一刷新 + 看海模式自动推进。</summary>
	public override void _Process(double delta)
	{
		ResAccess.PollAsyncLoads();
		if (_menu.InMenu) return;

		ProcessDirtyPanels();

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

	/// <summary>拦截未处理的键盘事件：优先让 PanelManager 处理（面板聚焦时），否则走 InputModule。</summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventKey key) return;

		if (_keyBindingsUI.IsOpen)
		{
			if (_keyBindingsUI.HandleKey(key) || key.Pressed)
				GetViewport().SetInputAsHandled();
			return;
		}

		if (_menu.InMenu) return;
		if (_panels.HandleKey(key) || _inputModule.HandleKeyInput(key))
			GetViewport().SetInputAsHandled();
	}

	/// <summary>全局输入：检测鼠标点击落在哪个面板内，切换焦点。</summary>
	public override void _Input(InputEvent @event)
	{
		if (_keyBindingsUI.IsOpen)
		{
			if (_keyBindingsUI.HandleMouseInput(@event))
				GetViewport().SetInputAsHandled();
			return;
		}

		if (_menu.InMenu || !_session.GameStarted) return;
		if (@event is not InputEventMouseButton mb || !mb.Pressed) return;
		if (mb.ButtonIndex == MouseButton.Right)
		{
			if (_panels.CloseFocused())
			{
				FlushMap();
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		if (mb.ButtonIndex != MouseButton.Left) return;

		var hit = _panels.HitTest(mb.GlobalPosition);
		if (hit == null) return;
		if (hit == _panels.Focused) return;
		if (hit.PanelId == "inventory" && !_inventoryPanel.Visible)
		{
			_inventoryPanel.Visible = true;
			FlushMap();
		}
		_panels.SetFocus(hit);
	}

	// ══════════════════════════════════════════════════════
	//  菜单事件处理（MenuModule 回调）
	// ══════════════════════════════════════════════════════

	private void HandleMenuContinue()
	{
		if (!_session.TryContinue())
		{
			DoStartNewGame();
		}
		else
		{
			_log.Clear();
			_log.Add("存档已加载 📂");
		}
		ShowGameHints();
		DoEnterGame();
	}

	private void HandleMenuNewGame()
	{
		DoStartNewGame();
		ShowGameHints();
		DoEnterGame();
	}

	private void HandleMenuLoadGame()
	{
		if (_session.TryLoadGame())
		{
			_log.Clear();
			_log.Add("存档已加载 📂");
		}
		else
		{
			_log.Clear();
			_log.Add("未找到存档，已创建新游戏");
			DoStartNewGame();
		}
		ShowGameHints();
		DoEnterGame();
	}

	private void HandleAutoTest()
	{
		DoStartNewGame();
		DoEnterGame();
		var test = new AutoTestModule();
		test.RunAll(this, _state, _session, _fogTracker, _mapRender, _log,
			RunTestCommand, FlushMap, delay: 0.05f, stopOnFail: false);
	}

	/// <summary>供 AutoTestModule 转发命令到 OnCommand。</summary>
	public void RunTestCommand(string cmd) => OnCommand(cmd);

	private void HandleBackToMenu()
	{
		if (_session.GameStarted)
			DoSave(GameSessionModule.QuickSavePath, "快速存档");
		_keyBindingsUI.Close();
		_inputModule.CancelSelection();
		_panels.ClearFocus();
		if (_dialogUI != null && _dialogUI.InDialog) _dialogUI.CloseDialog();
		if (_tradeUI != null && _tradeUI.InTrade) _tradeUI.CloseTrade();
		_skillBar.Visible = false;
		_menu.ShowMainMenu(_session.HasAnySave());
	}

	private void DoStartNewGame()
	{
		_log.Clear();
		PlayerDead = false;
		_keyBindingsUI.Close();
		_session.NewGame();
		_mapRender.ResetOverlays();
		_skillBar.Visible = true;
		_skillMgr.Close();
		_inventoryPanel.Visible = false;
		if (_chestPanel != null) _chestPanel.Visible = false;
		if (_dialogPanel != null) _dialogPanel.Close();
		if (_tradePanel != null) _tradePanel.Close();
		if (_questPanel != null) _questPanel.Close();
		_panels.ClearFocus();
		_log.Add("新游戏开始 🗺️");
	}

	private void DoEnterGame()
	{
		_menu.EnterGame();
		_inputModule.EnterActionMode();
		FlushMap();
	}

	private void ShowGameHints()
	{
		_log.Add("WASD 移动 | L 查看 | R 渲染 | ESC 设置");
		_log.Add("空格 上下楼 | F 交互 | I 背包 | F5 快存 | F9 快读");
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
			_menu.ShowMainMenu(_session.HasAnySave());
			return;
		}

		if (cmd.StartsWith(":dig_") && cmd != ":dig")
		{
			HandleDigDirection(cmd[":dig_".Length..]);
			return;
		}
		if (cmd == ":dir_cancel")
		{
			_log.Add("已取消");
			return;
		}

		switch (cmd)
		{
			case ":settings" or "settings":
				_keyBindingsUI.Close();
				if (_mapRender.FogMapVisible) { _mapRender.FogMapVisible = false; _log.Add("大地图: 关闭"); FlushMap(); return; }
				_menu.ToggleSettings(_session.GameStarted); return;
			case ":quicksave": DoSave(GameSessionModule.QuickSavePath, "快速存档"); return;
			case ":quickload": DoLoad(GameSessionModule.QuickSavePath, "快速存档"); return;
			case ":interact" or "interact": DoInteract(); return;
			case ":dig": StartDig(); return;
			case ":inventory": ToggleInventory(); return;
			case ":skills": ToggleSkillManager(); return;
			case ":toggle_status": ToggleStatusPanel(); return;
			case ":quests": ToggleQuestPanel(); return;
			case ":render" or "render": ToggleRender(); return;
			case ":status_prev": _statusPanelModule.CycleTab(-1); return;
			case ":status_next": _statusPanelModule.CycleTab(1); return;
			case ":minimap": ToggleMinimap(); return;
			case ":fogmap": ToggleFogMap(); return;
			case ":fogmap_center": CenterFogMap(); return;
		}

		if (_menu.SettingsOpen) return;

		if (_mapRender.FogMapVisible)
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
			case "save": DoSave(GameSessionModule.ManualSavePath); break;
			case "load": DoLoad(GameSessionModule.ManualSavePath); break;
			case "newmap":
				_session.NewGame();
				_log.Add("新地图已生成 🗺️");
				FlushMap();
				break;
			default:
				if (cmd.StartsWith('/'))
					HandleDebugCommand(cmd);
				else
					_log.Add("未知指令 ❓");
				break;
		}
	}

	private void HandleDebugCommand(string cmd)
	{
		var result = DebugModule.HandleCommand(cmd, _state, _session);
		foreach (var msg in result.Logs) _log.Add(msg);
		if (result.NeedsFlush) FlushMap();
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
			_log.Add(sb.ToString());

			_inputModule.EnterSelection(n =>
			{
				if (n < 1 || n > options.Count) { _log.Add("无效选择"); return; }
				options[n - 1].Execute();
			}, () => _log.Add("已取消"));
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
			_log.Add("附近没有可交互的对象 🤷");
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
			_log.Add("你没有任何地形破坏技能");
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
			_log.Add("周围没有可破坏的地形");
			return;
		}

		_log.Add("选择方向 (WASD/方向键)...");
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
			_log.Add("那个方向没有可破坏的地形");
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
			_log.Add($"你没有能破坏 {terrain.StringId} 的技能");
			return;
		}

		var events = InteractionModule.ExecuteDig(_state, player, tx, ty, tz, bestSkill);
		Dispatch(events);
		var turnEvents = TurnModule.Tick(_state);
		Dispatch(turnEvents);
		FlushMap();
	}

	/// <summary>从地面拾取一个物品放入背包。</summary>
	private void PickupGroundItem(Actor player, Item itemInfo)
	{
		var events = InteractionModule.PickupItem(_state, player, itemInfo.Id);
		Dispatch(events);
		_groundPanel.Invalidate();
		_groundPanel.Refresh();
	}

	/// <summary>打开宝箱面板。</summary>
	private void OpenChestPanel(Item chestItem)
	{
		_openChestPos = (_state.PlayerX, _state.PlayerY);
		var chest = EnsureChestPanel();
		chest.Open(chestItem);
		_panels.PushFocus(chest);
	}

	/// <summary>关闭宝箱面板。</summary>
	private void CloseChestPanel()
	{
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

	/// <summary>玩家移动后检查是否离开宝箱范围，超过1格自动关闭。</summary>
	private void CheckChestRange()
	{
		if (_openChestPos == null || _chestPanel == null || !_chestPanel.Visible) return;
		var (cx, cy) = _openChestPos.Value;
		var dist = Math.Max(Math.Abs(_state.PlayerX - cx), Math.Abs(_state.PlayerY - cy));
		if (dist > 1)
		{
			_log.Add("你离开了宝箱范围，宝箱已关闭。");
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
			_log.Add("背包是空的");
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
		_log.Add(sb.ToString());

		var chest = EnsureChestPanel();
		_inputModule.EnterSelection(n =>
		{
			if (n == 0) { _log.Add("取消"); _panels.SetFocus(chest); return; }
			if (n < 1 || n > inv.Count) { _log.Add("无效选择"); _panels.SetFocus(chest); return; }
			var (invIdx, item) = inv[n - 1];
			if (item.Equipped)
			{
				_log.Add($"请先卸下 {item.Name}");
				_panels.SetFocus(chest);
				return;
			}
			var removed = InventoryModule.RemoveAt(player, invIdx);
			if (removed != null)
			{
				chestItem.Contents!.Add(removed);
				_log.Add($"将 {removed.Name} 放入了 {chestItem.Name}");
			}
			_panels.SetFocus(chest);
			chest.Refresh();
			FlushMap();
		}, () => { _log.Add("取消"); _panels.SetFocus(chest); });
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
		options.Add(("没什么", () => _log.Add("你转身离开")));

		if (options.Count == 1)
		{
			options[0].Execute();
			return;
		}

		var sb = new StringBuilder($"{target.DisplayName}：");
		for (var i = 0; i < options.Count; i++)
			sb.Append($"  [{i + 1}] {options[i].Name}");
		_log.Add(sb.ToString());

		_inputModule.EnterSelection(n =>
		{
			if (n < 1 || n > options.Count) { _log.Add("无效选择"); return; }
			options[n - 1].Execute();
		}, () => _log.Add("已取消"));
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
		_log.Add(_state.WatchMode ? "看海模式已开启 🌊 世界将自动推进" : "看海模式已关闭 🎮 恢复手动控制");
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
				case "combat_bump" when e.InitiatorId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Attack_1");
					_combatUI.HandleCombatBump(e);
					break;
				case "combat_bump":
					_combatUI.HandleCombatBump(e);
					break;
				case "combat_attack" when e.TargetId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.PlayOneShot("Pain");
					break;
				case "actor_killed" when e.TargetId == _state.PlayerId:
					ResAccess.GetAnimatable(_state.PlayerId)?.Play("Die", false);
					HandlePlayerDeath("killed");
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
				case "item_picked_up" or "item_dropped":
					_groundPanel.Invalidate();
					break;
			}
		}
	}

	/// <summary>交互事件路由：流程类（trade/combat）转发到 UI Module，日志类由 LogModule 处理。</summary>
	private void DispatchInteraction(GameEvent e)
	{
		switch (e.EffectType)
		{
			case "trade":
				EnsureTradeUI().OpenTradeMenu(e);
				break;
			case "combat":
				_combatUI.OpenCombatMenu(e);
				break;
			case "talk":
				if (e.TargetId != null)
				{
					var talkTarget = ActorModule.GetById(_state, e.TargetId);
					if (talkTarget != null) { EnsureDialogUI().OpenDialog(talkTarget); break; }
				}
				_log.Add($"{e.TargetActorName}: 「……」");
				break;
			case "tame":
				_log.Add($"你成功驯服了 {e.TargetActorName}！它现在是友方了。");
				break;
			default:
				_log.Add($"[{e.InteractionName}] {e.TargetActorName}");
				break;
		}
	}

	// ══════════════════════════════════════════════════════
	//  楼梯（上行 / 下行）
	// ══════════════════════════════════════════════════════

	private void DoEnterStairs()
	{
		if (ChestOpen) CloseChestPanel();
		if (_session.TryUseStairs(out var msg))
		{
			_log.Add(msg!);
			FlushMap();
		}
		else
		{
			_log.Add("附近没有楼梯 🤷");
		}
	}

	private void DoLook() => _log.Add(LookModule.BuildLookText(_state));

	// ══════════════════════════════════════════════════════
	//  存档 / 读档
	// ══════════════════════════════════════════════════════

	/// <summary>保存游戏到指定路径。</summary>
	private void DoSave(string path, string label = "存档")
	{
		_session.SaveGame(path);
		_log.Add($"{label}已保存 💾");
	}

	/// <summary>从指定路径加载存档。加载后重建世界并确保玩家 Actor 存在。</summary>
	private void DoLoad(string path, string label = "存档")
	{
		if (_session.LoadGame(path))
		{
			_log.Add($"{label}已加载 📂 (Z{_state.PlayerZ})");
			FlushMap();
		}
		else
		{
			_log.Add($"未找到{label} ❌");
		}
	}

	// ══════════════════════════════════════════════════════
	//  渲染
	// ══════════════════════════════════════════════════════

	private void ToggleRender()
	{
		var msg = _mapRender.ToggleRenderMode();
		if (_session.GameStarted && !_menu.InMenu && msg != null)
			_log.Add(msg);
		if (!_menu.InMenu) FlushMap();
	}

	/// <summary>立即刷新地图，标记 UI 面板为脏（由 _Process 统一驱动刷新）。</summary>
	private void FlushMap()
	{
		_mapRender.Flush();
		MarkUIDirty();
	}

	/// <summary>标记所有常驻面板脏标记，下帧统一刷新。</summary>
	private void MarkUIDirty()
	{
		_statusPanelModule.Dirty = true;
		_skillBarDirty = true;
		if (_inventoryPanel.Visible) _inventoryPanel.Dirty = true;
		_groundPanel.Dirty = true;
	}

	/// <summary>在 _Process 中统一驱动脏面板刷新，避免单帧重复刷新。</summary>
	private void ProcessDirtyPanels()
	{
		if (_statusPanelModule.Dirty && _statusPanelModule.PanelNode.Visible)
		{
			var player = ActorModule.GetPlayer(_state);
			_statusPanelModule.Refresh(player, _state.PlayerZ, _state.Turn);
		}
		if (_skillBarDirty && _skillBar.Visible)
		{
			_skillBar.Refresh(ActorModule.GetPlayer(_state));
			_skillBarDirty = false;
		}
		if (_inventoryPanel.Visible && _inventoryPanel.Dirty)
			_inventoryPanel.FlushIfDirty();
		if (_groundPanel.Dirty)
			_groundPanel.FlushIfDirty();
	}

	// ══════════════════════════════════════════════════════
	//  状态面板 toggle
	// ══════════════════════════════════════════════════════

	private void ToggleStatusPanel()
	{
		var node = _statusPanelModule.PanelNode;
		if (node.Visible)
		{
			node.Visible = false;
			_panels.OnPanelClosed(_statusPanelModule);
		}
		else
		{
			node.Visible = true;
			_panels.PushFocus(_statusPanelModule);
		}

		if (node.Visible && _statusPanelModule.Dirty)
		{
			var player = ActorModule.GetPlayer(_state);
			_statusPanelModule.Refresh(player, _state.PlayerZ, _state.Turn);
		}
	}

	// ══════════════════════════════════════════════════════
	//  技能管理面板
	// ══════════════════════════════════════════════════════

	private void ToggleSkillManager()
	{
		if (_skillMgr.Visible)
		{
			_skillMgr.Close();
			_panels.OnPanelClosed(_skillMgr);
		}
		else
		{
			var player = ActorModule.GetPlayer(_state);
			_skillMgr.Open(player);
			_panels.PushFocus(_skillMgr);
		}
	}

	// ══════════════════════════════════════════════════════
	//  任务面板
	// ══════════════════════════════════════════════════════

	private void ToggleQuestPanel()
	{
		var quest = EnsureQuestPanel();
		if (quest.Visible)
		{
			quest.Close();
			_panels.OnPanelClosed(quest);
		}
		else
		{
			quest.Open(_state);
			_panels.PushFocus(quest);
		}
	}

	// ══════════════════════════════════════════════════════
	//  背包面板
	// ══════════════════════════════════════════════════════

	private void ToggleInventory()
	{
		if (_inventoryPanel.Visible)
		{
			_inventoryPanel.Visible = false;
			_panels.OnPanelClosed(_inventoryPanel);
		}
		else
		{
			_inventoryPanel.Visible = true;
			_panels.PushFocus(_inventoryPanel);
		}
		FlushMap();
	}

	// ══════════════════════════════════════════════════════
	//  小地图 / 大地图
	// ══════════════════════════════════════════════════════

	private void ToggleMinimap()
	{
		var msg = _mapRender.ToggleMinimap();
		if (msg.Length > 0) _log.Add(msg);
		FlushMap();
	}

	private void ToggleFogMap()
	{
		_log.Add(_mapRender.ToggleFogMap());
		FlushMap();
	}

	private void CenterFogMap()
	{
		_mapRender.CenterFogMap();
		FlushMap();
	}

	private void RefreshAllBorders() => _panels.RefreshBorders();
}
