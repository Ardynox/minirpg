using System;
using System.Collections.Generic;

namespace MiniRPG.Core.Data;

/// <summary>
/// Tradeable item instance with runtime state, optional container contents,
/// stacking metadata, firearm state, and surgery/corpse payloads.
/// </summary>
public class Item : ITagSource
{
	public string Id { get; set; } = "";
	public string InstanceId { get; set; } = "";
	public string Name { get; set; } = "";
	public string MaterialId { get; set; } = "";
	public int Price { get; set; }
	public bool Equipped { get; set; }
	public int MaxDurability { get; set; }
	public int Durability { get; set; }
	public string Category { get; set; } = ItemCategories.Misc;
	public string OwnerDomainId { get; set; } = "";
	public string SubCategory { get; set; } = "";
	public string TechTier { get; set; } = "";
	public int MaxStack { get; set; } = 1;
	public int StackCount { get; set; } = 1;
	public string AmmoType { get; set; } = "";
	public int MagazineSize { get; set; }
	public int LoadedAmmo { get; set; }
	public float Weight { get; set; }
	public string BodyPart { get; set; } = "";
	public EquipLayer Layer { get; set; }
	public List<string> CoveredParts { get; set; } = [];
	public float ColdInsulation { get; set; }
	public float HeatInsulation { get; set; }
	public float Waterproofing { get; set; }
	public float PortableWarmthC { get; set; }
	public float PortableDryingBonus { get; set; }
	public float SharpArmor { get; set; }
	public float BluntArmor { get; set; }
	public float SharpDamage { get; set; }
	public float BluntDamage { get; set; }
	public List<string> GrantedSkills { get; set; } = [];
	public List<Item>? Contents { get; set; }
	public ItemSurgeryMetadata? Surgery { get; set; }
	public ItemCorpseMetadata? Corpse { get; set; }
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;
	public float ConditionPercent => MaxDurability <= 0 ? 0f : Durability * 100f / MaxDurability;
	public bool IsContainer => Contents != null;
	public bool IsCorpse => Corpse != null;
	public bool IsStackable => MaxStack > 1;
	public bool RequiresAmmo => MagazineSize > 0 && !string.IsNullOrWhiteSpace(AmmoType);
	public int SafeStackCount => Math.Max(1, StackCount);
	public int SafeLoadedAmmo => MagazineSize <= 0 ? 0 : Math.Clamp(LoadedAmmo, 0, MagazineSize);
	public int AvailableStackSpace => Math.Max(0, MaxStack - SafeStackCount);

	public float EffectiveWeight
	{
		get
		{
			var unitWeight = Weight > 0 ? Weight : ItemCategoryDef.GetDefaultWeight(Category);
			var total = unitWeight * SafeStackCount;
			if (Contents != null)
				foreach (var content in Contents)
					total += content.EffectiveWeight;
			return total;
		}
	}

	public bool IsEquippable => !string.IsNullOrEmpty(BodyPart);

	public static float ClampProtection(float value) =>
		Math.Clamp(value, 0f, 100f);

	public static float ResolveColdInsulation(float? explicitValue, IReadOnlyDictionary<string, int>? tags)
	{
		if (explicitValue.HasValue)
			return ClampProtection(explicitValue.Value);

		if (TryGetWarmthTag(tags, out var warmth))
			return ClampProtection(warmth * 10f);

		return 0f;
	}

	private static bool TryGetWarmthTag(IReadOnlyDictionary<string, int>? tags, out int warmth)
	{
		warmth = 0;
		if (tags == null)
			return false;

		foreach (var key in WarmthTagAliases)
		{
			if (tags.TryGetValue(key, out warmth))
				return true;
		}

		return false;
	}

	public static float ClampPortableValue(float value) =>
		Math.Clamp(value, 0f, 100f);

	public static string CreateInstanceId() =>
		Guid.NewGuid().ToString("N");

