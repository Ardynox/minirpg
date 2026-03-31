using System;
using System.Collections.Generic;
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

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

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

	/// <summary>
	/// 从 Godot res:// 路径加载地形定义。使用 Godot.FileAccess 以正确解析虚拟路径。
	/// </summary>
	public static void Load(string resPath)
	{
		_byId.Clear();
		_byStringId.Clear();

		using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			throw new InvalidOperationException(
				$"Failed to open terrain file: {resPath} (error: {Godot.FileAccess.GetOpenError()})");

		var json = file.GetAsText();
		var presets = JsonSerializer.Deserialize<List<TerrainPreset>>(json, JsonOpts);
		if (presets == null || presets.Count == 0)
			throw new InvalidOperationException($"Terrain file is empty or invalid: {resPath}");

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
