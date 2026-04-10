using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Farm;

/// <summary>
/// 作物定义：描述一种可种植的作物。
/// 数据驱动，从 JSON 加载。
/// </summary>
public sealed class CropDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	/// <summary>生长所需总回合数。</summary>
	[JsonPropertyName("growthTurns")]
	public int GrowthTurns { get; set; } = 20;

	/// <summary>收获产出的物品 ID。</summary>
	[JsonPropertyName("harvestItemId")]
	public string HarvestItemId { get; set; } = "";

	/// <summary>每次收获的产出数量范围。</summary>
	[JsonPropertyName("harvestMin")]
	public int HarvestMin { get; set; } = 1;

	[JsonPropertyName("harvestMax")]
	public int HarvestMax { get; set; } = 3;

	/// <summary>种植所需的种子物品 ID。</summary>
	[JsonPropertyName("seedItemId")]
	public string SeedItemId { get; set; } = "";

	/// <summary>收获后是否自动重新种植。</summary>
	[JsonPropertyName("autoReplant")]
	public bool AutoReplant { get; set; }

	/// <summary>适宜生长的季节（空 = 全季节）。</summary>
	[JsonPropertyName("seasons")]
	public List<string> Seasons { get; set; } = [];
}

/// <summary>
/// 作物实例：一个格子上正在生长的作物。
/// </summary>
public sealed class CropInstance
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("cropDefId")]
	public string CropDefId { get; set; } = "";

	[JsonPropertyName("x")]
	public int X { get; set; }

	[JsonPropertyName("y")]
	public int Y { get; set; }

	[JsonPropertyName("z")]
	public int Z { get; set; }

	/// <summary>当前生长进度（0 ~ GrowthTurns）。</summary>
	[JsonPropertyName("growth")]
	public int Growth { get; set; }

	/// <summary>是否已成熟可收获。</summary>
	[JsonPropertyName("mature")]
	public bool Mature { get; set; }

	/// <summary>是否已枯萎（季节不对或缺水）。</summary>
	[JsonPropertyName("withered")]
	public bool Withered { get; set; }

	/// <summary>种植回合。</summary>
	[JsonPropertyName("plantedTurn")]
	public int PlantedTurn { get; set; }
}

/// <summary>
/// 作物定义注册表。
/// </summary>
public static class CropRegistry
{
	private static readonly Dictionary<string, CropDef> _defs = new(StringComparer.Ordinal);

	public static void Register(CropDef def) => _defs[def.Id] = def;
	public static void Clear() => _defs.Clear();
	public static CropDef? Get(string id) => _defs.TryGetValue(id, out var def) ? def : null;
	public static IReadOnlyDictionary<string, CropDef> All => _defs;

	public static void LoadFromJson(string json)
	{
		var defs = JsonSerializer.Deserialize<List<CropDef>>(json, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
		});
		if (defs == null) return;
		foreach (var def in defs)
			Register(def);
	}
}
