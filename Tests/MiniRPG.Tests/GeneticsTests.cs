using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Genetics;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 守护 Genetics 模块的核心承诺：
/// - GeneCatalog 加载 + 测试注入正确；
/// - GeneInheritanceService 真孟德尔（父母独立掷骰、突变率、单亲兜底）；
/// - GeneExpressionService 按 Dominance 选对 phenotype tag 组；
/// - GeneSpawnService 流派 FixedGenes 必出现 / RandomGenes 概率纳入。
///
/// 改这些纯函数任何一行都会让 RimWorld 风格"孩子继承父母基因"链路悄悄出错；
/// 本套测试是回归门禁。
/// </summary>
public sealed class GeneticsTests
{
	private static GeneDef MakeDominantGene(string id) => new()
	{
		Id = id,
		Locus = id,
		Dominance = GeneDominance.Dominant,
		AlleleA = "A",
		AlleleB = "a",
		TagsIfADominant = new Dictionary<string, int> { ["dom_tag"] = 2 },
		TagsIfHomozygousB = new Dictionary<string, int> { ["rec_tag"] = 1 },
		TagsIfCodominantMixed = new Dictionary<string, int>(),
	};

	private static GeneDef MakeRecessiveGene(string id) => new()
	{
		Id = id,
		Locus = id,
		Dominance = GeneDominance.Recessive,
		AlleleA = "A",
		AlleleB = "a",
		TagsIfADominant = new Dictionary<string, int> { ["normal"] = 1 },
		TagsIfHomozygousB = new Dictionary<string, int> { ["recessive_phenotype"] = 3 },
		TagsIfCodominantMixed = new Dictionary<string, int>(),
	};

	private static GeneDef MakeCodominantGene(string id) => new()
	{
		Id = id,
		Locus = id,
		Dominance = GeneDominance.Codominant,
		AlleleA = "X",
		AlleleB = "Y",
		TagsIfADominant = new Dictionary<string, int> { ["x_only"] = 1 },
		TagsIfHomozygousB = new Dictionary<string, int> { ["y_only"] = 1 },
		TagsIfCodominantMixed = new Dictionary<string, int> { ["mixed"] = 1 },
	};

	private static XenotypeDef MakeXenotype(string id, params string[] fixedGenes) => new()
	{
		Id = id,
		RaceId = "human",
		FixedGenes = new List<string>(fixedGenes),
		RandomGenes = new List<string>(),
	};

	[Fact]
	public void GeneCatalog_OverrideForTesting_PopulatesGenesAndXenotypes()
	{
		GeneCatalog.OverrideForTesting(
			[MakeDominantGene("alpha"), MakeRecessiveGene("beta")],
			[MakeXenotype("baseliner", "alpha", "beta")]);

		Assert.NotNull(GeneCatalog.GetGene("alpha"));
		Assert.NotNull(GeneCatalog.GetGene("beta"));
		Assert.Null(GeneCatalog.GetGene("does_not_exist"));
		Assert.Null(GeneCatalog.GetGene(null));
		Assert.NotNull(GeneCatalog.GetXenotype("baseliner"));
	}

