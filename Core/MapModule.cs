using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 地图模块：纯函数操作 GameState 的四层地图数据。
/// 层序：Terrain → Fixtures → Objects → Meta
/// 渲染优先级：Objects > Fixtures > Terrain
/// </summary>
public static class MapModule
{
	// ── 初始化 ─────────────────────────────────────────────

	/// <summary>
	/// 从字符串行数组加载地图。
	/// '#' '.' → Terrain；'D' 'N' 'I' → Fixtures；'P' 'M' → Objects。
	/// </summary>
	public static void LoadFromStrings(GameState state, string[] rows)
	{
		state.Terrain.Clear();
		state.Fixtures.Clear();
		state.Objects.Clear();
		state.Meta.Clear();
		state.MapHeight = rows.Length;
		state.MapWidth = rows.Length > 0 ? rows[0].Length : 0;
		state.PlayerX = -1;
		state.PlayerY = -1;

		for (var y = 0; y < rows.Length; y++)
		{
			var tRow = new List<string>();
			var fRow = new List<string>();
			var oRow = new List<string>();
			var mRow = new List<Dictionary<string, string>?>();

			for (var x = 0; x < rows[y].Length; x++)
			{
				var ch = rows[y][x].ToString();
				ClassifyCell(ch, out var terrain, out var fixture, out var obj);
				tRow.Add(terrain);
				fRow.Add(fixture);
				oRow.Add(obj);
				mRow.Add(null);
				if (ch == "P")
				{
					state.PlayerX = x;
					state.PlayerY = y;
				}
			}

			state.Terrain.Add(tRow);
			state.Fixtures.Add(fRow);
			state.Objects.Add(oRow);
			state.Meta.Add(mRow);
		}
	}

	private static void ClassifyCell(string ch, out string terrain, out string fixture, out string obj)
	{
		terrain = ".";
		fixture = "";
		obj = "";
		switch (ch)
		{
			case "#":
			case ".":
				terrain = ch;
				break;
			case "D":
			case "N":
			case "I":
			case "H":
			case ">":
			case "<":
				fixture = ch;
				break;
			default:
				obj = ch;
				break;
		}
	}

	// ── 读取 ──────────────────────────────────────────────

	public static string GetTerrain(GameState s, int x, int y) =>
		InBounds(s, x, y) ? s.Terrain[y][x] : "#";

	public static string GetFixture(GameState s, int x, int y) =>
		InBounds(s, x, y) ? s.Fixtures[y][x] : "";

	public static string GetObject(GameState s, int x, int y) =>
		InBounds(s, x, y) ? s.Objects[y][x] : "";

	public static Dictionary<string, string>? GetMeta(GameState s, int x, int y) =>
		InBounds(s, x, y) ? s.Meta[y][x] : null;

	/// <summary>渲染用：Objects > Fixtures > Terrain。</summary>
	public static string GetDisplayCell(GameState s, int x, int y)
	{
		var o = GetObject(s, x, y);
		if (!string.IsNullOrEmpty(o)) return o;
		var f = GetFixture(s, x, y);
		if (!string.IsNullOrEmpty(f)) return f;
		return GetTerrain(s, x, y);
	}

	// ── 写入 ──────────────────────────────────────────────

	public static void SetTerrain(GameState s, int x, int y, string v)
	{
		if (InBounds(s, x, y)) s.Terrain[y][x] = v;
	}

	public static void SetFixture(GameState s, int x, int y, string v)
	{
		if (InBounds(s, x, y)) s.Fixtures[y][x] = v;
	}

	public static void SetObject(GameState s, int x, int y, string v)
	{
		if (InBounds(s, x, y)) s.Objects[y][x] = v;
	}

	public static void SetMeta(GameState s, int x, int y, Dictionary<string, string>? v)
	{
		if (InBounds(s, x, y)) s.Meta[y][x] = v;
	}

	// ── 查询 ──────────────────────────────────────────────

	public static bool InBounds(GameState s, int x, int y) =>
		x >= 0 && y >= 0 && y < s.MapHeight && x < s.MapWidth;

	public static bool IsWall(GameState s, int x, int y) =>
		GetTerrain(s, x, y) == "#";

	/// <summary>是否可通行：非墙即可。角色重叠由上层逻辑处理。</summary>
	public static bool IsWalkable(GameState s, int x, int y) =>
		!IsWall(s, x, y);

	public static bool IsHostile(GameState s, int x, int y) =>
		ActorModule.GetHostileAt(s, x, y) != null;

	/// <summary>检查指定位置设施层是否有门。</summary>
	public static bool IsDoor(GameState s, int x, int y) =>
		GetFixture(s, x, y) == "D";

	public static bool IsDownStair(GameState s, int x, int y) =>
		GetFixture(s, x, y) == ">";

	public static bool IsUpStair(GameState s, int x, int y) =>
		GetFixture(s, x, y) == "<";

	// ── 楼层切换 ──────────────────────────────────────────

	/// <summary>保存当前楼层快照，切换到下一层。返回 true = 已有缓存，false = 需要生成新地图。</summary>
	public static bool GoDownFloor(GameState s)
	{
		SaveModule.SaveFloorToDict(s);
		s.CurrentFloor++;
		return SaveModule.LoadFloorFromDict(s, s.CurrentFloor);
	}

	/// <summary>保存当前楼层快照，切换到上一层。返回 false = 已经是最顶层。</summary>
	public static bool GoUpFloor(GameState s)
	{
		if (s.CurrentFloor <= 0) return false;
		SaveModule.SaveFloorToDict(s);
		s.CurrentFloor--;
		SaveModule.LoadFloorFromDict(s, s.CurrentFloor);
		return true;
	}

	/// <summary>在地图上找到指定 Fixture 并将玩家移到那里。</summary>
	public static void PlacePlayerAtFixture(GameState s, string fixtureType)
	{
		for (var y = 0; y < s.MapHeight; y++)
		for (var x = 0; x < s.MapWidth; x++)
		{
			if (GetFixture(s, x, y) == fixtureType)
			{
				ActorModule.MoveActor(s, s.PlayerId, x, y);
				return;
			}
		}
	}

	// ── 玩家移动 ──────────────────────────────────────────

	/// <summary>
	/// 尝试移动玩家。通过 ActorModule 同步 Objects 层。
	/// 目标是墙 → hit_wall；目标有敌人 → attack_hit（击杀通过 ActorModule.Remove）；
	/// 否则 → actor_moved。
	/// </summary>
	public static List<GameEvent> TryMovePlayer(GameState s, int dx, int dy)
	{
		var events = new List<GameEvent>();
		var nx = s.PlayerX + dx;
		var ny = s.PlayerY + dy;

		if (IsWall(s, nx, ny))
		{
			events.Add(new GameEvent("hit_wall"));
			return events;
		}

		var hostile = ActorModule.GetHostileAt(s, nx, ny);
		if (hostile != null)
		{
			events.Add(new GameEvent("combat_bump")
			{
				TargetX = nx, TargetY = ny,
				TargetActorName = hostile.DisplayName,
				InitiatorId = s.PlayerId,
				TargetId = hostile.Id,
			});
			return events;
		}

		ActorModule.MoveActor(s, s.PlayerId, nx, ny);
		events.Add(new GameEvent("actor_moved") { TargetX = nx, TargetY = ny, InitiatorId = s.PlayerId });
		return events;
	}
}
