using System;

namespace MiniRPG.Core.Genetics;

/// <summary>
/// Spawn 角色时一次性给基因池：
/// 流派固定基因必出现（locus 用 alleleA/alleleB 随机组合），可选基因按概率纳入。
/// </summary>
public static class GeneSpawnService
{
	public const double DefaultRandomGeneChance = 0.55;

	public static GenePool CreateForRace(Random rng, string? raceId, string? xenotypeId = null)
	{
		GeneCatalog.EnsureLoaded();
		var pool = new GenePool();
		var xeno = GeneCatalog.ResolveXenotype(raceId, xenotypeId);

		foreach (var gid in xeno.FixedGenes)
			FillRandomLocus(rng, pool, gid);

		foreach (var gid in xeno.RandomGenes)
		{
			if (pool.Loci.ContainsKey(gid))
				continue;
			if (rng.NextDouble() < DefaultRandomGeneChance)
				FillRandomLocus(rng, pool, gid);
		}

		return pool;
	}

	private static void FillRandomLocus(Random rng, GenePool pool, string geneId)
	{
		var def = GeneCatalog.GetGene(geneId);
		if (def == null)
			return;
		var a = rng.Next(2) == 0 ? def.AlleleA : def.AlleleB;
		var b = rng.Next(2) == 0 ? def.AlleleA : def.AlleleB;
		pool.Loci[geneId] = new LocusPair { A = a, B = b };
	}
}
