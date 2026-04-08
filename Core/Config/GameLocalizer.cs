using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MiniRPG.Core.Config;

public static class GameLocalizer
{
	private static readonly Dictionary<string, string> BaseItemNames = [];
	private static readonly Dictionary<string, string> BaseActorNames = [];
	private static readonly Dictionary<string, string> BaseRaceNames = [];
	private static readonly Dictionary<string, string> BaseProfessionNames = [];
	private static readonly Dictionary<string, string> BaseLimbNames = [];
	private static readonly Dictionary<string, string> BaseMaterialNames = [];
	private static readonly Dictionary<string, string> BaseCapacityNames = [];
	private static readonly Dictionary<string, string> BaseCapacityDescriptions = [];
	private static readonly Dictionary<string, string> BaseCategoryNames = [];
	private static readonly Dictionary<string, string> BaseInteractionNames = [];
	private static readonly Dictionary<string, string> BaseInteractionDescriptions = [];
	private static bool _snapshotsCaptured;

	public static void CaptureBaseSnapshots()
	{
		if (_snapshotsCaptured)
			return;

		foreach (var (id, item) in PresetDB.Items)
			BaseItemNames[id] = item.Name;
		foreach (var (id, actor) in PresetDB.Actors)
			BaseActorNames[id] = actor.DisplayName;
		foreach (var (id, race) in PresetDB.Races)
			BaseRaceNames[id] = race.Name;
		foreach (var (id, profession) in PresetDB.Professions)
			BaseProfessionNames[id] = profession.Name;
		foreach (var (id, limb) in PresetDB.Limbs)
			BaseLimbNames[id] = limb.Name;
		foreach (var material in MaterialRegistry.All.Values)
			BaseMaterialNames[material.Id] = material.Name;
		foreach (var capacity in PresetDB.Capacities.Values)
		{
			BaseCapacityNames[capacity.Id] = capacity.Name;
			BaseCapacityDescriptions[capacity.Id] = capacity.Description;
		}
		foreach (var category in ItemCategoryDef.All.Values)
			BaseCategoryNames[category.Id] = category.Name;
		foreach (var interaction in PresetDB.Interactions)
		{
			BaseInteractionNames[interaction.Id] = interaction.Name;
			BaseInteractionDescriptions[interaction.Id] = interaction.Description;
		}

		_snapshotsCaptured = true;
	}

	public static void ApplyPresetTranslations()
	{
		CaptureBaseSnapshots();

		foreach (var (id, item) in PresetDB.Items)
			item.Name = LocalizeItemName(id, BaseItemNames.GetValueOrDefault(id, item.Name));

		foreach (var (id, actor) in PresetDB.Actors)
			actor.DisplayName = LocalizeActorName(id, BaseActorNames.GetValueOrDefault(id, actor.DisplayName));

		foreach (var (id, race) in PresetDB.Races)
			race.Name = LocalizeRaceName(id, BaseRaceNames.GetValueOrDefault(id, race.Name));

		foreach (var (id, profession) in PresetDB.Professions)
			profession.Name = LocalizeProfessionName(id, BaseProfessionNames.GetValueOrDefault(id, profession.Name));

		foreach (var (id, limb) in PresetDB.Limbs)
			limb.Name = LocalizeLimbName(id, BaseLimbNames.GetValueOrDefault(id, limb.Name));

		foreach (var material in MaterialRegistry.All.Values)
			material.Name = LocalizeMaterialName(material.Id, BaseMaterialNames.GetValueOrDefault(material.Id, material.Name));

		foreach (var capacity in PresetDB.Capacities.Values)
		{
			capacity.Name = LocalizeCapacityName(capacity.Id, BaseCapacityNames.GetValueOrDefault(capacity.Id, capacity.Name));
			capacity.Description = LocalizeCapacityDescription(capacity.Id, BaseCapacityDescriptions.GetValueOrDefault(capacity.Id, capacity.Description));
		}

		foreach (var category in ItemCategoryDef.All.Values)
			category.Name = LocalizeItemCategoryName(category.Id, BaseCategoryNames.GetValueOrDefault(category.Id, category.Name));

		foreach (var interaction in PresetDB.Interactions)
		{
			interaction.Name = LocalizeInteractionName(interaction.Id, BaseInteractionNames.GetValueOrDefault(interaction.Id, interaction.Name));
			interaction.Description = LocalizeInteractionDescription(interaction.Id, BaseInteractionDescriptions.GetValueOrDefault(interaction.Id, interaction.Description));
		}
	}

