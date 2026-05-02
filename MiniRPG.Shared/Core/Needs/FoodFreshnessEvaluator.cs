using System;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Needs;

/// <summary>
/// 食物的新鲜阶段：从 spawn 起按 freshness_turns 比例分四档。
/// </summary>
public enum FoodFreshnessStage
{
	/// <summary>新鲜：age &lt; 0.5 × freshness_turns。营养与情绪正常。</summary>
	Fresh,
	/// <summary>陈旧：0.5 ≤ age &lt; 1.0 × freshness_turns。营养减半，吃了感觉不愉快。</summary>
	Stale,
	/// <summary>变质：1.0 ≤ age &lt; 2.0 × freshness_turns。营养残余，有概率食物中毒。</summary>
	Spoiled,
	/// <summary>腐烂：age ≥ 2.0 × freshness_turns。禁止主动吃。</summary>
	Rotten,
}

/// <summary>
/// 把 <see cref="Item.SpawnedAtTurn"/> + freshness_turns tag 翻译成 <see cref="FoodFreshnessStage"/>。
/// </summary>
/// <remarks>
/// <para>设计意图（见 <c>Docs/产品愿景.md</c> "代入感重于爽感" + "结果可追溯"）：
/// 玩家每次开背包都该感受到"什么快坏了"。这条压力源不靠数值膨胀，
/// 而是把已有的"吃食物 + 营养 + 情绪"循环和时间线挂钩。</para>
///
/// <para>静态构造把自身的 <see cref="SameStage"/> 注入到 <see cref="Item.FreshnessSamePhaseHook"/>，
/// 让 <see cref="Item.CanStackWith"/> 拒绝跨阶段的合堆。
/// 第一次访问本类（任意 public 静态方法）即触发 wire-up；测试可调 <see cref="EnsureInitialized"/> 显式预热。</para>
///
/// <para>合堆策略不依赖"当前回合"——用 spawn-time bucket：bucket = max(1, freshness_turns / 2)。
/// 同一 bucket 的两份食物 spawn 时间差 ≤ 半保质期，至多相邻一档；合堆后取 existing 的 SpawnedAtTurn
/// 作代表（相当于偏保守地按更早的 spawn 算 → 不会出现"过期不分"）。</para>
/// </remarks>
public static class FoodFreshnessEvaluator
{
	static FoodFreshnessEvaluator()
	{
		Item.FreshnessSamePhaseHook = SameStage;
	}

	/// <summary>显式触发静态构造（让 <see cref="Item.FreshnessSamePhaseHook"/> 替换为更宽松判断）。</summary>
	public static void EnsureInitialized() { /* 触发 cctor 的副作用 */ }

	/// <summary>评估一份食物的新鲜阶段。SpawnedAtTurn &lt; 0 或 freshness_turns &lt;= 0 永远 <see cref="FoodFreshnessStage.Fresh"/>。</summary>
	public static FoodFreshnessStage Evaluate(Item item, int currentTurn)
	{
		if (item == null)
			return FoodFreshnessStage.Fresh;

		var freshTurns = ResolveFreshnessTurns(item);
		if (item.SpawnedAtTurn < 0 || freshTurns <= 0)
			return FoodFreshnessStage.Fresh;

		var age = currentTurn - item.SpawnedAtTurn;
		if (age <= 0)
			return FoodFreshnessStage.Fresh;

		var ratio = (float)age / freshTurns;
		if (ratio < 0.5f) return FoodFreshnessStage.Fresh;
		if (ratio < 1.0f) return FoodFreshnessStage.Stale;
		if (ratio < 2.0f) return FoodFreshnessStage.Spoiled;
		return FoodFreshnessStage.Rotten;
	}

	/// <summary>
	/// 判定两份食物是否处在同一新鲜阶段（用于 <see cref="Item.CanStackWith"/>）。
	/// 不依赖"当前回合"——用 spawn-time bucket（bucket = max(1, freshness_turns / 2)）。
	/// 两侧都 -1 / 都不可腐 → 同阶段；一侧 -1 一侧追踪 → 不同阶段。
	/// </summary>
	public static bool SameStage(Item a, Item b)
	{
		if (a == null || b == null)
			return false;

		// 都不追踪 → 视为同阶段（罐头之类）
		if (a.SpawnedAtTurn < 0 && b.SpawnedAtTurn < 0)
			return true;

		// 一侧追踪一侧不追踪 → 不能合堆（避免新鲜的混进永不过期堆里被永久保鲜）
		if (a.SpawnedAtTurn < 0 || b.SpawnedAtTurn < 0)
			return false;

		var ftA = ResolveFreshnessTurns(a);
		var ftB = ResolveFreshnessTurns(b);
		if (ftA != ftB)
			return false;
		if (ftA <= 0)
			return true;

		var bucket = Math.Max(1, ftA / 2);
		return (a.SpawnedAtTurn / bucket) == (b.SpawnedAtTurn / bucket);
	}

	/// <summary>从 item 的 tags 中读出 freshness_turns；不存在或非正数视为 0（永不过期）。</summary>
	public static int ResolveFreshnessTurns(Item item)
	{
		if (item == null) return 0;
		foreach (var alias in FreshnessTurnsTagAliases)
		{
			if (item.Tags.TryGetValue(alias, out var v) && v > 0)
				return v;
		}
		return 0;
	}

	private static readonly string[] FreshnessTurnsTagAliases =
	[
		ItemTags.FreshnessTurns,
		"freshness_turns",
	];
}