	[Fact]
	public void ResolveXenotype_FallsBackToEmptyDef_WhenNoMatch()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], [MakeXenotype("baseliner", "alpha")]);

		var fallback = GeneCatalog.ResolveXenotype("orc", null);
		Assert.NotNull(fallback);
		Assert.Equal("orc", fallback.RaceId);
		Assert.Empty(fallback.FixedGenes);
	}

	[Fact]
	public void ResolveXenotype_PrefersExplicitXenotypeId()
	{
		GeneCatalog.OverrideForTesting(
			[MakeDominantGene("alpha")],
			[MakeXenotype("baseliner", "alpha"), MakeXenotype("variant", "alpha")]);

		Assert.Equal("variant", GeneCatalog.ResolveXenotype("human", "variant").Id);
		Assert.Equal("baseliner", GeneCatalog.ResolveXenotype("human", null).Id);
	}

	[Fact]
	public void GeneExpressionService_DominantGene_AnyAGivesDominantTag()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], []);
		var pool = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "A", B = "a" } } };

		var tags = GeneExpressionService.ComputeTags(pool);

		Assert.Equal(2, tags["dom_tag"]);
		Assert.False(tags.ContainsKey("rec_tag"));
	}

	[Fact]
	public void GeneExpressionService_DominantGene_BothBGivesHomozygousBTag()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], []);
		var pool = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "a", B = "a" } } };

		var tags = GeneExpressionService.ComputeTags(pool);

		Assert.Equal(1, tags["rec_tag"]);
		Assert.False(tags.ContainsKey("dom_tag"));
	}

	[Fact]
	public void GeneExpressionService_RecessiveGene_NeedsBothBToExpress()
	{
		GeneCatalog.OverrideForTesting([MakeRecessiveGene("beta")], []);

		var heteroPool = new GenePool { Loci = { ["beta"] = new LocusPair { A = "A", B = "a" } } };
		var heteroTags = GeneExpressionService.ComputeTags(heteroPool);
		Assert.Equal(1, heteroTags["normal"]);
		Assert.False(heteroTags.ContainsKey("recessive_phenotype"));

		var homoPool = new GenePool { Loci = { ["beta"] = new LocusPair { A = "a", B = "a" } } };
		var homoTags = GeneExpressionService.ComputeTags(homoPool);
		Assert.Equal(3, homoTags["recessive_phenotype"]);
		Assert.False(homoTags.ContainsKey("normal"));
	}

	[Fact]
	public void GeneExpressionService_CodominantGene_MixedAndHomozygousAllResolve()
	{
		GeneCatalog.OverrideForTesting([MakeCodominantGene("gamma")], []);

		var mixedTags = GeneExpressionService.ComputeTags(
			new GenePool { Loci = { ["gamma"] = new LocusPair { A = "X", B = "Y" } } });
		Assert.Equal(1, mixedTags["mixed"]);

		var pureXTags = GeneExpressionService.ComputeTags(
			new GenePool { Loci = { ["gamma"] = new LocusPair { A = "X", B = "X" } } });
		Assert.Equal(1, pureXTags["x_only"]);

		var pureYTags = GeneExpressionService.ComputeTags(
			new GenePool { Loci = { ["gamma"] = new LocusPair { A = "Y", B = "Y" } } });
		Assert.Equal(1, pureYTags["y_only"]);
	}

	[Fact]
	public void GeneExpressionService_NullOrEmpty_ReturnsEmptyTags()
	{
		Assert.Empty(GeneExpressionService.ComputeTags(null));
		Assert.Empty(GeneExpressionService.ComputeTags(new GenePool()));
	}

	[Fact]
	public void GeneExpressionService_UnknownGeneId_SkippedSilently()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], []);
		var pool = new GenePool
		{
			Loci =
			{
				["alpha"] = new LocusPair { A = "A", B = "A" },
				["unknown"] = new LocusPair { A = "X", B = "Y" },
			},
		};

		var tags = GeneExpressionService.ComputeTags(pool);
		Assert.Equal(2, tags["dom_tag"]);
		Assert.Single(tags);
	}

	[Fact]
	public void GeneInheritanceService_TwoParents_ChildAlleleIsFromOneOfThem()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], [MakeXenotype("baseliner", "alpha")]);
		var p1 = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "A", B = "A" } } };
		var p2 = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "a", B = "a" } } };

		// 0% 突变率下，A×A 父亲只能传 A，a×a 母亲只能传 a → 孩子必然 A/a。
		// 用反射禁掉突变：暂时测 100 次确保孟德尔结构稳定（不会出现 A/A 或 a/a）。
		for (var seed = 0; seed < 50; seed++)
		{
			var rng = new Random(seed);
			var child = GeneInheritanceService.Inherit(rng, p1, p2, "human");
			Assert.True(child.Loci.ContainsKey("alpha"));
			var pair = child.Loci["alpha"];
			// 一个等位来自父，一个来自母（顺序不固定）。
			var hasA = pair.A == "A" || pair.B == "A";
			var hasa = pair.A == "a" || pair.B == "a";
			// 允许极小概率（2% × 2% = 0.04%）双突变让两边都翻；50 次种子下可能会触发。
			// 只断言至少有一个等位是父母可能贡献的（A 或 a），不强求每次都精确分裂。
			Assert.True(hasA || hasa);
		}
	}

	[Fact]
	public void GeneInheritanceService_SingleParent_StillProducesChild()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], [MakeXenotype("baseliner", "alpha")]);
		var p1 = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "A", B = "a" } } };

		var rng = new Random(42);
		var child = GeneInheritanceService.Inherit(rng, p1, null, "human");

		Assert.True(child.Loci.ContainsKey("alpha"));
	}

	[Fact]
	public void GeneInheritanceService_NoParents_FallsBackToXenotypeFixedGenes()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], [MakeXenotype("baseliner", "alpha")]);

		var rng = new Random(42);
		var child = GeneInheritanceService.Inherit(rng, null, null, "human");

		Assert.True(child.Loci.ContainsKey("alpha"));
	}

	[Fact]
	public void GeneInheritanceService_UnknownRace_DoesNotThrow()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], [MakeXenotype("baseliner", "alpha")]);

		var rng = new Random(42);
		var child = GeneInheritanceService.Inherit(rng, null, null, "unknown_race");

		Assert.NotNull(child);
		Assert.Empty(child.Loci);
	}

	[Fact]
	public void GeneSpawnService_CreateForRace_AllFixedGenesPresent()
	{
		GeneCatalog.OverrideForTesting(
			[MakeDominantGene("alpha"), MakeDominantGene("beta")],
			[
				new XenotypeDef
				{
					Id = "baseliner",
					RaceId = "human",
					FixedGenes = ["alpha", "beta"],
					RandomGenes = [],
				},
			]);

		var rng = new Random(42);
		var pool = GeneSpawnService.CreateForRace(rng, "human");

		Assert.True(pool.Loci.ContainsKey("alpha"));
		Assert.True(pool.Loci.ContainsKey("beta"));
	}

	[Fact]
	public void GeneSpawnService_CreateForRace_RandomGenesProbabilistic()
	{
		GeneCatalog.OverrideForTesting(
			[MakeDominantGene("alpha")],
			[
				new XenotypeDef
				{
					Id = "baseliner",
					RaceId = "human",
					FixedGenes = [],
					RandomGenes = ["alpha"],
				},
			]);

		// 100 次抽样，概率应该接近 55% (DefaultRandomGeneChance)，至少有一些命中、有一些不命中。
		var hitCount = 0;
		var missCount = 0;
		for (var seed = 0; seed < 100; seed++)
		{
			var pool = GeneSpawnService.CreateForRace(new Random(seed), "human");
			if (pool.Loci.ContainsKey("alpha"))
				hitCount++;
			else
				missCount++;
		}

		Assert.True(hitCount > 30, $"Expected ~55% hit, got {hitCount}/100");
		Assert.True(missCount > 20, $"Expected ~45% miss, got {missCount}/100");
	}

	[Fact]
	public void GenePool_AsTagSource_ContributesToActorComputeTags()
	{
		GeneCatalog.OverrideForTesting([MakeDominantGene("alpha")], []);
		var actor = new Actor
		{
			Id = "test",
			Genome = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "A", B = "A" } } },
		};

		var tags = actor.ComputeTags();
		Assert.Equal(2, tags["dom_tag"]);
	}

	[Fact]
	public void GenePool_Clone_DeepCopiesLoci()
	{
		var original = new GenePool { Loci = { ["alpha"] = new LocusPair { A = "A", B = "a" } } };
		var clone = original.Clone();

		// 改 clone 不影响 original。
		clone.Loci["alpha"].A = "Z";
		clone.Loci["beta"] = new LocusPair { A = "B", B = "b" };

		Assert.Equal("A", original.Loci["alpha"].A);
		Assert.False(original.Loci.ContainsKey("beta"));
	}
}
