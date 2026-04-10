using System;
using System.Text;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Data;

public static class IdentificationModule
{
	public static bool IsActorIdentified(GameState? state, Actor? actor)
	{
		if (actor == null)
			return false;
		if (state == null)
			return true;
		if (actor.Id == state.PlayerId)
			return true;

		return state.IdentifiedActorTypes.Contains(GetActorTypeId(actor));
	}

	public static bool IsItemIdentified(GameState? state, Item? item)
	{
		if (item == null)
			return false;
		if (state == null)
			return true;

		return state.IdentifiedItemTypes.Contains(GetItemTypeId(item));
	}

	public static bool IdentifyActor(GameState state, Actor actor) =>
		state.IdentifiedActorTypes.Add(GetActorTypeId(actor));

	public static bool IdentifyItem(GameState state, Item item) =>
		state.IdentifiedItemTypes.Add(GetItemTypeId(item));

	public static string GetActorTypeId(Actor actor) =>
		string.IsNullOrWhiteSpace(actor.TemplateId) ? actor.Id : actor.TemplateId;

	public static string GetItemTypeId(Item item) =>
		string.IsNullOrWhiteSpace(item.Id) ? item.Name : item.Id;

	public static void PopulateInitiatorIdentity(GameEvent e, GameState? state, Actor actor)
	{
		e.InitiatorId = actor.Id;
		e.InitiatorActorName = GetActorDisplayName(state, actor);
		e.InitiatorActorTypeId = GetActorTypeId(actor);
		e.InitiatorFaction = actor.Faction;
	}

	public static void PopulateTargetIdentity(GameEvent e, GameState? state, Actor actor)
	{
		e.TargetId = actor.Id;
		e.TargetActorName = GetActorDisplayName(state, actor);
		e.TargetActorTypeId = GetActorTypeId(actor);
		e.TargetFaction = actor.Faction;
	}

	public static void PopulateItemIdentity(GameEvent e, GameState? state, Item item)
	{
		e.ItemName = GetItemDisplayName(state, item);
		e.ItemTypeId = GetItemTypeId(item);
		e.ItemCategory = item.Category;
	}

	public static string GetActorDisplayName(GameState? state, Actor? actor)
	{
		if (actor == null)
			return string.Empty;

		return IsActorIdentified(state, actor)
			? actor.DisplayName
			: BuildUnknownActorName(actor);
	}

	public static string GetItemDisplayName(GameState? state, Item? item)
	{
		if (item == null)
			return string.Empty;

		return IsItemIdentified(state, item)
			? item.Name
			: BuildUnknownItemName(item);
	}

	public static string BuildUnknownItemDetail(GameState state, Item item)
	{
		if (IsItemIdentified(state, item))
			return ItemDetailFormatter.BuildFullDetail(item);

		var sb = new StringBuilder();
		sb.Append($"[color=#ffffff]{GetItemDisplayName(state, item)}[/color]");
		sb.AppendLine($"  [color=#888888][{GetUnknownItemCategoryLabel(item)}][/color]");
		if (!string.IsNullOrWhiteSpace(item.MaterialId))
		{
			var materialName = GameLocalizer.LocalizeMaterialName(item.MaterialId, item.MaterialId);
			sb.AppendLine($"[color=#8ecae6]{LocalizationService.TOrFallback("item.detail.material", "Material:")}[/color] {materialName}");
		}
		if (item.IsContainer)
			sb.AppendLine($"[color=#66ccff]{LocalizationService.T("item.detail.container")}[/color]");
		sb.AppendLine($"[color=#ffaa66]{ItemConditionFormatter.BuildDetailDurability(item)}[/color]");
		sb.Append($"[color=#888888]{LocalizationService.TOrFallback("item.unidentified.reveal_hint", "Identify this item to reveal its properties.")}[/color]");
		return sb.ToString();
	}

	public static string BuildUnknownActorDetail(Actor actor)
	{
		var sb = new StringBuilder();
		sb.AppendLine(Localize("这是一种未鉴定生物。", "This creature has not been identified."));
		sb.Append(LocalizeFaction(actor.Faction));
		return sb.ToString();
	}

	private static string BuildUnknownActorName(Actor actor) => actor.Faction switch
	{
		Factions.Hostile => Localize("未知敌对生物", "Unknown hostile"),
		Factions.Friendly => Localize("未知友方生物", "Unknown ally"),
		_ => Localize("未知生物", "Unknown creature"),
	};

	private static string BuildUnknownItemName(Item item)
	{
		if (!string.IsNullOrWhiteSpace(item.SubCategory))
			return GameLocalizer.LocalizeItemSubcategoryName(item.SubCategory, item.SubCategory);
		if (!string.IsNullOrWhiteSpace(item.Category))
			return GameLocalizer.LocalizeItemCategoryName(item.Category, item.Category);
		if (!string.IsNullOrWhiteSpace(item.MaterialId))
			return GameLocalizer.LocalizeMaterialName(item.MaterialId, item.MaterialId);

		return LocalizationService.TOrFallback("item.unidentified.fallback_name", "Unknown item");
	}

	private static string GetUnknownItemCategoryLabel(Item item)
	{
		if (!string.IsNullOrWhiteSpace(item.Category))
			return GameLocalizer.LocalizeItemCategoryName(item.Category, item.Category);

		return LocalizationService.TOrFallback("item.unidentified.generic_category", "Item");
	}

	private static string LocalizeFaction(string faction) => faction switch
	{
		Factions.Player => Localize("阵营：玩家", "Faction: Player"),
		Factions.Friendly => Localize("阵营：友方", "Faction: Friendly"),
		Factions.Hostile => Localize("阵营：敌对", "Faction: Hostile"),
		_ => $"{Localize("阵营", "Faction")}: {faction}",
	};

	private static string Localize(string zhHans, string en) =>
		string.Equals(LocalizationService.CurrentLocale, "en", StringComparison.Ordinal)
			? en
			: zhHans;

	private static class ItemDetailFormatter
	{
		public static string BuildFullDetail(Item item)
		{
			var sb = new StringBuilder();

			sb.Append($"[color=#ffffff]{item.Name}[/color]");
			var catDef = ItemCategoryDef.Get(item.Category);
			sb.AppendLine($"  [color=#888888][{catDef?.Name ?? item.Category}][/color]");
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
				var skillNames = new System.Collections.Generic.List<string>();
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
	}
}
