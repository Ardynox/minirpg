using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Map;

internal static class ItemSnapshotMapper
{
	private const string LegacyContentsMetaKey = "contents";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() },
	};

	public static ItemSnapshot BuildSnapshot(Item item)
	{
		item.EnsureRuntimeState();
		return new ItemSnapshot
		{
			Id = item.Id,
			InstanceId = item.InstanceId,
			Name = item.Name,
			MaterialId = item.MaterialId,
			Price = item.Price,
			Equipped = item.Equipped,
			Category = item.Category,
			OwnerDomainId = item.OwnerDomainId,
			SubCategory = item.SubCategory,
			TechTier = item.TechTier,
			Weight = item.Weight,
			MaxStack = item.MaxStack,
			StackCount = item.StackCount,
			AmmoType = item.AmmoType,
			MagazineSize = item.MagazineSize,
			LoadedAmmo = item.LoadedAmmo,
			MaxDurability = item.MaxDurability,
			Durability = item.Durability,
			BodyPart = item.BodyPart,
			Layer = item.Layer,
			CoveredParts = [.. item.CoveredParts],
			ColdInsulation = item.ColdInsulation,
			HeatInsulation = item.HeatInsulation,
			Waterproofing = item.Waterproofing,
			PortableWarmthC = item.PortableWarmthC,
			PortableDryingBonus = item.PortableDryingBonus,
			SharpArmor = item.SharpArmor,
			BluntArmor = item.BluntArmor,
			SharpDamage = item.SharpDamage,
			BluntDamage = item.BluntDamage,
			GrantedSkills = [.. item.GrantedSkills],
			Contents = item.Contents != null ? MapList(item.Contents, BuildSnapshot) : null,
			Surgery = item.Surgery == null
				? null
				: new ItemSurgerySnapshot
				{
					OperationId = item.Surgery.OperationId,
					ReplacementLimbPresetId = item.Surgery.ReplacementLimbPresetId,
					HarvestedLimbPresetId = item.Surgery.HarvestedLimbPresetId,
					Role = item.Surgery.Role,
				},
			Corpse = item.Corpse == null
				? null
				: new ItemCorpseSnapshot
				{
					CorpseProfileId = item.Corpse.CorpseProfileId,
					SourceActorTemplateId = item.Corpse.SourceActorTemplateId,
					SourceRaceId = item.Corpse.SourceRaceId,
					SourceActorName = item.Corpse.SourceActorName,
					Stripped = item.Corpse.Stripped,
					Butchered = item.Corpse.Butchered,
					RemainingLimbIds = [.. item.Corpse.RemainingLimbIds],
				},
			Tags = CopyDictionary(item.Tags),
		};
	}

	public static Item CreateItem(ItemSnapshot snapshot)
	{
		var maxDurability = snapshot.MaxDurability
			?? PresetDB.ResolveItemMaxDurability(snapshot.Id, snapshot.MaterialId, snapshot.Category, snapshot.Weight);
		var durability = snapshot.Durability ?? maxDurability;
		var item = new Item
		{
			Id = snapshot.Id,
			Name = snapshot.Name,
			MaterialId = ResolveItemMaterialId(snapshot.MaterialId, snapshot.Id),
			Price = snapshot.Price,
			Equipped = snapshot.Equipped,
			Category = snapshot.Category,
			OwnerDomainId = snapshot.OwnerDomainId ?? string.Empty,
			SubCategory = snapshot.SubCategory ?? string.Empty,
			TechTier = snapshot.TechTier ?? string.Empty,
			Weight = snapshot.Weight,
			MaxStack = Math.Max(1, snapshot.MaxStack ?? 1),
			StackCount = Math.Max(1, snapshot.StackCount ?? 1),
			AmmoType = snapshot.AmmoType ?? string.Empty,
			MagazineSize = Math.Max(0, snapshot.MagazineSize ?? 0),
			LoadedAmmo = Math.Max(0, snapshot.LoadedAmmo ?? 0),
			BodyPart = snapshot.BodyPart,
			Layer = snapshot.Layer,
			CoveredParts = [.. snapshot.CoveredParts],
			ColdInsulation = Item.ResolveColdInsulation(snapshot.ColdInsulation, snapshot.Tags),
			HeatInsulation = Item.ClampProtection(snapshot.HeatInsulation ?? 0f),
			Waterproofing = Item.ClampProtection(snapshot.Waterproofing ?? 0f),
			PortableWarmthC = Item.ClampPortableValue(snapshot.PortableWarmthC ?? 0f),
			PortableDryingBonus = Item.ClampPortableValue(snapshot.PortableDryingBonus ?? 0f),
			SharpArmor = snapshot.SharpArmor,
			BluntArmor = snapshot.BluntArmor,
			SharpDamage = snapshot.SharpDamage,
			BluntDamage = snapshot.BluntDamage,
			GrantedSkills = [.. snapshot.GrantedSkills],
			Contents = snapshot.Contents != null ? MapList(snapshot.Contents, CreateItem) : null,
			Surgery = snapshot.Surgery == null
				? null
				: new ItemSurgeryMetadata
				{
					OperationId = snapshot.Surgery.OperationId ?? string.Empty,
					ReplacementLimbPresetId = snapshot.Surgery.ReplacementLimbPresetId ?? string.Empty,
					HarvestedLimbPresetId = snapshot.Surgery.HarvestedLimbPresetId ?? string.Empty,
					Role = snapshot.Surgery.Role ?? string.Empty,
				},
			Corpse = snapshot.Corpse == null
				? null
				: new ItemCorpseMetadata
				{
					CorpseProfileId = snapshot.Corpse.CorpseProfileId ?? string.Empty,
					SourceActorTemplateId = snapshot.Corpse.SourceActorTemplateId ?? string.Empty,
					SourceRaceId = snapshot.Corpse.SourceRaceId ?? string.Empty,
					SourceActorName = snapshot.Corpse.SourceActorName ?? string.Empty,
					Stripped = snapshot.Corpse.Stripped,
					Butchered = snapshot.Corpse.Butchered,
					RemainingLimbIds = snapshot.Corpse.RemainingLimbIds != null
						? [.. snapshot.Corpse.RemainingLimbIds]
						: [],
				},
			Tags = CopyDictionary(snapshot.Tags),
		};
		item.InitializeRuntimeState(snapshot.InstanceId, maxDurability, durability);
		return item;
	}

	public static string Serialize(Item item) =>
		JsonSerializer.Serialize(BuildSnapshot(item), JsonOptions);

	public static Item? Deserialize(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return null;

		try
		{
			var snapshot = JsonSerializer.Deserialize<ItemSnapshot>(json, JsonOptions);
			return snapshot == null ? null : CreateItem(snapshot);
		}
		catch
		{
			return null;
		}
	}

	public static Item CreateLegacyItem(string entityId, Dictionary<string, string>? meta)
	{
		meta ??= new Dictionary<string, string>(StringComparer.Ordinal);
		var templateId = ResolveLegacyTemplateId(entityId, meta);
		var item = PresetDB.Items.ContainsKey(templateId)
			? PresetDB.CloneItem(templateId)
			: new Item
			{
				Id = templateId,
				Name = meta.GetValueOrDefault("name", templateId),
				MaterialId = meta.GetValueOrDefault("materialId", string.Empty),
				Price = int.TryParse(meta.GetValueOrDefault("price", "0"), out var price) ? price : 0,
				Category = meta.GetValueOrDefault("category", ItemCategories.Misc),
				OwnerDomainId = meta.GetValueOrDefault("ownerDomainId", string.Empty),
				Weight = float.TryParse(meta.GetValueOrDefault("weight", "0"), out var weight) ? weight : 0f,
				Tags = DeserializeItemTags(meta.GetValueOrDefault("tags", string.Empty)),
			};

		if (item.IsContainer && item.Contents == null)
			item.Contents = [];

		if (meta.TryGetValue(LegacyContentsMetaKey, out var contentsJson))
		{
			var ids = JsonSerializer.Deserialize<List<string>>(contentsJson, JsonOptions) ?? [];
			item.Contents ??= [];
			item.Contents.Clear();
			foreach (var contentId in ids)
			{
				if (PresetDB.Items.ContainsKey(contentId))
					item.Contents.Add(PresetDB.CloneItem(contentId));
			}
		}

		var maxDurability = int.TryParse(meta.GetValueOrDefault("maxDurability", string.Empty), out var parsedMaxDurability)
			? parsedMaxDurability
			: PresetDB.ResolveItemMaxDurability(item.Id, item.MaterialId, item.Category, item.Weight);
		var durability = int.TryParse(meta.GetValueOrDefault("durability", string.Empty), out var parsedDurability)
			? parsedDurability
			: maxDurability;
		var instanceId = meta.GetValueOrDefault("instanceId", entityId);
		item.InitializeRuntimeState(instanceId, maxDurability, durability);
		return item;
	}

	private static string ResolveLegacyTemplateId(string entityId, IReadOnlyDictionary<string, string> meta)
	{
		if (meta.TryGetValue("templateId", out var templateId) && !string.IsNullOrWhiteSpace(templateId))
			return templateId;

		return entityId;
	}

	private static string ResolveItemMaterialId(string? materialId, string itemId)
	{
		if (!string.IsNullOrWhiteSpace(materialId))
			return materialId;

		return PresetDB.Items.TryGetValue(itemId, out var preset) && !string.IsNullOrWhiteSpace(preset.Material)
			? preset.Material
			: string.Empty;
	}

	private static List<TTarget> MapList<TSource, TTarget>(IEnumerable<TSource> source, Func<TSource, TTarget> map)
	{
		var result = new List<TTarget>();
		foreach (var item in source)
			result.Add(map(item));
		return result;
	}

	private static Dictionary<TKey, TValue> CopyDictionary<TKey, TValue>(IDictionary<TKey, TValue> source)
		where TKey : notnull
	{
		var copy = new Dictionary<TKey, TValue>(source.Count);
		foreach (var (key, value) in source)
			copy[key] = value;
		return copy;
	}

	private static Dictionary<string, int> DeserializeItemTags(string raw)
	{
		var tags = new Dictionary<string, int>(StringComparer.Ordinal);
		if (string.IsNullOrWhiteSpace(raw))
			return tags;

		foreach (var pair in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
		{
			var kv = pair.Split('=', 2, StringSplitOptions.TrimEntries);
			if (kv.Length == 2 && int.TryParse(kv[1], out var value))
				tags[kv[0]] = value;
		}

		return tags;
	}
}
