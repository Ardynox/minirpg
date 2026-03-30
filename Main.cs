using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using MiniRPG.Core;
using MiniRPG.Module;

namespace MiniRPG;

public partial class Main : Node, IGameUI
{
	private const int MaxLogLines = 30;
	private const double MapFps = 10.0;
	private const int GenWidth = 40;
	private const int GenHeight = 24;
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
	private bool _mapDirty;
	private double _renderTimer;
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
	private InputModule _inputModule = null!;
	private RenderModule _renderModule = null!;

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

	// ── IGameUI ──────────────────────────────────────────

	public GameState State => _state;
	public bool PlayerDead { get; set; }

	void IGameUI.AddLog(string msg) => AddLog(msg);
	void IGameUI.EnterSelection(Action<int> callback) => EnterSelection(callback);
	void IGameUI.CancelSelection() => CancelSelection();
	void IGameUI.FlushMap() => FlushMap();
	void IGameUI.Dispatch(List<GameEvent> events) => Dispatch(events);

	// ── Godot 生命周期 ───────────────────────────────────

	public override void _Ready()
	{
		PresetDB.Load();

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

	public override void _Process(double delta)
	{
		if (_inMenu) return;
		_renderTimer += delta;
		if (!_mapDirty || _renderTimer < 1.0 / MapFps)
			return;
		_renderTimer = 0;
		_mapDirty = false;
		FlushMap();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_inMenu) return;
		if (@event is InputEventKey key && _inputModule.HandleKeyInput(key))
			GetViewport().SetInputAsHandled();
	}

	// ── 主菜单 ───────────────────────────────────────────

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

	private void EnterGame()
	{
		_inMenu = false;
		_mainMenu.Visible = false;
		_ui.Visible = true;
		_inputModule.EnterActionMode();
		FlushMap();
	}

