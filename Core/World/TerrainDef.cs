using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MiniRPG.Core.Config;

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

	// ── 体素渲染属性 ──

	/// <summary>方块顶面贴图名（等距渲染用）。空 = 使用 tile_mapping 默认映射。</summary>
	public string TopTile { get; set; } = "";
	/// <summary>方块侧面贴图名（等距渲染用）。空 = 无侧面。</summary>
	public string SideTile { get; set; } = "";
	/// <summary>是否不透明（用于体素遮挡剔除）。true = 完全遮挡后方方块。</summary>
	public bool IsOpaque { get; set; } = true;
}

/// <summary>
/// 地形注册表：ushort ID ↔ TerrainDef 双向查表。
/// 所有地形从 Data/terrains.json 加载，ID 0 保留给 void。
/// </summary>
public static class TerrainRegistry
{
	private static readonly List<TerrainDef> _byId = [];
	private static readonly Dictionary<string, TerrainDef> _byStringId = new();
	private static readonly TerrainDef FallbackVoid = new()
	{
		Id = 0,
		StringId = "void",
		Glyph = " ",
		DefaultHardness = 0,
		Solid = true,
		Material = "",
		BreaksInto = "rubble",
	};

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	public static IReadOnlyList<TerrainDef> All => _byId;

	public static TerrainDef Get(ushort id)
	{
		if (_byId.Count == 0)
			return FallbackVoid;

		if (id < _byId.Count && _byId[id] != null)
			return _byId[id];

		return _byId[0] ?? FallbackVoid;
	}

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

	/// <summary>
	/// 从 Data/ 相对路径加载地形定义。
	/// </summary>
	public static void Load(string relativeDataPath)
	{
		_byId.Clear();
		_byStringId.Clear();
		LoadFromJson(GameDataLocator.ReadTextOrThrow(relativeDataPath));
	}

	public static void LoadFromFile(string filePath)
	{
		_byId.Clear();
		_byStringId.Clear();
		LoadFromJson(File.ReadAllText(filePath));
	}

	public static void LoadFromJson(string json)
	{
		var presets = JsonSerializer.Deserialize<List<TerrainPreset>>(json, JsonOpts);
		if (presets == null || presets.Count == 0)
			throw new InvalidOperationException("Terrain payload is empty or invalid.");

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
				TopTile = p.TopTile ?? "",
				SideTile = p.SideTile ?? "",
				IsOpaque = p.IsOpaque ?? p.Solid,
			});
		}
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
	public string? TopTile { get; set; }
	public string? SideTile { get; set; }
	public bool? IsOpaque { get; set; }
}
