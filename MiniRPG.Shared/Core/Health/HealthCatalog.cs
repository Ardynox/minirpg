using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Health;

public static class HealthCatalog
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() },
	};

	private static readonly Dictionary<string, string> RaceProfileFallbacks = new(StringComparer.OrdinalIgnoreCase)
	{
		["human"] = "flesh_humanoid",
		["elf"] = "flesh_humanoid",
		["orc"] = "flesh_humanoid",
		["goblin"] = "flesh_humanoid",
		["wolf"] = "flesh_beast",
		["bear"] = "flesh_beast",
		["rat"] = "flesh_beast",
		["spider"] = "flesh_beast",
		["scorpion"] = "flesh_beast",
		["treant"] = "woody_beast",
		["undead"] = "undead_numb",
		["slime"] = "gelatinous",
	};

	private static bool _loaded;

	public static Dictionary<string, HealthProfileDef> Profiles { get; private set; } = new(StringComparer.Ordinal);
	public static Dictionary<string, HealthConditionDef> Conditions { get; private set; } = new(StringComparer.Ordinal);
	public static Dictionary<string, HealthThoughtDef> Thoughts { get; private set; } = new(StringComparer.Ordinal);

	public static void Load()
	{
		if (_loaded)
			return;

		_loaded = true;
		Profiles = LoadDictionary<HealthProfileDef>("health_profiles.json");
		Conditions = LoadDictionary<HealthConditionDef>("health_conditions.json");
		Thoughts = LoadDictionary<HealthThoughtDef>("health_thoughts.json");
	}

	public static HealthProfileDef GetProfileForActor(Actor actor)
	{
		Load();
		var resolvedId = ResolveProfileId(actor.Race?.Id, actor.Race?.HealthProfileId);
		return Profiles.TryGetValue(resolvedId, out var profile)
			? profile
			: Profiles["flesh_humanoid"];
	}

	public static string ResolveProfileId(string? raceId, string? explicitProfileId)
	{
		Load();
		if (!string.IsNullOrWhiteSpace(explicitProfileId) && Profiles.ContainsKey(explicitProfileId))
			return explicitProfileId;

		if (!string.IsNullOrWhiteSpace(raceId) && RaceProfileFallbacks.TryGetValue(raceId, out var fallback))
			return fallback;

		return "flesh_humanoid";
	}

	public static HealthConditionDef? GetCondition(string conditionId)
	{
		Load();
		return Conditions.GetValueOrDefault(conditionId);
	}

	public static HealthThoughtDef? GetThought(string thoughtId)
	{
		Load();
		return Thoughts.GetValueOrDefault(thoughtId);
	}

	public static string GetConditionDisplayName(string conditionId)
	{
		var def = GetCondition(conditionId);
		if (def == null)
			return conditionId;

		return LocalizationService.TOrFallback(def.LabelKey, def.DisplayName.Length > 0 ? def.DisplayName : conditionId);
	}

	public static string GetThoughtDisplayName(string thoughtId)
	{
		var def = GetThought(thoughtId);
		if (def == null)
			return thoughtId;

		return LocalizationService.TOrFallback(def.LabelKey, def.DisplayName.Length > 0 ? def.DisplayName : thoughtId);
	}

	private static Dictionary<string, T> LoadDictionary<T>(string relativePath) where T : class
	{
		var json = GameDataLocator.ReadTextOrThrow(relativePath);
		var list = JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
		var result = new Dictionary<string, T>(StringComparer.Ordinal);
		foreach (var item in list)
		{
			var id = typeof(T).GetProperty("Id")?.GetValue(item) as string;
			if (!string.IsNullOrWhiteSpace(id))
				result[id] = item;
		}

		return result;
	}
}
