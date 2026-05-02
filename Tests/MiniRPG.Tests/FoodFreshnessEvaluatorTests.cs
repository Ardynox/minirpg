using MiniRPG.Core.Data;
using MiniRPG.Core.Needs;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 验证 <see cref="FoodFreshnessEvaluator"/> 的阈值边界、SameStage 合堆判定，以及
/// <see cref="Item.FreshnessSamePhaseHook"/> 在 evaluator 加载后被替换。
/// </summary>
public sealed class FoodFreshnessEvaluatorTests
{
	public FoodFreshnessEvaluatorTests()
	{
		FoodFreshnessEvaluator.EnsureInitialized();
	}

	[Fact]
	public void Evaluate_NoSpawnedAtTurn_AlwaysFresh()
	{
		var item = new Item { Id = "x", SpawnedAtTurn = -1 };
		item.Tags[ItemTags.FreshnessTurns] = 10;
		Assert.Equal(FoodFreshnessStage.Fresh, FoodFreshnessEvaluator.Evaluate(item, 1000));
	}

	[Fact]
	public void Evaluate_NoFreshnessTag_AlwaysFresh()
	{
		var item = new Item { Id = "x", SpawnedAtTurn = 0 };
		Assert.Equal(FoodFreshnessStage.Fresh, FoodFreshnessEvaluator.Evaluate(item, 9999));
	}

	[Theory]
	[InlineData(0, FoodFreshnessStage.Fresh)]   // age=0
	[InlineData(4, FoodFreshnessStage.Fresh)]   // ratio=0.4
	[InlineData(5, FoodFreshnessStage.Stale)]   // ratio=0.5
	[InlineData(9, FoodFreshnessStage.Stale)]   // ratio=0.9
	[InlineData(10, FoodFreshnessStage.Spoiled)] // ratio=1.0
	[InlineData(19, FoodFreshnessStage.Spoiled)] // ratio=1.9
	[InlineData(20, FoodFreshnessStage.Rotten)]  // ratio=2.0
	[InlineData(50, FoodFreshnessStage.Rotten)]  // far past
	public void Evaluate_FourStageThresholds(int age, FoodFreshnessStage expected)
	{
		var item = new Item { Id = "x", SpawnedAtTurn = 0 };
		item.Tags[ItemTags.FreshnessTurns] = 10;
		Assert.Equal(expected, FoodFreshnessEvaluator.Evaluate(item, age));
	}

	[Fact]
	public void Evaluate_NegativeAge_TreatedAsFresh()
	{
		var item = new Item { Id = "x", SpawnedAtTurn = 100 };
		item.Tags[ItemTags.FreshnessTurns] = 10;
		Assert.Equal(FoodFreshnessStage.Fresh, FoodFreshnessEvaluator.Evaluate(item, 50));
	}

	[Fact]
	public void ResolveFreshnessTurns_AcceptsLegacyEnglishAlias()
	{
		var item = new Item();
		item.Tags["freshness_turns"] = 12;
		Assert.Equal(12, FoodFreshnessEvaluator.ResolveFreshnessTurns(item));
	}

	[Fact]
	public void ResolveFreshnessTurns_ChineseAliasViaItemTagsConst()
	{
		var item = new Item();
		item.Tags[ItemTags.FreshnessTurns] = 8;
		Assert.Equal(8, FoodFreshnessEvaluator.ResolveFreshnessTurns(item));
	}

	[Fact]
	public void SameStage_BothUntracked_True()
	{
		var a = new Item { Id = "x", SpawnedAtTurn = -1 };
		var b = new Item { Id = "x", SpawnedAtTurn = -1 };
		Assert.True(FoodFreshnessEvaluator.SameStage(a, b));
	}

	[Fact]
	public void SameStage_OneTrackedOneUntracked_False()
	{
		var a = MakeTrackedFood(spawnTurn: 0, freshnessTurns: 10);
		var b = MakeTrackedFood(spawnTurn: -1, freshnessTurns: 10);
		Assert.False(FoodFreshnessEvaluator.SameStage(a, b));
	}

	[Fact]
	public void SameStage_DifferentFreshnessTurns_False()
	{
		var a = MakeTrackedFood(spawnTurn: 0, freshnessTurns: 10);
		var b = MakeTrackedFood(spawnTurn: 0, freshnessTurns: 20);
		Assert.False(FoodFreshnessEvaluator.SameStage(a, b));
	}

	[Fact]
	public void SameStage_WithinHalfFreshnessBucket_True()
	{
		// freshness=10 → bucket=5
		var a = MakeTrackedFood(spawnTurn: 0, freshnessTurns: 10);
		var b = MakeTrackedFood(spawnTurn: 4, freshnessTurns: 10);
		Assert.True(FoodFreshnessEvaluator.SameStage(a, b));
	}

	[Fact]
	public void SameStage_AcrossHalfFreshnessBucket_False()
	{
		// freshness=10 → bucket=5；spawn 4 vs 5 跨 bucket
		var a = MakeTrackedFood(spawnTurn: 4, freshnessTurns: 10);
		var b = MakeTrackedFood(spawnTurn: 5, freshnessTurns: 10);
		Assert.False(FoodFreshnessEvaluator.SameStage(a, b));
	}

	[Fact]
	public void Cctor_ReplacesItemFreshnessSamePhaseHook()
	{
		// 经过 EnsureInitialized 后，Item.CanStackWith 调 FreshnessSamePhaseHook 应走 evaluator.SameStage：
		// 测试方式是构造两份 SpawnedAtTurn 不同但同 bucket 的食物，预期可以合堆。
		var a = MakeStackableFood(spawnTurn: 0, freshnessTurns: 10);
		var b = MakeStackableFood(spawnTurn: 4, freshnessTurns: 10);
		Assert.True(a.CanStackWith(b), "evaluator.SameStage 已替换默认 hook，同 bucket 的食物应该可以合堆");
	}

	private static Item MakeTrackedFood(int spawnTurn, int freshnessTurns)
	{
		var item = new Item
		{
			Id = "raw_meat",
			MaterialId = "meat",
			Category = ItemCategories.Food,
			SpawnedAtTurn = spawnTurn,
			MaxStack = 10,
			StackCount = 1,
		};
		item.Tags[ItemTags.FreshnessTurns] = freshnessTurns;
		item.EnsureRuntimeState();
		return item;
	}

	private static Item MakeStackableFood(int spawnTurn, int freshnessTurns)
	{
		// CanStackWith 要求 IsStackable + 所有"身份字段"对齐（Id/MaterialId/MaxDurability/...）
		// 这里两个 item 都用相同的种子值，唯一不同就是 SpawnedAtTurn。
		var item = MakeTrackedFood(spawnTurn, freshnessTurns);
		item.MaxDurability = 1;
		item.Durability = 1;
		return item;
	}
}
