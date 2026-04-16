using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Needs;

public static class NeedIds
{
	public const string Hunger = "hunger";
	public const string Thirst = "thirst";
	public const string Rest = "rest";
	public const string Mood = "mood";
}

public static class NeedThoughtSources
{
	public const string HungerStage = "need_stage:hunger";
	public const string ThirstStage = "need_stage:thirst";
	public const string RestStage = "need_stage:rest";
	public const string Meal = "meal";
	public const string Drink = "drink";
	public const string Sleep = "sleep";
	public const string Social = "social";
	public const string Combat = "combat";
}

public sealed class NeedState
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("current")]
	public float Current { get; set; }

	[JsonPropertyName("min")]
	public float Min { get; set; }

	[JsonPropertyName("max")]
	public float Max { get; set; } = 100f;

	[JsonPropertyName("lastUpdatedTurn")]
	public int LastUpdatedTurn { get; set; }
}

public sealed class ThoughtState
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("moodOffset")]
	public float MoodOffset { get; set; }

	[JsonPropertyName("expiresOnTurn")]
	public int ExpiresOnTurn { get; set; } = -1;

	[JsonPropertyName("source")]
	public string Source { get; set; } = "";
}

public sealed class NeedDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("labelKey")]
	public string LabelKey { get; set; } = "";

	[JsonPropertyName("displayName")]
	public string DisplayName { get; set; } = "";

	[JsonPropertyName("warningThreshold")]
	public float WarningThreshold { get; set; } = 35f;

	[JsonPropertyName("stages")]
	public List<NeedStageDef> Stages { get; set; } = [];
}

public sealed class NeedStageDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("maxValue")]
	public float MaxValue { get; set; } = 100f;

	/// <summary>
	/// 可选：该 stage 激活时对 actor capacity 的乘数（如 { "moving": 0.9, "consciousness": 0.85 }）。
	/// 留空视为无乘数。让 <see cref="NeedSystem.GetCapacityMultiplier"/> 可以纯数据驱动，避免硬编码分支。
	/// </summary>
	[JsonPropertyName("capacityMultipliers")]
	public Dictionary<string, float> CapacityMultipliers { get; set; } = new(System.StringComparer.Ordinal);
}

public sealed class NeedProfileDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("enabledNeeds")]
	public List<string> EnabledNeeds { get; set; } = [];

	[JsonPropertyName("allowMood")]
	public bool AllowMood { get; set; } = true;

	[JsonPropertyName("allowThoughts")]
	public bool AllowThoughts { get; set; } = true;

	[JsonPropertyName("decayPerTurn")]
	public Dictionary<string, float> DecayPerTurn { get; set; } = new(System.StringComparer.Ordinal);

	[JsonPropertyName("restGainPerTurn")]
	public float RestGainPerTurn { get; set; } = 18f;

	[JsonPropertyName("satisfiedThreshold")]
	public float SatisfiedThreshold { get; set; } = 85f;

	[JsonPropertyName("npcHungerThreshold")]
	public float NpcHungerThreshold { get; set; } = 35f;

	[JsonPropertyName("npcRestThreshold")]
	public float NpcRestThreshold { get; set; } = 30f;
}

public sealed class ThoughtDef
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

public readonly record struct RestContext(
	bool RequiresBedroll,
	bool UsesHomeFloor,
	float QualityMultiplier,
	string CompletionThoughtId,
	bool EmitCompletionEvent)
{
	public static RestContext ForPlayerBedroll(float qualityMultiplier) =>
		new(true, false, qualityMultiplier, "slept_bedroll", true);

	public static RestContext ForNpcBedroll(float qualityMultiplier) =>
		new(false, false, qualityMultiplier, "slept_bedroll", false);

	public static RestContext ForHomeFloor() =>
		new(false, true, 0.8f, "slept_home_floor", false);
}

public sealed class NeedActionResult
{
	public bool Consumed { get; set; }
	public List<GameEvent> Events { get; } = [];
}
