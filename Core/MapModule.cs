using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 地图模块：GameState.Cells 格子栈的完整 CRUD + 查询 API。
/// 每格是一个 List&lt;CellEntity&gt;，按 CellEntityType 优先级从底（Terrain=0）到顶（Effect=60）排列。
/// Actor 不存在栈中——渲染时从 GameState.Actors 动态查询。
///
/// 职责：
///   1. 初始化 / 加载地图
///   2. 栈读写（Push / Remove / Get）
///   3. 便捷查询（IsWall / IsWalkable / HasFixture …）
///   4. 楼层切换（GoDownFloor / GoUpFloor）
///   5. 兼容旧四层 API 的过渡方法（SetTerrain / SetFixture / GetFixture / GetTerrain）
/// </summary>
public static class MapModule
{
	// ══════════════════════════════════════════════════════
	//  初始化
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 从字符串行数组加载地图（用于硬编码关卡或测试）。
	/// '#' '.' → Terrain；'D' 'N' 'I' 'H' '>' '&lt;' → Fixture；
	/// 'P' → floor + 记录玩家坐标；其他未识别字符 → 默认为 floor。
	/// Actor 不在此创建，由调用方单独处理。
	/// </summary>
	// REVIEW: LoadFromStrings 假设每行等长（MapWidth = rows[0].Length），
	//         但如果 rows 各行长度不同会导致越界。应加校验或用 Max。
	public static void LoadFromStrings(GameState state, string[] rows)
	{
		state.Cells.Clear();
		state.MapHeight = rows.Length;
		state.MapWidth = rows.Length > 0 ? rows[0].Length : 0;
		state.PlayerX = -1;
		state.PlayerY = -1;

		for (var y = 0; y < rows.Length; y++)
		{
			var row = new List<List<CellEntity>>();
			for (var x = 0; x < rows[y].Length; x++)
			{
				var ch = rows[y][x].ToString();
				var stack = new List<CellEntity>();
				ClassifyCell(ch, stack);
				row.Add(stack);
				if (ch == "P")
				{
					state.PlayerX = x;
					state.PlayerY = y;
				}
			}
			state.Cells.Add(row);
		}
	}

