using System;
using System.Collections.Generic;
using System.Text;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

/// <summary>
/// Item text formatter shared by inventory, chest, ground, and trade panels.
/// </summary>
public static class ItemFormatHelper
{
	public static string GetDisplayName(Item item) => item.Name;

	public static string GetDisplayName(GameState? state, Item item) =>
		state == null
			? GetDisplayName(item)
			: IdentificationModule.GetItemDisplayName(state, item);

	public static string GetInspectDisplayName(GameState? state, Item item)
	{
		if (item.IsCorpse)
			return BuildCorpseDisplayName(item);

		return GetDisplayName(state, item);
	}

	public static string BuildCorpseDisplayName(Item item)
	{
		var corpse = item.Corpse;
		var baseName = string.IsNullOrWhiteSpace(item.Name)
			? GameLocalizer.LocalizeItemName(item.Id, item.Name)
			: item.Name;
		if (corpse == null || string.IsNullOrWhiteSpace(corpse.SourceActorName))
			return baseName;

		if (baseName.Contains(corpse.SourceActorName, StringComparison.Ordinal))
			return baseName;

		return $"{corpse.SourceActorName} {baseName}".Trim();
	}

	public static string InlineStats(Item item)
	{
		var parts = new List<string>();
		if (item.IsStackable && item.SafeStackCount > 1)
			parts.Add($"x{item.SafeStackCount}");
		if (item.RequiresAmmo)
			parts.Add($"{item.SafeLoadedAmmo}/{item.MagazineSize}");
		parts.Add(ItemConditionFormatter.BuildInlineDurability(item));
		if (item.SharpDamage > 0) parts.Add($"{LocalizationService.T("enum.damage_type.short.sharp")}{item.SharpDamage:F0}");
		if (item.BluntDamage > 0) parts.Add($"{LocalizationService.T("enum.damage_type.short.blunt")}{item.BluntDamage:F0}");
		if (item.SharpArmor > 0) parts.Add($"{LocalizationService.T("item.stat.armor.short.sharp")}{item.SharpArmor:F0}");
		if (item.BluntArmor > 0) parts.Add($"{LocalizationService.T("item.stat.armor.short.blunt")}{item.BluntArmor:F0}");
		return string.Join(" ", parts);
	}

	public static string InlineStats(GameState? state, Item item) =>
		state != null && IdentificationModule.IsItemIdentified(state, item)
			? InlineStats(item)
			: string.Empty;

	public static string BuildWeight(Item item) => $"{item.EffectiveWeight:F1}kg";

	public static string BuildWeight(GameState? state, Item item) =>
		state != null && IdentificationModule.IsItemIdentified(state, item)
			? BuildWeight(item)
			: string.Empty;

	public static string BuildRowText(GameState? state, Item item, string prefix = "")
	{
		var name = GetDisplayName(state, item);
		var stats = InlineStats(state, item);
		var weight = BuildWeight(state, item);
		var statSegment = string.IsNullOrWhiteSpace(stats) ? string.Empty : $"  {stats}";
		var weightSegment = string.IsNullOrWhiteSpace(weight) ? string.Empty : $"  {weight}";
		return $"{prefix}{name}{statSegment}{weightSegment}";
	}

