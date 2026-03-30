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

	private static readonly string SavePath =
		System.IO.Path.Combine(OS.GetUserDataDir(), "save", "map.json");

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
		GetNode<Button>("SettingsPanel/VBox/CloseBtn").Pressed += ToggleSettings;

		if (!SaveModule.LoadMap(_state, SavePath))
			GenerateNewMap();

		AddLog("WASD 移动 | L 查看 | R 渲染 | ESC 设置");
		AddLog("输入: save / load / newmap / enter(进门)");
		_mapDirty = true;
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
		if (_settingsOpen && cmd != ":settings")
			return;

		switch (cmd)
		{
			case "w": DoMove(0, -1); break;
			case "s": DoMove(0, 1); break;
			case "a": DoMove(-1, 0); break;
			case "d": DoMove(1, 0); break;
			case "look": DoLook(); break;
			case "enter": DoEnterDoor(); break;
			case "save": DoSave(); break;
			case "load": DoLoad(); break;
			case "newmap": GenerateNewMap(); AddLog("新地图已生成 🗺️"); _mapDirty = true; break;
			case ":render": ToggleRender(); break;
			case "render": ToggleRender(); break;
			case ":settings": ToggleSettings(); break;
			case "settings": ToggleSettings(); break;
			default: AddLog("未知指令 ❓"); break;
		}
	}

	// ── 移动（方向键遇敌自动攻击） ───────────────────────

	private void DoMove(int dx, int dy)
	{
		var events = MapModule.TryMovePlayer(_state, dx, dy);
		_state.Turn++;
		var nestEvents = NestModule.Tick(_state);
		events.AddRange(nestEvents);
		Dispatch(events);
		_mapDirty = true;
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

	// ── 门（进入下一层） ─────────────────────────────────

	private void DoEnterDoor()
	{
		var dirs = new (int Dx, int Dy)[] { (0, 0), (0, -1), (0, 1), (-1, 0), (1, 0) };
		foreach (var (dx, dy) in dirs)
		{
			if (MapModule.GetObject(_state, _state.PlayerX + dx, _state.PlayerY + dy) == "D")
			{
				GenerateNewMap();
				AddLog("你进入了下一层 🚪");
				_mapDirty = true;
				return;
			}
		}
		AddLog("附近没有门 🤷");
	}

	// ── 查看 ──────────────────────────────────────────────

	private void DoLook()
	{
		var sb = new StringBuilder();
		sb.Append($"📍 你在 ({_state.PlayerX}, {_state.PlayerY})  回合: {_state.Turn}");
		var dirs = new (string Name, int Dx, int Dy)[]
		{
			("上", 0, -1), ("下", 0, 1), ("左", -1, 0), ("右", 1, 0),
		};
		foreach (var (name, dx, dy) in dirs)
		{
			var tx = _state.PlayerX + dx;
			var ty = _state.PlayerY + dy;
			string label;
			if (MapModule.IsWall(_state, tx, ty)) label = "墙 🚧";
			else if (MapModule.IsHostile(_state, tx, ty)) label = "怪物 👾";
			else if (MapModule.GetObject(_state, tx, ty) == "N") label = "巢穴 🕳️";
			else if (MapModule.GetObject(_state, tx, ty) == "D") label = "门 🚪";
			else label = "空地";
			sb.Append($"  {name}: {label}");
		}
		AddLog(sb.ToString());
	}

	// ── 存档/读档 ────────────────────────────────────────

	private void DoSave()
	{
		SaveModule.SaveMap(_state, SavePath);
		AddLog($"地图已保存 💾");
	}

	private void DoLoad()
	{
		if (SaveModule.LoadMap(_state, SavePath))
		{
			AddLog("存档已加载 📂");
			_mapDirty = true;
		}
		else
		{
			AddLog("未找到存档 ❌");
		}
	}

	// ── 地图生成 ──────────────────────────────────────────

	private void GenerateNewMap()
	{
		MapGenModule.Generate(_state, GenWidth, GenHeight);
	}

	// ── 渲染 ──────────────────────────────────────────────

	private void ToggleRender()
	{
		var mode = _renderModule.ToggleMode();
		_renderModule.ApplyFont(_mapPanel);
		_mapPanel.BbcodeEnabled = _renderModule.UsesBBCode;
		AddLog(mode == RenderMode.Emoji ? "渲染模式: Emoji 🎨" : "渲染模式: ASCII ⌨️");
		_mapDirty = true;
	}

	private void FlushMap()
	{
		var displayMap = BuildDisplayMap();
		var text = _renderModule.RenderMap(displayMap);
		_mapPanel.Clear();
		if (_renderModule.UsesBBCode)
		{
			_mapPanel.BbcodeEnabled = true;
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
		var result = new List<List<string>>();
		for (var y = 0; y < _state.MapHeight; y++)
		{
			var row = new List<string>();
			for (var x = 0; x < _state.MapWidth; x++)
				row.Add(MapModule.GetDisplayCell(_state, x, y));
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
