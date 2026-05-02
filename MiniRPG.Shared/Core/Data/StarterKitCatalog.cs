using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

/// <summary>
/// 「初始携带物品」白名单 + 每类上限 + 通用兜底包的注册表。
/// 数据从 <c>Data/Config/starter_kit.json</c> 加载，加载失败 / JSON 缺失时退回**内置默认**
/// （和当前硬编码 starter kit 等价的 9 件包），保证游戏一定可启动。
/// 线程安全（单写多读，加载只发生一次）。
/// </summary>
public static class StarterKitCatalog
{
	private static readonly object _lock = new();
	private static bool _loaded;
	private static readonly List<StarterKitCategoryDef> _categories = [];
	private static readonly Dictionary<string, StarterKitCategoryDef> _categoryByItem = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> _fallbackKit = new(StringComparer.Ordinal);

	public static void EnsureLoaded()
	{
		if (_loaded) return;
		lock (_lock)
		{
			if (_loaded) return;
			LoadFromDefaultDataFile();
			_loaded = true;
		}
	}

	public static void ResetForTesting()
	{
		lock (_lock)
		{
			_categories.Clear();
			_categoryByItem.Clear();
			_fallbackKit.Clear();
			_loaded = false;
		}
	}

	/// <summary>测试 / 编辑器夹具直接喂表，跳过文件加载。</summary>
	public static void OverrideForTesting(
		IEnumerable<StarterKitCategoryDef> categories,
		IReadOnlyDictionary<string, int>? fallbackKit = null)
	{
		lock (_lock)
		{
			_categories.Clear();
			_categoryByItem.Clear();
			_fallbackKit.Clear();
			foreach (var cat in categories)
			{
				if (cat == null || string.IsNullOrEmpty(cat.Id)) continue;
				_categories.Add(cat);
				foreach (var itemId in cat.Items)
				{
					if (string.IsNullOrEmpty(itemId)) continue;
					_categoryByItem[itemId] = cat;
				}
			}
			if (fallbackKit != null)
				foreach (var (k, v) in fallbackKit)
					if (!string.IsNullOrEmpty(k) && v > 0)
						_fallbackKit[k] = v;
			_loaded = true;
		}
	}

	public static IReadOnlyList<StarterKitCategoryDef> GetCategories()
	{
		EnsureLoaded();
		return _categories;
	}

	public static StarterKitCategoryDef? GetCategoryFor(string itemId)
	{
		EnsureLoaded();
		return _categoryByItem.GetValueOrDefault(itemId);
	}

	public static bool IsAllowed(string itemId)
	{
		EnsureLoaded();
		return _categoryByItem.ContainsKey(itemId);
	}

	public static IReadOnlyDictionary<string, int> GetFallbackKit()
	{
		EnsureLoaded();
		return _fallbackKit;
	}

	private static void LoadFromDefaultDataFile()
	{
		_categories.Clear();
		_categoryByItem.Clear();
		_fallbackKit.Clear();

		StarterKitFile? schema = null;
		if (GameDataLocator.TryReadText("Config/starter_kit.json", out var json, out _))
		{
			try
			{
				schema = JsonSerializer.Deserialize<StarterKitFile>(json, JsonOpts);
			}
			catch (JsonException)
			{
				// JSON 损坏：退回内置默认，不卡死游戏启动。
			}
		}

		if (schema?.Categories != null)
		{
			foreach (var entry in schema.Categories)
			{
				if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
				var def = new StarterKitCategoryDef
				{
					Id = entry.Id,
					Limit = Math.Max(0, entry.Limit),
					Items = entry.Items?
						.Where(static id => !string.IsNullOrWhiteSpace(id))
						.Distinct(StringComparer.Ordinal)
						.ToArray() ?? [],
				};
				_categories.Add(def);
				foreach (var itemId in def.Items)
					_categoryByItem[itemId] = def;
			}
		}

		if (schema?.FallbackKit != null)
			foreach (var (k, v) in schema.FallbackKit)
				if (!string.IsNullOrEmpty(k) && v > 0)
					_fallbackKit[k] = v;

		if (_categories.Count == 0)
			SeedBuiltinDefaults();
	}

	private static void SeedBuiltinDefaults()
	{
		// JSON 缺失 / 全损时的兜底：和原硬编码 starter kit 等价的 9 件包 + 5 个分类。
		AddBuiltinCategory("food", 4, "meal_simple", "meal_lavish", "bread");
		AddBuiltinCategory("drink", 3, "water_flask", "water_bottle_clean");
		AddBuiltinCategory("rest", 1, "bedroll");
		AddBuiltinCategory("ingredient", 4, "raw_meat", "berries");
		AddBuiltinCategory("tool", 2, "torch", "rope");

		_fallbackKit["meal_simple"] = 2;
		_fallbackKit["water_flask"] = 2;
		_fallbackKit["bedroll"] = 1;
		_fallbackKit["raw_meat"] = 2;
		_fallbackKit["berries"] = 2;
	}

	private static void AddBuiltinCategory(string id, int limit, params string[] items)
	{
		var def = new StarterKitCategoryDef { Id = id, Limit = limit, Items = items };
		_categories.Add(def);
		foreach (var itemId in items)
			_categoryByItem[itemId] = def;
	}

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	private sealed class StarterKitFile
	{
		public List<StarterKitCategoryEntry>? Categories { get; set; }
		[JsonPropertyName("fallbackKit")]
		public Dictionary<string, int>? FallbackKit { get; set; }
	}

	private sealed class StarterKitCategoryEntry
	{
		public string Id { get; set; } = "";
		public int Limit { get; set; }
		public List<string>? Items { get; set; }
	}
}

/// <summary>白名单分类的运行时定义。一个分类有自己的"item id 集合"和"该类总数上限"。</summary>
public sealed class StarterKitCategoryDef
{
	public string Id { get; init; } = "";
	public int Limit { get; init; }
	public IReadOnlyList<string> Items { get; init; } = [];
}