	private void MenuContinue()
	{
		if (SaveModule.LoadGame(_state, QuickSavePath)
			|| SaveModule.LoadGame(_state, ManualSavePath))
		{
			EnsurePlayerActor();
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

	private void MenuNewGame()
	{
		StartNewGame();
		ShowGameHints();
		EnterGame();
	}

	private void MenuLoadGame()
	{
		if (SaveModule.LoadGame(_state, ManualSavePath))
		{
			EnsurePlayerActor();
			_logLines.Clear();
			AddLog("存档已加载 📂");
		}
		else if (SaveModule.LoadGame(_state, QuickSavePath))
		{
			EnsurePlayerActor();
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

	private void MenuSettings()
	{
		_settingsFromMenu = true;
		_settingsPanel.Visible = true;
		_mainMenu.Visible = false;
		UpdateSettingsContext();
	}

	private void MenuQuit() => GetTree().Quit();

	private void BackToMenu()
	{
		_settingsOpen = false;
		_settingsPanel.Visible = false;
		if (!_gameStarted) { ShowMainMenu(); return; }
		DoSave(QuickSavePath, "快速存档");
		ShowMainMenu();
	}

	private void StartNewGame()
	{
		_state.Reset();
		_logLines.Clear();
		GenerateNewMap();
		EnsurePlayerActor();
		_gameStarted = true;
		AddLog("新游戏开始 🗺️");
	}

	private void ShowGameHints()
	{
		_gameStarted = true;
		AddLog("WASD 移动 | L 查看 | R 渲染 | ESC 设置");
		AddLog("空格 上下楼 | F 交互 | I 背包 | F5 快存 | F9 快读");
	}

	// ── 选择模式 ─────────────────────────────────────────

	private void EnterSelection(Action<int> callback)
	{
		_selectionCallback = callback;
		_inputModule.EnterSelectionMode();
	}

	private void CancelSelection()
	{
		_selectionCallback = null;
		if (_inputModule != null)
			_inputModule.EnterActionMode();
	}

	// ── 命令分发 ──────────────────────────────────────────

	private void OnCommand(string cmd)
	{
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
			case ":settings": ToggleSettings(); return;
			case ":quicksave": DoSave(QuickSavePath, "快速存档"); return;
			case ":quickload": DoLoad(QuickSavePath, "快速存档"); return;
			case ":interact": DoInteract(); return;
			case ":inventory": _inventoryUI.Open(); return;
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
			case "interact": DoInteract(); break;
			case "save": DoSave(ManualSavePath); break;
			case "load": DoLoad(ManualSavePath); break;
			case "newmap":
				_state.Reset();
				GenerateNewMap();
				EnsurePlayerActor();
				AddLog("新地图已生成 🗺️");
				FlushMap();
				break;
			case ":render": ToggleRender(); break;
			case "render": ToggleRender(); break;
			case "settings": ToggleSettings(); break;
			default: AddLog("未知指令 ❓"); break;
		}
	}

	// ── 交互 ──────────────────────────────────────────────

	private void DoInteract()
	{
		var player = ActorModule.GetPlayer(_state);
		if (player == null) return;

		var targets = InteractionModule.GetAvailableTargets(_state);
		if (targets.Count == 0)
		{
			AddLog("附近没有可交互的对象 🤷");
			return;
		}

		if (targets.Count == 1)
		{
			ShowInteractionsFor(player, targets[0]);
			return;
		}

		var sb = new StringBuilder("选择目标：");
		for (var i = 0; i < targets.Count; i++)
			sb.Append($"  [{i + 1}] {targets[i].DisplayName}");
		AddLog(sb.ToString());

		EnterSelection(n =>
		{
			if (n < 1 || n > targets.Count) { AddLog("无效选择"); return; }
			ShowInteractionsFor(player, targets[n - 1]);
		});
	}

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

	// ── 移动 ──────────────────────────────────────────────

	private void DoMove(int dx, int dy)
	{
		if (PlayerDead)
		{
			PlayerDead = false;
			ShowMainMenu();
			return;
		}
		var events = MapModule.TryMovePlayer(_state, dx, dy);
		_state.Turn++;
		var nestEvents = NestModule.Tick(_state);
		events.AddRange(nestEvents);
		Dispatch(events);
		FlushMap();
	}

	// ── 事件分发 ──────────────────────────────────────────

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
				{
					AddLog($"⚔️ {e.ActionName} → {e.TargetActorName}的{e.LimbName}，造成{e.Damage}点伤害");
					var hitTarget = e.TargetId != null ? ActorModule.GetById(_state, e.TargetId) : null;
					if (hitTarget != null)
					{
						var hl = hitTarget.Limbs.Find(l => l.Name == e.LimbName);
						if (hl != null)
							AddLog($"   {e.LimbName} ({hl.Durability}/{hl.MaxDurability})");
					}
					break;
				}
				case "combat_block":
					AddLog($"🛡️ {e.TargetActorName}使用了{e.ActionName}！防御+5 (1回合)");
					break;
				case "limb_destroyed":
					AddLog($"💥 {e.TargetActorName}的{e.LimbName}被摧毁了！");
					break;
				case "actor_killed":
					_combatUI.HandleActorKilled(e);
					break;
				case "actor_incapacitated":
					AddLog($"😵 {e.TargetActorName}失去了意识！");
					break;
				case "player_limb_hit":
					AddLog($"🩸 {e.TargetActorName}攻击了你的{e.LimbName}，造成{e.Damage}点伤害");
					break;
				case "player_died":
					AddLog("💀 你死了……");
					AddLog("按任意方向键返回主菜单");
					break;
			}
		}
	}

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

	// ── 楼梯（上行 / 下行） ──────────────────────────────

	private void DoEnterStairs()
	{
		var px = _state.PlayerX;
		var py = _state.PlayerY;
		var dirs = new (int Dx, int Dy)[] { (0, 0), (0, -1), (0, 1), (-1, 0), (1, 0) };

		foreach (var (dx, dy) in dirs)
		{
			var fixture = MapModule.GetFixture(_state, px + dx, py + dy);
			switch (fixture)
			{
				case ">": GoDown(); return;
				case "<": GoUp(); return;
			}
		}
		AddLog("附近没有楼梯 🤷");
	}

	private void GoDown()
	{
		if (MapModule.GoDownFloor(_state))
		{
			EnsurePlayerActor();
			MapModule.PlacePlayerAtFixture(_state, "<");
			AddLog($"你回到了第 {_state.CurrentFloor} 层 ⬇️");
		}
		else
		{
			GenerateNewMap();
			AddLog($"你进入了第 {_state.CurrentFloor} 层 ⬇️");
		}
		FlushMap();
	}

	private void GoUp()
	{
		if (!MapModule.GoUpFloor(_state))
		{
			AddLog("已经是最顶层 🚫");
			return;
		}
		EnsurePlayerActor();
		MapModule.PlacePlayerAtFixture(_state, ">");
		AddLog($"你回到了第 {_state.CurrentFloor} 层 ⬆️");
		FlushMap();
	}

	// ── 查看 ──────────────────────────────────────────────

