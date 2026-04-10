namespace MiniRPG.Core.Data;

public sealed class PlayerCreationOptions
{
	public const int MaxDisplayNameLength = 20;

	public string DisplayName { get; init; } = string.Empty;
	public string RaceId { get; init; } = string.Empty;
	public string? ProfessionId { get; init; }
	public string AppearanceId { get; init; } = GameState.DefaultPlayerAppearanceId;

	public static PlayerCreationOptions CreateDefault()
	{
		var defaultName = "Player";
		var defaultRaceId = "human";
		string? defaultProfessionId = null;

		if (PresetDB.Actors.TryGetValue(Factions.Player, out var playerPreset))
		{
			defaultName = string.IsNullOrWhiteSpace(playerPreset.DisplayName)
				? defaultName
				: playerPreset.DisplayName;
			defaultRaceId = string.IsNullOrWhiteSpace(playerPreset.RaceId)
				? defaultRaceId
				: playerPreset.RaceId;
			defaultProfessionId = playerPreset.ProfessionId;
		}

		return new PlayerCreationOptions
		{
			DisplayName = defaultName,
			RaceId = defaultRaceId,
			ProfessionId = defaultProfessionId,
			AppearanceId = GameState.DefaultPlayerAppearanceId,
		};
	}

	public string ResolveDisplayName(string fallback)
	{
		var normalized = NormalizeDisplayName(DisplayName);
		if (!string.IsNullOrWhiteSpace(normalized))
			return normalized;

		var normalizedFallback = NormalizeDisplayName(fallback);
		return string.IsNullOrWhiteSpace(normalizedFallback)
			? "Player"
			: normalizedFallback;
	}

	public string ResolveRaceId(string fallback)
	{
		if (!string.IsNullOrWhiteSpace(RaceId)
			&& PresetDB.Races.ContainsKey(RaceId))
		{
			return RaceId;
		}

		return !string.IsNullOrWhiteSpace(fallback) && PresetDB.Races.ContainsKey(fallback)
			? fallback
			: CreateDefault().RaceId;
	}

	public string? ResolveProfessionId(string? fallback)
	{
		if (!string.IsNullOrWhiteSpace(ProfessionId)
			&& PresetDB.Professions.ContainsKey(ProfessionId))
		{
			return ProfessionId;
		}

		if (!string.IsNullOrWhiteSpace(fallback)
			&& PresetDB.Professions.ContainsKey(fallback))
		{
			return fallback;
		}

		return null;
	}

	public string ResolveAppearanceId() => PlayerAppearanceCatalog.NormalizeId(AppearanceId);

	public static string NormalizeDisplayName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;

		var trimmed = value.Trim();
		if (trimmed.Length <= MaxDisplayNameLength)
			return trimmed;

		return trimmed[..MaxDisplayNameLength];
	}
}
