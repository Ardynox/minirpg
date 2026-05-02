using MiniRPG.Core.Config;
using MiniRPG.Core.Needs;

namespace MiniRPG.Core.Data;

public static class ItemConditionFormatter
{
	public static string GetConditionLabel(Item item)
	{
		var percent = item.ConditionPercent;
		var key = percent switch
		{
			>= 95f => "item.condition.pristine",
			>= 75f => "item.condition.good",
			>= 45f => "item.condition.worn",
			>= 15f => "item.condition.damaged",
			_ => "item.condition.critical",
		};
		var fallback = percent switch
		{
			>= 95f => "Pristine",
			>= 75f => "Good",
			>= 45f => "Worn",
			>= 15f => "Damaged",
			_ => "Critical",
		};
		return LocalizationService.TOrFallback(key, fallback);
	}

	public static string BuildInlineDurability(Item item) =>
		LocalizationService.TOrFallback(
			"item.inline.durability",
			"Dur {current}/{max} {condition}",
			("current", item.Durability),
			("max", item.MaxDurability),
			("condition", GetConditionLabel(item)));

	public static string BuildDetailDurability(Item item) =>
		LocalizationService.TOrFallback(
			"item.detail.durability",
			"Durability: {current}/{max} ({condition})",
			("current", item.Durability),
			("max", item.MaxDurability),
			("condition", GetConditionLabel(item)));

	/// <summary>食物当前新鲜度的本地化短标签，例如 "新鲜" / "陈旧" / "变质" / "腐烂"。</summary>
	public static string GetFreshnessLabel(FoodFreshnessStage stage)
	{
		var key = stage switch
		{
			FoodFreshnessStage.Fresh => "item.freshness.fresh",
			FoodFreshnessStage.Stale => "item.freshness.stale",
			FoodFreshnessStage.Spoiled => "item.freshness.spoiled",
			FoodFreshnessStage.Rotten => "item.freshness.rotten",
			_ => "item.freshness.fresh",
		};
		var fallback = stage switch
		{
			FoodFreshnessStage.Fresh => "Fresh",
			FoodFreshnessStage.Stale => "Stale",
			FoodFreshnessStage.Spoiled => "Spoiled",
			FoodFreshnessStage.Rotten => "Rotten",
			_ => "Fresh",
		};
		return LocalizationService.TOrFallback(key, fallback);
	}

	/// <summary>详情面板用的"新鲜度：陈旧"行；item 不是食物或不追踪保质期时返回空。</summary>
	public static string BuildDetailFreshness(Item item, int currentTurn)
	{
		if (!IsTrackedFood(item))
			return string.Empty;
		var stage = FoodFreshnessEvaluator.Evaluate(item, currentTurn);
		return LocalizationService.TOrFallback(
			"item.detail.freshness",
			"Freshness: {label}",
			("label", GetFreshnessLabel(stage)));
	}

	/// <summary>背包行内紧凑显示用 "[陈旧]"；非追踪食物返回空。</summary>
	public static string BuildInlineFreshness(Item item, int currentTurn)
	{
		if (!IsTrackedFood(item))
			return string.Empty;
		var stage = FoodFreshnessEvaluator.Evaluate(item, currentTurn);
		// Fresh 阶段不显示，避免 UI 噪音；只在 Stale 起开始显示。
		if (stage == FoodFreshnessStage.Fresh)
			return string.Empty;
		return LocalizationService.TOrFallback(
			"item.inline.freshness",
			"[{label}]",
			("label", GetFreshnessLabel(stage)));
	}

	private static bool IsTrackedFood(Item item) =>
		item != null
		&& item.SpawnedAtTurn >= 0
		&& FoodFreshnessEvaluator.ResolveFreshnessTurns(item) > 0;
}
