using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

/// <summary>
/// 单件物品在网格背包里的尺寸定义 (W×H) + 是否可旋转 + 是否提供子网格 + 携带量加成。
/// 数据从 <c>Data/Config/item_sizes.json</c> 加载，按 item id 优先、按 category 兜底。
/// 缺省 1×1（杂物 / 弹药 / 食物等小件物品）。
/// </summary>
public sealed class ItemSizeDef
{
	public int Width { get; init; } = 1;
	public int Height { get; init; } = 1;
	public bool AllowRotation { get; init; } = true;

	/// <summary>该物品被装备到角色身上时，在 GridInventories 里 expose 出来的子网格。</summary>
	public ItemProvidedGrid? ProvidesGrid { get; init; }

	/// <summary>装上后给角色加的最大负重（不替换 manipulation 公式，只叠加）。</summary>
	public int CarryWeightBonus { get; init; }
}

/// <summary>
/// 装备型容器（背包 / 胸挂 / 腰带 / 枪套）装上后开放的内置子网格规格。
/// </summary>
public sealed class ItemProvidedGrid
{
	public GridContainerKind Kind { get; init; } = GridContainerKind.ContainerItem;
	public int Width { get; init; }
	public int Height { get; init; }
}

/// <summary>
/// 物品尺寸注册表。线程安全（单写多读，加载只发生一次）。
/// </summary>
public static class ItemSizeRegistry
{
	private static readonly Dictionary<string, ItemSizeDef> _byId = new(StringComparer.Ordinal);
	private static readonly Dictionary<string, ItemSizeDef> _byCategory = new(StringComparer.Ordinal);
	private static readonly ItemSizeDef Fallback = new();
	private static bool _loaded;
	private static readonly object _lock = new();

	/// <summary>玩家身上自带的"口袋"网格默认 W×H（4 格起步，最生存）。</summary>
	public const int DefaultPocketWidth = 2;
	public const int DefaultPocketHeight = 2;

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
			_byId.Clear();
			_byCategory.Clear();
			_loaded = false;
		}
	}

	/// <summary>测试夹具直接喂 W×H 表，跳过文件加载。</summary>
	public static void OverrideForTesting(
		IEnumerable<KeyValuePair<string, ItemSizeDef>> byId,
		IEnumerable<KeyValuePair<string, ItemSizeDef>>? byCategory = null)
	{
		lock (_lock)
		{
			_byId.Clear();
			_byCategory.Clear();
			foreach (var (k, v) in byId)
				_byId[k] = v;
			if (byCategory != null)
				foreach (var (k, v) in byCategory)
					_byCategory[k] = v;
			_loaded = true;
		}
	}

	public static ItemSizeDef GetDef(Item? item)
	{
		if (item == null) return Fallback;
		return GetDef(item.Id, item.Category);
	}

	public static ItemSizeDef GetDef(string? itemId, string? category)
	{
		if (!_loaded) EnsureLoaded();

		if (!string.IsNullOrEmpty(itemId) && _byId.TryGetValue(itemId, out var byId))
			return byId;
		if (!string.IsNullOrEmpty(category) && _byCategory.TryGetValue(category, out var byCat))
			return byCat;
		return Fallback;
	}

	public static (int Width, int Height) GetBaseSize(Item? item)
	{
		var def = GetDef(item);
		return (def.Width, def.Height);
	}

	public static (int Width, int Height) GetEffectiveSize(Item? item, bool rotated)
	{
		var (w, h) = GetBaseSize(item);
		return rotated ? (h, w) : (w, h);
	}

	public static ItemProvidedGrid? GetProvidedGrid(Item? item) => GetDef(item).ProvidesGrid;

	public static bool IsContainerEquipment(Item? item) => GetProvidedGrid(item) != null;

	private static void LoadFromDefaultDataFile()
	{
		_byId.Clear();
		_byCategory.Clear();

		if (!GameDataLocator.TryReadText("Config/item_sizes.json", out var json, out _))
			return;

		try
		{
			var schema = JsonSerializer.Deserialize<ItemSizeFile>(json, JsonOpts);
			if (schema == null) return;

			if (schema.Defaults != null)
			{
				foreach (var (key, def) in schema.Defaults)
				{
					if (string.IsNullOrEmpty(key) || def == null) continue;
					_byCategory[key] = ToRuntime(def);
				}
			}

			if (schema.Items != null)
			{
				foreach (var entry in schema.Items)
				{
					if (entry == null || string.IsNullOrEmpty(entry.Id)) continue;
					_byId[entry.Id] = ToRuntime(entry);
				}
			}
		}
		catch (JsonException)
		{
			// JSON 损坏时降级为 fallback 1×1，不让加载失败拉爆游戏启动。
		}
	}

	private static ItemSizeDef ToRuntime(ItemSizeEntry entry)
	{
		ItemProvidedGrid? grid = null;
		if (entry.ProvidesGrid != null)
		{
			grid = new ItemProvidedGrid
			{
				Kind = entry.ProvidesGrid.Kind,
				Width = Math.Max(1, entry.ProvidesGrid.Width),
				Height = Math.Max(1, entry.ProvidesGrid.Height),
			};
		}

		return new ItemSizeDef
		{
			Width = Math.Max(1, entry.Width),
			Height = Math.Max(1, entry.Height),
			AllowRotation = entry.AllowRotation,
			ProvidesGrid = grid,
			CarryWeightBonus = Math.Max(0, entry.CarryWeightBonus),
		};
	}

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
	};

	private sealed class ItemSizeFile
	{
		public Dictionary<string, ItemSizeEntry>? Defaults { get; set; }
		public List<ItemSizeEntry>? Items { get; set; }
	}

	private sealed class ItemSizeEntry
	{
		public string Id { get; set; } = "";
		public int Width { get; set; } = 1;
		public int Height { get; set; } = 1;
		public bool AllowRotation { get; set; } = true;
		public int CarryWeightBonus { get; set; }
		public ProvidedGridEntry? ProvidesGrid { get; set; }
	}

	private sealed class ProvidedGridEntry
	{
		public GridContainerKind Kind { get; set; } = GridContainerKind.ContainerItem;
		public int Width { get; set; } = 1;
		public int Height { get; set; } = 1;
	}
}
