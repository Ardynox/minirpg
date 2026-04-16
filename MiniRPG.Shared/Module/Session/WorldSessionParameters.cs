using System;
using MiniRPG.Core.Config;
using MiniRPG.Core.Map;

namespace MiniRPG.Module.Session;

/// <summary>
/// Pure helpers that normalize the world-session parameters a caller
/// supplies to <c>GameSessionModule</c>: density clamps, generator id
/// fallbacks, and preset-scenario id/display resolution.
/// </summary>
/// <remarks>
/// Extracted from <c>GameSessionModule</c> so the session orchestrator
/// stays free of the input-normalization policy. Any caller that builds
/// a <see cref="WorldSettings"/> for the session can reuse these helpers.
/// </remarks>
public static class WorldSessionParameters
{
	/// <summary>Default generator id used when a caller leaves the id blank or passes the legacy blank-floor id.</summary>
	public const string DefaultGeneratorId = "dwarf_fortress";

	/// <summary>
	/// Clone <paramref name="settings"/>, clamp density/volatility sliders and
	/// fill in fallback seed/climate/season/civilization defaults.
	/// </summary>
	public static WorldSettings Normalize(WorldSettings settings)
	{
		var normalized = settings.Clone();
		if (normalized.Seed == 0)
			normalized.Seed = Environment.TickCount;

		normalized.GeneratorId = NormalizeGeneratorId(normalized.GeneratorId);
		normalized.MonsterDensityPercent = Math.Clamp(normalized.MonsterDensityPercent, 0, 500);
		normalized.NpcDensityPercent = Math.Clamp(normalized.NpcDensityPercent, 0, 500);
		normalized.LootAbundancePercent = Math.Clamp(normalized.LootAbundancePercent, 0, 500);
		normalized.NestIntensityPercent = Math.Clamp(normalized.NestIntensityPercent, 0, 500);
		normalized.WeatherVolatilityPercent = Math.Clamp(normalized.WeatherVolatilityPercent, 0, 500);
		normalized.ClimateId = string.IsNullOrWhiteSpace(normalized.ClimateId) ? "temperate" : normalized.ClimateId.Trim();
		normalized.StartSeasonId = string.IsNullOrWhiteSpace(normalized.StartSeasonId) ? "spring" : normalized.StartSeasonId.Trim();
		normalized.CivilizationLevelId = string.IsNullOrWhiteSpace(normalized.CivilizationLevelId) ? "frontier" : normalized.CivilizationLevelId.Trim();
		return normalized;
	}

	/// <summary>
	/// Trim the generator id and fall back to <see cref="DefaultGeneratorId"/>
	/// when missing or the legacy <c>blank_floor</c> id.
	/// </summary>
	public static string NormalizeGeneratorId(string? generatorId)
	{
		var normalized = generatorId?.Trim();
		if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "blank_floor", StringComparison.Ordinal))
			return DefaultGeneratorId;

		return normalized;
	}

	/// <summary>
	/// Resolve a human-readable display name for the preset scenario id:
	/// localized display name when the scenario exists, humanized id when
	/// it does not, and <c>ui.common.none</c> when the id is blank.
	/// </summary>
	public static string ResolvePresetScenarioDisplayName(string? presetScenarioId)
	{
		if (!string.IsNullOrWhiteSpace(presetScenarioId)
			&& PresetScenarioCatalog.TryGet(presetScenarioId, out var scenario))
		{
			return LocalizationService.TOrFallback(
				scenario.DisplayNameKey,
				GameLocalizer.HumanizeId(scenario.Id));
		}

		return string.IsNullOrWhiteSpace(presetScenarioId)
			? LocalizationService.T("ui.common.none")
			: GameLocalizer.HumanizeId(presetScenarioId);
	}

	/// <summary>
	/// Validate and trim a preset scenario id before it reaches the
	/// scenario catalog. Throws when the id is null or whitespace.
	/// </summary>
	public static string NormalizePresetScenarioId(string id)
	{
		var normalized = (id ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
			throw new ArgumentException("Preset scenario id cannot be empty.", nameof(id));

		return normalized;
	}
}
