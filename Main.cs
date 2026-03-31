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
public partial class Main : Node, IGameUI
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
	private RichTextLabel _mapPanel = null!;
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

	private PanelContainer _statusPanel = null!;
	private RichTextLabel _statusName = null!;
	private RichTextLabel _statusLimb = null!;
	private RichTextLabel _statusCap = null!;
	private RichTextLabel _statusTag = null!;
	private RichTextLabel _statusBuff = null!;
	private RichTextLabel _statusEquip = null!;

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

		_ui = GetNode<VBoxContainer>("UI");
		_mapPanel = GetNode<RichTextLabel>("UI/TopRow/MapPanel");
		_logPanel = GetNode<RichTextLabel>("UI/LogPanel");
		_settingsPanel = GetNode<PanelContainer>("SettingsPanel");
		_mainMenu = GetNode<PanelContainer>("MainMenu");
		_continueBtn = GetNode<Button>("MainMenu/Center/VBox/ContinueBtn");
		var lineEdit = GetNode<LineEdit>("UI/InputBar");

		_statusPanel = GetNode<PanelContainer>("UI/TopRow/StatusPanel");
		var statusVBox = _statusPanel.GetNode("MarginContainer/ScrollContainer/VBox");
		_statusName = statusVBox.GetNode<RichTextLabel>("NameInfo");
		_statusLimb = statusVBox.GetNode<RichTextLabel>("LimbInfo");
		_statusCap = statusVBox.GetNode<RichTextLabel>("CapInfo");
		_statusTag = statusVBox.GetNode<RichTextLabel>("TagInfo");
		_statusBuff = statusVBox.GetNode<RichTextLabel>("BuffInfo");
		_statusEquip = statusVBox.GetNode<RichTextLabel>("EquipInfo");

		_renderModule = new RenderModule();
		_renderModule.ApplyFont(_mapPanel);

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
		InitializeWorld();
		_gameStarted = true;
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

		switch (cmd)
		{
			case ":settings" or "settings": ToggleSettings(); return;
			case ":quicksave": DoSave(QuickSavePath, "快速存档"); return;
			case ":quickload": DoLoad(QuickSavePath, "快速存档"); return;
			case ":interact" or "interact": DoInteract(); return;
			case ":inventory": _inventoryUI.Open(); return;
			case ":render" or "render": ToggleRender(); return;
		}

		if (_settingsOpen) return;

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
			default: AddLog("未知指令 ❓"); break;
		}
	}

	// ══════════════════════════════════════════════════════
	//  交互流程
	// ══════════════════════════════════════════════════════

	/// <summary>触发交互：扫描周围 Actor + 脚下物品 → 选择交互目标。</summary>
	private void DoInteract()
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var targets = InteractionModule.GetAvailableTargets(_state, player);
		var groundItems = MapModule.PeekGroundItems(_state, _state.PlayerX, _state.PlayerY);

		if (targets.Count == 0 && groundItems.Count == 0)
		{
			AddLog("附近没有可交互的对象 🤷");
			return;
		}

		if (targets.Count == 1 && groundItems.Count == 0)
		{
			ShowInteractionsFor(player, targets[0]);
			return;
		}

		if (targets.Count == 0 && groundItems.Count > 0)
		{
			ShowPickupOptions(player, groundItems);
			return;
		}

		var options = new List<(string Name, Action Execute)>();
		foreach (var t in targets)
		{
			var target = t;
			options.Add((target.DisplayName, () => ShowInteractionsFor(player, target)));
		}
		if (groundItems.Count > 0)
			options.Add(($"地上的物品 ({groundItems.Count})", () => ShowPickupOptions(player, groundItems)));

		var sb = new StringBuilder("选择目标：");
		for (var i = 0; i < options.Count; i++)
			sb.Append($"  [{i + 1}] {options[i].Name}");
		AddLog(sb.ToString());

		EnterSelection(n =>
		{
			if (n < 1 || n > options.Count) { AddLog("无效选择"); return; }
			options[n - 1].Execute();
		});
	}

	/// <summary>显示脚下掉落物拾取选项。</summary>
	private void ShowPickupOptions(Actor player, List<Item> groundItems)
	{
		if (groundItems.Count == 1)
		{
			PickupGroundItem(player, groundItems[0]);
			return;
		}

		var sb = new StringBuilder("拾取物品：");
		for (var i = 0; i < groundItems.Count; i++)
			sb.Append($"  [{i + 1}] {groundItems[i].Name}");
		sb.Append($"  [{groundItems.Count + 1}] 全部拾取");
		sb.Append("  [0] 取消");
		AddLog(sb.ToString());

		EnterSelection(n =>
		{
			if (n == 0) { AddLog("取消拾取"); return; }
			if (n == groundItems.Count + 1)
			{
				foreach (var item in groundItems)
					PickupGroundItem(player, item);
				FlushMap();
				return;
			}
			if (n < 1 || n > groundItems.Count) { AddLog("无效选择"); return; }
			PickupGroundItem(player, groundItems[n - 1]);
			FlushMap();
		});
	}

	/// <summary>从地面拾取一个物品放入背包。</summary>
	private void PickupGroundItem(Actor player, Item itemInfo)
	{
		var events = InteractionModule.PickupItem(_state, player, itemInfo.Id);
		Dispatch(events);
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
					AddLog($"挖掘成功！墙壁被破坏了 ⛏️");
					break;
				case "dig_progress":
					AddLog($"挖掘中... 造成 {e.Damage} 点破坏 ⛏️");
					break;
				case "dig_failed":
					AddLog($"无法挖掘：{e.ItemName}");
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
	/// 正常楼层切换由 GoDownFloor/GoUpFloor 携带 player，不需要调用此方法。
	/// 仅在新游戏、加载存档、newmap 等场景作为 fallback 使用。
	/// </summary>
	private void EnsurePlayerActor()
	{
		if (_state.Actors.ContainsKey(_state.PlayerId))
			return;
		GD.PushWarning($"EnsurePlayerActor: player '{_state.PlayerId}' not found, creating fallback");
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
		_renderModule.ApplyFont(_mapPanel);
		if (_gameStarted && !_inMenu)
			AddLog(mode == RenderMode.Emoji ? "渲染模式: Emoji 🎨" : "渲染模式: ASCII ⌨️");
		if (!_inMenu) FlushMap();
	}

	/// <summary>立即刷新地图面板和状态面板。</summary>
	private void FlushMap()
	{
		var displayMap = BuildDisplayMap();
		var text = _renderModule.RenderMap(displayMap);
		if (_renderModule.UsesBBCode)
		{
			_mapPanel.BbcodeEnabled = true;
			_mapPanel.Clear();
			_mapPanel.AppendText(text);
		}
		else
		{
			_mapPanel.BbcodeEnabled = false;
			_mapPanel.Text = text;
		}
		RefreshStatus();
	}

	/// <summary>刷新右侧状态面板（名字/肢体/能力/标签/Buff/装备 6 个子面板）。</summary>
	private void RefreshStatus()
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null)
		{
			_statusName.Text = "";
			_statusLimb.Text = "";
			_statusCap.Text = "";
			_statusTag.Text = "";
			_statusBuff.Text = "";
			_statusEquip.Text = "";
			return;
		}

		_statusName.Clear();
		_statusName.AppendText(StatusModule.BuildNameInfo(player, _state.PlayerZ, _state.Turn));
		_statusLimb.Clear();
		_statusLimb.AppendText(StatusModule.BuildLimbInfo(player));
		_statusCap.Clear();
		_statusCap.AppendText(StatusModule.BuildCapacityInfo(player));
		_statusTag.Clear();
		_statusTag.AppendText(StatusModule.BuildTagInfo(player));
		_statusBuff.Clear();
		_statusBuff.AppendText(StatusModule.BuildBuffInfo(player));
		_statusEquip.Clear();
		_statusEquip.AppendText(StatusModule.BuildEquipInfo(player));
	}

	/// <summary>委托给当前 IViewMode 构建显示地图。</summary>
	private List<List<string>> BuildDisplayMap() =>
		_viewMode.BuildDisplayMap(_state, ViewW, ViewH);

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
}
