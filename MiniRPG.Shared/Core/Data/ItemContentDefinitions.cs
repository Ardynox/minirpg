using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.Data;

public sealed class ItemSubcategoryDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Category { get; set; } = "";
	public string Group { get; set; } = "";
}

public static class ItemSubcategoryRegistry
{
	private static readonly Dictionary<string, ItemSubcategoryDef> Registry = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, ItemSubcategoryDef> All => Registry;

	public static void Clear() => Registry.Clear();

	public static void Register(ItemSubcategoryDef def)
	{
		if (string.IsNullOrWhiteSpace(def.Id))
			return;

		Registry[def.Id] = def;
	}

	public static ItemSubcategoryDef? Get(string id) =>
		Registry.GetValueOrDefault(id);

	public static IReadOnlyList<ItemSubcategoryDef> GetByCategory(string category) =>
		Registry.Values
			.Where(def => string.Equals(def.Category, category, StringComparison.Ordinal))
			.OrderBy(def => def.Name, StringComparer.Ordinal)
			.ToArray();
}

public sealed class AmmoProfileDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string ItemId { get; set; } = "";
	public int AmmoPerShot { get; set; } = 1;
}

public static class AmmoProfileRegistry
{
	private static readonly Dictionary<string, AmmoProfileDef> Registry = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, AmmoProfileDef> All => Registry;

	public static void Clear() => Registry.Clear();

	public static void Register(AmmoProfileDef def)
	{
		if (string.IsNullOrWhiteSpace(def.Id))
			return;

		Registry[def.Id] = def;
	}

	public static AmmoProfileDef? Get(string id) =>
		Registry.GetValueOrDefault(id);
}

public sealed class ButcherYieldDef
{
	public string ItemId { get; set; } = "";
	public int Count { get; set; } = 1;
}

public sealed class CorpseProfileDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string CorpseItemId { get; set; } = "";
	public List<string> RaceIds { get; set; } = [];
	public List<string> TemplateIds { get; set; } = [];
	public List<ButcherYieldDef> ButcherYields { get; set; } = [];
}

public static class CorpseProfileRegistry
{
	private static readonly Dictionary<string, CorpseProfileDef> Registry = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, CorpseProfileDef> All => Registry;

	public static void Clear() => Registry.Clear();

	public static void Register(CorpseProfileDef def)
	{
		if (string.IsNullOrWhiteSpace(def.Id))
			return;

		Registry[def.Id] = def;
	}

	public static CorpseProfileDef? Get(string id) =>
		Registry.GetValueOrDefault(id);

	public static CorpseProfileDef? FindForActor(Actor actor)
	{
		var raceId = actor.Race?.Id ?? string.Empty;
		var templateId = actor.TemplateId ?? string.Empty;
		foreach (var profile in Registry.Values)
		{
			if (profile.TemplateIds.Any(id => string.Equals(id, templateId, StringComparison.Ordinal)))
				return profile;
			if (profile.RaceIds.Any(id => string.Equals(id, raceId, StringComparison.Ordinal)))
				return profile;
		}

		return Registry.Values.FirstOrDefault();
	}
}

public sealed class SurgeryOperationDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Mode { get; set; } = "";
	public List<string> TargetLimbSuffixes { get; set; } = [];
	public string ResultItemId { get; set; } = "";
	public string RequiredToolTag { get; set; } = ItemTags.Healing;
	public int RequiredToolLevel { get; set; } = 1;
	public string RequiredSupplyTag { get; set; } = ItemTags.Healing;
	public int RequiredSupplyLevel { get; set; } = 1;
	public int SupplyUnits { get; set; } = 1;
	public int Priority { get; set; }
}

public static class SurgeryOperationModes
{
	public const string CorpseHarvest = "corpse_harvest";
	public const string LiveHarvest = "live_harvest";
	public const string LiveInstall = "live_install";
}

public static class SurgeryOperationRegistry
{
	private static readonly Dictionary<string, SurgeryOperationDef> Registry = new(StringComparer.Ordinal);

	public static IReadOnlyDictionary<string, SurgeryOperationDef> All => Registry;

	public static void Clear() => Registry.Clear();

	public static void Register(SurgeryOperationDef def)
	{
		if (string.IsNullOrWhiteSpace(def.Id))
			return;

		Registry[def.Id] = def;
	}

	public static SurgeryOperationDef? Get(string id) =>
		Registry.GetValueOrDefault(id);

	public static IReadOnlyList<SurgeryOperationDef> FindMatching(string mode, string limbId)
	{
		if (string.IsNullOrWhiteSpace(limbId))
			return [];

		return Registry.Values
			.Where(def =>
				string.Equals(def.Mode, mode, StringComparison.Ordinal)
				&& def.TargetLimbSuffixes.Any(suffix =>
					limbId.EndsWith(suffix, StringComparison.Ordinal)))
			.OrderByDescending(def => def.Priority)
			.ThenBy(def => def.Id, StringComparer.Ordinal)
			.ToArray();
	}
}

public sealed class ItemSurgeryMetadata
{
	public string OperationId { get; set; } = "";
	public string ReplacementLimbPresetId { get; set; } = "";
	public string HarvestedLimbPresetId { get; set; } = "";
	public string Role { get; set; } = "";

	public ItemSurgeryMetadata Clone() => new()
	{
		OperationId = OperationId,
		ReplacementLimbPresetId = ReplacementLimbPresetId,
		HarvestedLimbPresetId = HarvestedLimbPresetId,
		Role = Role,
	};
}

public sealed class ItemCorpseMetadata
{
	public string CorpseProfileId { get; set; } = "";
	public string SourceActorTemplateId { get; set; } = "";
	public string SourceActorId { get; set; } = "";
	public string SourceRaceId { get; set; } = "";
	public string SourceActorName { get; set; } = "";
	public bool Stripped { get; set; }
	public bool Butchered { get; set; }
	public List<string> RemainingLimbIds { get; set; } = [];

	public ItemCorpseMetadata Clone() => new()
	{
		CorpseProfileId = CorpseProfileId,
		SourceActorTemplateId = SourceActorTemplateId,
		SourceActorId = SourceActorId,
		SourceRaceId = SourceRaceId,
		SourceActorName = SourceActorName,
		Stripped = Stripped,
		Butchered = Butchered,
		RemainingLimbIds = [.. RemainingLimbIds],
	};
}
