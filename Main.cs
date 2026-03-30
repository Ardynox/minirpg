using Godot;
using System.Collections.Generic;
using System.Text;
using MiniRPG.Core;
using MiniRPG.Module;

namespace MiniRPG;

public partial class Main : Node
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

	private RichTextLabel _mapPanel = null!;
	private RichTextLabel _logPanel = null!;
	private PanelContainer _settingsPanel = null!;
	private InputModule _inputModule = null!;
	private RenderModule _renderModule = null!;

	public override void _Ready()
	{
		_mapPanel = GetNode<RichTextLabel>("UI/MapPanel");
		_logPanel = GetNode<RichTextLabel>("UI/LogPanel");
		_settingsPanel = GetNode<PanelContainer>("SettingsPanel");
		var lineEdit = GetNode<LineEdit>("UI/Input");

		_renderModule = new RenderModule();
		_renderModule.ApplyFont(_mapPanel);

		_inputModule = new InputModule(lineEdit);
		_inputModule.CommandReceived += OnCommand;

		GetNode<Button>("SettingsPanel/VBox/RenderToggle").Pressed += ToggleRender;
		GetNode<Button>("SettingsPanel/VBox/SaveBtn").Pressed += () => DoSave(ManualSavePath);
		GetNode<Button>("SettingsPanel/VBox/LoadBtn").Pressed += () => DoLoad(ManualSavePath);
		GetNode<Button>("SettingsPanel/VBox/CloseBtn").Pressed += ToggleSettings;

		if (SaveModule.LoadGame(_state, QuickSavePath))
			AddLog("快速存档已加载 📂");
		else if (SaveModule.LoadGame(_state, ManualSavePath))
			AddLog("存档已加载 📂");
		else
			GenerateNewMap();

		AddLog("WASD 移动 | L 查看 | R 渲染 | ESC 设置");
		AddLog("空格 上下楼 | F5 快存 | F9 快读");
		FlushMap();
	}

	public override void _Process(double delta)
	{
		_renderTimer += delta;
		if (!_mapDirty || _renderTimer < 1.0 / MapFps)
			return;
		_renderTimer = 0;
		_mapDirty = false;
		FlushMap();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && _inputModule.HandleKeyInput(key))
			GetViewport().SetInputAsHandled();
	}

	// ── 命令分发 ──────────────────────────────────────────

	private void OnCommand(string cmd)
	{
		switch (cmd)
		{
			case ":settings": ToggleSettings(); return;
			case ":quicksave": DoSave(QuickSavePath, "快速存档"); return;
			case ":quickload": DoLoad(QuickSavePath, "快速存档"); return;
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
				_state.Floors.Clear();
				_state.CurrentFloor = 0;
				GenerateNewMap();
				AddLog("新地图已生成 🗺️");
				FlushMap();
				break;
			case ":render": ToggleRender(); break;
			case "render": ToggleRender(); break;
			case "settings": ToggleSettings(); break;
			default: AddLog("未知指令 ❓"); break;
		}
	}

	// ── 移动 ──────────────────────────────────────────────

	private void DoMove(int dx, int dy)
	{
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
				case "attack_hit":
					AddLog("你发动攻击 ⚔️ — 命中目标 💥");
					break;
				case "actor_moved":
					break;
				case "monster_spawned":
					AddLog($"巢穴刷出怪物 👾 ({e.TargetX},{e.TargetY})");
					break;
			}
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
		SaveModule.SaveFloorToDict(_state);
		_state.CurrentFloor++;
		if (SaveModule.LoadFloorFromDict(_state, _state.CurrentFloor))
		{
			PlacePlayerAtFixture("<");
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
		if (_state.CurrentFloor <= 0)
		{
			AddLog("已经是最顶层 🚫");
			return;
		}
		SaveModule.SaveFloorToDict(_state);
		_state.CurrentFloor--;
		SaveModule.LoadFloorFromDict(_state, _state.CurrentFloor);
		PlacePlayerAtFixture(">");
		AddLog($"你回到了第 {_state.CurrentFloor} 层 ⬆️");
		FlushMap();
	}

	private void PlacePlayerAtFixture(string fixtureType)
	{
		MapModule.SetObject(_state, _state.PlayerX, _state.PlayerY, "");
		for (var y = 0; y < _state.MapHeight; y++)
		for (var x = 0; x < _state.MapWidth; x++)
		{
			if (MapModule.GetFixture(_state, x, y) == fixtureType)
			{
				_state.PlayerX = x;
				_state.PlayerY = y;
				MapModule.SetObject(_state, x, y, "P");
				return;
			}
		}
	}

	// ── 查看 ──────────────────────────────────────────────

	private void DoLook()
	{
		var sb = new StringBuilder();
		sb.Append($"📍 第 {_state.CurrentFloor} 层 ({_state.PlayerX}, {_state.PlayerY})  回合: {_state.Turn}");
		var standingOn = MapModule.GetFixture(_state, _state.PlayerX, _state.PlayerY);
		if (!string.IsNullOrEmpty(standingOn))
			sb.Append($"  脚下: {FixtureLabel(standingOn)}");

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
		if (MapModule.IsHostile(_state, x, y)) return "怪物 👾";
		var f = MapModule.GetFixture(_state, x, y);
		if (!string.IsNullOrEmpty(f)) return FixtureLabel(f);
		return "空地";
	}

	private static string FixtureLabel(string f) => f switch
	{
		">" => "下行楼梯 ⬇️",
		"<" => "上行楼梯 ⬆️",
		"N" => "巢穴 🕳️",
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

	// ── 渲染 ──────────────────────────────────────────────

	private void ToggleRender()
	{
		var mode = _renderModule.ToggleMode();
		_renderModule.ApplyFont(_mapPanel);
		AddLog(mode == RenderMode.Emoji ? "渲染模式: Emoji 🎨" : "渲染模式: ASCII ⌨️");
		FlushMap();
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

	private void ToggleSettings()
	{
		_settingsOpen = !_settingsOpen;
		_settingsPanel.Visible = _settingsOpen;
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
