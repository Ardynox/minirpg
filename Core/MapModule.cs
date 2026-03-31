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
	public static void LoadFromStrings(GameState state, string[] rows)
	{
		state.Cells.Clear();
		state.MapHeight = rows.Length;
		state.MapWidth = 0;
		foreach (var r in rows)
			if (r.Length > state.MapWidth) state.MapWidth = r.Length;
		state.PlayerX = -1;
		state.PlayerY = -1;

		for (var y = 0; y < rows.Length; y++)
		{
			var row = new List<List<CellEntity>>();
			for (var x = 0; x < state.MapWidth; x++)
			{
				if (x < rows[y].Length)
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
				else
				{
					row.Add([new CellEntity { Type = CellEntityType.Terrain, Glyph = "#", EntityId = Entities.Wall }]);
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
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = "#", EntityId = Entities.Wall });
				break;
			case ".":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
				break;
			case "D":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "D", EntityId = Entities.Door });
				break;
			case "N":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "N", EntityId = Entities.Nest });
				break;
			case "I":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "I", EntityId = Entities.Item });
				break;
			case "H":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "H", EntityId = Entities.House });
				break;
			case ">":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				break;
			case "<":
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
				stack.Add(new CellEntity { Type = CellEntityType.Fixture, Glyph = "<", EntityId = Entities.StairUp });
				break;
			default:
				stack.Add(new CellEntity { Type = CellEntityType.Terrain, Glyph = ".", EntityId = Entities.Floor });
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
			if (a.Faction == Factions.Hostile && best.Faction != Factions.Hostile) best = a;
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

	/// <summary>从格子上移除指定 Type + EntityId 的第一个实体。</summary>
	public static bool Remove(GameState s, int x, int y, CellEntityType type, string entityId)
	{
		if (!InBounds(s, x, y)) return false;
		var stack = s.Cells[y][x];
		for (var i = 0; i < stack.Count; i++)
		{
			if (stack[i].Type == type && stack[i].EntityId == entityId)
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
	//  Item 层操作
	// ══════════════════════════════════════════════════════

	/// <summary>在格子上放置一个掉落物。物品数据存储在 CellEntity.Meta 中。</summary>
	public static void PlaceItem(GameState s, int x, int y, Item item)
	{
		if (!InBounds(s, x, y)) return;
		var entity = new CellEntity
		{
			Type = CellEntityType.Item,
			Glyph = "!",
			EntityId = item.Id,
			Meta = new Dictionary<string, string>
			{
				["name"] = item.Name,
				["price"] = item.Price.ToString(),
				["tags"] = SerializeItemTags(item.Tags),
			},
		};
		Push(s, x, y, entity);
	}

	/// <summary>获取指定格子上的所有掉落物。</summary>
	public static List<CellEntity> GetGroundItems(GameState s, int x, int y) =>
		GetByType(s, x, y, CellEntityType.Item);

	/// <summary>从格子上拾取（移除）第一个匹配 EntityId 的掉落物，返回还原后的 Item。</summary>
	public static Item? PickupItem(GameState s, int x, int y, string entityId)
	{
		if (!InBounds(s, x, y)) return null;
		var stack = s.Cells[y][x];
		for (var i = 0; i < stack.Count; i++)
		{
			if (stack[i].Type != CellEntityType.Item) continue;
			if (stack[i].EntityId != entityId) continue;

			var entity = stack[i];
			stack.RemoveAt(i);
			return RestoreItemFromEntity(entity);
		}
		return null;
	}

	/// <summary>从 CellEntity.Meta 还原 Item 对象。</summary>
	private static Item RestoreItemFromEntity(CellEntity entity)
	{
		var meta = entity.Meta ?? new Dictionary<string, string>();
		return new Item
		{
			Id = entity.EntityId,
			Name = meta.GetValueOrDefault("name", entity.EntityId),
			Price = int.TryParse(meta.GetValueOrDefault("price", "0"), out var p) ? p : 0,
			Tags = DeserializeItemTags(meta.GetValueOrDefault("tags", "")),
		};
	}

	/// <summary>将格子上的所有掉落物还原为 Item 列表（不移除）。</summary>
	public static List<Item> PeekGroundItems(GameState s, int x, int y)
	{
		var items = new List<Item>();
		foreach (var e in GetGroundItems(s, x, y))
			items.Add(RestoreItemFromEntity(e));
		return items;
	}

	private static string SerializeItemTags(Dictionary<string, int> tags)
	{
		var parts = new List<string>();
		foreach (var (k, v) in tags)
			parts.Add($"{k}={v}");
		return string.Join(";", parts);
	}

	private static Dictionary<string, int> DeserializeItemTags(string raw)
	{
		var tags = new Dictionary<string, int>();
		if (string.IsNullOrEmpty(raw)) return tags;
		foreach (var pair in raw.Split(';'))
		{
			var kv = pair.Split('=', 2);
			if (kv.Length == 2 && int.TryParse(kv[1], out var val))
				tags[kv[0]] = val;
		}
		return tags;
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
		var id = glyph == "#" ? Entities.Wall : Entities.Floor;
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

	/// <summary>读取设施 Glyph（兼容旧代码）。新代码应使用 GetFixtureId 或 HasFixture。</summary>
	public static string GetFixture(GameState s, int x, int y)
	{
		var f = GetFirst(s, x, y, CellEntityType.Fixture);
		return f?.Glyph ?? "";
	}

	/// <summary>读取设施 EntityId。无设施时返回空字符串。</summary>
	public static string GetFixtureId(GameState s, int x, int y)
	{
		var f = GetFirst(s, x, y, CellEntityType.Fixture);
		return f?.EntityId ?? "";
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

	/// <summary>该格是否是墙（Terrain EntityId == "wall"）。</summary>
	public static bool IsWall(GameState s, int x, int y) =>
		GetFirst(s, x, y, CellEntityType.Terrain)?.EntityId == Entities.Wall;

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
		HasFixture(s, x, y, Entities.Door);

	public static bool IsDownStair(GameState s, int x, int y) =>
		HasFixture(s, x, y, Entities.StairDown);

	public static bool IsUpStair(GameState s, int x, int y) =>
		HasFixture(s, x, y, Entities.StairUp);

	/// <summary>检查格子上是否有指定 EntityId 的 Fixture。</summary>
	public static bool HasFixture(GameState s, int x, int y, string fixtureId) =>
		GetStack(s, x, y).Any(e => e.Type == CellEntityType.Fixture && e.EntityId == fixtureId);

	// ══════════════════════════════════════════════════════
	//  楼层切换
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 保存当前楼层快照 → CurrentFloor++ → 尝试从缓存加载。
	/// player Actor 跨楼层携带，不随快照丢失。
	/// 返回 true = 命中缓存。
	/// </summary>
	public static bool GoDownFloor(GameState s)
	{
		var player = ActorModule.GetById(s, s.PlayerId);
		SaveModule.SaveFloorToDict(s);
		s.CurrentFloor++;
		var loaded = SaveModule.LoadFloorFromDict(s, s.CurrentFloor);
		if (player != null) s.Actors[s.PlayerId] = player;
		return loaded;
	}

	/// <summary>
	/// 保存当前楼层快照 → CurrentFloor-- → 从缓存加载。
	/// 返回 false = 已是最顶层。
	/// </summary>
	public static bool GoUpFloor(GameState s)
	{
		if (s.CurrentFloor <= 0) return false;
		var player = ActorModule.GetById(s, s.PlayerId);
		SaveModule.SaveFloorToDict(s);
		s.CurrentFloor--;
		SaveModule.LoadFloorFromDict(s, s.CurrentFloor);
		if (player != null) s.Actors[s.PlayerId] = player;
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
		">" => Entities.StairDown,
		"<" => Entities.StairUp,
		"N" => Entities.Nest,
		"D" => Entities.Door,
		"H" => Entities.House,
		"I" => Entities.Item,
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
				row.Add([new CellEntity { Type = CellEntityType.Terrain, Glyph = "#", EntityId = Entities.Wall }]);
			}
			state.Cells.Add(row);
		}
	}
}
