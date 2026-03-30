using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 地图模块：纯函数操作 GameState 的三层地图数据。
/// 不引用任何 Godot 节点。
/// </summary>
public static class MapModule
{
	// ── 初始化 ─────────────────────────────────────────────

	/// <summary>
	/// 从字符串行数组加载地图。
	/// 字符串中 '#' '.' 写入 Terrain 层；'P' 'M' 等写入 Objects 层（地形填 '.'）。
	/// Meta 层初始化为 null。
	/// </summary>
	public static void LoadFromStrings(GameState state, string[] rows)
	{
		state.Terrain.Clear();
		state.Objects.Clear();
		state.Meta.Clear();
		state.MapHeight = rows.Length;
		state.MapWidth = rows.Length > 0 ? rows[0].Length : 0;
		state.PlayerX = -1;
		state.PlayerY = -1;

		for (var y = 0; y < rows.Length; y++)
		{
			var terrainRow = new List<string>();
			var objectRow = new List<string>();
			var metaRow = new List<Dictionary<string, string>?>();

			for (var x = 0; x < rows[y].Length; x++)
			{
				var ch = rows[y][x].ToString();
				switch (ch)
				{
					case "#":
					case ".":
						terrainRow.Add(ch);
						objectRow.Add("");
						break;
					default:
						terrainRow.Add(".");
						objectRow.Add(ch);
						if (ch == "P")
						{
							state.PlayerX = x;
							state.PlayerY = y;
						}
						break;
				}
				metaRow.Add(null);
			}

			state.Terrain.Add(terrainRow);
			state.Objects.Add(objectRow);
			state.Meta.Add(metaRow);
		}
	}

	// ── 读取 ──────────────────────────────────────────────

	public static string GetTerrain(GameState state, int x, int y) =>
		InBounds(state, x, y) ? state.Terrain[y][x] : "#";

	public static string GetObject(GameState state, int x, int y) =>
		InBounds(state, x, y) ? state.Objects[y][x] : "";

	public static Dictionary<string, string>? GetMeta(GameState state, int x, int y) =>
		InBounds(state, x, y) ? state.Meta[y][x] : null;

	/// <summary>返回用于渲染的合成字符：Objects 层优先，空则取 Terrain 层。</summary>
	public static string GetDisplayCell(GameState state, int x, int y)
	{
		var obj = GetObject(state, x, y);
		return string.IsNullOrEmpty(obj) ? GetTerrain(state, x, y) : obj;
	}

	// ── 写入 ──────────────────────────────────────────────

	public static void SetTerrain(GameState state, int x, int y, string value)
	{
		if (InBounds(state, x, y)) state.Terrain[y][x] = value;
	}

	public static void SetObject(GameState state, int x, int y, string value)
	{
		if (InBounds(state, x, y)) state.Objects[y][x] = value;
	}

	public static void SetMeta(GameState state, int x, int y, Dictionary<string, string>? value)
	{
		if (InBounds(state, x, y)) state.Meta[y][x] = value;
	}

	// ── 查询 ──────────────────────────────────────────────

	public static bool InBounds(GameState state, int x, int y) =>
		x >= 0 && y >= 0 && y < state.MapHeight && x < state.MapWidth;

	public static bool IsWall(GameState state, int x, int y) =>
		GetTerrain(state, x, y) == "#";

	public static bool IsWalkable(GameState state, int x, int y) =>
		!IsWall(state, x, y) && string.IsNullOrEmpty(GetObject(state, x, y));

	public static bool IsHostile(GameState state, int x, int y) =>
		GetObject(state, x, y) == "M";

	// ── 玩家移动（修改 Objects 层 + PlayerX/Y） ──────────

	/// <summary>
	/// 尝试移动玩家。返回事件列表。
	/// 目标是墙 → hit_wall；目标有敌人 → attack；目标可通行 → actor_moved。
	/// </summary>
	public static List<GameEvent> TryMovePlayer(GameState state, int dx, int dy)
	{
		var events = new List<GameEvent>();
		var nx = state.PlayerX + dx;
		var ny = state.PlayerY + dy;

		if (IsWall(state, nx, ny))
		{
			events.Add(new GameEvent("hit_wall"));
			return events;
		}

		if (IsHostile(state, nx, ny))
		{
			events.Add(new GameEvent("attack_hit") { TargetX = nx, TargetY = ny });
			SetObject(state, nx, ny, "");
			return events;
		}

		SetObject(state, state.PlayerX, state.PlayerY, "");
		state.PlayerX = nx;
		state.PlayerY = ny;
		SetObject(state, nx, ny, "P");
		events.Add(new GameEvent("actor_moved") { TargetX = nx, TargetY = ny });
		return events;
	}

	/// <summary>检查玩家四周是否有敌人，返回方向或 null。</summary>
	public static (int Dx, int Dy)? FindAdjacentHostile(GameState state)
	{
		var dirs = new[] { (0, -1), (0, 1), (-1, 0), (1, 0) };
		foreach (var (dx, dy) in dirs)
		{
			if (IsHostile(state, state.PlayerX + dx, state.PlayerY + dy))
				return (dx, dy);
		}
		return null;
	}
}
