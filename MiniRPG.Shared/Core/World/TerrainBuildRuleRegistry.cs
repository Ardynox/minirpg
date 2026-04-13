using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.World;

public sealed class TerrainBuildRuleDef
{
	[JsonPropertyName("terrainId")]
	public string TerrainId { get; set; } = string.Empty;

	[JsonPropertyName("costs")]
	public List<ItemAmount> Costs { get; set; } = [];
}

public static class TerrainBuildRuleRegistry
{
	private static readonly Dictionary<string, TerrainBuildRuleDef> _rules = new(StringComparer.Ordinal);
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	public static IReadOnlyDictionary<string, TerrainBuildRuleDef> All => _rules;

	public static void Clear() => _rules.Clear();

	public static void Load(string relativeDataPath = "terrain_build_rules.json")
	{
		_rules.Clear();
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out _))
			return;

		LoadFromJson(json);
	}

	public static void LoadFromJson(string json)
	{
		_rules.Clear();
		var rules = JsonSerializer.Deserialize<List<TerrainBuildRuleDef>>(json, JsonOptions) ?? [];
		foreach (var rule in rules)
		{
			if (string.IsNullOrWhiteSpace(rule.TerrainId))
				continue;

			rule.Costs = NormalizeCosts(rule.Costs);
			_rules[rule.TerrainId] = rule;
		}
	}

	public static TerrainBuildRuleDef? Get(string? terrainId)
	{
		if (string.IsNullOrWhiteSpace(terrainId))
			return null;

		return _rules.GetValueOrDefault(terrainId);
	}

	public static bool IsAllowed(string? terrainId) => Get(terrainId) != null;

	private static List<ItemAmount> NormalizeCosts(IEnumerable<ItemAmount>? costs) =>
		costs?
			.Where(static item => item != null && !string.IsNullOrWhiteSpace(item.ItemId) && item.Count > 0)
			.GroupBy(static item => item.ItemId, StringComparer.Ordinal)
			.Select(static group => new ItemAmount
			{
				ItemId = group.Key,
				Count = group.Sum(static item => item.Count),
			})
			.ToList() ?? [];
}
