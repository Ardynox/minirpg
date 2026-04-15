using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Health;

public static class HealthSystem
{
	public const float ValueMin = 0f;
	public const float ValueMax = 100f;

	private const float PainThresholdMild = 15f;
	private const float PainThresholdModerate = 40f;
	private const float PainThresholdSevere = 75f;

	private const float BloodLossThresholdMild = 15f;
	private const float BloodLossThresholdModerate = 40f;
	private const float BloodLossThresholdSevere = 75f;
	private const float BloodLossThresholdFatal = 100f;

	private const float InfectionThresholdModerate = 30f;
	private const float InfectionThresholdSevere = 70f;

	private const float TemperatureThresholdModerate = 20f;
	private const float TemperatureThresholdSevere = 50f;

	private const float OnFireThresholdModerate = 30f;

	public static void EnsureInitialized(Actor actor, int currentTurn)
	{
		HealthCatalog.Load();
		actor.HealthConditions ??= [];

		foreach (var limb in actor.Limbs)
		{
			limb.PermanentDamage = Math.Clamp(limb.PermanentDamage, 0, Math.Max(0, limb.MaxDurability));
			var maxRecoverable = Math.Max(0, limb.MaxDurability - limb.PermanentDamage);
			limb.Durability = Math.Clamp(limb.Durability, 0, Math.Max(maxRecoverable, limb.Durability));
		}

		actor.PainValue = Clamp(actor.PainValue);
		actor.BloodLossValue = Clamp(actor.BloodLossValue);
		actor.WetnessValue = Clamp(actor.WetnessValue);
		if (actor.HealthLastUpdatedTurn > currentTurn)
			actor.HealthLastUpdatedTurn = currentTurn;
	}

	public static void Sync(
		Actor actor,
		int currentTurn,
		EnvironmentExposureSnapshot exposure,
		List<GameEvent>? events = null,
		GameState? state = null)
	{
		EnsureInitialized(actor, currentTurn);
		var profile = HealthCatalog.GetProfileForActor(actor);
		var delta = Math.Max(0, currentTurn - actor.HealthLastUpdatedTurn);
		if (delta > 0)
			Simulate(actor, profile, delta, currentTurn, exposure, events, state);

		RefreshDerivedState(actor, profile, currentTurn, exposure, events, state);
		actor.HealthLastUpdatedTurn = currentTurn;
		NeedSystem.Sync(actor, currentTurn, events, state);
		actor.InvalidateCapacityCache();
	}

