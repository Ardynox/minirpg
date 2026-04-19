using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Genetics;

/// <summary>
/// 真孟德尔遗传：每个 locus 父母各贡献一个等位基因（独立掷骰），可选 2% 突变翻转。
/// 父母任意一方为空时，该 locus 走 race 默认流派的随机基因。
/// </summary>
public static class GeneInheritanceService
{
	public const float DefaultMutationRate = 0.02f;

	public static GenePool Inherit(Random rng, GenePool? p1, GenePool? p2, string raceId)
	{
		var child = new GenePool();
		var xeno = GeneCatalog.ResolveXenotype(raceId, null);
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var g in xeno.FixedGenes)
			keys.Add(g);
		if (p1 != null)
			foreach (var k in p1.Loci.Keys)
				keys.Add(k);
		if (p2 != null)
			foreach (var k in p2.Loci.Keys)
				keys.Add(k);

		foreach (var geneId in keys)
		{
			var def = GeneCatalog.GetGene(geneId);
			if (def == null)
				continue;

			var a1 = PickAllele(rng, p1, geneId, def);
			var a2 = PickAllele(rng, p2, geneId, def);
			if (rng.NextDouble() < DefaultMutationRate)
				a1 = Flip(def, a1);
			if (rng.NextDouble() < DefaultMutationRate)
				a2 = Flip(def, a2);

			child.Loci[geneId] = new LocusPair { A = a1, B = a2 };
		}

		return child;
	}

	private static string PickAllele(Random rng, GenePool? parent, string geneId, GeneDef def)
	{
		if (parent != null && parent.Loci.TryGetValue(geneId, out var pair))
			return rng.Next(2) == 0 ? pair.A : pair.B;
		return rng.Next(2) == 0 ? def.AlleleA : def.AlleleB;
	}

	private static string Flip(GeneDef def, string current) =>
		string.Equals(current, def.AlleleA, StringComparison.Ordinal) ? def.AlleleB : def.AlleleA;
}
