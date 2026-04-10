using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

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
	[JsonPropertyName("needProfileId")]
	public string NeedProfileId { get; set; } = "";
	[JsonPropertyName("healthProfileId")]
	public string HealthProfileId { get; set; } = "";
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
	[JsonPropertyName("material")]
	public string Material { get; set; } = "flesh";
	[JsonPropertyName("bodyPart")]
	public string BodyPart { get; set; } = "";
	[JsonPropertyName("equipLayers")]
	public List<EquipLayer> EquipLayers { get; set; } = [];
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
	[JsonPropertyName("category")]
	public string Category { get; set; } = ItemCategories.Misc;
	[JsonPropertyName("subCategory")]
	public string SubCategory { get; set; } = "";
	[JsonPropertyName("techTier")]
	public string TechTier { get; set; } = "";
	[JsonPropertyName("price")]
	public int Price { get; set; }
	[JsonPropertyName("material")]
	public string Material { get; set; } = "";
	[JsonPropertyName("weight")]
	public float Weight { get; set; }
	[JsonPropertyName("maxStack")]
	public int MaxStack { get; set; } = 1;
	[JsonPropertyName("stackCount")]
	public int StackCount { get; set; } = 1;
	[JsonPropertyName("ammoType")]
	public string AmmoType { get; set; } = "";
	[JsonPropertyName("magazineSize")]
	public int MagazineSize { get; set; }
	[JsonPropertyName("loadedAmmo")]
	public int LoadedAmmo { get; set; }
	[JsonPropertyName("maxDurability")]
	public int MaxDurability { get; set; }
	[JsonPropertyName("bodyPart")]
	public string BodyPart { get; set; } = "";
	[JsonPropertyName("layer")]
	public EquipLayer Layer { get; set; }
	[JsonPropertyName("coveredParts")]
	public List<string> CoveredParts { get; set; } = [];
	[JsonPropertyName("coldInsulation")]
	public float? ColdInsulation { get; set; }
	[JsonPropertyName("heatInsulation")]
	public float? HeatInsulation { get; set; }
	[JsonPropertyName("waterproofing")]
	public float? Waterproofing { get; set; }
	[JsonPropertyName("portableWarmthC")]
	public float? PortableWarmthC { get; set; }
	[JsonPropertyName("portableDryingBonus")]
	public float? PortableDryingBonus { get; set; }
	[JsonPropertyName("sharpArmor")]
	public float SharpArmor { get; set; }
	[JsonPropertyName("bluntArmor")]
	public float BluntArmor { get; set; }
	[JsonPropertyName("sharpDamage")]
	public float SharpDamage { get; set; }
	[JsonPropertyName("bluntDamage")]
	public float BluntDamage { get; set; }
	[JsonPropertyName("grantedSkills")]
	public List<string> GrantedSkills { get; set; } = [];
	[JsonPropertyName("isContainer")]
	public bool IsContainer { get; set; }
	[JsonPropertyName("surgery")]
	public ItemSurgeryMetadata? Surgery { get; set; }
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
	public string Faction { get; set; } = Factions.Hostile;
	[JsonPropertyName("gold")]
	public int Gold { get; set; }
	[JsonPropertyName("raceId")]
	public string RaceId { get; set; } = "";
	[JsonPropertyName("professionId")]
	public string? ProfessionId { get; set; }
	[JsonPropertyName("shopSlots")]
	public List<ShopSlotPreset> ShopSlots { get; set; } = [];

	[JsonPropertyName("dialogMood")]
	public float DialogMood { get; set; }
	[JsonPropertyName("dialogAffinity")]
	public float DialogAffinity { get; set; }
	[JsonPropertyName("dialogPersonality")]
	public Dictionary<string, float>? DialogPersonality { get; set; }
	[JsonPropertyName("dialogNeeds")]
	public Dictionary<string, float>? DialogNeeds { get; set; }
}

public class ItemCategoryPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("defaultWeight")]
	public float DefaultWeight { get; set; } = 1.0f;
}

