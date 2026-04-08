using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Needs;

public static class NeedCatalog
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Converters = { new JsonStringEnumConverter() },
	};

	private static readonly Dictionary<string, string> RaceProfileFallbacks = new(StringComparer.OrdinalIgnoreCase)
	{
		["human"] = "humanoid_standard",
		["elf"] = "humanoid_standard",
		["orc"] = "humanoid_standard",
		["goblin"] = "humanoid_standard",
		["wolf"] = "beast_standard",
		["bear"] = "beast_standard",
		["rat"] = "beast_standard",
		["spider"] = "beast_standard",
		["scorpion"] = "beast_standard",
		["treant"] = "beast_standard",
		["undead"] = "undead_none",
		["slime"] = "slime_basic",
	};

	private static bool _loaded;

	public static Dictionary<string, NeedDef> Needs { get; private set; } = new(StringComparer.Ordinal);
	public static Dictionary<string, NeedProfileDef> Profiles { get; private set; } = new(StringComparer.Ordinal);
	public static Dictionary<string, ThoughtDef> Thoughts { get; private set; } = new(StringComparer.Ordinal);

	public static void Load()
	{
		if (_loaded)
			return;

		_loaded = true;
		Needs = LoadDictionary<NeedDef>("needs.json");
		Profiles = LoadDictionary<NeedProfileDef>("need_profiles.json");
		Thoughts = LoadDictionary<ThoughtDef>("thoughts.json");
	}

	public static NeedDef? GetNeed(string needId)
	{
		Load();
		return Needs.GetValueOrDefault(needId);
	}

	public static NeedProfileDef GetProfileForActor(Actor actor)
	{
		Load();
		var resolvedId = ResolveProfileId(actor.Race?.Id, actor.Race?.NeedProfileId);
		return Profiles.TryGetValue(resolvedId, out var profile)
			? profile
			: Profiles["humanoid_standard"];
	}

	public static string ResolveProfileId(string? raceId, string? explicitProfileId)
	{
		Load();
		if (!string.IsNullOrWhiteSpace(explicitProfileId) && Profiles.ContainsKey(explicitProfileId))
			return explicitProfileId;

		if (!string.IsNullOrWhiteSpace(raceId) && RaceProfileFallbacks.TryGetValue(raceId, out var fallback))
			return fallback;

		return "humanoid_standard";
	}

	public static ThoughtDef? GetThought(string thoughtId)
	{
		Load();
		if (Thoughts.TryGetValue(thoughtId, out var thought))
			return thought;

		var healthThought = HealthCatalog.GetThought(thoughtId);
		return healthThought == null
			? null
			: new ThoughtDef
			{
				Id = healthThought.Id,
				LabelKey = healthThought.LabelKey,
				DisplayName = healthThought.DisplayName,
				MoodOffset = healthThought.MoodOffset,
				DurationTurns = healthThought.DurationTurns,
			};
	}

	public static string GetNeedDisplayName(string needId)
	{
		var def = GetNeed(needId);
		if (def == null)
			return needId;

		return LocalizationService.TOrFallback(def.LabelKey, def.DisplayName.Length > 0 ? def.DisplayName : needId);
	}

	public static string GetThoughtDisplayName(string thoughtId)
	{
		var def = GetThought(thoughtId);
		if (def == null)
			return HealthCatalog.GetThoughtDisplayName(thoughtId);

		return LocalizationService.TOrFallback(def.LabelKey, def.DisplayName.Length > 0 ? def.DisplayName : thoughtId);
	}

	public static string? ResolveStageId(string needId, float current)
	{
		var def = GetNeed(needId);
		if (def == null || def.Stages.Count == 0)
			return null;

		return def.Stages
			.OrderBy(stage => stage.MaxValue)
			.FirstOrDefault(stage => current <= stage.MaxValue)
			?.Id;
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