	/// <summary>将单个字符映射为格子栈内容（Terrain + 可能的 Fixture）。</summary>
	private static void ClassifyCell(string ch, List<CellEntity> stack)
	{
		switch (ch)
		{
			case "#":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = "#", EntityId = "wall" });
				break;
			case ".":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				break;
			case "D":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "D", EntityId = "door" });
				break;
			case "N":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "N", EntityId = "nest" });
				break;
			case "I":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "I", EntityId = "item" });
				break;
			case "H":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "H", EntityId = "house" });
				break;
			case ">":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = ">", EntityId = "stair_down" });
				break;
			case "<":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "<", EntityId = "stair_up" });
				break;
			default:
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = "floor" });
				break;
		}
	}

	// ══════════════════════════════════════════════════════
	//  栈读取
	// ══════════════════════════════════════════════════════

	/// <summary>获取格子的完整栈（底→顶）。越界返回空列表。</summary>
	public static List<CellEntity> GetStack(GameState s, int x, int y) =>
		InBounds(s, x, y) ? s.Cells[y][x] : [];

	/// <summary>获取格子中指定类型的所有实体。</summary>
	public static List<CellEntity> GetByType(GameState s, int x, int y, CellEntityType type) =>
		GetStack(s, x, y).Where(e => e.Type == type).ToList();

	/// <summary>获取格子中指定类型的第一个实体。</summary>
	public static CellEntity? GetFirst(GameState s, int x, int y, CellEntityType type) =>
		GetStack(s, x, y).FirstOrDefault(e => e.Type == type);

	/// <summary>
	/// 渲染用：优先显示 Actor（从 Actors 字典动态查询），否则显示栈顶 Glyph。
	/// 栈为空时 fallback 到 "#"（墙），保证不返回空字符串。
	/// </summary>
	public static string GetDisplayCell(GameState s, int x, int y)
	{
		var actor = GetDisplayActor(s, x, y);
		if (actor != null) return actor.Glyph;

		var stack = GetStack(s, x, y);
		if (stack.Count > 0) return stack[^1].Glyph;

		return "#";
	}

	/// <summary>
	/// 同一格有多个 Actor 时的显示优先级：player > hostile > 其他。
	/// </summary>
	private static Actor? GetDisplayActor(GameState s, int x, int y)
	{
		var actors = ActorModule.GetAllAt(s, x, y);
		if (actors.Count == 0) return null;

		var best = actors[0];
		foreach (var a in actors)
		{
			if (a.Id == s.PlayerId) { best = a; break; }
			if (a.Faction == "hostile" && best.Faction != "hostile") best = a;
		}
		return best;
	}

	// ══════════════════════════════════════════════════════
	//  栈写入
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 往格子上压入一个实体，自动按 CellEntityType 排序插入以保持栈有序。
	/// 越界时静默忽略。
	/// </summary>
	public static void Push(GameState s, int x, int y, CellEntity entity)
	{
		if (!InBounds(s, x, y)) return;
		var stack = s.Cells[y][x];
		var idx = stack.Count;
		for (var i = 0; i < stack.Count; i++)
		{
			if (stack[i].Type > entity.Type)
			{
				idx = i;
				break;
			}
		}
		stack.Insert(idx, entity);
	}

	/// <summary>
	/// 从格子上移除指定 EntityId 的第一个实体。
	/// </summary>
	// REVIEW: 按 EntityId 匹配，但不限制 Type。
	//         如果不同 Type 的实体碰巧有相同 EntityId（如 "floor"），可能误删。
	//         建议增加 Type 参数或确保 EntityId 全局唯一。
	public static bool Remove(GameState s, int x, int y, string entityId)
	{
		if (!InBounds(s, x, y)) return false;
		var stack = s.Cells[y][x];
		for (var i = 0; i < stack.Count; i++)
		{
			if (stack[i].EntityId == entityId)
			{
				stack.RemoveAt(i);
				return true;
			}
		}
		return false;
	}

	/// <summary>从格子上移除指定类型的所有实体，返回移除数量。</summary>
	public static int RemoveByType(GameState s, int x, int y, CellEntityType type)
	{
		if (!InBounds(s, x, y)) return 0;
		return s.Cells[y][x].RemoveAll(e => e.Type == type);
	}

	// ══════════════════════════════════════════════════════
	//  兼容旧逻辑的便捷方法（过渡期保留，新代码应使用 Push/Remove）
	// ══════════════════════════════════════════════════════

	/// <summary>设置地形（替换已有 Terrain 实体，只保留一个）。</summary>
	// REVIEW: 直接用 Insert(0, ...) 而非 Push，因为 Terrain 优先级最低必定在栈底，
	//         这是正确的，但与 Push 的自动排序逻辑不一致（两种插入路径）。
	public static void SetTerrain(GameState s, int x, int y, string glyph)
	{
		if (!InBounds(s, x, y)) return;
		RemoveByType(s, x, y, CellEntityType.Terrain);
		var id = glyph == "#" ? "wall" : "floor";
		s.Cells[y][x].Insert(0, new CellEntity { Type = CellEntityType.Terrain, Glyph = glyph, EntityId = id });
	}

	/// <summary>设置设施（先清除已有 Fixture，再压入新的；空字符串 = 只清除）。</summary>
	// REVIEW: 每格只允许一个 Fixture（先 RemoveByType 再 Push）。
	//         格子栈设计本允许多 Fixture 共存，但此方法强制单 Fixture 语义。
	//         如果未来需要多 Fixture（如楼梯+陷阱），此方法需要重新设计。
	public static void SetFixture(GameState s, int x, int y, string glyph)
	{
		if (!InBounds(s, x, y)) return;
		RemoveByType(s, x, y, CellEntityType.Fixture);
		if (string.IsNullOrEmpty(glyph)) return;
		var id = GlyphToFixtureId(glyph);
		Push(s, x, y, new CellEntity { Type = CellEntityType.Fixture, Glyph = glyph, EntityId = id });
	}

	/// <summary>读取设施 Glyph。兼容旧代码，新代码应使用 GetFirst + HasFixture。</summary>
	// REVIEW: 只返回第一个 Fixture 的 Glyph，多 Fixture 时会丢失信息。
	//         Main.cs 中 FixtureLabel() 用 Glyph 字符串做 switch，
	//         但格子栈模型下应使用 EntityId 而非 Glyph 来判断语义。
	public static string GetFixture(GameState s, int x, int y)
	{
		var f = GetFirst(s, x, y, CellEntityType.Fixture);
		return f?.Glyph ?? "";
	}

	/// <summary>读取地形 Glyph。越界返回 "#"（墙）。</summary>
	public static string GetTerrain(GameState s, int x, int y)
	{
		if (!InBounds(s, x, y)) return "#";
		var t = GetFirst(s, x, y, CellEntityType.Terrain);
		return t?.Glyph ?? "#";
	}

	// ══════════════════════════════════════════════════════
	//  查询
	// ══════════════════════════════════════════════════════

	/// <summary>坐标是否在地图范围内。</summary>
	public static bool InBounds(GameState s, int x, int y) =>
		x >= 0 && y >= 0 && y < s.MapHeight && x < s.MapWidth;

	/// <summary>该格是否是墙（Terrain Glyph == "#"）。</summary>
	// REVIEW: 通过 Glyph 字符串 "#" 判断而非 EntityId "wall"。
	//         如果未来有非 "#" 显示的墙（如 Emoji 模式下），逻辑会失效。
	//         应改为 GetFirst(s, x, y, Terrain)?.EntityId == "wall"。
	public static bool IsWall(GameState s, int x, int y) =>
		GetTerrain(s, x, y) == "#";

	/// <summary>是否可通行：非墙即可。不考虑门是否关闭、Actor 占位等。</summary>
	public static bool IsWalkable(GameState s, int x, int y) =>
		!IsWall(s, x, y);

	/// <summary>该格是否有敌对 Actor。本质是 ActorModule 的查询，放在 MapModule 里略显越界。</summary>
	// REVIEW: 此方法是对 ActorModule.GetHostileAt 的简单包装，
	//         放在 MapModule 中违反了 Actor 查询归 ActorModule 的职责边界。
	public static bool IsHostile(GameState s, int x, int y) =>
		ActorModule.GetHostileAt(s, x, y) != null;

	/// <summary>检查指定位置是否有门。</summary>
	public static bool IsDoor(GameState s, int x, int y) =>
		HasFixture(s, x, y, "door");

	public static bool IsDownStair(GameState s, int x, int y) =>
		HasFixture(s, x, y, "stair_down");

	public static bool IsUpStair(GameState s, int x, int y) =>
		HasFixture(s, x, y, "stair_up");

	/// <summary>检查格子上是否有指定 EntityId 的 Fixture。</summary>
	// REVIEW: 内部调用 GetByType 创建临时 List 再 Any，
	//         可改为 GetStack().Any(e => e.Type == type && e.EntityId == id) 避免分配。
	public static bool HasFixture(GameState s, int x, int y, string fixtureId) =>
		GetByType(s, x, y, CellEntityType.Fixture).Any(e => e.EntityId == fixtureId);

	// ══════════════════════════════════════════════════════
	//  楼层切换
	// ══════════════════════════════════════════════════════

	/// <summary>保存当前楼层快照 → CurrentFloor++ → 尝试从缓存加载。返回 true = 命中缓存。</summary>
	// REVIEW: GoDownFloor / GoUpFloor 调用了 SaveModule，
	//         使 MapModule 对 SaveModule 产生了正向依赖。
	//         按架构图 MapModule 和 SaveModule 是同级基础服务，
	//         楼层切换编排逻辑更适合放在上层（如 Main 或 TurnModule）。
	public static bool GoDownFloor(GameState s)
	{
		SaveModule.SaveFloorToDict(s);
		s.CurrentFloor++;
		return SaveModule.LoadFloorFromDict(s, s.CurrentFloor);
	}

	/// <summary>保存当前楼层快照 → CurrentFloor-- → 从缓存加载。返回 false = 已是最顶层。</summary>
	public static bool GoUpFloor(GameState s)
	{
		if (s.CurrentFloor <= 0) return false;
		SaveModule.SaveFloorToDict(s);
		s.CurrentFloor--;
		SaveModule.LoadFloorFromDict(s, s.CurrentFloor);
		return true;
	}

	/// <summary>遍历全图找到指定 Fixture 并将玩家传送至该位置。找不到时静默返回。</summary>
	public static void PlacePlayerAtFixture(GameState s, string fixtureId)
	{
		for (var y = 0; y < s.MapHeight; y++)
		for (var x = 0; x < s.MapWidth; x++)
		{
			if (HasFixture(s, x, y, fixtureId))
			{
				ActorModule.MoveActor(s, s.PlayerId, x, y);
				return;
			}
		}
	}

	// ══════════════════════════════════════════════════════
	//  内部工具
	// ══════════════════════════════════════════════════════

	/// <summary>Glyph 字符 → Fixture EntityId 的映射表。</summary>
	private static string GlyphToFixtureId(string glyph) => glyph switch
	{
		">" => "stair_down",
		"<" => "stair_up",
		"N" => "nest",
		"D" => "door",
		"H" => "house",
		"I" => "item",
		_ => glyph,
	};

	/// <summary>初始化空地图：w×h 全部填充为墙。供 MapGenModule.Generate 使用。</summary>
	public static void InitCells(GameState state, int w, int h)
	{
		state.Cells.Clear();
		for (var y = 0; y < h; y++)
		{
			var row = new List<List<CellEntity>>();
			for (var x = 0; x < w; x++)
			{
				row.Add([new CellEntity { Type = CellEntityType.Terrain, Glyph = "#", EntityId = "wall" }]);
			}
			state.Cells.Add(row);
		}
	}
}
