using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Map;

/// <summary>
/// 存档模块：负责游戏状态的持久化。
///
/// 无限世界存档策略：
/// - 未修改的 chunk 可以从种子重新生成，不需要存档
/// - 只存储被修改过的 dirty chunk（挖掘、建造等）
/// - Actor 表、玩家位置、世界种子、设置等存在全局存档中
/// </summary>
public static class SaveModule
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
	};

	// ══════════════════════════════════════════════════════
	//  全局存读档
	// ══════════════════════════════════════════════════════

	public static void SaveGame(GameState state, string filePath)
	{
		var data = new WorldSaveData
		{
			WorldSeed = state.WorldSeed,
			Turn = state.Turn,
			PlayerX = state.PlayerX,
			PlayerY = state.PlayerY,
			PlayerZ = state.PlayerZ,
			BumpAttack = state.BumpAttack,
			WatchMode = state.WatchMode,
			KillCount = state.KillCount,
			GeneratorId = state.GeneratorId,
			ViewModeId = state.ViewModeId,
			Actors = CopyActors(state.Actors),
		};

		if (state.World != null)
		{
			foreach (var (coord, chunk) in state.World.Chunks.LoadedChunks)
			{
				if (!chunk.Dirty) continue;
				data.DirtyChunks.Add(SerializeChunk(coord, chunk));
			}
		}

		var json = JsonSerializer.Serialize(data, JsonOpts);
		EnsureDir(filePath);
		File.WriteAllText(filePath, json);
	}

	public static bool LoadGame(GameState state, string filePath)
	{
		if (!File.Exists(filePath)) return false;

		var json = File.ReadAllText(filePath);
		var data = JsonSerializer.Deserialize<WorldSaveData>(json, JsonOpts);
		if (data == null) return false;

		state.WorldSeed = data.WorldSeed;
		state.Turn = data.Turn;
		state.PlayerX = data.PlayerX;
		state.PlayerY = data.PlayerY;
		state.PlayerZ = data.PlayerZ;
		state.BumpAttack = data.BumpAttack;
		state.WatchMode = data.WatchMode;
		state.KillCount = data.KillCount;
		state.GeneratorId = data.GeneratorId ?? "room_corridor";
		state.ViewModeId = data.ViewModeId ?? "single_layer";
		state.Actors = data.Actors ?? new();

		DirtyChunkCache.Clear();
		if (data.DirtyChunks != null)
		{
			foreach (var cs in data.DirtyChunks)
			{
				var coord = new ChunkCoord(cs.Cx, cs.Cy, cs.Cz);
				DirtyChunkCache[coord] = cs;
			}
		}

		return true;
	}

	// ══════════════════════════════════════════════════════
	//  Dirty Chunk 缓存（存档加载后供 ChunkManager 使用）
	// ══════════════════════════════════════════════════════

	internal static readonly Dictionary<ChunkCoord, ChunkSaveData> DirtyChunkCache = new();

	/// <summary>供 ChunkManager.OnChunkLoad 使用：从存档缓存中还原 dirty chunk。</summary>
	public static ChunkData? LoadChunkFromCache(ChunkCoord coord)
	{
		if (!DirtyChunkCache.TryGetValue(coord, out var cs)) return null;
		return DeserializeChunk(cs);
	}

	/// <summary>供 ChunkManager.OnChunkUnload 使用：将 dirty chunk 写入缓存。</summary>
	public static void SaveChunkToCache(ChunkCoord coord, ChunkData chunk)
	{
		DirtyChunkCache[coord] = SerializeChunk(coord, chunk);
	}

	// ══════════════════════════════════════════════════════
	//  Chunk 序列化 / 反序列化
	// ══════════════════════════════════════════════════════

	private static ChunkSaveData SerializeChunk(ChunkCoord coord, ChunkData chunk)
	{
		var cs = new ChunkSaveData
		{
			Cx = coord.Cx, Cy = coord.Cy, Cz = coord.Cz,
			TerrainIds = new ushort[ChunkData.Area],
			Hardness = new byte[ChunkData.Area],
		};
		System.Array.Copy(chunk.TerrainIds, cs.TerrainIds, ChunkData.Area);
		System.Array.Copy(chunk.Hardness, cs.Hardness, ChunkData.Area);

		foreach (var (idx, entities) in chunk.Entities)
		{
			var list = new List<CellEntity>();
			foreach (var e in entities) list.Add(CopyCellEntity(e));
			cs.Entities[idx] = list;
		}

		cs.Nests = CopyNests(chunk.Nests);
		return cs;
	}

	private static ChunkData DeserializeChunk(ChunkSaveData cs)
	{
		var chunk = new ChunkData
		{
			Coord = new ChunkCoord(cs.Cx, cs.Cy, cs.Cz),
			Dirty = true,
			TerrainIds = new ushort[ChunkData.Area],
			Hardness = new byte[ChunkData.Area],
		};
		if (cs.TerrainIds != null) System.Array.Copy(cs.TerrainIds, chunk.TerrainIds, ChunkData.Area);
		if (cs.Hardness != null) System.Array.Copy(cs.Hardness, chunk.Hardness, ChunkData.Area);

		foreach (var (idx, entities) in cs.Entities)
		{
			var list = new List<CellEntity>();
			foreach (var e in entities) list.Add(CopyCellEntity(e));
			chunk.Entities[idx] = list;
		}

		chunk.Nests = cs.Nests ?? [];
		return chunk;
	}

	// ══════════════════════════════════════════════════════
	//  深拷贝
	// ══════════════════════════════════════════════════════

	private static CellEntity CopyCellEntity(CellEntity e) => new()
	{
		Type = e.Type, Glyph = e.Glyph, EntityId = e.EntityId,
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
		foreach (var (id, a) in src) copy[id] = CopyActor(a);
		return copy;
	}

	private static Actor CopyActor(Actor a) => new()
	{
		Id = a.Id, X = a.X, Y = a.Y, Z = a.Z,
		Glyph = a.Glyph, DisplayName = a.DisplayName,
		Faction = a.Faction, BrainId = a.BrainId,
		FacingX = a.FacingX, FacingY = a.FacingY,
		Gold = a.Gold,
		Inventory = a.Inventory.ConvertAll(CopyItem),
		ShopSlots = a.ShopSlots.ConvertAll(CopyShopSlot),
		Limbs = a.Limbs.ConvertAll(CopyLimb),
		Race = a.Race != null ? CopyRace(a.Race) : null,
		Profession = a.Profession != null ? CopyProfession(a.Profession) : null,
		Buffs = a.Buffs.ConvertAll(CopyBuff),
		Experiences = a.Experiences.ConvertAll(CopyExperience),
		DialogMood = a.DialogMood,
		DialogAffinity = a.DialogAffinity,
		DialogMemory = [.. a.DialogMemory],
		DialogTalkCount = a.DialogTalkCount,
		DialogPersonality = new(a.DialogPersonality),
		DialogNeeds = new(a.DialogNeeds),
	};

	private static Item CopyItem(Item i) => new()
	{
		Id = i.Id, Name = i.Name, Category = i.Category,
		Price = i.Price, Weight = i.Weight, Equipped = i.Equipped,
		BodyPart = i.BodyPart, Layer = i.Layer,
		CoveredParts = [.. i.CoveredParts],
		SharpArmor = i.SharpArmor, BluntArmor = i.BluntArmor,
		SharpDamage = i.SharpDamage, BluntDamage = i.BluntDamage,
		GrantedSkills = [.. i.GrantedSkills],
		Contents = i.Contents?.ConvertAll(CopyItem),
		Tags = new Dictionary<string, int>(i.Tags),
	};

	private static ShopSlot CopyShopSlot(ShopSlot s) => new()
		{ Stock = s.Stock, Item = CopyItem(s.Item) };

	private static Limb CopyLimb(Limb l) => new()
	{
		Id = l.Id, Name = l.Name, MaxDurability = l.MaxDurability, Durability = l.Durability,
		Material = l.Material, BodyPart = l.BodyPart,
		EquipLayers = [.. l.EquipLayers],
		EquipSlots = l.EquipSlots.ConvertAll(CopyEquipSlot),
		Capacities = new Dictionary<string, float>(l.Capacities),
		Tags = new Dictionary<string, int>(l.Tags),
	};

	private static EquipSlot CopyEquipSlot(EquipSlot s) => new()
	{
		LimbId = s.LimbId, BodyPart = s.BodyPart, Layer = s.Layer, ItemId = s.ItemId,
	};

	private static Race CopyRace(Race r) => new()
		{ Id = r.Id, Name = r.Name, Tags = new Dictionary<string, int>(r.Tags) };
	private static Profession CopyProfession(Profession p) => new()
		{ Id = p.Id, Name = p.Name, Tags = new Dictionary<string, int>(p.Tags) };
	private static Buff CopyBuff(Buff b) => new()
		{ Id = b.Id, Name = b.Name, RemainingTurns = b.RemainingTurns, Tags = new Dictionary<string, int>(b.Tags) };
	private static Experience CopyExperience(Experience e) => new()
		{ Id = e.Id, Name = e.Name, Tags = new Dictionary<string, int>(e.Tags) };

	private static void EnsureDir(string filePath)
	{
		var dir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
	}
}

// ══════════════════════════════════════════════════════
//  存档数据结构
// ══════════════════════════════════════════════════════

public class WorldSaveData
{
	public int WorldSeed { get; set; }
	public int Turn { get; set; }
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
	public int PlayerZ { get; set; }
	public bool BumpAttack { get; set; } = true;
	public bool WatchMode { get; set; }
	public int KillCount { get; set; }
	public string? GeneratorId { get; set; }
	public string? ViewModeId { get; set; }
	public Dictionary<string, Actor>? Actors { get; set; }
	public List<ChunkSaveData> DirtyChunks { get; set; } = [];
}

public class ChunkSaveData
{
	public int Cx { get; set; }
	public int Cy { get; set; }
	public int Cz { get; set; }
	public ushort[]? TerrainIds { get; set; }
	public byte[]? Hardness { get; set; }
	public Dictionary<int, List<CellEntity>> Entities { get; set; } = new();
	public List<NestData>? Nests { get; set; }
}
