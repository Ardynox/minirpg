using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core;

/// <summary>
/// 存档模块：负责游戏状态的持久化（JSON 文件）和楼层切换时的内存快照。
///
/// 两种用途：
///   1. 全局存读档：SaveGame / LoadGame → 将所有楼层写入/读取 JSON 文件
///   2. 楼层切换：SaveFloorToDict / LoadFloorFromDict → 内存中临时缓存楼层快照
///
/// 序列化策略：
///   - Cells 二维数组平铺为一维 List（FlattenCells / UnflattenCells），减小 JSON 嵌套深度
///   - 所有实体采用手动深拷贝（CopyActor / CopyCellEntity 等），保证独立副本
/// </summary>
public static class SaveModule
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	// ══════════════════════════════════════════════════════
	//  全局存读档（所有楼层 → JSON 文件）
	// ══════════════════════════════════════════════════════

	/// <summary>将当前游戏的所有楼层（含当前层）序列化为 JSON 并写入文件。</summary>
	public static void SaveGame(GameState state, string filePath)
	{
		var data = new FullSaveData
		{
			CurrentFloor = state.CurrentFloor,
			Turn = state.Turn,
			RngSeed = state.RngSeed,
			BumpAttack = state.BumpAttack,
			WatchMode = state.WatchMode,
		};

		data.Floors[state.CurrentFloor.ToString()] = SnapshotToSaveData(state);

		foreach (var (floor, floorData) in state.Floors)
		{
			if (floor == state.CurrentFloor) continue;
			data.Floors[floor.ToString()] = FloorToSaveData(floorData);
		}

		var json = JsonSerializer.Serialize(data, JsonOpts);
		EnsureDir(filePath);
		File.WriteAllText(filePath, json);
	}

	/// <summary>从 JSON 文件加载存档并恢复到 state。返回 false = 文件不存在或解析失败。</summary>
	// REVIEW: LoadGame 在异常情况下（JSON 损坏、版本不兼容）
	//         只返回 false，不提供失败原因。调用方无法区分「无存档」和「存档损坏」。
	public static bool LoadGame(GameState state, string filePath)
	{
		if (!File.Exists(filePath)) return false;

		var json = File.ReadAllText(filePath);
		var data = JsonSerializer.Deserialize<FullSaveData>(json, JsonOpts);
		if (data is null) return false;

		state.CurrentFloor = data.CurrentFloor;
		state.Turn = data.Turn;
		state.RngSeed = data.RngSeed;
		state.BumpAttack = data.BumpAttack;
		state.WatchMode = data.WatchMode;
		state.Floors.Clear();

		foreach (var (key, mapData) in data.Floors)
		{
			if (!int.TryParse(key, out var floor)) continue;
			state.Floors[floor] = SaveDataToFloor(mapData);
		}

		if (state.Floors.TryGetValue(state.CurrentFloor, out var current))
		{
			RestoreFloor(state, current);
			state.Floors.Remove(state.CurrentFloor);
		}

		return true;
	}

	// ══════════════════════════════════════════════════════
	//  楼层切换用的内存快照
	// ══════════════════════════════════════════════════════

	/// <summary>
	/// 将当前楼层数据深拷贝到 state.Floors 缓存。
	/// player Actor 不会被存入快照（由调用方负责跨楼层携带）。
	/// </summary>
	public static void SaveFloorToDict(GameState state)
	{
		var playerId = state.PlayerId;
		Actor? player = null;
		if (state.Actors.TryGetValue(playerId, out var p))
		{
			player = p;
			state.Actors.Remove(playerId);
		}

		state.Floors[state.CurrentFloor] = SnapshotFloor(state);

		if (player != null)
			state.Actors[playerId] = player;
	}

	/// <summary>从 state.Floors 缓存中恢复指定楼层。成功后从缓存中移除。</summary>
	public static bool LoadFloorFromDict(GameState state, int floor)
	{
		if (!state.Floors.TryGetValue(floor, out var data))
			return false;
		RestoreFloor(state, data);
		state.Floors.Remove(floor);
		state.CurrentFloor = floor;
		return true;
	}

	// ══════════════════════════════════════════════════════
	//  快照 / 恢复
	// ══════════════════════════════════════════════════════

	/// <summary>将 GameState 当前楼层数据深拷贝为 FloorData。</summary>
	private static FloorData SnapshotFloor(GameState state) => new()
	{
		Width = state.MapWidth,
		Height = state.MapHeight,
		Cells = CopyCells(state.Cells),
		Nests = CopyNests(state.Nests),
		Actors = CopyActors(state.Actors),
		PlayerX = state.PlayerX,
		PlayerY = state.PlayerY,
	};

	/// <summary>从 FloorData 恢复到 GameState 的当前楼层字段。</summary>
	private static void RestoreFloor(GameState state, FloorData data)
	{
		state.MapWidth = data.Width;
		state.MapHeight = data.Height;
		state.Cells = CopyCells(data.Cells);
		state.Nests = CopyNests(data.Nests);
		state.Actors = CopyActors(data.Actors);
		state.PlayerX = data.PlayerX;
		state.PlayerY = data.PlayerY;
	}

	// ══════════════════════════════════════════════════════
	//  转换：GameState ↔ MapSaveData（持久化格式）
	// ══════════════════════════════════════════════════════

	/// <summary>当前楼层 → 持久化格式（Cells 平铺）。</summary>
	private static MapSaveData SnapshotToSaveData(GameState s) => new()
	{
		Width = s.MapWidth,
		Height = s.MapHeight,
		Cells = FlattenCells(s.Cells),
		PlayerX = s.PlayerX,
		PlayerY = s.PlayerY,
		Nests = CopyNests(s.Nests),
		Actors = CopyActors(s.Actors),
	};

	/// <summary>缓存的 FloorData → 持久化格式。</summary>
	private static MapSaveData FloorToSaveData(FloorData f) => new()
	{
		Width = f.Width,
		Height = f.Height,
		Cells = FlattenCells(f.Cells),
		PlayerX = f.PlayerX,
		PlayerY = f.PlayerY,
		Nests = new List<NestData>(f.Nests),
		Actors = CopyActors(f.Actors),
	};

	/// <summary>持久化格式 → FloorData（Cells 从一维还原为二维）。</summary>
	private static FloorData SaveDataToFloor(MapSaveData d) => new()
	{
		Width = d.Width,
		Height = d.Height,
		Cells = UnflattenCells(d.Cells, d.Width, d.Height),
		Nests = d.Nests ?? [],
		Actors = d.Actors ?? new(),
		PlayerX = d.PlayerX,
		PlayerY = d.PlayerY,
	};

	// ══════════════════════════════════════════════════════
	//  深拷贝（手动逐字段复制）
	// ══════════════════════════════════════════════════════
	// REVIEW: 全部手动深拷贝，每次新增字段都必须同步修改对应的 Copy 方法。
	//         容易遗漏导致浅拷贝 bug。考虑：
	//         1. 使用 JSON 序列化/反序列化做通用深拷贝（牺牲性能换安全）
	//         2. 为每个数据类实现 ICloneable 或 record 的 with 表达式

	private static List<List<List<CellEntity>>> CopyCells(List<List<List<CellEntity>>> src)
	{
		var copy = new List<List<List<CellEntity>>>();
		foreach (var row in src)
		{
			var r = new List<List<CellEntity>>();
			foreach (var stack in row)
			{
				var s = new List<CellEntity>();
				foreach (var e in stack)
					s.Add(CopyCellEntity(e));
				r.Add(s);
			}
			copy.Add(r);
		}
		return copy;
	}

	private static CellEntity CopyCellEntity(CellEntity e) => new()
	{
		Type = e.Type,
		Glyph = e.Glyph,
		EntityId = e.EntityId,
		Meta = e.Meta != null ? new Dictionary<string, string>(e.Meta) : null,
	};

	private static List<NestData> CopyNests(List<NestData> src)
	{
		var copy = new List<NestData>();
		foreach (var n in src)
			copy.Add(new NestData
			{
				X = n.X, Y = n.Y,
				SpawnInterval = n.SpawnInterval,
				TurnsSinceSpawn = n.TurnsSinceSpawn,
				MaxSpawned = n.MaxSpawned,
				TemplateId = n.TemplateId,
			});
		return copy;
	}

	private static Dictionary<string, Actor> CopyActors(Dictionary<string, Actor> src)
	{
		var copy = new Dictionary<string, Actor>();
		foreach (var (id, a) in src)
			copy[id] = CopyActor(a);
		return copy;
	}

	private static Actor CopyActor(Actor a) => new()
	{
		Id = a.Id, X = a.X, Y = a.Y,
		Glyph = a.Glyph, DisplayName = a.DisplayName,
		Faction = a.Faction,
		BrainId = a.BrainId,
		Gold = a.Gold,
		Inventory = a.Inventory.ConvertAll(CopyItem),
		ShopSlots = a.ShopSlots.ConvertAll(CopyShopSlot),
		Limbs = a.Limbs.ConvertAll(CopyLimb),
		Race = a.Race != null ? CopyRace(a.Race) : null,
		Profession = a.Profession != null ? CopyProfession(a.Profession) : null,
		Buffs = a.Buffs.ConvertAll(CopyBuff),
		Experiences = a.Experiences.ConvertAll(CopyExperience),
	};

	private static Item CopyItem(Item i) => new()
	{
		Id = i.Id, Name = i.Name, Price = i.Price, Equipped = i.Equipped,
		Tags = new Dictionary<string, int>(i.Tags),
	};

	private static ShopSlot CopyShopSlot(ShopSlot s) => new()
	{
		Stock = s.Stock,
		Item = CopyItem(s.Item),
	};

	// BUG-FIX: 原代码遗漏了 Capacities 字段，导致存档/换层后肢体的能力权重全部丢失。
	private static Limb CopyLimb(Limb l) => new()
		{ Id = l.Id, Name = l.Name, MaxDurability = l.MaxDurability, Durability = l.Durability,
		  Capacities = new Dictionary<string, float>(l.Capacities),
		  Tags = new Dictionary<string, int>(l.Tags) };
	private static Race CopyRace(Race r) => new()
		{ Id = r.Id, Name = r.Name, Tags = new Dictionary<string, int>(r.Tags) };
	private static Profession CopyProfession(Profession p) => new()
		{ Id = p.Id, Name = p.Name, Tags = new Dictionary<string, int>(p.Tags) };
	private static Buff CopyBuff(Buff b) => new()
		{ Id = b.Id, Name = b.Name, RemainingTurns = b.RemainingTurns,
		  Tags = new Dictionary<string, int>(b.Tags) };
	private static Experience CopyExperience(Experience e) => new()
		{ Id = e.Id, Name = e.Name, Tags = new Dictionary<string, int>(e.Tags) };

	// ══════════════════════════════════════════════════════
	//  平铺 / 还原 Cells（二维 ↔ 一维）
	// ══════════════════════════════════════════════════════

	/// <summary>二维 Cells[y][x] → 一维 flat[y*w+x]，用于 JSON 序列化。</summary>
	private static List<List<CellEntity>> FlattenCells(List<List<List<CellEntity>>> cells)
	{
		var flat = new List<List<CellEntity>>();
		foreach (var row in cells)
			foreach (var stack in row)
			{
				var s = new List<CellEntity>();
				foreach (var e in stack)
					s.Add(CopyCellEntity(e));
				flat.Add(s);
			}
		return flat;
	}

	/// <summary>一维 flat[y*w+x] → 二维 Cells[y][x]，从 JSON 反序列化恢复。</summary>
	private static List<List<List<CellEntity>>> UnflattenCells(
		List<List<CellEntity>>? flat, int w, int h)
	{
		var cells = new List<List<List<CellEntity>>>();
		for (var y = 0; y < h; y++)
		{
			var row = new List<List<CellEntity>>();
			for (var x = 0; x < w; x++)
			{
				var idx = y * w + x;
				if (flat != null && idx < flat.Count)
				{
					var s = new List<CellEntity>();
					foreach (var e in flat[idx])
						s.Add(CopyCellEntity(e));
					row.Add(s);
				}
				else
				{
					row.Add([new CellEntity { Type = CellEntityType.Terrain, Glyph = "#", EntityId = Entities.Wall }]);
				}
			}
			cells.Add(row);
		}
		return cells;
	}

	/// <summary>确保文件路径的目录存在，不存在则创建。</summary>
	private static void EnsureDir(string filePath)
	{
		var dir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
	}
}

/// <summary>
/// 全局存档的根数据结构：包含所有楼层的 MapSaveData + 全局元数据。
/// </summary>
// REVIEW: Floors 的 key 是字符串（"0", "1"），而非 int。
//         这是因为 JSON 对象的 key 必须是字符串，但解析时需要 int.TryParse 转换。
//         可考虑用 List 或自定义 JsonConverter 来简化。
public class FullSaveData
{
	public int CurrentFloor { get; set; }
	public int Turn { get; set; }
	public int RngSeed { get; set; }
	public bool BumpAttack { get; set; } = true;
	public bool WatchMode { get; set; }
	public Dictionary<string, MapSaveData> Floors { get; set; } = new();
}

/// <summary>
/// 单层地图的序列化格式：Cells 平铺为一维列表以减少 JSON 嵌套层级。
/// </summary>
public class MapSaveData
{
	public int Width { get; set; }
	public int Height { get; set; }
	/// <summary>平铺的格子栈：索引 = y * Width + x，值 = 该格的实体栈。</summary>
	public List<List<CellEntity>>? Cells { get; set; }
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
	public List<NestData>? Nests { get; set; }
	public Dictionary<string, Actor>? Actors { get; set; }
}
