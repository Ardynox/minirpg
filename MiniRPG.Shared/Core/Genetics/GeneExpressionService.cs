using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Genetics;

/// <summary>
/// 表型计算：基因池 → tag 增量。被 <see cref="GenePool.GetTags"/> 调用，进入 Actor.ComputeTags 聚合。
/// 决策表（按 Dominance）：
/// - Dominant：至少一个 A 走 a_dominant；纯 B 走 homozygous_b。
/// - Recessive：纯 B 走 homozygous_b；至少一个 A 走 a_dominant。
/// - Codominant：A+B 混合走 codominant_mixed；纯 A 走 a_dominant；纯 B 走 homozygous_b。
/// 多个 locus 的 tag 数值按 key 求和。
/// </summary>
public static class GeneExpressionService
{
	public static Dictionary<string, int> ComputeTags(GenePool? pool)
	{
		var tags = new Dictionary<string, int>(StringComparer.Ordinal);
		if (pool == null || pool.Loci.Count == 0)
			return tags;

		GeneCatalog.EnsureLoaded();
		foreach (var (geneId, pair) in pool.Loci)
		{
			var def = GeneCatalog.GetGene(geneId);
			if (def == null)
				continue;
			var contribution = ResolvePhenotypeTags(def, pair);
			foreach (var (key, value) in contribution)
				tags[key] = tags.GetValueOrDefault(key, 0) + value;
		}

		return tags;
	}

	private static IReadOnlyDictionary<string, int> ResolvePhenotypeTags(GeneDef def, LocusPair pair)
	{
		var hasA = string.Equals(pair.A, def.AlleleA, StringComparison.Ordinal)
			|| string.Equals(pair.B, def.AlleleA, StringComparison.Ordinal);
		var hasB = string.Equals(pair.A, def.AlleleB, StringComparison.Ordinal)
			|| string.Equals(pair.B, def.AlleleB, StringComparison.Ordinal);
		var bothB = string.Equals(pair.A, def.AlleleB, StringComparison.Ordinal)
			&& string.Equals(pair.B, def.AlleleB, StringComparison.Ordinal);
		var mixed = hasA && hasB && !bothB
			&& !(string.Equals(pair.A, def.AlleleA, StringComparison.Ordinal)
				&& string.Equals(pair.B, def.AlleleA, StringComparison.Ordinal));

		return def.Dominance switch
		{
			GeneDominance.Dominant => hasA ? def.TagsIfADominant : def.TagsIfHomozygousB,
			GeneDominance.Recessive => bothB ? def.TagsIfHomozygousB : def.TagsIfADominant,
			GeneDominance.Codominant => mixed
				? def.TagsIfCodominantMixed
				: (bothB ? def.TagsIfHomozygousB : def.TagsIfADominant),
			_ => def.TagsIfADominant,
		};
	}
}