	public static void RelocalizeGameState(GameState state)
	{
		CaptureBaseSnapshots();

		foreach (var actor in state.Actors.Values)
			RelocalizeActor(state, actor);

		foreach (var quest in state.Quests)
			RelocalizeQuest(quest);
	}

	public static string LocalizeItemName(string itemId, string fallback) =>
		LocalizeDataValue($"data.item.{itemId}.name", fallback, HumanizeItemId(itemId));

	public static string LocalizeActorName(string actorId, string fallback) =>
		LocalizeDataValue($"data.actor.{actorId}.display_name", fallback, HumanizeId(actorId));

	public static string LocalizeRaceName(string raceId, string fallback) =>
		LocalizeDataValue($"data.race.{raceId}.name", fallback, HumanizeId(raceId));

	public static string LocalizeProfessionName(string professionId, string fallback) =>
		LocalizeDataValue($"data.profession.{professionId}.name", fallback, HumanizeId(professionId));

	public static string LocalizeLimbName(string limbId, string fallback) =>
		LocalizeDataValue($"data.limb.{limbId}.name", fallback, HumanizeLimbId(limbId));

	public static string LocalizeMaterialName(string materialId, string fallback) =>
		LocalizeDataValue($"data.material.{materialId}.name", fallback, HumanizeId(materialId));

	public static string LocalizeCapacityName(string capacityId, string fallback) =>
		LocalizeDataValue($"data.capacity.{capacityId}.name", fallback, HumanizeId(capacityId));

	public static string LocalizeCapacityDescription(string capacityId, string fallback) =>
		LocalizeDataValue($"data.capacity.{capacityId}.description", fallback, fallback);

	public static string LocalizeItemCategoryName(string categoryId, string fallback) =>
		LocalizeDataValue($"data.item_category.{categoryId}.name", fallback, HumanizeId(categoryId));

	public static string LocalizeInteractionName(string interactionId, string fallback) =>
		LocalizeDataValue($"data.interaction.{interactionId}.name", fallback, HumanizeId(interactionId));

	public static string LocalizeInteractionDescription(string interactionId, string fallback) =>
		LocalizeDataValue($"data.interaction.{interactionId}.description", fallback, fallback);

	public static string LocalizeQuestTitle(string questId, string fallback) =>
		LocalizeDataValue($"data.quest.{questId}.title", fallback, fallback);

	public static string LocalizeQuestDescription(string questId, string fallback) =>
		LocalizeDataValue($"data.quest.{questId}.description", fallback, fallback);

	public static string LocalizeQuestObjective(string questId, int index, string fallback) =>
		LocalizeDataValue($"data.quest.{questId}.objective.{index}", fallback, fallback);

	public static string LocalizeQuestSource(string questId, string fallback) =>
		LocalizeDataValue($"data.quest.{questId}.source", fallback, fallback);

	public static string LocalizeBodyPart(string bodyPartId) =>
		LocalizationService.TOrFallback($"body_part.{bodyPartId}", HumanizeId(bodyPartId));

	public static string LocalizeEquipLayer(EquipLayer layer) => layer switch
	{
		EquipLayer.Skin => LocalizationService.T("enum.equip_layer.skin"),
		EquipLayer.Middle => LocalizationService.T("enum.equip_layer.middle"),
		EquipLayer.Shell => LocalizationService.T("enum.equip_layer.shell"),
		EquipLayer.Overhead => LocalizationService.T("enum.equip_layer.overhead"),
		_ => layer.ToString(),
	};

