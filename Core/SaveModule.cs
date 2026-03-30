using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core;

/// <summary>
/// 存档模块：支持全局存档（所有楼层）和楼层切换时的内存快照。
/// </summary>
public static class SaveModule
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	// ── 全局存读档（所有楼层 → JSON 文件） ──────────────────

	public static void SaveGame(GameState state, string filePath)
	{
		var data = new FullSaveData
		{
			CurrentFloor = state.CurrentFloor,
			Turn = state.Turn,
			RngSeed = state.RngSeed,
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

	public static bool LoadGame(GameState state, string filePath)
	{
		if (!File.Exists(filePath)) return false;

		var json = File.ReadAllText(filePath);
		var data = JsonSerializer.Deserialize<FullSaveData>(json, JsonOpts);
		if (data is null) return false;

		state.CurrentFloor = data.CurrentFloor;
		state.Turn = data.Turn;
		state.RngSeed = data.RngSeed;
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

	// ── 楼层切换用的内存快照 ──────────────────────────────

	public static void SaveFloorToDict(GameState state)
	{
		state.Floors[state.CurrentFloor] = SnapshotFloor(state);
	}

	public static bool LoadFloorFromDict(GameState state, int floor)
	{
		if (!state.Floors.TryGetValue(floor, out var data))
			return false;
		RestoreFloor(state, data);
		state.Floors.Remove(floor);
		state.CurrentFloor = floor;
		return true;
	}

	// ── 快照 / 恢复 ──────────────────────────────────────

	private static FloorData SnapshotFloor(GameState state) => new()
	{
		Width = state.MapWidth,
		Height = state.MapHeight,
		Terrain = CopyLayer(state.Terrain),
		Fixtures = CopyLayer(state.Fixtures),
		Objects = CopyLayer(state.Objects),
		Meta = CopyMeta(state.Meta),
		Nests = CopyNests(state.Nests),
		Actors = CopyActors(state.Actors),
		PlayerX = state.PlayerX,
		PlayerY = state.PlayerY,
	};

	private static void RestoreFloor(GameState state, FloorData data)
	{
		state.MapWidth = data.Width;
		state.MapHeight = data.Height;
		state.Terrain = CopyLayer(data.Terrain);
		state.Fixtures = CopyLayer(data.Fixtures);
		state.Objects = CopyLayer(data.Objects);
		state.Meta = CopyMeta(data.Meta);
		state.Nests = CopyNests(data.Nests);
		state.Actors = CopyActors(data.Actors);
		state.PlayerX = data.PlayerX;
		state.PlayerY = data.PlayerY;
	}

	// ── 转换：GameState ↔ MapSaveData ────────────────────

	private static MapSaveData SnapshotToSaveData(GameState s) => new()
	{
		Width = s.MapWidth,
		Height = s.MapHeight,
		Terrain = FlattenLayer(s.Terrain),
		Fixtures = FlattenLayer(s.Fixtures),
		Objects = FlattenLayer(s.Objects),
		Meta = FlattenMeta(s.Meta),
		PlayerX = s.PlayerX,
		PlayerY = s.PlayerY,
		Nests = s.Nests,
		Actors = CopyActors(s.Actors),
	};

	private static MapSaveData FloorToSaveData(FloorData f) => new()
	{
		Width = f.Width,
		Height = f.Height,
		Terrain = FlattenLayer(f.Terrain),
		Fixtures = FlattenLayer(f.Fixtures),
		Objects = FlattenLayer(f.Objects),
		Meta = FlattenMeta(f.Meta),
		PlayerX = f.PlayerX,
		PlayerY = f.PlayerY,
		Nests = new List<NestData>(f.Nests),
		Actors = CopyActors(f.Actors),
	};

	private static FloorData SaveDataToFloor(MapSaveData d) => new()
	{
		Width = d.Width,
		Height = d.Height,
		Terrain = UnflattenLayer(d.Terrain, d.Width, d.Height),
		Fixtures = UnflattenLayer(d.Fixtures ?? [], d.Width, d.Height),
		Objects = UnflattenLayer(d.Objects, d.Width, d.Height),
		Meta = UnflattenMeta(d.Meta, d.Width, d.Height),
		Nests = d.Nests ?? [],
		Actors = d.Actors ?? new(),
		PlayerX = d.PlayerX,
		PlayerY = d.PlayerY,
	};

	// ── 深拷贝 ───────────────────────────────────────────

	private static List<List<string>> CopyLayer(List<List<string>> src)
	{
		var copy = new List<List<string>>();
		foreach (var row in src)
			copy.Add(new List<string>(row));
		return copy;
	}

	private static List<List<Dictionary<string, string>?>> CopyMeta(
		List<List<Dictionary<string, string>?>> src)
	{
		var copy = new List<List<Dictionary<string, string>?>>();
		foreach (var row in src)
		{
			var r = new List<Dictionary<string, string>?>();
			foreach (var cell in row)
				r.Add(cell != null ? new Dictionary<string, string>(cell) : null);
			copy.Add(r);
		}
		return copy;
	}

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
		Limbs = a.Limbs.ConvertAll(CopyLimb),
		Race = a.Race != null ? CopyRace(a.Race) : null,
		Profession = a.Profession != null ? CopyProfession(a.Profession) : null,
		Buffs = a.Buffs.ConvertAll(CopyBuff),
		Experiences = a.Experiences.ConvertAll(CopyExperience),
	};

	private static Limb CopyLimb(Limb l) => new()
		{ Id = l.Id, Name = l.Name, Tags = new Dictionary<string, int>(l.Tags) };
	private static Race CopyRace(Race r) => new()
		{ Id = r.Id, Name = r.Name, Tags = new Dictionary<string, int>(r.Tags) };
	private static Profession CopyProfession(Profession p) => new()
		{ Id = p.Id, Name = p.Name, Tags = new Dictionary<string, int>(p.Tags) };
	private static Buff CopyBuff(Buff b) => new()
		{ Id = b.Id, Name = b.Name, RemainingTurns = b.RemainingTurns,
		  Tags = new Dictionary<string, int>(b.Tags) };
	private static Experience CopyExperience(Experience e) => new()
		{ Id = e.Id, Name = e.Name, Tags = new Dictionary<string, int>(e.Tags) };

	// ── 平铺 / 还原 ─────────────────────────────────────

	private static List<string> FlattenLayer(List<List<string>> layer)
	{
		var flat = new List<string>();
		foreach (var row in layer) flat.AddRange(row);
		return flat;
	}

	private static List<List<string>> UnflattenLayer(List<string> flat, int w, int h)
	{
		var layer = new List<List<string>>();
		for (var y = 0; y < h; y++)
		{
			var row = new List<string>();
			for (var x = 0; x < w; x++)
				row.Add(y * w + x < flat.Count ? flat[y * w + x] : "");
			layer.Add(row);
		}
		return layer;
	}

	private static List<Dictionary<string, string>?> FlattenMeta(
		List<List<Dictionary<string, string>?>> meta)
	{
		var flat = new List<Dictionary<string, string>?>();
		foreach (var row in meta) flat.AddRange(row);
		return flat;
	}

	private static List<List<Dictionary<string, string>?>> UnflattenMeta(
		List<Dictionary<string, string>?>? flat, int w, int h)
	{
		var meta = new List<List<Dictionary<string, string>?>>();
		for (var y = 0; y < h; y++)
		{
			var row = new List<Dictionary<string, string>?>();
			for (var x = 0; x < w; x++)
				row.Add(flat != null && y * w + x < flat.Count ? flat[y * w + x] : null);
			meta.Add(row);
		}
		return meta;
	}

	private static void EnsureDir(string filePath)
	{
		var dir = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			Directory.CreateDirectory(dir);
	}
}

/// <summary>全局存档：包含所有楼层数据。</summary>
public class FullSaveData
{
	public int CurrentFloor { get; set; }
	public int Turn { get; set; }
	public int RngSeed { get; set; }
	public Dictionary<string, MapSaveData> Floors { get; set; } = new();
}

/// <summary>单层地图的序列化数据（平铺）。</summary>
public class MapSaveData
{
	public int Width { get; set; }
	public int Height { get; set; }
	public List<string> Terrain { get; set; } = [];
	public List<string>? Fixtures { get; set; }
	public List<string> Objects { get; set; } = [];
	public List<Dictionary<string, string>?>? Meta { get; set; }
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
	public List<NestData>? Nests { get; set; }
	public Dictionary<string, Actor>? Actors { get; set; }
}
