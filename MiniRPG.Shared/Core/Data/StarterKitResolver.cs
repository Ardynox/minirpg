using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Data;

/// <summary>
/// 把 <see cref="PlayerCreationOptions"/> 上的「初始携带物品」意图转成 <c>MapGenModule.GiveStarterKit</c>
/// 真正会写入背包的物品清单。规则：
/// <list type="number">
/// <item>玩家明确给了 <see cref="PlayerCreationOptions.StartingItems"/>（即使空列表）→ 走玩家清单，
/// 非 debug 模式下按 <see cref="StarterKitCatalog"/> 白名单 + 每类上限严格裁剪；
/// debug 模式不裁剪（仅本地单机有效）。</item>
/// <item>玩家没指定（null）→ 取该职业 <see cref="ProfessionPreset.StarterKit"/>；为空再退到
/// <see cref="StarterKitCatalog.GetFallbackKit"/>（通用 9 件求生包）。</item>
/// </list>
/// 所有出口都保证 <see cref="PresetDB.Items"/> 里命中的合法 itemId，重复 itemId 会按
/// "出现顺序合并 count"压成同一行（不是合堆，只是聚合数量）。
/// </summary>
public static class StarterKitResolver
{
	/// <summary>玩家清单单行最大数量上限，纯防御用，避免 UI 异常或恶意输入产出 99999 件。</summary>
	public const int PerEntryMaxCount = 99;

	public static IReadOnlyList<PlayerStartingItem> Resolve(PlayerCreationOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (options.StartingItems != null)
			return Validate(options.StartingItems, options.StartingItemsDebugOverride);

		return ResolveDefault(options.ProfessionId);
	}

	public static IReadOnlyList<PlayerStartingItem> ResolveDefault(string? professionId)
	{
		if (!string.IsNullOrWhiteSpace(professionId)
			&& PresetDB.Professions.TryGetValue(professionId, out var profession)
			&& profession.StarterKit is { Count: > 0 } professionKit)
		{
			return DictionaryToEntries(professionKit);
		}

		return DictionaryToEntries(StarterKitCatalog.GetFallbackKit());
	}

	/// <summary>
	/// 把玩家提交的清单按规则规范化：①过滤未知 itemId / count<=0；②非 debug 模式按白名单丢弃 +
	/// 按每类 limit 截断；③合并同 itemId 行；④截到 PerEntryMaxCount。
	/// </summary>
	public static IReadOnlyList<PlayerStartingItem> Validate(
		IEnumerable<PlayerStartingItem> entries,
		bool debugOverride)
	{
		ArgumentNullException.ThrowIfNull(entries);

		var merged = new Dictionary<string, int>(StringComparer.Ordinal);
		var insertionOrder = new List<string>();
		foreach (var entry in entries)
		{
			if (entry == null) continue;
			var id = entry.ItemId;
			if (string.IsNullOrWhiteSpace(id)) continue;
			if (entry.Count <= 0) continue;
			if (!PresetDB.Items.ContainsKey(id)) continue;

			if (!merged.ContainsKey(id))
			{
				merged[id] = 0;
				insertionOrder.Add(id);
			}
			merged[id] = Math.Min(PerEntryMaxCount, merged[id] + entry.Count);
		}

		if (!debugOverride)
		{
			ApplyWhitelistAndCategoryLimits(merged, insertionOrder);
		}

		var result = new List<PlayerStartingItem>(insertionOrder.Count);
		foreach (var id in insertionOrder)
		{
			if (merged.TryGetValue(id, out var count) && count > 0)
				result.Add(new PlayerStartingItem(id, count));
		}
		return result;
	}

	private static void ApplyWhitelistAndCategoryLimits(
		Dictionary<string, int> merged,
		List<string> insertionOrder)
	{
		var categoryUsed = new Dictionary<string, int>(StringComparer.Ordinal);
		for (int i = 0; i < insertionOrder.Count; i++)
		{
			var id = insertionOrder[i];
			var category = StarterKitCatalog.GetCategoryFor(id);
			if (category == null)
			{
				merged.Remove(id);
				insertionOrder.RemoveAt(i--);
				continue;
			}

			var requested = merged[id];
			var alreadyUsed = categoryUsed.GetValueOrDefault(category.Id);
			var roomLeft = Math.Max(0, category.Limit - alreadyUsed);
			var allowed = Math.Min(requested, roomLeft);
			if (allowed <= 0)
			{
				merged.Remove(id);
				insertionOrder.RemoveAt(i--);
				continue;
			}
			merged[id] = allowed;
			categoryUsed[category.Id] = alreadyUsed + allowed;
		}
	}

	private static IReadOnlyList<PlayerStartingItem> DictionaryToEntries(IReadOnlyDictionary<string, int> dict)
	{
		if (dict.Count == 0)
			return [];

		var list = new List<PlayerStartingItem>(dict.Count);
		foreach (var (id, count) in dict)
		{
			if (string.IsNullOrWhiteSpace(id) || count <= 0) continue;
			if (!PresetDB.Items.ContainsKey(id)) continue;
			list.Add(new PlayerStartingItem(id, Math.Min(PerEntryMaxCount, count)));
		}
		return list;
	}
}