	public static string LocalizeDamageType(string damageType) => damageType switch
	{
		DamageTypes.Sharp => LocalizationService.T("enum.damage_type.sharp"),
		DamageTypes.Blunt => LocalizationService.T("enum.damage_type.blunt"),
		DamageTypes.Poison => LocalizationService.T("enum.damage_type.poison"),
		DamageTypes.Lightning => LocalizationService.TOrFallback("enum.damage_type.lightning", "Lightning"),
		DamageTypes.Fire => LocalizationService.TOrFallback("enum.damage_type.fire", "Fire"),
		_ => damageType,
	};

	public static string LocalizeWeatherName(string? weatherTypeId)
	{
		var id = string.IsNullOrWhiteSpace(weatherTypeId) ? "clear" : weatherTypeId!;
		var fallback = HumanizeId(id);
		return LocalizationService.TOrFallback($"weather.type.{id}", fallback);
	}

	public static string LocalizeWeatherIntensity(string? intensityId)
	{
		var id = string.IsNullOrWhiteSpace(intensityId) ? "normal" : intensityId!;
		var fallback = HumanizeId(id);
		return LocalizationService.TOrFallback($"weather.intensity.{id}", fallback);
	}

	public static string LocalizeEffectType(string effectType) => effectType switch
	{
		"melee_attack" => LocalizationService.T("enum.effect_type.melee_attack"),
		"heavy_attack" => LocalizationService.T("enum.effect_type.heavy_attack"),
		"poison_attack" => LocalizationService.T("enum.effect_type.poison_attack"),
		"drain_attack" => LocalizationService.T("enum.effect_type.drain_attack"),
		"ranged_attack" => LocalizationService.T("enum.effect_type.ranged_attack"),
		"block" => LocalizationService.T("enum.effect_type.block"),
		"dig" => LocalizationService.T("enum.effect_type.dig"),
		"identify" => LocalizationService.T("enum.effect_type.identify"),
		"trade" => LocalizationService.T("enum.effect_type.trade"),
		"talk" => LocalizationService.T("enum.effect_type.talk"),
		"tame" => LocalizationService.T("enum.effect_type.tame"),
		"light_fire" => LocalizationService.TOrFallback("enum.effect_type.light_fire", "Light Fire"),
		"extinguish_fire" => LocalizationService.TOrFallback("enum.effect_type.extinguish_fire", "Extinguish Fire"),
		"combat" => LocalizationService.T("enum.effect_type.combat"),
		_ => effectType,
	};

	public static string LocalizeRange(int range) => range switch
	{
		0 => LocalizationService.T("enum.range.self"),
		1 => LocalizationService.T("enum.range.adjacent"),
		_ => range.ToString(),
	};

	public static string LocalizeSkillCategory(string category) => category switch
	{
		"combat" => LocalizationService.T("skill.category.combat"),
		"utility" => LocalizationService.T("skill.category.utility"),
		"social" => LocalizationService.T("skill.category.social"),
		_ => category,
	};

	public static string LocalizeQuestStatus(QuestStatus status) => status switch
	{
		QuestStatus.Active => LocalizationService.T("quest.status.active"),
		QuestStatus.Completed => LocalizationService.T("quest.status.completed"),
		QuestStatus.Failed => LocalizationService.T("quest.status.failed"),
		_ => status.ToString(),
	};

	public static string LocalizeTagKey(string key) => key switch
	{
		"治疗" => LocalizationService.T("data.tag.healing"),
		"强化" => LocalizationService.T("data.tag.boost"),
		"镇静" => LocalizationService.T("data.tag.calm"),
		"饱腹" => LocalizationService.T("data.tag.nutrition"),
		"心情" => LocalizationService.T("data.tag.mood"),
		"要害" => LocalizationService.T("data.tag.vital"),
		"格挡" => LocalizationService.T("data.tag.block"),
		"格挡中" => LocalizationService.T("data.tag.blocking"),
		"毒性" => LocalizationService.T("data.tag.poison"),
		"驯服经验" => LocalizationService.T("data.tag.taming_experience"),
		_ => key,
	};

