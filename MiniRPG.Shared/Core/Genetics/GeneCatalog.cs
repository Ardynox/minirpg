using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Genetics;

/// <summary>
/// 基因 / 流派注册表。启动时一次加载 Data/Genes/genes.json + Data/Xenotypes/xenotypes.json。
/// 测试可走 <see cref="OverrideForTesting"/> 注入数据，避开磁盘 IO。
/// </summary>
public static class GeneCatalog
{
	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};

	private static Dictionary<string, GeneDef> _genes = new(StringComparer.Ordinal);
	private static Dictionary<string, XenotypeDef> _xenotypes = new(StringComparer.Ordinal);
	private static bool _loaded;

	private sealed class GenesFile
	{
		[JsonPropertyName("genes")]
		public List<GeneDef> Genes { get; set; } = [];
	}

	private sealed class XenotypesFile
	{
		[JsonPropertyName("xenotypes")]
		public List<XenotypeDef> Xenotypes { get; set; } = [];
	}

	public static void EnsureLoaded()
	{
		if (_loaded)
			return;
		Load();
	}

	public static void Load()
	{
		_genes = new Dictionary<string, GeneDef>(StringComparer.Ordinal);
		if (GameDataLocator.TryReadText("Genes/genes.json", out var genesJson, out _))
		{
			var file = JsonSerializer.Deserialize<GenesFile>(genesJson, JsonOpts);
			if (file != null)
			{
				foreach (var g in file.Genes)
				{
					if (!string.IsNullOrWhiteSpace(g.Id))
						_genes[g.Id] = g;
				}
			}
		}

		_xenotypes = new Dictionary<string, XenotypeDef>(StringComparer.Ordinal);
		if (GameDataLocator.TryReadText("Xenotypes/xenotypes.json", out var xenoJson, out _))
		{
			var file = JsonSerializer.Deserialize<XenotypesFile>(xenoJson, JsonOpts);
			if (file != null)
			{
				foreach (var x in file.Xenotypes)
				{
					if (!string.IsNullOrWhiteSpace(x.Id))
						_xenotypes[x.Id] = x;
				}
			}
		}

		_loaded = true;
	}

	public static void Reset()
	{
		_genes.Clear();
		_xenotypes.Clear();
		_loaded = false;
	}

	/// <summary>测试钩子：直接注入基因 + 流派表，跳过磁盘 IO。</summary>
	public static void OverrideForTesting(IEnumerable<GeneDef> genes, IEnumerable<XenotypeDef> xenotypes)
	{
		_genes = new Dictionary<string, GeneDef>(StringComparer.Ordinal);
		foreach (var g in genes)
		{
			if (!string.IsNullOrWhiteSpace(g.Id))
				_genes[g.Id] = g;
		}

		_xenotypes = new Dictionary<string, XenotypeDef>(StringComparer.Ordinal);
		foreach (var x in xenotypes)
		{
			if (!string.IsNullOrWhiteSpace(x.Id))
				_xenotypes[x.Id] = x;
		}

		_loaded = true;
	}

	public static GeneDef? GetGene(string? id) =>
		string.IsNullOrWhiteSpace(id) ? null : _genes.GetValueOrDefault(id!);

	public static XenotypeDef? GetXenotype(string? id) =>
		string.IsNullOrWhiteSpace(id) ? null : _xenotypes.GetValueOrDefault(id!);

	public static IReadOnlyDictionary<string, GeneDef> AllGenes => _genes;

	public static IReadOnlyDictionary<string, XenotypeDef> AllXenotypes => _xenotypes;

	/// <summary>
	/// 按 raceId / xenotypeId 解析流派；找不到时返回空 fallback（不抛），让调用者拿到一个安全的零基因结果。
	/// xenotypeId 显式传入时优先；否则按 raceId 找第一个匹配的。
	/// </summary>
	public static XenotypeDef ResolveXenotype(string? raceId, string? xenotypeId)
	{
		EnsureLoaded();
		if (!string.IsNullOrWhiteSpace(xenotypeId) && _xenotypes.TryGetValue(xenotypeId!, out var byId))
			return byId;

		var key = string.IsNullOrWhiteSpace(raceId) ? "human" : raceId!;
		foreach (var xeno in _xenotypes.Values)
		{
			if (string.Equals(xeno.RaceId, key, StringComparison.Ordinal))
				return xeno;
		}

		return new XenotypeDef { Id = "fallback", RaceId = key };
	}
}
