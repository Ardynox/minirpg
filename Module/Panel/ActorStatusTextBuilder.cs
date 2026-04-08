using System.Collections.Generic;
using System.Linq;
using System.Text;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Panel;

internal static class ActorStatusTextBuilder
{
	public static string GetTabLabel(StatusTab tab) => tab switch
	{
		StatusTab.Limb => LocalizationService.T("ui.status.tab.limb"),
		StatusTab.Capacity => LocalizationService.T("ui.status.tab.capacity"),
		StatusTab.Tag => LocalizationService.T("ui.status.tab.tag"),
		StatusTab.Buff => LocalizationService.T("ui.status.tab.buff"),
		StatusTab.Equip => LocalizationService.T("ui.status.tab.equip"),
		StatusTab.Needs => LocalizationService.T("ui.status.tab.needs"),
		StatusTab.Health => LocalizationService.TOrFallback("ui.status.tab.health", "Health"),
		_ => tab.ToString(),
	};

	public static void BuildLines(List<string> lines, StatusTab tab, Actor actor) =>
		BuildLinesCore(lines, tab, state: null, actor);

	public static void BuildLines(List<string> lines, StatusTab tab, GameState state, Actor actor) =>
		BuildLinesCore(lines, tab, state as GameState, actor);

	public static string BuildPlayerHeader(GameState state, Actor actor, int floor, int turn)
	{
		var sb = new StringBuilder();
		sb.Append(actor.DisplayName);
		if (actor.Race != null) sb.Append($"  {actor.Race.Name}");
		if (actor.Profession != null) sb.Append($" / {actor.Profession.Name}");
		sb.Append($"\n{LocalizationService.T("ui.status.header.meta", ("gold", actor.Gold), ("floor", floor), ("turn", turn))}");
		if (state.World != null)
		{
			var weather = GetDisplayWeatherSample(state, actor);
			var exposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);
			sb.Append("  ");
			sb.Append(LocalizationService.TOrFallback(
				"ui.status.header.weather",
				"Weather: {weather} ({intensity}) {temp}C",
				("weather", GameLocalizer.LocalizeWeatherName(weather.TypeId)),
				("intensity", GameLocalizer.LocalizeWeatherIntensity(weather.IntensityId)),
				("temp", exposure.AmbientTemperature.ToString("0.#"))));
		}
		return sb.ToString();
	}

	public static string BuildPlayerHeader(Actor actor, int floor, int turn)
	{
		var sb = new StringBuilder();
		sb.Append(actor.DisplayName);
		if (actor.Race != null) sb.Append($"  {actor.Race.Name}");
		if (actor.Profession != null) sb.Append($" / {actor.Profession.Name}");
		sb.Append($"\n{LocalizationService.T("ui.status.header.meta", ("gold", actor.Gold), ("floor", floor), ("turn", turn))}");
		return sb.ToString();
	}

	public static string BuildInspectHeader(Actor actor) =>
		BuildInspectHeaderCore(state: null, actor);

	public static string BuildInspectHeader(GameState state, Actor actor) =>
		BuildInspectHeaderCore(state as GameState, actor);

	public static string LocalizeFaction(string faction) => faction switch
	{
		Factions.Player => LocalizationService.T("ui.actor_inspect.faction.player"),
		Factions.Friendly => LocalizationService.T("ui.actor_inspect.faction.friendly"),
		Factions.Hostile => LocalizationService.T("ui.actor_inspect.faction.hostile"),
		_ => faction,
	};

	private static void BuildLinesCore(List<string> lines, StatusTab tab, GameState? state, Actor actor)
	{
		lines.Clear();
		if (state != null && !IdentificationModule.IsActorIdentified(state, actor))
		{
			lines.Add(IdentificationModule.BuildUnknownActorDetail(actor));
			return;
		}

		switch (tab)
		{
			case StatusTab.Limb:
				BuildLimbLines(lines, actor);
				break;
			case StatusTab.Capacity:
				BuildCapacityLines(lines, actor);
				break;
			case StatusTab.Tag:
				BuildTagLines(lines, actor);
				break;
			case StatusTab.Buff:
				BuildBuffLines(lines, actor);
				break;
			case StatusTab.Equip:
				BuildEquipLines(lines, actor, state);
				break;
			case StatusTab.Needs:
				BuildNeedsLines(lines, actor);
				break;
			case StatusTab.Health:
				BuildHealthLines(lines, state, actor);
				break;
		}
	}

	private static string BuildInspectHeaderCore(GameState? state, Actor actor)
	{
		var identified = state == null || IdentificationModule.IsActorIdentified(state, actor);
		var sb = new StringBuilder();
		sb.Append(state == null
			? actor.DisplayName
			: IdentificationModule.GetActorDisplayName(state, actor));
		if (identified && actor.Race != null) sb.Append($"  {actor.Race.Name}");
		if (identified && actor.Profession != null) sb.Append($" / {actor.Profession.Name}");
		sb.Append($"\n{LocalizationService.T("ui.actor_inspect.header.meta",
			("faction", LocalizeFaction(actor.Faction)),
			("x", actor.X),
			("y", actor.Y),
			("floor", actor.Z),
			("gold", identified ? actor.Gold : "--"))}");
		return sb.ToString();
	}

	private static void BuildLimbLines(List<string> lines, Actor actor)
	{
		if (actor.Limbs.Count == 0)
		{
			lines.Add(LocalizationService.T("ui.status.empty.limbs"));
			return;
		}

		foreach (var limb in actor.Limbs)
		{
			var ratio = limb.MaxDurability > 0
				? (float)limb.Durability / limb.MaxDurability
				: 0f;
			var vital = CombatModule.IsVitalLimb(limb) ? " *" : string.Empty;
			var capParts = new List<string>();
			foreach (var (capId, weight) in limb.Capacities)
			{
				var def = PresetDB.GetCapacity(capId);
				var name = GameLocalizer.LocalizeCapacityName(capId, def?.Name ?? capId);
				var pct = (int)(weight * ratio * 100);
				capParts.Add(LocalizationService.T("ui.status.limb.capacity_value", ("name", name), ("value", pct)));
			}

			var caps = capParts.Count > 0 ? $"  {string.Join(" ", capParts)}" : string.Empty;
			lines.Add($"{limb.Name}{vital}  {limb.Durability}/{limb.MaxDurability}{caps}");
		}
	}

	private static void BuildCapacityLines(List<string> lines, Actor actor)
	{
		var caps = actor.ComputeCapacities();
		if (caps.Count == 0)
		{
			lines.Add(LocalizationService.T("ui.common.none"));
			return;
		}

		foreach (var (capId, value) in caps)
		{
			var def = PresetDB.GetCapacity(capId);
			var name = GameLocalizer.LocalizeCapacityName(capId, def?.Name ?? capId);
			var pct = (int)(value * 100);
			var effect = def?.VitalEffect switch
			{
				"death_instant" => $" [{LocalizationService.T("ui.status.capacity.effect.death_instant")}]",
				"incapacitate" => $" [{LocalizationService.T("ui.status.capacity.effect.incapacitate")}]",
				"death_slow" => $" [{LocalizationService.T("ui.status.capacity.effect.death_slow")}]",
				_ => string.Empty,
			};
			lines.Add($"{name}: {pct}%{effect}");
		}
	}

	private static void BuildTagLines(List<string> lines, Actor actor)
	{
		var tags = actor.ComputeTags();
		if (tags.Count == 0)
		{
			lines.Add(LocalizationService.T("ui.common.none"));
			return;
		}

		foreach (var (key, value) in tags)
			lines.Add($"{GameLocalizer.LocalizeTagKey(key)}: {value}");
	}

	private static void BuildBuffLines(List<string> lines, Actor actor)
	{
		if (actor.Buffs.Count == 0)
		{
			lines.Add(LocalizationService.T("ui.common.none"));
			return;
		}

		foreach (var buff in actor.Buffs)
		{
			var turns = buff.RemainingTurns < 0
				? LocalizationService.T("ui.status.buff.permanent")
				: LocalizationService.T("ui.status.buff.turns", ("value", buff.RemainingTurns));
			var tagParts = new List<string>();
			foreach (var (key, value) in buff.Tags)
				tagParts.Add($"{GameLocalizer.LocalizeTagKey(key)}{(value >= 0 ? "+" : string.Empty)}{value}");
			var tagStr = tagParts.Count > 0 ? $" {string.Join(" ", tagParts)}" : string.Empty;
			lines.Add($"{buff.Name} ({turns}){tagStr}");
		}
	}

	private static void BuildEquipLines(List<string> lines, Actor actor, GameState? state)
	{
		var hasAny = false;
		foreach (var limb in actor.Limbs)
		{
			if (limb.EquipSlots.Count == 0)
				continue;

			var limbHasEquip = false;
			foreach (var slot in limb.EquipSlots)
			{
				if (slot.ItemId == null)
					continue;

				var item = actor.Inventory.Find(i => i.InstanceId == slot.ItemId);
				if (item == null)
					continue;

				if (!limbHasEquip)
				{
					lines.Add(LocalizationService.T("ui.status.equip.section", ("limb", limb.Name)));
					limbHasEquip = true;
				}

				hasAny = true;

				var inlineStats = state == null
					? ItemFormatHelper.InlineStats(item)
					: ItemFormatHelper.InlineStats(state, item);
				var statStr = string.IsNullOrWhiteSpace(inlineStats) ? string.Empty : $" {inlineStats}";
				var itemName = state == null
					? item.Name
					: IdentificationModule.GetItemDisplayName(state, item);
				lines.Add($"  {itemName} ({GameLocalizer.LocalizeEquipLayer(slot.Layer)}){statStr}");
			}
		}

		if (!hasAny)
		{
			lines.Add(LocalizationService.T("ui.status.empty.equipment"));
			return;
		}

		var weight = LocalizationService.T("ui.status.equip.weight", ("current", actor.CarryWeight.ToString("F1")), ("max", actor.MaxCarryWeight.ToString("F1")));
		if (actor.IsOverweight)
			weight += $" {LocalizationService.T("ui.status.equip.overweight")}";
		lines.Add(weight);
	}

	private static void BuildNeedsLines(List<string> lines, Actor actor)
	{
		NeedSystem.Sync(actor, actor.NeedsLastUpdatedTurn);
		lines.Add(BuildNeedLine(actor, NeedIds.Hunger));
		lines.Add(BuildNeedLine(actor, NeedIds.Rest));
		lines.Add(BuildMoodLine(actor));

		var thoughts = NeedSystem.GetTopThoughts(actor, actor.NeedsLastUpdatedTurn, 3);
		if (thoughts.Count == 0)
		{
			lines.Add(LocalizationService.T("ui.needs.thoughts.none"));
			return;
		}

		foreach (var thought in thoughts)
		{
			var sign = thought.MoodOffset >= 0 ? "+" : string.Empty;
			lines.Add($"{NeedCatalog.GetThoughtDisplayName(thought.Id)} ({sign}{thought.MoodOffset:0.#})");
		}
	}

	private static void BuildHealthLines(List<string> lines, GameState? state, Actor actor)
	{
		var profile = HealthCatalog.GetProfileForActor(actor);
		if (!profile.AllowPain
			&& !profile.AllowBleeding
			&& !profile.AllowInfection
			&& !profile.AllowWetness
			&& !profile.AllowTemperature
			&& actor.HealthConditions.Count == 0)
		{
			lines.Add(LocalizationService.TOrFallback("ui.status.health.none", "No active health simulation"));
			return;
		}

		var ambient = "--";
		var shelter = "--";
		var heatSource = "--";
		var drying = "--";
		if (state != null)
		{
			var exposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);
			ambient = $"{exposure.AmbientTemperature:0.#}C";
			shelter = $"{exposure.ShelterStrength * 100f:0}%";
			heatSource = $"{exposure.HeatSourceTemperatureBonus:0.#}";
			drying = $"{exposure.DryingBonus:0.#}";
		}

		lines.Add(LocalizationService.TOrFallback(
			"ui.status.health.summary.detail",
			"Pain {pain} | Blood {blood} | Wet {wet} | Ambient {ambient} | Shelter {shelter} | HeatSrc {heat_source} | Drying {drying} | ColdProt {cold} | HeatProt {heat} | RainProt {rain}",
			("pain", actor.PainValue.ToString("0")),
			("blood", actor.BloodLossValue.ToString("0")),
			("wet", actor.WetnessValue.ToString("0")),
			("ambient", ambient),
			("shelter", shelter),
			("heat_source", heatSource),
			("drying", drying),
			("cold", actor.GetColdInsulation().ToString("0.#")),
			("heat", actor.GetHeatInsulation().ToString("0.#")),
			("rain", actor.GetWaterproofing().ToString("0.#"))));
		foreach (var limb in actor.Limbs)
		{
			var maxRecoverable = System.Math.Max(0, limb.MaxDurability - limb.PermanentDamage);
			var localConditions = actor.HealthConditions
				.Where(condition => string.Equals(condition.LimbId, limb.Id, System.StringComparison.Ordinal))
				.OrderByDescending(condition => condition.Severity)
				.ThenBy(condition => condition.Id, System.StringComparer.Ordinal)
				.ToList();
			var conditionText = localConditions.Count == 0
				? LocalizationService.TOrFallback("ui.status.health.stable", "stable")
				: string.Join(", ", localConditions.Select(FormatCondition));
			var bleed = localConditions.Sum(GetBleedingContribution);
			var infection = localConditions.Count == 0 ? 0f : localConditions.Max(condition => condition.InfectionProgress);
			var tended = localConditions
				.Where(condition => condition.TendedOnTurn >= 0)
				.Select(condition => condition.TendedQuality)
				.DefaultIfEmpty(-1f)
				.Max();
			var tendText = tended < 0f
				? LocalizationService.TOrFallback("ui.status.health.untended", "untended")
				: LocalizationService.TOrFallback("ui.status.health.tended", "Tended {value}%", ("value", (tended * 100f).ToString("0")));
			var permanentLabel = LocalizationService.TOrFallback("ui.status.health.perm", "perm");
			var bleedLabel = LocalizationService.TOrFallback("ui.status.health.bleed", "bleed");
			var infectionLabel = LocalizationService.TOrFallback("ui.status.health.infection", "inf");
			lines.Add($"{limb.Name}: {limb.Durability}/{maxRecoverable} {permanentLabel} {limb.PermanentDamage} | {conditionText} | {bleedLabel} {bleed:0.#} | {infectionLabel} {infection:0.#} | {tendText}");
		}

		var systemic = actor.HealthConditions
			.Where(condition => string.IsNullOrEmpty(condition.LimbId)
				&& condition.Id is HealthConditionIds.BloodLoss
					or HealthConditionIds.Infection
					or HealthConditionIds.Hypothermia
					or HealthConditionIds.Heatstroke)
			.OrderByDescending(condition => condition.Severity)
			.ThenBy(condition => condition.Id, System.StringComparer.Ordinal)
			.ToList();
		lines.Add(systemic.Count == 0
			? $"{LocalizationService.TOrFallback("ui.status.health.system", "System")}: {LocalizationService.T("ui.common.none")}"
			: $"{LocalizationService.TOrFallback("ui.status.health.system", "System")}: {string.Join(", ", systemic.Select(condition => $"{HealthCatalog.GetConditionDisplayName(condition.Id)} {condition.Severity:0.#}"))}");

		var permanent = actor.HealthConditions
			.Where(condition => condition.Id is HealthConditionIds.MissingLimb or HealthConditionIds.Scar)
			.Where(condition => !actor.Limbs.Any(limb => string.Equals(limb.Id, condition.LimbId, System.StringComparison.Ordinal))
				|| condition.Id == HealthConditionIds.MissingLimb)
			.OrderBy(condition => condition.Id, System.StringComparer.Ordinal)
			.ToList();
		if (permanent.Count > 0)
		{
			lines.Add($"{LocalizationService.TOrFallback("ui.status.health.permanent", "Permanent")}: {string.Join(", ", permanent.Select(FormatCondition))}");
		}
	}

	private static string FormatCondition(HealthConditionState condition)
	{
		var label = HealthCatalog.GetConditionDisplayName(condition.Id);
		if (string.IsNullOrWhiteSpace(condition.LimbId))
			return $"{label} {condition.Severity:0.#}";
		return $"{label} {condition.Severity:0.#}";
	}

	private static float GetBleedingContribution(HealthConditionState condition)
	{
		var def = HealthCatalog.GetCondition(condition.Id);
		if (def == null || def.BleedPerSeverity <= 0f)
			return 0f;

		var tendedFactor = condition.TendedOnTurn >= 0
			? System.Math.Max(0.2f, 1f - condition.TendedQuality * 0.7f)
			: 1f;
		return def.BleedPerSeverity * condition.Severity * tendedFactor;
	}

	private static string BuildNeedLine(Actor actor, string needId)
	{
		if (!actor.Needs.TryGetValue(needId, out var need))
			return $"{NeedCatalog.GetNeedDisplayName(needId)}: --";

		var stageId = NeedCatalog.ResolveStageId(needId, need.Current);
		var stageLabel = string.IsNullOrWhiteSpace(stageId)
			? string.Empty
			: $" [{NeedCatalog.GetThoughtDisplayName(stageId)}]";
		return $"{NeedCatalog.GetNeedDisplayName(needId)}: {need.Current:0}{stageLabel}";
	}

	private static string BuildMoodLine(Actor actor)
	{
		var profile = NeedCatalog.GetProfileForActor(actor);
		return profile.AllowMood
			? $"{NeedCatalog.GetNeedDisplayName(NeedIds.Mood)}: {actor.MoodValue:0}"
			: $"{NeedCatalog.GetNeedDisplayName(NeedIds.Mood)}: --";
	}

	private static WeatherSample GetDisplayWeatherSample(GameState state, Actor actor)
	{
		if (state.World == null || state.Weather == null)
			return new WeatherSample(WeatherType.Clear, WeatherIntensity.Normal);

		return WeatherFieldSampler.Sample(state, actor.X, actor.Y, actor.Z == 0 ? actor.Z : 0, state.Turn, state.Weather.FrontPhase);
	}
}
