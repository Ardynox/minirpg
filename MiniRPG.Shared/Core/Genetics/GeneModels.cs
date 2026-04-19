using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Genetics;

/// <summary>
/// 基因显隐性。
/// Dominant 表示 A 等位基因显性，至少一个 A 就走 a_dominant 表型；纯 B 走 homozygous_b。
/// Recessive 表示 A 等位基因隐性，纯 B 才走 homozygous_b 表型；至少一个 A 就走 a_dominant。
/// Codominant 表示混合基因型走 codominant_mixed；纯 A / 纯 B 分别走 a_dominant / homozygous_b。
/// </summary>
public enum GeneDominance
{
	Dominant,
	Recessive,
	Codominant,
}

/// <summary>
/// 基因定义（DTO）。对应 Data/Genes/genes.json 中的一条。
/// 等位基因用字符串 token（"A"/"a" 风格），不强制单字符。
/// 表型 tag 三套（a 显性 / 纯 B 隐性 / 共显混合）由 <see cref="GeneExpressionService"/> 按 dominance 选用。
/// </summary>
public sealed class GeneDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("locus")]
	public string Locus { get; set; } = "";

	[JsonPropertyName("dominance")]
	[JsonConverter(typeof(JsonStringEnumConverter))]
	public GeneDominance Dominance { get; set; } = GeneDominance.Dominant;

	[JsonPropertyName("allele_a")]
	public string AlleleA { get; set; } = "A";

	[JsonPropertyName("allele_b")]
	public string AlleleB { get; set; } = "a";

	[JsonPropertyName("tags_if_a_dominant")]
	public Dictionary<string, int> TagsIfADominant { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("tags_if_homozygous_b")]
	public Dictionary<string, int> TagsIfHomozygousB { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("tags_if_codominant_mixed")]
	public Dictionary<string, int> TagsIfCodominantMixed { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 流派定义（XenotypeDef）。对应 Data/Xenotypes/xenotypes.json 中的一条。
/// 一个流派绑定一个 race，固定基因总会出现，可选基因按 spawn 概率随机选。
/// </summary>
public sealed class XenotypeDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("race_id")]
	public string RaceId { get; set; } = "human";

	[JsonPropertyName("fixed_genes")]
	public List<string> FixedGenes { get; set; } = [];

	[JsonPropertyName("random_genes")]
	public List<string> RandomGenes { get; set; } = [];
}

/// <summary>双等位基因对（孟德尔意义下的二倍体）。两个 token 都对应 <see cref="GeneDef.AlleleA"/> 或 <see cref="GeneDef.AlleleB"/>。</summary>
public sealed class LocusPair
{
	[JsonPropertyName("a")]
	public string A { get; set; } = "";

	[JsonPropertyName("b")]
	public string B { get; set; } = "";
}

/// <summary>
/// 一个 actor 的完整基因池。每个 locus 一对等位基因。
/// 通过实现 <see cref="ITagSource"/> 直接接入 <c>Actor.ComputeTags()</c>。
/// </summary>
public sealed class GenePool : ITagSource
{
	[JsonPropertyName("loci")]
	public Dictionary<string, LocusPair> Loci { get; set; } = new(StringComparer.Ordinal);

	public Dictionary<string, int> GetTags() => GeneExpressionService.ComputeTags(this);

	public GenePool Clone()
	{
		var clone = new GenePool();
		foreach (var (key, pair) in Loci)
			clone.Loci[key] = new LocusPair { A = pair.A, B = pair.B };

		return clone;
	}
}