	public static float GetCapacityMultiplier(Actor actor, string capacityId)
	{
		EnsureInitialized(actor, actor.HealthLastUpdatedTurn);

		var multiplier = 1f;
		var pain = actor.PainValue;
		if (pain >= PainThresholdSevere)
		{
			if (capacityId == Caps.Moving || capacityId == Caps.Manipulation)
				multiplier *= 0.6f;
			if (capacityId == Caps.Consciousness)
				multiplier *= 0.65f;
		}
		else if (pain >= PainThresholdModerate)
		{
			if (capacityId == Caps.Moving || capacityId == Caps.Manipulation)
				multiplier *= 0.8f;
			if (capacityId == Caps.Consciousness)
				multiplier *= 0.85f;
		}
		else if (pain >= PainThresholdMild)
		{
			if (capacityId == Caps.Moving || capacityId == Caps.Manipulation || capacityId == Caps.Consciousness)
				multiplier *= 0.92f;
		}

		var bloodLoss = actor.BloodLossValue;
		if (bloodLoss >= BloodLossThresholdFatal)
		{
			if (capacityId == Caps.BloodCirculation || capacityId == Caps.Consciousness)
				return 0f;
		}
		else if (bloodLoss >= BloodLossThresholdSevere)
		{
			if (capacityId == Caps.BloodCirculation)
				multiplier *= 0.2f;
			if (capacityId == Caps.Consciousness)
				multiplier *= 0.35f;
			if (capacityId == Caps.Moving)
				multiplier *= 0.6f;
		}
		else if (bloodLoss >= BloodLossThresholdModerate)
		{
			if (capacityId == Caps.BloodCirculation)
				multiplier *= 0.55f;
			if (capacityId == Caps.Consciousness)
				multiplier *= 0.75f;
			if (capacityId == Caps.Moving)
				multiplier *= 0.85f;
		}
		else if (bloodLoss >= BloodLossThresholdMild)
		{
			if (capacityId == Caps.Consciousness || capacityId == Caps.Moving)
				multiplier *= 0.94f;
		}

		var infection = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Infection, StringComparison.Ordinal));
		if (infection != null)
		{
			if (infection.Severity >= InfectionThresholdSevere)
			{
				if (capacityId == Caps.Consciousness || capacityId == Caps.Metabolism)
					multiplier *= 0.65f;
			}
			else if (infection.Severity >= InfectionThresholdModerate)
			{
				if (capacityId == Caps.Consciousness || capacityId == Caps.Metabolism)
					multiplier *= 0.88f;
			}
		}

		var hypothermia = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Hypothermia, StringComparison.Ordinal));
		var heatstroke = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Heatstroke, StringComparison.Ordinal));
		var onFire = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.OnFire, StringComparison.Ordinal));
		var temperatureSeverity = Math.Max(hypothermia?.Severity ?? 0f, heatstroke?.Severity ?? 0f);
		if (temperatureSeverity >= TemperatureThresholdSevere)
		{
			if (capacityId == Caps.Moving || capacityId == Caps.Consciousness || capacityId == Caps.Manipulation)
				multiplier *= 0.75f;
		}
		else if (temperatureSeverity >= TemperatureThresholdModerate)
		{
			if (capacityId == Caps.Moving || capacityId == Caps.Consciousness)
				multiplier *= 0.9f;
		}

		if ((onFire?.Severity ?? 0f) >= OnFireThresholdModerate)
		{
			if (capacityId is Caps.Moving or Caps.Manipulation or Caps.Consciousness)
				multiplier *= 0.7f;
		}
		else if ((onFire?.Severity ?? 0f) > 0f)
		{
			if (capacityId is Caps.Moving or Caps.Manipulation)
				multiplier *= 0.85f;
		}

		return Math.Clamp(multiplier, 0f, 1f);
	}

	public static HealthConditionState AddOrUpdateInjury(
		Actor actor,
		string conditionId,
		string? limbId,
		float severity,
		string source,
		int currentTurn,
		List<GameEvent>? events = null,
		GameState? state = null)
	{
		EnsureInitialized(actor, currentTurn);
		var def = HealthCatalog.GetCondition(conditionId);
		var normalizedSeverity = Math.Max(0.1f, severity * Math.Max(0.01f, def?.SeverityScale ?? 1f));
		var existing = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, conditionId, StringComparison.Ordinal)
			&& string.Equals(condition.LimbId, limbId, StringComparison.Ordinal));

		if (existing == null)
		{
			existing = new HealthConditionState
			{
				Id = conditionId,
				LimbId = limbId,
				Severity = normalizedSeverity,
				Permanent = string.Equals(def?.Kind, HealthConditionKinds.Permanent, StringComparison.Ordinal),
				Source = source,
				CreatedOnTurn = currentTurn,
				LastUpdatedTurn = currentTurn,
			};
			actor.HealthConditions.Add(existing);
		}
		else
		{
			existing.Severity = Clamp(existing.Severity + normalizedSeverity);
			existing.LastUpdatedTurn = currentTurn;
			existing.Source = source;
		}

		if (events != null)
		{
			var injuryEvent = new GameEvent("injury_applied")
			{
				ActionName = conditionId,
				LimbName = limbId,
			};
			IdentificationModule.PopulateInitiatorIdentity(injuryEvent, state, actor);
			IdentificationModule.PopulateTargetIdentity(injuryEvent, state, actor);
			events.Add(injuryEvent);
		}
		return existing;
	}

	public static bool HasTreatableCondition(Actor actor, int currentTurn)
	{
		EnsureInitialized(actor, currentTurn);
		return GetMostSevereTreatableCondition(actor, currentTurn) != null;
	}

	public static HealthConditionState? GetMostSevereTreatableCondition(Actor actor, int currentTurn)
	{
		EnsureInitialized(actor, currentTurn);
		return actor.HealthConditions
			.Where(IsTreatable)
			.OrderByDescending(GetTreatmentPriority)
			.ThenByDescending(condition => condition.Severity)
			.FirstOrDefault();
	}

	public static float ApplyTreatment(
		Actor actor,
		HealthConditionState condition,
		float quality,
		int currentTurn,
		List<GameEvent>? events = null,
		GameState? state = null)
	{
		EnsureInitialized(actor, currentTurn);
		var normalized = Math.Clamp(quality, 0.05f, 1f);
		condition.TendedQuality = Math.Max(condition.TendedQuality, normalized);
		condition.TendedOnTurn = currentTurn;
		condition.LastUpdatedTurn = currentTurn;

		condition.Severity = Math.Max(0f, condition.Severity - 2f * normalized);
		condition.InfectionProgress = Math.Max(0f, condition.InfectionProgress - 8f * normalized);

		var thoughtId = normalized >= 0.55f ? "tended_well" : "tended_poorly";
		NeedSystem.ApplyThought(actor, thoughtId, currentTurn, HealthThoughtSources.Treatment, events, state);
		if (events != null)
		{
			var treatmentEvent = new GameEvent("treatment_applied")
			{
				ActionName = condition.Id,
			};
			IdentificationModule.PopulateInitiatorIdentity(treatmentEvent, state, actor);
			IdentificationModule.PopulateTargetIdentity(treatmentEvent, state, actor);
			events.Add(treatmentEvent);
		}
		return normalized;
	}

	public static string? GetFatalCause(Actor actor)
	{
		EnsureInitialized(actor, actor.HealthLastUpdatedTurn);
		if (actor.BloodLossValue >= 100f)
			return "death_blood_loss";

		var infection = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Infection, StringComparison.Ordinal));
		return infection != null && infection.Severity >= 100f
			? "death_infection"
			: null;
	}

	public static bool IsDeadFromHealth(Actor actor) =>
		!string.IsNullOrEmpty(GetFatalCause(actor));

	private static void Simulate(
		Actor actor,
		HealthProfileDef profile,
		int delta,
		int currentTurn,
		EnvironmentExposureSnapshot exposure,
		List<GameEvent>? events,
		GameState? state)
	{
		var maxInfectionProgress = 0f;
		var bloodGain = 0f;
		var hadBleeding = actor.BloodLossValue > 0f;

		foreach (var condition in actor.HealthConditions.ToList())
		{
			var def = HealthCatalog.GetCondition(condition.Id);
			if (def == null)
				continue;
			if (string.Equals(def.Kind, HealthConditionKinds.Permanent, StringComparison.Ordinal))
				continue;
			if (string.Equals(condition.Id, HealthConditionIds.Infection, StringComparison.Ordinal)
				|| string.Equals(condition.Id, HealthConditionIds.BloodLoss, StringComparison.Ordinal)
				|| string.Equals(condition.Id, HealthConditionIds.Hypothermia, StringComparison.Ordinal)
				|| string.Equals(condition.Id, HealthConditionIds.Heatstroke, StringComparison.Ordinal))
			{
				continue;
			}

			var previousSeverity = condition.Severity;
			var treatmentQuality = condition.TendedOnTurn >= 0 ? Math.Clamp(condition.TendedQuality, 0f, 1f) : 0f;
			var recovery = def.RecoveryPerTurn * profile.WoundHealingFactor;
			if (condition.TendedOnTurn >= 0)
				recovery += def.TendedRecoveryBonus * treatmentQuality;
			else
				recovery *= profile.UntendedHealingFactor;

			var worsening = def.WorsenPerTurn * Math.Max(0.15f, 1f - treatmentQuality);
			condition.Severity = Clamp(condition.Severity + (worsening - recovery) * delta);
			condition.LastUpdatedTurn = currentTurn;

			if (profile.AllowBleeding)
				bloodGain += def.BleedPerSeverity * previousSeverity * delta * Math.Max(0.2f, 1f - treatmentQuality * 0.7f);

			if (profile.AllowInfection && def.InfectionPerTurn > 0f)
			{
				var cleanlinessFactor = 1f + Math.Max(0f, 50f - exposure.Cleanliness) / 100f;
				var infectionGain = def.InfectionPerTurn
					* delta
					* profile.InfectionGrowthFactor
					* cleanlinessFactor
					* Math.Max(0.25f, 1f - treatmentQuality * profile.InfectionTreatmentFactor);
				condition.InfectionProgress = Clamp(condition.InfectionProgress + infectionGain);
				maxInfectionProgress = Math.Max(maxInfectionProgress, condition.InfectionProgress);
			}
			else
			{
				condition.InfectionProgress = 0f;
			}

			if (condition.Severity <= 0.05f)
			{
				TryCreateScar(actor, condition, def, previousSeverity, currentTurn, events, state);
				actor.HealthConditions.Remove(condition);
			}
		}

		if (!profile.AllowBleeding)
		{
			actor.BloodLossValue = 0f;
		}
		else
		{
			actor.BloodLossValue = Clamp(actor.BloodLossValue + bloodGain - profile.BloodRecoveryPerTurn * delta);
			if (!hadBleeding && actor.BloodLossValue > 0f)
			{
				if (events != null)
				{
					var bleedingEvent = new GameEvent("bleeding_started");
					IdentificationModule.PopulateInitiatorIdentity(bleedingEvent, state, actor);
					IdentificationModule.PopulateTargetIdentity(bleedingEvent, state, actor);
					events.Add(bleedingEvent);
				}
			}
		}

		if (!profile.AllowWetness)
		{
			actor.WetnessValue = 0f;
		}
		else
		{
			var dryingFactor = exposure.IsIndoors ? 1.5f : 1f;
			actor.WetnessValue = Clamp(actor.WetnessValue + exposure.WetnessDelta * delta - profile.WetnessDryingPerTurn * dryingFactor * delta);
		}

		UpdateDerivedSystemicConditions(actor, profile, maxInfectionProgress, currentTurn, exposure, events, state);
	}

	private static void RefreshDerivedState(
		Actor actor,
		HealthProfileDef profile,
		int currentTurn,
		EnvironmentExposureSnapshot exposure,
		List<GameEvent>? events,
		GameState? state)
	{
		actor.PainValue = profile.AllowPain
			? Clamp(actor.HealthConditions.Sum(GetPainContribution))
			: 0f;
		if (!profile.AllowBleeding)
			actor.BloodLossValue = 0f;
		if (!profile.AllowWetness)
			actor.WetnessValue = 0f;

		RefreshThought(actor, ResolvePainThought(actor.PainValue), currentTurn, HealthThoughtSources.PainStage, events, state);
		RefreshThought(actor, actor.BloodLossValue >= 8f ? "bleeding" : null, currentTurn, HealthThoughtSources.BleedingStage, events, state);

		var infection = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Infection, StringComparison.Ordinal));
		RefreshThought(actor, infection != null && infection.Severity >= 8f ? "infected" : null, currentTurn, HealthThoughtSources.InfectionStage, events, state);

		var temperatureThought = actor.HealthConditions.Any(condition => string.Equals(condition.Id, HealthConditionIds.Hypothermia, StringComparison.Ordinal))
			? "hypothermic"
			: actor.HealthConditions.Any(condition => string.Equals(condition.Id, HealthConditionIds.Heatstroke, StringComparison.Ordinal))
				? "heatstroke"
				: null;
		RefreshThought(actor, temperatureThought, currentTurn, HealthThoughtSources.TemperatureStage, events, state);

		RefreshThought(actor,
			actor.HealthConditions.Any(condition => string.Equals(condition.Id, HealthConditionIds.Scar, StringComparison.Ordinal))
				? "scarred"
				: null,
			currentTurn,
			$"{HealthThoughtSources.Permanent}:scar",
			events,
			state);
		RefreshThought(actor,
			actor.HealthConditions.Any(condition => string.Equals(condition.Id, HealthConditionIds.MissingLimb, StringComparison.Ordinal))
				? "lost_limb"
				: null,
			currentTurn,
			$"{HealthThoughtSources.Permanent}:missing",
			events,
			state);
	}

	private static void UpdateDerivedSystemicConditions(
		Actor actor,
		HealthProfileDef profile,
		float maxInfectionProgress,
		int currentTurn,
		EnvironmentExposureSnapshot exposure,
		List<GameEvent>? events,
		GameState? state)
	{
		UpsertSystemicCondition(actor, HealthConditionIds.BloodLoss, actor.BloodLossValue, currentTurn, "health:derived");

		var existingInfection = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Infection, StringComparison.Ordinal));
		if (!profile.AllowInfection || maxInfectionProgress <= 0.05f)
		{
			if (existingInfection != null)
				actor.HealthConditions.Remove(existingInfection);
		}
		else
		{
			var wasMissing = existingInfection == null;
			var infection = UpsertSystemicCondition(actor, HealthConditionIds.Infection, maxInfectionProgress, currentTurn, "health:infection");
			infection.InfectionProgress = maxInfectionProgress;
			if (wasMissing)
			{
				if (events != null)
				{
					var startedEvent = new GameEvent("infection_started");
					IdentificationModule.PopulateInitiatorIdentity(startedEvent, state, actor);
					IdentificationModule.PopulateTargetIdentity(startedEvent, state, actor);
					events.Add(startedEvent);
				}
			}
			else if (existingInfection != null && infection.Severity > existingInfection.Severity + 0.5f)
			{
				if (events != null)
				{
					var worsenedEvent = new GameEvent("infection_worsened");
					IdentificationModule.PopulateInitiatorIdentity(worsenedEvent, state, actor);
					IdentificationModule.PopulateTargetIdentity(worsenedEvent, state, actor);
					events.Add(worsenedEvent);
				}
			}
		}

		if (!profile.AllowTemperature || !exposure.HasWeatherData)
		{
			RemoveCondition(actor, HealthConditionIds.Hypothermia);
			RemoveCondition(actor, HealthConditionIds.Heatstroke);
			return;
		}

		if (exposure.AmbientTemperature <= 0f)
		{
			UpsertSystemicCondition(actor, HealthConditionIds.Hypothermia, (0f - exposure.AmbientTemperature) * 3f, currentTurn, "health:temperature");
			RemoveCondition(actor, HealthConditionIds.Heatstroke);
		}
		else if (exposure.AmbientTemperature >= 36f)
		{
			UpsertSystemicCondition(actor, HealthConditionIds.Heatstroke, (exposure.AmbientTemperature - 36f) * 3f, currentTurn, "health:temperature");
			RemoveCondition(actor, HealthConditionIds.Hypothermia);
		}
		else
		{
			RemoveCondition(actor, HealthConditionIds.Hypothermia);
			RemoveCondition(actor, HealthConditionIds.Heatstroke);
		}
	}

	private static void TryCreateScar(
		Actor actor,
		HealthConditionState healedCondition,
		HealthConditionDef def,
		float previousSeverity,
		int currentTurn,
		List<GameEvent>? events,
		GameState? state)
	{
		if (string.IsNullOrWhiteSpace(healedCondition.LimbId))
			return;

		var limb = actor.Limbs.FirstOrDefault(entry => string.Equals(entry.Id, healedCondition.LimbId, StringComparison.Ordinal));
		if (limb == null)
			return;

		var permanentDamage = Math.Clamp((int)MathF.Round(previousSeverity * def.PermanentDamageFactor), 0, Math.Max(0, limb.MaxDurability));
		if (permanentDamage <= 0)
			return;

		limb.PermanentDamage = Math.Clamp(limb.PermanentDamage + permanentDamage, 0, Math.Max(0, limb.MaxDurability));
		var maxRecoverable = Math.Max(0, limb.MaxDurability - limb.PermanentDamage);
		limb.Durability = Math.Min(limb.Durability, maxRecoverable);

		var scar = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, HealthConditionIds.Scar, StringComparison.Ordinal)
			&& string.Equals(condition.LimbId, healedCondition.LimbId, StringComparison.Ordinal));
		if (scar == null)
		{
			actor.HealthConditions.Add(new HealthConditionState
			{
				Id = HealthConditionIds.Scar,
				LimbId = healedCondition.LimbId,
				Severity = permanentDamage,
				Permanent = true,
				Source = healedCondition.Source,
				CreatedOnTurn = currentTurn,
				LastUpdatedTurn = currentTurn,
			});
		}
		else
		{
			scar.Severity = Clamp(scar.Severity + permanentDamage);
			scar.LastUpdatedTurn = currentTurn;
		}

		if (events != null)
		{
			var scarEvent = new GameEvent("scar_gained")
			{
				LimbName = healedCondition.LimbId,
			};
			IdentificationModule.PopulateInitiatorIdentity(scarEvent, state, actor);
			IdentificationModule.PopulateTargetIdentity(scarEvent, state, actor);
			events.Add(scarEvent);
		}
	}

	private static HealthConditionState UpsertSystemicCondition(
		Actor actor,
		string conditionId,
		float severity,
		int currentTurn,
		string source)
	{
		var existing = actor.HealthConditions.FirstOrDefault(condition =>
			string.Equals(condition.Id, conditionId, StringComparison.Ordinal)
			&& string.IsNullOrEmpty(condition.LimbId));
		if (existing == null)
		{
			existing = new HealthConditionState
			{
				Id = conditionId,
				Severity = Clamp(severity),
				Permanent = false,
				Source = source,
				CreatedOnTurn = currentTurn,
				LastUpdatedTurn = currentTurn,
			};
			actor.HealthConditions.Add(existing);
			return existing;
		}

		existing.Severity = Clamp(severity);
		existing.LastUpdatedTurn = currentTurn;
		existing.Source = source;
		return existing;
	}

	private static void RemoveCondition(Actor actor, string conditionId)
	{
		actor.HealthConditions.RemoveAll(condition =>
			string.Equals(condition.Id, conditionId, StringComparison.Ordinal)
			&& string.IsNullOrEmpty(condition.LimbId));
	}

	private static void RefreshThought(
		Actor actor,
		string? thoughtId,
		int currentTurn,
		string source,
		List<GameEvent>? events,
		GameState? state)
	{
		var existing = actor.Thoughts.FirstOrDefault(thought =>
			string.Equals(thought.Source, source, StringComparison.Ordinal));
		if (string.IsNullOrWhiteSpace(thoughtId))
		{
			if (existing != null)
				actor.Thoughts.Remove(existing);
			return;
		}

		if (existing != null && string.Equals(existing.Id, thoughtId, StringComparison.Ordinal))
			return;

		if (existing != null)
			actor.Thoughts.Remove(existing);
		NeedSystem.ApplyThought(actor, thoughtId, currentTurn, source, events, state);
	}

	private static string? ResolvePainThought(float painValue)
	{
		if (painValue >= 70f)
			return "extreme_pain";
		if (painValue >= 35f)
			return "major_pain";
		if (painValue >= 10f)
			return "minor_pain";
		return null;
	}

	private static float GetPainContribution(HealthConditionState condition)
	{
		var def = HealthCatalog.GetCondition(condition.Id);
		return def == null ? 0f : def.PainPerSeverity * condition.Severity;
	}

	private static bool IsTreatable(HealthConditionState condition) =>
		condition.Permanent == false
		&& condition.Id is not HealthConditionIds.BloodLoss
		&& condition.Id is not HealthConditionIds.Hypothermia
		&& condition.Id is not HealthConditionIds.Heatstroke
		&& condition.Id is not HealthConditionIds.OnFire;

	private static float GetTreatmentPriority(HealthConditionState condition)
	{
		var untreatedBonus = condition.TendedOnTurn < 0 ? 50f : Math.Max(0f, 20f - condition.TendedQuality * 20f);
		var infectionBonus = condition.InfectionProgress;
		return untreatedBonus + infectionBonus + condition.Severity;
	}

	private static float Clamp(float value) => Math.Clamp(value, ValueMin, ValueMax);
}
