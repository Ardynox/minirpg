using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.AI.Utility;

public static class PersonalityModule
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
	};

	private static PersonalityConfig _config = new();
	private static bool _loaded;

	public static void EnsureLoaded()
	{
		if (_loaded) return;
		Load();
	}

	public static void Load()
	{
		var json = GameDataLocator.ReadTextOrThrow("Config/personality_defaults.json");
		_config = JsonSerializer.Deserialize<PersonalityConfig>(json, JsonOptions) ?? new PersonalityConfig();
		_loaded = true;
	}

	public static void GeneratePersonality(Actor actor, Random rng, Actor? parent1 = null, Actor? parent2 = null)
	{
		EnsureLoaded();

		var raceId = actor.Race?.Id ?? "";
		var professionId = actor.Profession?.Id ?? "";

		foreach (var axis in _config.Axes)
		{
			var baseValue = axis.Default;

			if (_config.RaceOffsets.TryGetValue(raceId, out var raceOffsets)
				&& raceOffsets.TryGetValue(axis.Id, out var raceOffset))
			{
				baseValue += raceOffset;
			}

			if (_config.ProfessionOffsets.TryGetValue(professionId, out var profOffsets)
				&& profOffsets.TryGetValue(axis.Id, out var profOffset))
			{
				baseValue += profOffset;
			}

			if (parent1 != null && parent2 != null)
			{
				var p1Val = GetPersonalityValue(parent1, axis.Id, axis.Default);
				var p2Val = GetPersonalityValue(parent2, axis.Id, axis.Default);
				var inherited = (p1Val + p2Val) * 0.5f;
				var mutation = (float)(rng.NextDouble() * 2 - 1) * _config.MutationRange;
				baseValue = inherited + mutation;
			}
			else
			{
				var randomOffset = (float)(rng.NextDouble() * 2 - 1) * _config.RandomRange;
				baseValue += randomOffset;
			}

			baseValue = Math.Clamp(baseValue, axis.Min, axis.Max);
			actor.DialogPersonality[axis.Id] = baseValue;
		}
	}

	public static float GetPersonalityValue(Actor actor, string traitId, float defaultValue = 0.5f)
	{
		return actor.DialogPersonality.TryGetValue(traitId, out var val) ? val : defaultValue;
	}

	public static IReadOnlyList<PersonalityAxisDef> GetAxes()
	{
		EnsureLoaded();
		return _config.Axes;
	}

	public static void Reset()
	{
		_config = new PersonalityConfig();
		_loaded = false;
	}
}

public sealed class PersonalityConfig
{
	[JsonPropertyName("axes")]
	public List<PersonalityAxisDef> Axes { get; set; } = [];

	[JsonPropertyName("race_offsets")]
	public Dictionary<string, Dictionary<string, float>> RaceOffsets { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("profession_offsets")]
	public Dictionary<string, Dictionary<string, float>> ProfessionOffsets { get; set; } = new(StringComparer.Ordinal);

	[JsonPropertyName("random_range")]
	public float RandomRange { get; set; } = 0.15f;

	[JsonPropertyName("mutation_range")]
	public float MutationRange { get; set; } = 0.1f;
}

public sealed class PersonalityAxisDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("min")]
	public float Min { get; set; }

	[JsonPropertyName("max")]
	public float Max { get; set; } = 1f;

	[JsonPropertyName("default")]
	public float Default { get; set; } = 0.5f;
}