	public void InitializeRuntimeState(string? instanceId, int maxDurability, int? durability = null)
	{
		InstanceId = string.IsNullOrWhiteSpace(instanceId) ? CreateInstanceId() : instanceId;
		MaxDurability = maxDurability > 0
			? maxDurability
			: PresetDB.ResolveItemMaxDurability(Id, MaterialId, Category, Weight);
		Durability = Math.Clamp(durability ?? MaxDurability, 0, MaxDurability);
		MaxStack = Math.Max(1, MaxStack);
		StackCount = Math.Clamp(StackCount <= 0 ? 1 : StackCount, 1, MaxStack);
		LoadedAmmo = MagazineSize > 0
			? Math.Clamp(LoadedAmmo, 0, MagazineSize)
			: 0;

		if (Contents == null)
			return;

		foreach (var content in Contents)
			content.EnsureRuntimeState();
	}

	public void EnsureRuntimeState()
	{
		InstanceId = string.IsNullOrWhiteSpace(InstanceId) ? CreateInstanceId() : InstanceId;
		MaxDurability = MaxDurability > 0
			? MaxDurability
			: PresetDB.ResolveItemMaxDurability(Id, MaterialId, Category, Weight);
		Durability = Math.Clamp(Durability, 0, MaxDurability);
		MaxStack = Math.Max(1, MaxStack);
		StackCount = Math.Clamp(StackCount <= 0 ? 1 : StackCount, 1, MaxStack);
		LoadedAmmo = MagazineSize > 0
			? Math.Clamp(LoadedAmmo, 0, MagazineSize)
			: 0;

		if (Contents == null)
			return;

		foreach (var content in Contents)
			content.EnsureRuntimeState();
	}

	public bool ApplyDurabilityDamage(int amount)
	{
		if (amount <= 0)
			return Durability <= 0;

		Durability = Math.Max(0, Durability - amount);
		return Durability <= 0;
	}

	public bool CanStackWith(Item other)
	{
		if (other == null
			|| !IsStackable
			|| !other.IsStackable
			|| Equipped
			|| other.Equipped
			|| IsContainer
			|| other.IsContainer
			|| Corpse != null
			|| other.Corpse != null
			|| Surgery != null
			|| other.Surgery != null)
		{
			return false;
		}

		return string.Equals(Id, other.Id, StringComparison.Ordinal)
			&& string.Equals(MaterialId, other.MaterialId, StringComparison.Ordinal)
			&& string.Equals(OwnerDomainId, other.OwnerDomainId, StringComparison.Ordinal)
			&& string.Equals(SubCategory, other.SubCategory, StringComparison.Ordinal)
			&& string.Equals(TechTier, other.TechTier, StringComparison.Ordinal)
			&& string.Equals(AmmoType, other.AmmoType, StringComparison.Ordinal)
			&& MaxDurability == other.MaxDurability
			&& Durability == other.Durability
			&& MaxStack == other.MaxStack
			&& MagazineSize == other.MagazineSize
			&& LoadedAmmo == other.LoadedAmmo;
	}

	public int MergeFrom(Item other)
	{
		if (!CanStackWith(other))
			return 0;

		var moved = Math.Min(AvailableStackSpace, other.SafeStackCount);
		if (moved <= 0)
			return 0;

		StackCount += moved;
		other.StackCount -= moved;
		return moved;
	}

	private static readonly string[] WarmthTagAliases =
	[
		ItemTags.Warmth,
		ItemTags.LegacyWarmth,
		"淇濇殩",
		"保暖",
		"warmth",
	];
}

public class ShopSlot
{
	public Item Item { get; set; } = new();
	public int Stock { get; set; } = 1;
}

public class ItemCategoryDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public float DefaultWeight { get; set; } = 1.0f;

	private static readonly Dictionary<string, ItemCategoryDef> Registry = new();

	public static void Register(ItemCategoryDef def) => Registry[def.Id] = def;
	public static ItemCategoryDef? Get(string id) => Registry.GetValueOrDefault(id);
	public static float GetDefaultWeight(string category) => Registry.TryGetValue(category, out var def) ? def.DefaultWeight : 1.0f;
	public static IReadOnlyDictionary<string, ItemCategoryDef> All => Registry;

	public static void Clear() => Registry.Clear();
}
