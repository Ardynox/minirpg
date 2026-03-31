using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MiniRPG.Core.World;

/// <summary>
/// 地形定义：描述一种地形类型的所有属性。
/// 由 TerrainRegistry 从 Data/terrains.json 加载。
/// </summary>
public class TerrainDef
{
	public ushort Id { get; set; }
	public string StringId { get; set; } = "";
	public string Glyph { get; set; } = "#";
	public byte DefaultHardness { get; set; }
	public bool Solid { get; set; }
	public string Material { get; set; } = "";
	/// <summary>破坏后变成的地形 StringId。空 = 变为 rubble。</summary>
	public string BreaksInto { get; set; } = "rubble";
	public Dictionary<string, int> Tags { get; set; } = new();
}

/// <summary>
/// 地形注册表：ushort ID ↔ TerrainDef 双向查表。
/// 所有地形从 Data/terrains.json 加载，ID 0 保留给 void。
/// </summary>
public static class TerrainRegistry
{
	private static readonly List<TerrainDef> _byId = [];
	private static readonly Dictionary<string, TerrainDef> _byStringId = new();

	public static IReadOnlyList<TerrainDef> All => _byId;

	public static TerrainDef Get(ushort id) =>
		id < _byId.Count ? _byId[id] : _byId[0];

	public static TerrainDef? Get(string stringId) =>
		_byStringId.GetValueOrDefault(stringId);

	public static ushort GetId(string stringId) =>
		_byStringId.TryGetValue(stringId, out var def) ? def.Id : (ushort)0;

	public static void Register(TerrainDef def)
	{
		while (_byId.Count <= def.Id) _byId.Add(null!);
		_byId[def.Id] = def;
		_byStringId[def.StringId] = def;
	}

	public static void Load(string jsonPath)
	{
		_byId.Clear();
		_byStringId.Clear();

		if (!File.Exists(jsonPath)) { LoadDefaults(); return; }

		var json = File.ReadAllText(jsonPath);
		var presets = JsonSerializer.Deserialize<List<TerrainPreset>>(json);
		if (presets == null) { LoadDefaults(); return; }

		foreach (var p in presets)
		{
			Register(new TerrainDef
			{
				Id = p.Id,
				StringId = p.StringId,
				Glyph = p.Glyph,
				DefaultHardness = p.DefaultHardness,
				Solid = p.Solid,
				Material = p.Material ?? "",
				BreaksInto = p.BreaksInto ?? "rubble",
				Tags = p.Tags ?? new(),
			});
		}
	}

	private static void LoadDefaults()
	{
		Register(new TerrainDef { Id = 0, StringId = "void", Glyph = " ", Solid = true, DefaultHardness = 0 });
		Register(new TerrainDef { Id = 1, StringId = "floor", Glyph = ".", Solid = false, DefaultHardness = 0 });
		Register(new TerrainDef { Id = 2, StringId = "wall_soil", Glyph = "#", Solid = true, DefaultHardness = 20, Material = "soil" });
		Register(new TerrainDef { Id = 3, StringId = "wall_stone", Glyph = "#", Solid = true, DefaultHardness = 60, Material = "stone" });
		Register(new TerrainDef { Id = 4, StringId = "wall_granite", Glyph = "#", Solid = true, DefaultHardness = 120, Material = "granite" });
		Register(new TerrainDef { Id = 5, StringId = "wall_obsidian", Glyph = "#", Solid = true, DefaultHardness = 200, Material = "obsidian" });
		Register(new TerrainDef { Id = 6, StringId = "rubble", Glyph = ".", Solid = false, DefaultHardness = 0, Material = "rubble" });
		Register(new TerrainDef { Id = 7, StringId = "grass", Glyph = ".", Solid = false, DefaultHardness = 0, Material = "grass" });
		Register(new TerrainDef { Id = 8, StringId = "water", Glyph = "~", Solid = false, DefaultHardness = 0, Material = "water" });
		Register(new TerrainDef { Id = 9, StringId = "tree", Glyph = "T", Solid = true, DefaultHardness = 15, Material = "wood", BreaksInto = "grass" });
		Register(new TerrainDef { Id = 10, StringId = "lava", Glyph = "~", Solid = false, DefaultHardness = 0, Material = "lava" });
		Register(new TerrainDef { Id = 11, StringId = "sand", Glyph = ".", Solid = false, DefaultHardness = 0, Material = "sand" });
		Register(new TerrainDef { Id = 12, StringId = "mountain", Glyph = "^", Solid = true, DefaultHardness = 150, Material = "stone" });
	}
}

internal class TerrainPreset
{
	public ushort Id { get; set; }
	public string StringId { get; set; } = "";
	public string Glyph { get; set; } = "#";
	public byte DefaultHardness { get; set; }
	public bool Solid { get; set; }
	public string? Material { get; set; }
	public string? BreaksInto { get; set; }
	public Dictionary<string, int>? Tags { get; set; }
}