public class InteractionPreset
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";
	[JsonPropertyName("name")]
	public string Name { get; set; } = "";
	[JsonPropertyName("description")]
	public string Description { get; set; } = "";
	[JsonPropertyName("category")]
	public string Category { get; set; } = "combat";
	[JsonPropertyName("required")]
	public Dictionary<string, int> Required { get; set; } = new();
	[JsonPropertyName("capacityRequired")]
	public Dictionary<string, float> CapacityRequired { get; set; } = new();
	[JsonPropertyName("targetRequired")]
	public Dictionary<string, int> TargetRequired { get; set; } = new();
	[JsonPropertyName("effectType")]
	public string EffectType { get; set; } = "";
	[JsonPropertyName("damageType")]
	public string DamageType { get; set; } = "";
	[JsonPropertyName("power")]
	public int Power { get; set; }
	[JsonPropertyName("cooldown")]
	public int Cooldown { get; set; }
	[JsonPropertyName("range")]
	public int Range { get; set; } = 1;
	[JsonPropertyName("hidden")]
	public bool Hidden { get; set; }
	[JsonPropertyName("terrainMaterial")]
	public string TerrainMaterial { get; set; } = "";
}

/// <summary>
/// 预设数据库：从 Data/ 加载所有 JSON 预设，提供类型安全的查询和实例化 API。
/// </summary>
public static class PresetDB
{
	public static Dictionary<string, RacePreset> Races { get; private set; } = new();
	public static Dictionary<string, LimbPreset> Limbs { get; private set; } = new();
	public static Dictionary<string, ProfessionPreset> Professions { get; private set; } = new();
	public static Dictionary<string, ItemPreset> Items { get; private set; } = new();
	public static Dictionary<string, ActorPreset> Actors { get; private set; } = new();
	public static Dictionary<string, CapacityDef> Capacities { get; private set; } = new();
	public static List<InteractionDef> Interactions { get; private set; } = [];
	public static IReadOnlyDictionary<string, FacilityDef> Facilities => FacilityRegistry.All;
	public static IReadOnlyDictionary<string, RecipeDef> Recipes => RecipeRegistry.All;
	public static IReadOnlyDictionary<string, RoomRoleDef> RoomRoles => RoomRoleRegistry.All;
	public static IReadOnlyDictionary<string, ItemSubcategoryDef> ItemSubcategories => ItemSubcategoryRegistry.All;
	public static IReadOnlyDictionary<string, AmmoProfileDef> AmmoProfiles => AmmoProfileRegistry.All;
	public static IReadOnlyDictionary<string, CorpseProfileDef> CorpseProfiles => CorpseProfileRegistry.All;
	public static IReadOnlyDictionary<string, SurgeryOperationDef> SurgeryOperations => SurgeryOperationRegistry.All;

	/// <summary>hostile faction 的 actor 模板 ID 列表，用于怪物刷新。</summary>
	public static IReadOnlyCollection<string> MonsterIds { get; private set; } = [];

	private static bool _loaded;

