using Godot;
using System.Collections.Generic;
using System.Text;
using MiniRPG.Module;

namespace MiniRPG;

public partial class Main : Node
{
	private const int MaxLogLines = 30;

	private static readonly string[] MapRows =
	{
		"####################",
		"#P.................#",
		"#.##..........##...#",
		"#.#............#...#",
		"#.#...M....M...#...#",
		"#.#............#...#",
		"#.##..........##...#",
		"#..................#",
		"####################",
	};

	private const double MapFps = 10.0;

	private readonly List<List<string>> _map = [];
	private int _playerX;
	private int _playerY;
	private readonly List<string> _logLines = [];
	private bool _mapDirty;
	private double _renderTimer;

	private RichTextLabel _mapPanel = null!;
	private RichTextLabel _logPanel = null!;
	private InputModule _inputModule = null!;

	public override void _Ready()
	{
		_mapPanel = GetNode<RichTextLabel>("UI/MapPanel");
		_logPanel = GetNode<RichTextLabel>("UI/LogPanel");
		var lineEdit = GetNode<LineEdit>("UI/Input");

		SetupMapFont();

		_inputModule = new InputModule(lineEdit);
		_inputModule.CommandReceived += OnCommand;

		InitMap();
		AddLog("WASD 移动 | J 攻击 | L 查看 | Enter 打字输入");
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

	private void SetupMapFont()
	{
		var font = new SystemFont();
		font.FontNames = ["Consolas", "Courier New", "Liberation Mono", "monospace"];
		_mapPanel.AddThemeFontOverride("normal_font", font);
		_mapPanel.AddThemeFontSizeOverride("normal_font_size", 22);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && _inputModule.HandleKeyInput(key))
			GetViewport().SetInputAsHandled();
	}

	private void OnCommand(string cmd)
	{
		switch (cmd)
		{
			case "w": Move(0, -1); break;
			case "s": Move(0, 1); break;
			case "a": Move(-1, 0); break;
			case "d": Move(1, 0); break;
			case "atk": Attack(); break;
			case "look": Look(); break;
			default: AddLog("未知指令 ❓"); break;
		}
	}

	// ── 地图 ──────────────────────────────────────────────

	private void InitMap()
	{
		_map.Clear();
		_playerX = -1;
		_playerY = -1;
		for (var y = 0; y < MapRows.Length; y++)
		{
			var rowStr = MapRows[y];
			var line = new List<string>();
			for (var x = 0; x < rowStr.Length; x++)
			{
				var ch = rowStr[x].ToString();
				if (ch == "P")
				{
					_playerX = x;
					_playerY = y;
				}
				line.Add(ch);
			}
			_map.Add(line);
		}

		if (_playerX < 0 || _playerY < 0)
			GD.PushError("MAP_ROWS 中未找到 P（玩家起点）");
	}

	/// <summary>等宽字符 + BBCode 着色，彻底解决 emoji 宽度不一致导致的错位。</summary>
	private static string CellBBCode(string c) => c switch
	{
		"#" => "[color=#555555]██[/color]",
		"." => "[color=#333333]· [/color]",
		"P" => "[color=#44ee44]@·[/color]",
		"M" => "[color=#ee4444]M·[/color]",
		_ => c + " ",
	};

	private string MapBBCode()
	{
		var sb = new StringBuilder();
		foreach (var row in _map)
		{
			foreach (var c in row)
				sb.Append(CellBBCode(c));
			sb.Append('\n');
		}
		return sb.ToString();
	}

	private void RenderMap() => _mapDirty = true;

	private void FlushMap()
	{
		_mapPanel.Clear();
		_mapPanel.AppendText(MapBBCode());
	}

	// ── 移动 ──────────────────────────────────────────────

	private void Move(int dx, int dy)
	{
		var nx = _playerX + dx;
		var ny = _playerY + dy;
		var target = _map[ny][nx];

		if (target == "#")
		{
			AddLog("撞墙了 🚧");
			return;
		}
		if (target == "M")
		{
			AddLog("遇到怪物 👾，请使用 J 或 atk 攻击");
			return;
		}
		UpdatePlayer(nx, ny);
	}

	private void UpdatePlayer(int nx, int ny)
	{
		_map[_playerY][_playerX] = ".";
		_playerX = nx;
		_playerY = ny;
		_map[_playerY][_playerX] = "P";
		RenderMap();
	}

	// ── 战斗 ──────────────────────────────────────────────

	private void Attack()
	{
		var dirs = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
		foreach (var (dx, dy) in dirs)
		{
			if (_map[_playerY + dy][_playerX + dx] != "M")
				continue;
			AddLog("你发动攻击 ⚔️");
			AddLog("命中目标 💥");
			_map[_playerY + dy][_playerX + dx] = ".";
			RenderMap();
			return;
		}
		AddLog("附近没有敌人 🤷");
	}

	// ── 查看 ──────────────────────────────────────────────

	private void Look()
	{
		var sb = new StringBuilder();
		sb.Append($"📍 你在 ({_playerX}, {_playerY})");
		var dirs = new (string Name, int Dx, int Dy)[]
		{
			("上", 0, -1), ("下", 0, 1), ("左", -1, 0), ("右", 1, 0),
		};
		foreach (var (name, dx, dy) in dirs)
		{
			var cell = _map[_playerY + dy][_playerX + dx];
			var label = cell switch
			{
				"#" => "墙 🚧",
				"." => "空地",
				"M" => "怪物 👾",
				_ => cell,
			};
			sb.Append($"  {name}: {label}");
		}
		AddLog(sb.ToString());
	}

	// ── 日志 ──────────────────────────────────────────────

	private void AddLog(string msg)
	{
		_logLines.Add(msg);
		if (_logLines.Count > MaxLogLines)
			_logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
		RefreshLog();
	}

	private void RefreshLog()
	{
		_logPanel.Text = string.Join("\n", _logLines);
	}
}