	private void DoLook()
	{
		var sb = new StringBuilder();
		var player = ActorModule.GetPlayer(_state);
		sb.Append($"📍 第 {_state.CurrentFloor} 层 ({_state.PlayerX}, {_state.PlayerY})  回合: {_state.Turn}");
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

		var standingOn = MapModule.GetFixture(_state, _state.PlayerX, _state.PlayerY);
		if (!string.IsNullOrEmpty(standingOn))
			sb.Append($"  脚下: {FixtureLabel(standingOn)}");

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

	private string CellLabel(int x, int y)
	{
		if (MapModule.IsWall(_state, x, y)) return "墙 🚧";
		var actors = ActorModule.GetAllAt(_state, x, y);
		if (actors.Count > 0)
		{
			var names = actors.ConvertAll(a => a.DisplayName);
			return string.Join("+", names);
		}
		var f = MapModule.GetFixture(_state, x, y);
		if (!string.IsNullOrEmpty(f)) return FixtureLabel(f);
		return "空地";
	}

	private static string FixtureLabel(string f) => f switch
	{
		">" => "下行楼梯 ⬇️",
		"<" => "上行楼梯 ⬆️",
		"N" => "巢穴 🕳️",
		"H" => "房屋 🏠",
		"I" => "道具 📦",
		_ => f,
	};

	// ── 存档 / 读档 ──────────────────────────────────────

	private void DoSave(string path, string label = "存档")
	{
		SaveModule.SaveGame(_state, path);
		AddLog($"{label}已保存 💾");
	}

	private void DoLoad(string path, string label = "存档")
	{
		if (SaveModule.LoadGame(_state, path))
		{
			EnsurePlayerActor();
			AddLog($"{label}已加载 📂 (第 {_state.CurrentFloor} 层)");
			FlushMap();
		}
		else
		{
			AddLog($"未找到{label} ❌");
		}
	}

	private void GenerateNewMap()
	{
		MapGenModule.Generate(_state, GenWidth, GenHeight, _state.CurrentFloor);
	}

	private void EnsurePlayerActor()
	{
		if (_state.Actors.ContainsKey(_state.PlayerId))
			return;
		var player = ActorTemplates.Spawn("player", _state.PlayerId);
		player.X = _state.PlayerX;
		player.Y = _state.PlayerY;
		_state.Actors[player.Id] = player;
		ActorModule.RefreshObjectsCell(_state, player.X, player.Y);
	}

	// ── 渲染 ──────────────────────────────────────────────

	private void ToggleRender()
	{
		var mode = _renderModule.ToggleMode();
		_renderModule.ApplyFont(_mapPanel);
		if (_gameStarted && !_inMenu)
			AddLog(mode == RenderMode.Emoji ? "渲染模式: Emoji 🎨" : "渲染模式: ASCII ⌨️");
		if (!_inMenu) FlushMap();
	}

	private void FlushMap()
	{
		_mapDirty = false;
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
		_statusName.AppendText(StatusModule.BuildNameInfo(player, _state.CurrentFloor, _state.Turn));
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

	private List<List<string>> BuildDisplayMap()
	{
		var cx = _state.PlayerX;
		var cy = _state.PlayerY;
		var halfW = ViewW / 2;
		var halfH = ViewH / 2;

		var result = new List<List<string>>();
		for (var vy = 0; vy < ViewH; vy++)
		{
			var row = new List<string>();
			var my = cy - halfH + vy;
			for (var vx = 0; vx < ViewW; vx++)
			{
				var mx = cx - halfW + vx;
				row.Add(MapModule.InBounds(_state, mx, my)
					? MapModule.GetDisplayCell(_state, mx, my)
					: "#");
			}
			result.Add(row);
		}
		return result;
	}

	// ── 设置面板 ──────────────────────────────────────────

	private void UpdateSettingsContext()
	{
		var inGame = _gameStarted && !_settingsFromMenu;
		_settingSaveBtn.Visible = inGame;
		_settingLoadBtn.Visible = inGame;
		_settingBackToMenuBtn.Visible = inGame;
	}

	private void ToggleSettings()
	{
		_settingsFromMenu = false;
		_settingsOpen = !_settingsOpen;
		_settingsPanel.Visible = _settingsOpen;
		if (_settingsOpen) UpdateSettingsContext();
	}

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

	// ── 日志 ──────────────────────────────────────────────

	private void AddLog(string msg)
	{
		_logLines.Add(msg);
		if (_logLines.Count > MaxLogLines)
			_logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
		_logPanel.Text = string.Join("\n", _logLines);
	}
}
