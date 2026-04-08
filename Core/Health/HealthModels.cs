using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Health;

public static class HealthConditionIds
{
	public const string CutWound = "cut_wound";
	public const string BluntTrauma = "blunt_trauma";
	public const string ToxicWound = "toxic_wound";
	public const string BurnWound = "burn_wound";
	public const string Infection = "infection";
	public const string BloodLoss = "blood_loss";
	public const string Hypothermia = "hypothermia";
	public const string Heatstroke = "heatstroke";
	public const string OnFire = "on_fire";
	public const string Scar = "scar";
	public const string MissingLimb = "missing_limb";
}

public static class HealthThoughtSources
{
	public const string PainStage = "health:pain";
	public const string BleedingStage = "health:bleeding";
	public const string InfectionStage = "health:infection";
	public const string TemperatureStage = "health:temperature";
	public const string Treatment = "health:treatment";
	public const string Permanent = "health:permanent";
	public const string Sleep = "health:sleep";
}

public static class HealthConditionKinds
{
	public const string Local = "local";
	public const string Systemic = "systemic";
	public const string Permanent = "permanent";
}

public sealed class HealthConditionState
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("limbId")]
	public string? LimbId { get; set; }

	[JsonPropertyName("severity")]
	public float Severity { get; set; }

	[JsonPropertyName("permanent")]
	public bool Permanent { get; set; }

	[JsonPropertyName("source")]
	public string Source { get; set; } = "";

	[JsonPropertyName("createdOnTurn")]
	public int CreatedOnTurn { get; set; }

	[JsonPropertyName("lastUpdatedTurn")]
	public int LastUpdatedTurn { get; set; }

	[JsonPropertyName("tendedQuality")]
	public float TendedQuality { get; set; }

	[JsonPropertyName("tendedOnTurn")]
	public int TendedOnTurn { get; set; } = -1;

	[JsonPropertyName("infectionProgress")]
	public float InfectionProgress { get; set; }
}

public sealed class HealthProfileDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("allowPain")]
	public bool AllowPain { get; set; } = true;

	[JsonPropertyName("allowBleeding")]
	public bool AllowBleeding { get; set; } = true;

	[JsonPropertyName("allowInfection")]
	public bool AllowInfection { get; set; } = true;

	[JsonPropertyName("allowWetness")]
	public bool AllowWetness { get; set; } = true;

	[JsonPropertyName("allowTemperature")]
	public bool AllowTemperature { get; set; } = true;

	[JsonPropertyName("woundHealingFactor")]
	public float WoundHealingFactor { get; set; } = 1f;

	[JsonPropertyName("untendedHealingFactor")]
	public float UntendedHealingFactor { get; set; } = 0.35f;

	[JsonPropertyName("bloodRecoveryPerTurn")]
	public float BloodRecoveryPerTurn { get; set; } = 0.08f;

	[JsonPropertyName("wetnessDryingPerTurn")]
	public float WetnessDryingPerTurn { get; set; } = 2f;

	[JsonPropertyName("restDryingBonusPerTurn")]
	public float RestDryingBonusPerTurn { get; set; } = 6f;

	[JsonPropertyName("infectionGrowthFactor")]
	public float InfectionGrowthFactor { get; set; } = 1f;

	[JsonPropertyName("infectionTreatmentFactor")]
	public float InfectionTreatmentFactor { get; set; } = 0.7f;
}

public sealed class HealthConditionDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("labelKey")]
	public string LabelKey { get; set; } = "";

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = "";

	[JsonPropertyName("kind")]
	public string Kind { get; set; } = HealthConditionKinds.Local;

	[JsonPropertyName("painPerSeverity")]
	public float PainPerSeverity { get; set; }

	[JsonPropertyName("bleedPerSeverity")]
	public float BleedPerSeverity { get; set; }

	[JsonPropertyName("infectionPerTurn")]
	public float InfectionPerTurn { get; set; }

	[JsonPropertyName("recoveryPerTurn")]
	public float RecoveryPerTurn { get; set; }

	[JsonPropertyName("tendedRecoveryBonus")]
	public float TendedRecoveryBonus { get; set; }

	[JsonPropertyName("worsenPerTurn")]
	public float WorsenPerTurn { get; set; }

	[JsonPropertyName("severityScale")]
	public float SeverityScale { get; set; } = 1f;

	[JsonPropertyName("permanentDamageFactor")]
	public float PermanentDamageFactor { get; set; }
}

public sealed class HealthThoughtDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("labelKey")]
	public string LabelKey { get; set; } = "";

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = "";

	[JsonPropertyName("moodOffset")]
	public float MoodOffset { get; set; }

	[JsonPropertyName("durationTurns")]
	public int DurationTurns { get; set; } = -1;
}

public readonly record struct EnvironmentExposureSnapshot(
	bool IsIndoors,
	float Cleanliness,
	float SleepSurfaceQuality,
	float WetnessDelta,
	float AmbientTemperature,
	float ShelterStrength,
	float HeatSourceTemperatureBonus,
	float DryingBonus,
	bool HasWeatherData)
{
	public static readonly EnvironmentExposureSnapshot Neutral = new(
		IsIndoors: false,
		Cleanliness: 50f,
		SleepSurfaceQuality: 25f,
		WetnessDelta: 0f,
		AmbientTemperature: 21f,
		ShelterStrength: 0f,
		HeatSourceTemperatureBonus: 0f,
		DryingBonus: 0f,
		HasWeatherData: false);
}

public readonly record struct RoomContextSnapshot(
	bool IsIndoors,
	int CellCount,
	float ShelterStrength)
{
	public static readonly RoomContextSnapshot Exposed = new(
		IsIndoors: false,
		CellCount: 0,
		ShelterStrength: 0f);
}

public interface IEnvironmentExposureProvider
{
	EnvironmentExposureSnapshot Capture(GameState state, Actor actor);
}