	public static void Load()
	{
		if (_loaded) return;
		var materials = LoadList<MaterialDef>("materials.json");
		foreach (var m in materials)
			MaterialRegistry.Register(m);

		var categories = LoadList<ItemCategoryPreset>("item_categories.json");
		foreach (var c in categories)
			ItemCategoryDef.Register(new ItemCategoryDef { Id = c.Id, Name = c.Name, DefaultWeight = c.DefaultWeight });
		ItemSubcategoryRegistry.Clear();
		foreach (var def in LoadOptionalList<ItemSubcategoryDef>("item_subcategories.json"))
			ItemSubcategoryRegistry.Register(def);
		AmmoProfileRegistry.Clear();
		foreach (var def in LoadOptionalList<AmmoProfileDef>("ammo_profiles.json"))
			AmmoProfileRegistry.Register(def);
		CorpseProfileRegistry.Clear();
		foreach (var def in LoadOptionalList<CorpseProfileDef>("corpse_profiles.json"))
			CorpseProfileRegistry.Register(def);
		SurgeryOperationRegistry.Clear();
		foreach (var def in LoadOptionalList<SurgeryOperationDef>("surgery_operations.json"))
			SurgeryOperationRegistry.Register(def);
		FixtureRegistry.Load();
		FacilityRegistry.Load();
		RecipeRegistry.Load();
		RoomRoleRegistry.Load();

		NeedCatalog.Load();
		Races = LoadDict<RacePreset>("races.json");
		Limbs = LoadDict<LimbPreset>("limbs.json");
		Professions = LoadDict<ProfessionPreset>("professions.json");
		Items = LoadDict<ItemPreset>("items.json");
		Actors = LoadDict<ActorPreset>("actors.json");
		Capacities = LoadDict<CapacityDef>("capacities.json");
		Interactions = LoadList<InteractionPreset>("interactions.json")
			.Select(i => new InteractionDef
			{
				Id = i.Id, Name = i.Name, Description = i.Description,
				Category = i.Category,
				Required = new(i.Required),
				CapacityRequired = new(i.CapacityRequired),
				TargetRequired = new(i.TargetRequired),
				EffectType = i.EffectType, DamageType = i.DamageType,
				Power = i.Power,
				Cooldown = i.Cooldown, Range = i.Range,
				Hidden = i.Hidden,
				TerrainMaterial = i.TerrainMaterial,
			}).ToList();

		MonsterIds = Actors.Values
			.Where(a => a.Faction == Factions.Hostile)
			.Select(a => a.Id)
			.ToArray();

		// ── 新系统注册表 ──
		Farm.CropRegistry.LoadFromJson(
			GameDataLocator.TryReadText("crops.json", out var cropsJson, out _) ? cropsJson : "[]");
		Event.StorytellerDefLoader.Load();
		Social.SocialInteractionLoader.Load();

		_loaded = true;
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
			TemplateId = templateId,
			Faction = preset.Faction,
			Gold = preset.Gold,
		};
		if (string.Equals(preset.Faction, Factions.Player, StringComparison.Ordinal))
			actor.PrimaryDomainId = DomainIds.Player;