	public static string BuildDetail(Item item)
	{
		var sb = new StringBuilder();

		sb.Append($"[color=#ffffff]{item.Name}[/color]");
		var catDef = ItemCategoryDef.Get(item.Category);
		sb.AppendLine($"  [color=#888888][{catDef?.Name ?? item.Category}][/color]");
		if (!string.IsNullOrWhiteSpace(item.TechTier) || !string.IsNullOrWhiteSpace(item.SubCategory))
		{
			sb.AppendLine(LocalizationService.TOrFallback(
				"item.detail.tech_class",
				Localize(
					"[color=#88c0d0]科技[/color] {tech}  [color=#88c0d0]分类[/color] {subCategory}",
					"[color=#88c0d0]Tech[/color] {tech}  [color=#88c0d0]Class[/color] {subCategory}"),
				("tech", GetTechTierDisplayName(item.TechTier)),
				("subCategory", GetSubcategoryDisplayName(item.SubCategory))));
		}
		if (item.IsStackable)
		{
			sb.AppendLine(LocalizationService.TOrFallback(
				"item.detail.stack",
				Localize(
					"[color=#8fbc8f]堆叠[/color] {count}/{max}",
					"[color=#8fbc8f]Stack[/color] {count}/{max}"),
				("count", item.SafeStackCount),
				("max", item.MaxStack)));
		}
		if (item.RequiresAmmo)
		{
			sb.AppendLine(LocalizationService.TOrFallback(
				"item.detail.loaded_ammo",
				Localize(
					"[color=#d08770]弹药[/color] {loaded}/{magazine} ({ammoType})",
					"[color=#d08770]Ammo[/color] {loaded}/{magazine} ({ammoType})"),
				("loaded", item.SafeLoadedAmmo),
				("magazine", item.MagazineSize),
				("ammoType", GetAmmoTypeDisplayName(item.AmmoType))));
		}
		if (item.Surgery != null)
		{
			sb.AppendLine(LocalizationService.TOrFallback(
				"item.detail.surgery_role",
				Localize(
					"[color=#b48ead]用途[/color] {role}",
					"[color=#b48ead]Role[/color] {role}"),
				("role", GetSurgeryRoleDisplayName(item))));
		}
		if (item.Corpse != null)
		{
			var corpseProfileName = CorpseProfileRegistry.Get(item.Corpse.CorpseProfileId)?.Name
				?? GetSubcategoryDisplayName(item.SubCategory);
			sb.AppendLine(LocalizationService.TOrFallback(
				"item.detail.corpse_state",
				Localize(
					"[color=#bf616a]尸体[/color] {profile}  剥取={stripped}  肢解={butchered}  可摘取={count}",
					"[color=#bf616a]Corpse[/color] {profile}  stripped={stripped}  butchered={butchered}  harvestable={count}"),
				("profile", corpseProfileName),
				("stripped", FormatBool(item.Corpse.Stripped)),
				("butchered", FormatBool(item.Corpse.Butchered)),
				("count", item.Corpse.RemainingLimbIds.Count)));
		}
		if (item.Tags.ContainsKey(ItemTags.Healing))
		{
			sb.AppendLine(LocalizationService.TOrFallback(
				"item.detail.medical_supply",
				Localize(
					"[color=#8ecae6]医疗耗材[/color] 品质 {value}",
					"[color=#8ecae6]Medical Supply[/color] quality {value}"),
				("value", item.Tags.GetValueOrDefault(ItemTags.Healing, 0))));
		}

		sb.AppendLine($"[color=#ffaa66]{ItemConditionFormatter.BuildDetailDurability(item)}[/color]");

		if (item.SharpDamage > 0 || item.BluntDamage > 0)
		{
			sb.Append($"[color=#ff6666]{LocalizationService.T("item.detail.damage")}[/color] ");
			if (item.SharpDamage > 0) sb.Append($"{LocalizationService.T("enum.damage_type.short.sharp")}{item.SharpDamage:F0} ");
			if (item.BluntDamage > 0) sb.Append($"{LocalizationService.T("enum.damage_type.short.blunt")}{item.BluntDamage:F0} ");
			sb.AppendLine();
		}

		if (item.SharpArmor > 0 || item.BluntArmor > 0)
		{
			sb.Append($"[color=#6699ff]{LocalizationService.T("item.detail.armor")}[/color] ");
			if (item.SharpArmor > 0) sb.Append($"{LocalizationService.T("item.stat.armor.short.sharp")}{item.SharpArmor:F0} ");
			if (item.BluntArmor > 0) sb.Append($"{LocalizationService.T("item.stat.armor.short.blunt")}{item.BluntArmor:F0} ");
			sb.AppendLine();
		}

		if (item.IsEquippable)
		{
			var layerName = GameLocalizer.LocalizeEquipLayer(item.Layer);
			sb.AppendLine($"[color=#66cc99]{LocalizationService.T("item.detail.slot")}[/color] {GameLocalizer.LocalizeBodyPart(item.BodyPart)} / {layerName}");

			if (item.CoveredParts.Count > 0)
			{
				var parts = item.CoveredParts.ConvertAll(GameLocalizer.LocalizeBodyPart);
				sb.AppendLine($"[color=#66cc99]{LocalizationService.T("item.detail.coverage")}[/color] {string.Join(", ", parts)}");
			}
		}

		if (item.GrantedSkills.Count > 0)
		{
			var skillNames = new List<string>();
			foreach (var sid in item.GrantedSkills)
			{
				var def = InteractionDefs.Get(sid);
				skillNames.Add(def?.Name ?? sid);
			}
			sb.AppendLine($"[color=#cc99ff]{LocalizationService.T("item.detail.skills")}[/color] {string.Join(", ", skillNames)}");
		}

		if (item.IsContainer)
		{
			var count = item.Contents?.Count ?? 0;
			sb.AppendLine($"[color=#66ccff]{LocalizationService.T("item.detail.container")}[/color] {LocalizationService.T("item.detail.container_count", ("count", count))}");
		}

		sb.Append($"[color=#888888]{LocalizationService.T("item.detail.weight_price", ("weight", item.EffectiveWeight.ToString("F1")), ("price", item.Price))}[/color]");
		return sb.ToString();
	}

	public static string BuildDetail(GameState? state, Item item) =>
		state == null
			? BuildDetail(item)
			: IdentificationModule.IsItemIdentified(state, item)
				? BuildDetail(item)
				: IdentificationModule.BuildUnknownItemDetail(state, item);

	private static string Localize(string zhHans, string en) =>
		string.Equals(LocalizationService.CurrentLocale, "en", StringComparison.Ordinal)
			? en
			: zhHans;

	private static string GetTechTierDisplayName(string techTier) => techTier switch
	{
		"natural" => Localize("天然", "Natural"),
		"medieval" => Localize("中世纪", "Medieval"),
		"industrial" => Localize("工业", "Industrial"),
		"spacer" => Localize("太空", "Spacer"),
		"archotech" => Localize("远古超科技", "Archotech"),
		_ when string.IsNullOrWhiteSpace(techTier) => "-",
		_ => techTier,
	};

	private static string GetSubcategoryDisplayName(string subCategoryId)
	{
		if (string.IsNullOrWhiteSpace(subCategoryId))
			return "-";

		return ItemSubcategoryRegistry.Get(subCategoryId)?.Name ?? subCategoryId;
	}

	private static string GetAmmoTypeDisplayName(string ammoTypeId)
	{
		if (string.IsNullOrWhiteSpace(ammoTypeId))
			return "-";

		return AmmoProfileRegistry.Get(ammoTypeId)?.Name ?? ammoTypeId;
	}

	private static string GetSurgeryRoleDisplayName(Item item)
	{
		if (item.Surgery == null)
			return "-";

		if (!string.IsNullOrWhiteSpace(item.Surgery.Role))
			return item.Surgery.Role;

		if (!string.IsNullOrWhiteSpace(item.Surgery.OperationId))
			return SurgeryOperationRegistry.Get(item.Surgery.OperationId)?.Name ?? item.Surgery.OperationId;

		return "-";
	}

	private static string FormatBool(bool value) =>
		value
			? Localize("是", "Yes")
			: Localize("否", "No");
}