	public static string LocalizeFixtureName(string fixtureId) => fixtureId switch
	{
		Entities.StairDown => LocalizationService.T("fixture.stair_down"),
		Entities.StairUp => LocalizationService.T("fixture.stair_up"),
		Entities.Nest => LocalizationService.T("fixture.nest"),
		Entities.House => LocalizationService.T("fixture.house"),
		Entities.Item => LocalizationService.T("fixture.item"),
		Entities.Door => LocalizationService.T("fixture.door"),
		Entities.Campfire => LocalizationService.TOrFallback("fixture.campfire", "Campfire"),
		Entities.Fire => LocalizationService.TOrFallback("fixture.fire", "Fire"),
		_ => HumanizeId(fixtureId),
	};

	public static string LocalizeTerrainName(string terrainId) =>
		LocalizeDataValue($"data.terrain.{terrainId}.name", terrainId, HumanizeId(terrainId));

	public static string HumanizeId(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
			return id;

		var parts = id.Split('_', StringSplitOptions.RemoveEmptyEntries);
		var textInfo = CultureInfo.InvariantCulture.TextInfo;
		var sb = new StringBuilder();
		for (var i = 0; i < parts.Length; i++)
		{
			if (i > 0)
				sb.Append(' ');

			sb.Append(parts[i] switch
			{
				"hp" => "HP",
				"npc" => string.Empty,
				_ => textInfo.ToTitleCase(parts[i]),
			});
		}

		return sb.ToString().Replace("  ", " ").Trim();
	}

	private static void RelocalizeActor(GameState state, Actor actor)
	{
		var templateId = FindActorTemplateId(actor);
		var nameTemplateId = actor.Id == state.PlayerId ? Factions.Player : templateId;
		if (!string.IsNullOrEmpty(nameTemplateId)
			&& !IsPlayerCustomName(state, actor, nameTemplateId))
		{
			actor.DisplayName = LocalizeActorName(nameTemplateId, BaseActorNames.GetValueOrDefault(nameTemplateId, actor.DisplayName));
		}

		if (actor.Race != null)
			actor.Race.Name = LocalizeRaceName(actor.Race.Id, BaseRaceNames.GetValueOrDefault(actor.Race.Id, actor.Race.Name));

		if (actor.Profession != null)
			actor.Profession.Name = LocalizeProfessionName(actor.Profession.Id, BaseProfessionNames.GetValueOrDefault(actor.Profession.Id, actor.Profession.Name));

		foreach (var item in actor.Inventory)
			RelocalizeItem(item);
		foreach (var slot in actor.ShopSlots)
			RelocalizeItem(slot.Item);
		foreach (var limb in actor.Limbs)
			limb.Name = LocalizeLimbName(limb.Id, BaseLimbNames.GetValueOrDefault(limb.Id, limb.Name));
		foreach (var buff in actor.Buffs)
			buff.Name = LocalizeBuffName(buff, state);
		foreach (var experience in actor.Experiences)
			experience.Name = LocalizeExperienceName(experience, state);
	}

	private static void RelocalizeItem(Item item)
	{
		item.Name = LocalizeItemName(item.Id, BaseItemNames.GetValueOrDefault(item.Id, item.Name));
		if (item.Contents == null)
			return;

		foreach (var child in item.Contents)
			RelocalizeItem(child);
	}

	private static void RelocalizeQuest(Quest quest)
	{
		quest.Title = LocalizeQuestTitle(quest.Id, quest.Title);
		quest.Description = LocalizeQuestDescription(quest.Id, quest.Description);
		quest.Source = LocalizeQuestSource(quest.Id, quest.Source);
		for (var i = 0; i < quest.Objectives.Count; i++)
			quest.Objectives[i].Text = LocalizeQuestObjective(quest.Id, i, quest.Objectives[i].Text);
	}

