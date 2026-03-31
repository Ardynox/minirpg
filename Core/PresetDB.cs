using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace MiniRPG.Core;

/// <summary>
/// JSON 预设数据的反序列化类型。
/// 运行时类（Race, Limb 等）直接复用，这里只定义 JSON 独有的结构。
/// </summary>
public class RacePreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("tags")]
	public Dictionary<string, int> Tags { get; set; } = new();
	[JsonPropertyName("defaultLimbs")]
	public List<string> DefaultLimbs { get; set; } = [];
}

public class LimbPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("maxDurability")]
	public int MaxDurability { get; set; } = 5;
	[JsonPropertyName("capacities")]
	public Dictionary<string, float> Capacities { get; set; } = new();
	[JsonPropertyName("tags")]
	public Dictionary<string, int> Tags { get; set; } = new();
}

public class ProfessionPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("tags")]
	public Dictionary<string, int> Tags { get; set; } = new();
}

public class ItemPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("price")]
	public int Price { get; set; }
	[JsonPropertyName("tags")]
	public Dictionary<string, int> Tags { get; set; } = new();
}

public class ShopSlotPreset
{
	[JsonPropertyName("itemId")]
	public string ItemId { get; set; } = "";
	[JsonPropertyName("stock")]
	public int Stock { get; set; } = 1;
}

public class ActorPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("glyph")]
	public string Glyph { get; set; } = "?";
	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = "";
	[JsonPropertyName("faction")]
	public string Faction { get; set; } = "hostile";
	[JsonPropertyName("gold")]
	public int Gold { get; set; }
	[JsonPropertyName("raceId")]
	public string RaceId { get; set; } = "";
	[JsonPropertyName("professionId")]
	public string? ProfessionId { get; set; }
	[JsonPropertyName("shopSlots")]
	public List<ShopSlotPreset> ShopSlots { get; set; } = [];
}

public class ActionPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("required")]
	public Dictionary<string, int> Required { get; set; } = new();
	[JsonPropertyName("capacityRequired")]
	public Dictionary<string, float> CapacityRequired { get; set; } = new();
	[JsonPropertyName("effectType")]
	public string EffectType { get; set; } = "";
	[JsonPropertyName("power")]
	public int Power { get; set; }
}

public class InteractionPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("required")]
	public Dictionary<string, int> Required { get; set; } = new();
	[JsonPropertyName("capacityRequired")]
	public Dictionary<string, float> CapacityRequired { get; set; } = new();
	[JsonPropertyName("targetRequired")]
	public Dictionary<string, int> TargetRequired { get; set; } = new();
	[JsonPropertyName("effectType")]
	public string EffectType { get; set; } = "";
	[JsonPropertyName("power")]
	public int Power { get; set; }
}

/// <summary>
/// 预设数据库：从 res://Data/ 加载所有 JSON 预设，提供类型安全的查询和实例化 API。
/// </summary>
public static class PresetDB
{
	public static Dictionary<string, RacePreset> Races { get; private set; } = new();
	public static Dictionary<string, LimbPreset> Limbs { get; private set; } = new();
	public static Dictionary<string, ProfessionPreset> Professions { get; private set; } = new();
	public static Dictionary<string, ItemPreset> Items { get; private set; } = new();
	public static Dictionary<string, ActorPreset> Actors { get; private set; } = new();
	public static Dictionary<string, CapacityDef> Capacities { get; private set; } = new();
	public static List<ActionDef> Actions { get; private set; } = [];
	public static List<InteractionDef> Interactions { get; private set; } = [];

	/// <summary>hostile faction 的 actor 模板 ID 列表，用于怪物刷新。</summary>
	public static IReadOnlyCollection<string> MonsterIds { get; private set; } = [];

	private static bool _loaded;