		if (Races.TryGetValue(preset.RaceId, out var race))
		{
			actor.Race = new Race
			{
				Id = race.Id,
				Name = race.Name,
				NeedProfileId = race.NeedProfileId,
				HealthProfileId = race.HealthProfileId,
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

		if (preset.Faction != Factions.Player)
			actor.BrainId = "simple";

		actor.DialogMood = preset.DialogMood;
		actor.DialogAffinity = preset.DialogAffinity;
		if (preset.DialogPersonality != null)
			actor.DialogPersonality = new(preset.DialogPersonality);
		if (preset.DialogNeeds != null)
			actor.DialogNeeds = new(preset.DialogNeeds);
		NeedSystem.EnsureInitialized(actor, currentTurn: 0);
		HealthSystem.EnsureInitialized(actor, currentTurn: 0);

		return actor;
	}

	/// <summary>根据肢体预设 ID 克隆一个新的 Limb 实例。</summary>
	public static Limb CloneLimb(string limbId)
	{
		if (!Limbs.TryGetValue(limbId, out var preset))
			throw new ArgumentException($"Unknown limb preset: {limbId}");
		var limb = new Limb
		{
			Id = preset.Id,
			Name = preset.Name,
			MaxDurability = preset.MaxDurability,
			Durability = preset.MaxDurability,
			PermanentDamage = 0,
			Material = preset.Material,
			BodyPart = preset.BodyPart,
			EquipLayers = [.. preset.EquipLayers],
			Capacities = new(preset.Capacities),
			Tags = new(preset.Tags),
		};
		limb.InitEquipSlots();
		return limb;
	}

	/// <summary>按 ID 查询能力定义，不存在返回 null。</summary>
	public static CapacityDef? GetCapacity(string capId) =>
		Capacities.GetValueOrDefault(capId);

	public static int ResolveItemMaxDurability(string itemId, string? materialId = null, string? category = null, float weight = 0f)
	{
		if (!string.IsNullOrWhiteSpace(itemId) && Items.TryGetValue(itemId, out var preset))
			return ResolveItemMaxDurability(preset);

		var resolvedMaterialId = string.IsNullOrWhiteSpace(materialId) ? "wood" : materialId!;
		var resolvedCategory = string.IsNullOrWhiteSpace(category) ? ItemCategories.Misc : category!;
		return ResolveDerivedItemMaxDurability(resolvedMaterialId, resolvedCategory, weight);
	}

	public static int ResolveItemMaxDurability(ItemPreset preset)
	{
		if (preset.MaxDurability > 0)
			return preset.MaxDurability;

		var materialId = string.IsNullOrWhiteSpace(preset.Material) ? "wood" : preset.Material;
		return ResolveDerivedItemMaxDurability(materialId, preset.Category, preset.Weight);
	}

	/// <summary>根据物品预设 ID 克隆一个新的 Item 实例。</summary>
	public static Item CloneItem(string itemId)
	{
		if (!Items.TryGetValue(itemId, out var preset))
			throw new ArgumentException($"Unknown item preset: {itemId}");
		var item = new Item
		{
			Id = preset.Id,
			Name = preset.Name,
			MaterialId = preset.Material,
			Category = preset.Category,
			SubCategory = preset.SubCategory,
			TechTier = preset.TechTier,
			Price = preset.Price,
			Weight = preset.Weight,
			MaxStack = Math.Max(1, preset.MaxStack),
			StackCount = Math.Max(1, preset.StackCount),
			AmmoType = preset.AmmoType,
			MagazineSize = preset.MagazineSize,
			LoadedAmmo = preset.LoadedAmmo > 0 ? preset.LoadedAmmo : preset.MagazineSize,
			BodyPart = preset.BodyPart,
			Layer = preset.Layer,
			CoveredParts = [.. preset.CoveredParts],
			ColdInsulation = Item.ResolveColdInsulation(preset.ColdInsulation, preset.Tags),
			HeatInsulation = Item.ClampProtection(preset.HeatInsulation ?? 0f),
			Waterproofing = Item.ClampProtection(preset.Waterproofing ?? 0f),
			PortableWarmthC = Item.ClampPortableValue(preset.PortableWarmthC ?? 0f),
			PortableDryingBonus = Item.ClampPortableValue(preset.PortableDryingBonus ?? 0f),
			SharpArmor = preset.SharpArmor,
			BluntArmor = preset.BluntArmor,
			SharpDamage = preset.SharpDamage,
			BluntDamage = preset.BluntDamage,
			GrantedSkills = [.. preset.GrantedSkills],
			Contents = preset.IsContainer ? [] : null,
			Surgery = preset.Surgery?.Clone(),
			Tags = new(preset.Tags),
		};
		item.InitializeRuntimeState(instanceId: null, maxDurability: ResolveItemMaxDurability(preset));
		return item;
	}

	// ── 内部工具 ─────────────────────────────────────────

	private interface IHasId { string Id { get; } }

	private static readonly JsonSerializerOptions JsonOpts = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() },
	};

	private static Dictionary<string, T> LoadDict<T>(string relativeDataPath) where T : class
	{
		var list = LoadList<T>(relativeDataPath);
		var dict = new Dictionary<string, T>();
		foreach (var item in list)
		{
			var id = GetId(item);
			dict[id] = item;
		}
		return dict;
	}

	private static List<T> LoadList<T>(string relativeDataPath)
	{
		var json = GameDataLocator.ReadTextOrThrow(relativeDataPath);
		return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];
	}

	private static List<T> LoadOptionalList<T>(string relativeDataPath)
	{
		if (!GameDataLocator.TryReadText(relativeDataPath, out var json, out _))
			return [];

		return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? [];
	}

	private static int ResolveDerivedItemMaxDurability(string materialId, string category, float weight)
	{
		var hardness = Math.Max(0.5f, MaterialRegistry.Get(materialId).Hardness);
		var categoryBase = category switch
		{
			ItemCategories.Weapon => 45f,
			ItemCategories.Armor => 55f,
			ItemCategories.Clothing => 32f,
			ItemCategories.Tool => 42f,
			ItemCategories.Material => 28f,
			ItemCategories.Food => 10f,
			ItemCategories.Consumable => 12f,
			ItemCategories.Ammo => 8f,
			_ => 24f,
		};
		var derived = categoryBase + hardness * 8f + Math.Max(0f, weight) * 4f;
		return Math.Max(6, (int)MathF.Round(derived, MidpointRounding.AwayFromZero));
	}

	private static string GetId<T>(T item)
	{
		var prop = typeof(T).GetProperty("Id");
		return prop?.GetValue(item) as string ?? "";
	}
}