	private static string LocalizeBuffName(Buff buff, GameState state)
	{
		if (buff.Id.StartsWith("block_", StringComparison.Ordinal))
			return LocalizationService.T("runtime.buff.block.name");

		if (buff.Id.StartsWith("used_", StringComparison.Ordinal))
		{
			var lastUnderscore = buff.Id.LastIndexOf('_');
			if (lastUnderscore > "used_".Length)
			{
				var itemId = buff.Id["used_".Length..lastUnderscore];
				var itemName = LocalizeItemName(itemId, BaseItemNames.GetValueOrDefault(itemId, itemId));
				return LocalizationService.T("runtime.buff.used_item.name", ("item", itemName));
			}
		}

		return buff.Name;
	}

	private static string LocalizeExperienceName(Experience experience, GameState state)
	{
		if (experience.Id.StartsWith("tamed_", StringComparison.Ordinal))
		{
			var actorId = experience.Id["tamed_".Length..];
			var actorName = state.Actors.TryGetValue(actorId, out var actor)
				? actor.DisplayName
				: actorId;
			return LocalizationService.T("runtime.experience.tamed_actor.name", ("actor", actorName));
		}

		return experience.Name;
	}

	private static string LocalizeDataValue(string key, string fallback, string englishFallback)
	{
		var fallbackValue = LocalizationService.CurrentLocale == "en"
			? englishFallback
			: fallback;
		return LocalizationService.TOrFallback(key, fallbackValue);
	}

	private static string HumanizeLimbId(string limbId)
	{
		if (string.IsNullOrWhiteSpace(limbId))
			return limbId;

		var remaining = limbId;
		foreach (var raceId in PresetDB.Races.Keys)
		{
			var prefix = raceId + "_";
			if (remaining.StartsWith(prefix, StringComparison.Ordinal))
			{
				remaining = remaining[prefix.Length..];
				break;
			}
		}

		return HumanizeId(remaining);
	}

	private static string HumanizeItemId(string itemId)
	{
		if (string.IsNullOrWhiteSpace(itemId))
			return itemId;

		return itemId switch
		{
			"meal_simple" => "Simple Meal",
			"meal_fine" => "Fine Meal",
			"meal_lavish" => "Lavish Meal",
			"bow_short" => "Short Bow",
			"bow_great" => "Longbow",
			"club_wood" => "Wooden Club",
			"wood_axe" => "Wood Axe",
			"hammer_smith" => "Smith Hammer",
			"knife_butcher" => "Butcher Knife",
			_ => HumanizeId(itemId),
		};
	}

	private static string? FindActorTemplateId(Actor actor)
	{
		if (!string.IsNullOrWhiteSpace(actor.TemplateId))
			return actor.TemplateId;

		string? fallbackMatch = null;
		foreach (var (presetId, preset) in PresetDB.Actors)
		{
			if (preset.Glyph != actor.Glyph || preset.Faction != actor.Faction)
				continue;
			if (preset.RaceId != actor.Race?.Id)
				continue;
			if ((preset.ProfessionId ?? string.Empty) != (actor.Profession?.Id ?? string.Empty))
				continue;

			if (actor.DisplayName == preset.DisplayName
				|| actor.DisplayName == BaseActorNames.GetValueOrDefault(presetId))
				return presetId;

			fallbackMatch ??= presetId;
		}

		return fallbackMatch;
	}

	private static bool IsPlayerCustomName(GameState state, Actor actor, string templateId)
	{
		if (actor.Id != state.PlayerId)
			return false;

		var knownNames = new HashSet<string>(StringComparer.Ordinal);
		AddKnownPlayerName(knownNames, PresetDB.Actors.GetValueOrDefault(templateId)?.DisplayName);
		AddKnownPlayerName(knownNames, BaseActorNames.GetValueOrDefault(templateId));
		foreach (var locale in LocalizationService.SupportedLocales)
		{
			AddKnownPlayerName(knownNames,
				LocalizationService.TForLocale(locale, $"data.actor.{templateId}.display_name", string.Empty));
		}

		return !knownNames.Contains(actor.DisplayName);
	}

	private static void AddKnownPlayerName(HashSet<string> names, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
			names.Add(value);
	}
}