	public static void Load()
	{
		if (_loaded) return;
		_loaded = true;

		Races = LoadDict<RacePreset>("res://Data/races.json");
		Limbs = LoadDict<LimbPreset>("res://Data/limbs.json");
		Professions = LoadDict<ProfessionPreset>("res://Data/professions.json");
		Items = LoadDict<ItemPreset>("res://Data/items.json");
		Actors = LoadDict<ActorPreset>("res://Data/actors.json");
		Capacities = LoadDict<CapacityDef>("res://Data/capacities.json");
		Actions = LoadList<ActionPreset>("res://Data/actions.json")
			.Select(a => new ActionDef
			{
				Id = a.Id, Name = a.Name,
				Required = new(a.Required),
				CapacityRequired = new(a.CapacityRequired),
				EffectType = a.EffectType, Power = a.Power,
			}).ToList();
		Interactions = LoadList<InteractionPreset>("res://Data/interactions.json")
			.Select(i => new InteractionDef
			{
				Id = i.Id, Name = i.Name,
				Required = new(i.Required),
				CapacityRequired = new(i.CapacityRequired),
				TargetRequired = new(i.TargetRequired),
				EffectType = i.EffectType, Power = i.Power,
			}).ToList();

		MonsterIds = Actors.Values
			.Where(a => a.Faction == "hostile")
			.Select(a => a.Id)
			.ToArray();
	}

	/// <summary>
	/// 根据 actor 预设 ID 组装完整 Actor 实例。
	/// 查种族 -> 克隆默认肢体 -> 查职业 -> 组装商店货架。
	/// </summary>
	public static Actor SpawnActor(string templateId, string instanceId)
	{
		if (!Actors.TryGetValue(templateId, out var preset))
			throw new ArgumentException($"Unknown actor template: {templateId}");

		var actor = new Actor
		{
			Id = instanceId,
			Glyph = preset.Glyph,
			DisplayName = preset.DisplayName,
			Faction = preset.Faction,
			Gold = preset.Gold,
		};

		if (Races.TryGetValue(preset.RaceId, out var race))
		{
			actor.Race = new Race
			{
				Id = race.Id,
				Name = race.Name,
				Tags = new(race.Tags),
			};
			foreach (var limbId in race.DefaultLimbs)
				actor.Limbs.Add(CloneLimb(limbId));
		}

		if (preset.ProfessionId is { } profId && Professions.TryGetValue(profId, out var prof))
		{
			actor.Profession = new Profession
			{
				Id = prof.Id,
				Name = prof.Name,
				Tags = new(prof.Tags),
			};
		}

		foreach (var slot in preset.ShopSlots)
		{
			actor.ShopSlots.Add(new ShopSlot
			{
				Stock = slot.Stock,
				Item = CloneItem(slot.ItemId),
			});
		}

		if (preset.Faction != "player")
			actor.BrainId = "simple";

		return actor;
	}

	/// <summary>根据肢体预设 ID 克隆一个新的 Limb 实例。</summary>
	public static Limb CloneLimb(string limbId)
	{
		if (!Limbs.TryGetValue(limbId, out var preset))
			throw new ArgumentException($"Unknown limb preset: {limbId}");
		return new Limb
		{
			Id = preset.Id,
			Name = preset.Name,
			MaxDurability = preset.MaxDurability,
			Durability = preset.MaxDurability,
			Capacities = new(preset.Capacities),
			Tags = new(preset.Tags),
		};
	}

	/// <summary>按 ID 查询能力定义，不存在返回 null。</summary>
	public static CapacityDef? GetCapacity(string capId) =>
		Capacities.GetValueOrDefault(capId);

	/// <summary>根据物品预设 ID 克隆一个新的 Item 实例。</summary>
	public static Item CloneItem(string itemId)
	{
		if (!Items.TryGetValue(itemId, out var preset))
			throw new ArgumentException($"Unknown item preset: {itemId}");
		return new Item
		{
			Id = preset.Id,
			Name = preset.Name,
			Price = preset.Price,
			Tags = new(preset.Tags),
		};
	}

	// ── 内部工具 ─────────────────────────────────────────

	private interface IHasId { string Id { get; } }

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static Dictionary<string, T> LoadDict<T>(string resPath) where T : class
	{
		var list = LoadList<T>(resPath);
		var dict = new Dictionary<string, T>();
		foreach (var item in list)
		{
			var id = GetId(item);
			dict[id] = item;
		}
		return dict;
	}

	private static List<T> LoadList<T>(string resPath)
	{
		using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
		if (file == null)
			throw new InvalidOperationException(
				$"Failed to open preset file: {resPath} (error: {Godot.FileAccess.GetOpenError()})");
		var json = file.GetAsText();
		return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];
	}

	private static string GetId<T>(T item)
	{
		var prop = typeof(T).GetProperty("Id");
		return prop?.GetValue(item) as string ?? "";
	}
}
