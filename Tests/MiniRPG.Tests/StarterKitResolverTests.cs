using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// 守护「初始携带物品」三档解析与 debug 绕过：
/// - <see cref="StarterKitResolver.ResolveDefault"/>：职业默认包 → 通用 fallback；
/// - <see cref="StarterKitResolver.Resolve"/>：玩家选项优先于职业默认；
/// - <see cref="StarterKitResolver.Validate"/>：非 debug 严格按白名单 + 每类上限裁剪；
///   debug 模式仅按 PerEntryMaxCount 截断。
/// </summary>
public sealed class StarterKitResolverTests
{
	public StarterKitResolverTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
		StarterKitCatalog.ResetForTesting();
		StarterKitCatalog.EnsureLoaded();
	}

	private static int Count(IEnumerable<PlayerStartingItem> entries, string itemId)
	{
		return entries.Where(e => e.ItemId == itemId).Sum(e => e.Count);
	}

	[Fact]
	public void ResolveDefault_NullProfession_FallsBackToCatalogDefault()
	{
		var entries = StarterKitResolver.ResolveDefault(null);
		Assert.NotEmpty(entries);
		// fallback 包至少包含 meal_simple 和 water_flask，是"温饱兜底"两件套。
		Assert.True(Count(entries, "meal_simple") > 0);
		Assert.True(Count(entries, "water_flask") > 0);
	}

	[Fact]
	public void ResolveDefault_UnknownProfession_FallsBackToCatalogDefault()
	{
		var entries = StarterKitResolver.ResolveDefault("not_a_real_profession");
		var fallback = StarterKitResolver.ResolveDefault(null);
		Assert.Equal(
			fallback.OrderBy(e => e.ItemId).Select(e => (e.ItemId, e.Count)),
			entries.OrderBy(e => e.ItemId).Select(e => (e.ItemId, e.Count)));
	}

	[Fact]
	public void ResolveDefault_HunterProfession_DeliversHunterKit()
	{
		// Data/professions.json 配置：hunter 给 raw_meat × 3 + berries × 2 + water_flask × 2 + bedroll × 1。
		// 没有 meal_simple——猎人开局靠原料下饭，不靠温饱保险。
		var entries = StarterKitResolver.ResolveDefault("hunter");
		Assert.True(Count(entries, "raw_meat") >= 2);
		Assert.True(Count(entries, "berries") >= 1);
		Assert.True(Count(entries, "bedroll") >= 1);
		Assert.Equal(0, Count(entries, "meal_simple"));
	}

	[Fact]
	public void ResolveDefault_ProfessionWithoutKit_FallsBackToCatalogDefault()
	{
		// elder 在 Data/professions.json 里**没有** starterKit 字段 → 走 fallback。
		var elder = StarterKitResolver.ResolveDefault("elder");
		var fallback = StarterKitResolver.ResolveDefault(null);
		Assert.Equal(
			fallback.OrderBy(e => e.ItemId).Select(e => (e.ItemId, e.Count)),
			elder.OrderBy(e => e.ItemId).Select(e => (e.ItemId, e.Count)));
	}

	[Fact]
	public void Resolve_NullStartingItems_GoesProfessionPath()
	{
		var options = new PlayerCreationOptions
		{
			DisplayName = "Tester",
			RaceId = "human",
			ProfessionId = "hunter",
			StartingItems = null,
		};
		var entries = StarterKitResolver.Resolve(options);
		Assert.True(Count(entries, "raw_meat") >= 2);
	}

	[Fact]
	public void Resolve_EmptyStartingItems_ReturnsEmpty()
	{
		var options = new PlayerCreationOptions
		{
			DisplayName = "Tester",
			RaceId = "human",
			ProfessionId = "hunter",
			StartingItems = new List<PlayerStartingItem>(),
		};
		var entries = StarterKitResolver.Resolve(options);
		Assert.Empty(entries);
	}

	[Fact]
	public void Validate_OutsideWhitelist_FilteredOutInNormalMode()
	{
		// potion_hp 是 consumable，不在 starter_kit.json 的任一 category。
		var input = new[]
		{
			new PlayerStartingItem("potion_hp", 1),
			new PlayerStartingItem("meal_simple", 1),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: false);
		Assert.Equal(0, Count(result, "potion_hp"));
		Assert.Equal(1, Count(result, "meal_simple"));
	}

	[Fact]
	public void Validate_OutsideWhitelist_KeptInDebugMode()
	{
		var input = new[]
		{
			new PlayerStartingItem("potion_hp", 1),
			new PlayerStartingItem("meal_simple", 1),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: true);
		Assert.Equal(1, Count(result, "potion_hp"));
		Assert.Equal(1, Count(result, "meal_simple"));
	}

	[Fact]
	public void Validate_ExceedsCategoryLimit_TruncatedInNormalMode()
	{
		// food 类上限 4：5 件 meal_simple → 截到 4。
		var input = new[]
		{
			new PlayerStartingItem("meal_simple", 5),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: false);
		Assert.Equal(4, Count(result, "meal_simple"));
	}

	[Fact]
	public void Validate_ExceedsCategoryLimit_NotTruncatedInDebugMode()
	{
		var input = new[]
		{
			new PlayerStartingItem("meal_simple", 5),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: true);
		Assert.Equal(5, Count(result, "meal_simple"));
	}

	[Fact]
	public void Validate_DuplicateEntries_MergedIntoSingleRow()
	{
		var input = new[]
		{
			new PlayerStartingItem("meal_simple", 1),
			new PlayerStartingItem("meal_simple", 2),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: false);
		Assert.Single(result);
		Assert.Equal(3, result[0].Count);
		Assert.Equal("meal_simple", result[0].ItemId);
	}

	[Fact]
	public void Validate_UnknownItemId_DroppedSilently()
	{
		var input = new[]
		{
			new PlayerStartingItem("definitely_not_a_real_item_id", 5),
			new PlayerStartingItem("meal_simple", 1),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: true);
		Assert.Single(result);
		Assert.Equal("meal_simple", result[0].ItemId);
	}

	[Fact]
	public void Validate_NonPositiveCount_DroppedSilently()
	{
		var input = new[]
		{
			new PlayerStartingItem("meal_simple", 0),
			new PlayerStartingItem("water_flask", -2),
			new PlayerStartingItem("bedroll", 1),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: false);
		Assert.Single(result);
		Assert.Equal("bedroll", result[0].ItemId);
	}

	[Fact]
	public void Validate_PerEntryCap_AppliedEvenInDebugMode()
	{
		// PerEntryMaxCount = 99：单条 200 件被截到 99。
		var input = new[]
		{
			new PlayerStartingItem("meal_simple", 200),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: true);
		Assert.Equal(StarterKitResolver.PerEntryMaxCount, result[0].Count);
	}

	[Fact]
	public void Validate_RestCategoryLimit_OnePerSession()
	{
		// rest 类上限 1：玩家想带 3 个 bedroll → 截到 1。
		var input = new[]
		{
			new PlayerStartingItem("bedroll", 3),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: false);
		Assert.Equal(1, Count(result, "bedroll"));
	}

	[Fact]
	public void Validate_MixedCategoryLimits_BlocksFurtherIncrementsAfterCap()
	{
		// food 类上限 4：先 meal_simple × 3 + bread × 3 → bread 应该只能加 1（剩余 4 - 3 = 1）。
		var input = new[]
		{
			new PlayerStartingItem("meal_simple", 3),
			new PlayerStartingItem("bread", 3),
		};
		var result = StarterKitResolver.Validate(input, debugOverride: false);
		Assert.Equal(3, Count(result, "meal_simple"));
		Assert.Equal(1, Count(result, "bread"));
	}
}
