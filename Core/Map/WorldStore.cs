using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Map;

public sealed class WorldSettings
{
	[JsonPropertyName("seed")]
	public int Seed { get; set; } = Environment.TickCount;

	[JsonPropertyName("generatorId")]
	public string GeneratorId { get; set; } = "room_corridor";

	[JsonPropertyName("climateId")]
	public string ClimateId { get; set; } = "temperate";

	[JsonPropertyName("startSeasonId")]
	public string StartSeasonId { get; set; } = "spring";

	[JsonPropertyName("civilizationLevelId")]
	public string CivilizationLevelId { get; set; } = "frontier";

	[JsonPropertyName("monsterDensityPercent")]
	public int MonsterDensityPercent { get; set; } = 100;

	[JsonPropertyName("npcDensityPercent")]
	public int NpcDensityPercent { get; set; } = 100;

	[JsonPropertyName("lootAbundancePercent")]
	public int LootAbundancePercent { get; set; } = 100;

	[JsonPropertyName("nestIntensityPercent")]
	public int NestIntensityPercent { get; set; } = 100;

	[JsonPropertyName("weatherVolatilityPercent")]
	public int WeatherVolatilityPercent { get; set; } = 100;

	public static WorldSettings CreateDefault() => new();

	public WorldSettings Clone() => new()
	{
		Seed = Seed,
		GeneratorId = GeneratorId,
		ClimateId = ClimateId,
		StartSeasonId = StartSeasonId,
		CivilizationLevelId = CivilizationLevelId,
		MonsterDensityPercent = MonsterDensityPercent,
		NpcDensityPercent = NpcDensityPercent,
		LootAbundancePercent = LootAbundancePercent,
		NestIntensityPercent = NestIntensityPercent,
		WeatherVolatilityPercent = WeatherVolatilityPercent,
	};
}

public sealed class WorldManifest
{
	[JsonPropertyName("worldId")]
	public required string WorldId { get; set; }

	[JsonPropertyName("displayName")]
	public required string DisplayName { get; set; }

	[JsonPropertyName("settings")]
	public required WorldSettings Settings { get; set; }

	[JsonPropertyName("createdAtUtc")]
	public required DateTimeOffset CreatedAtUtc { get; set; }

	[JsonPropertyName("lastPlayedAtUtc")]
	public DateTimeOffset? LastPlayedAtUtc { get; set; }

	[JsonPropertyName("lastPlayedCharacterId")]
	public string? LastPlayedCharacterId { get; set; }
}

public sealed class WorldEntryInfo
{
	public string WorldId { get; init; } = string.Empty;
	public string DisplayName { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
	public WorldSettings Settings { get; init; } = WorldSettings.CreateDefault();
	public DateTimeOffset CreatedAtUtc { get; init; }
	public DateTimeOffset? LastPlayedAtUtc { get; init; }
	public string? LastPlayedCharacterId { get; init; }
	public IReadOnlyList<WorldCharacterEntryInfo> Characters { get; init; } = Array.Empty<WorldCharacterEntryInfo>();
	public int CharacterCount { get; init; }
}

public sealed class WorldCharacterEntryInfo
{
	public string WorldId { get; init; } = string.Empty;
	public string WorldName { get; init; } = string.Empty;
	public string CharacterId { get; init; } = string.Empty;
	public string CharacterName { get; init; } = string.Empty;
	public string SavePath { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
	public DateTimeOffset SavedAtUtc { get; init; }
	public DateTimeOffset ModifiedAtUtc { get; init; }
	public int Turn { get; init; }
	public int PlayerZ { get; init; }
	public string GeneratorId { get; init; } = string.Empty;
	public bool IsLastPlayedCharacter { get; init; }
}

public enum WorldLaunchTab
{
	Worlds,
	Scenarios,
	LegacySaves,
}

public enum ContinueTargetKind
{
	None,
	WorldCharacter,
	LegacySave,
}

public sealed class ContinueTarget
{
	public static ContinueTarget None { get; } = new() { Kind = ContinueTargetKind.None };

	public ContinueTargetKind Kind { get; init; }
	public string? WorldId { get; init; }
	public string? WorldName { get; init; }
	public string? CharacterId { get; init; }
	public string? CharacterName { get; init; }
	public string? SavePath { get; init; }
	public string Label { get; init; } = string.Empty;
}

public sealed class WorldStore
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private readonly string _userDataRoot;

	public WorldStore(string userDataRoot)
	{
		_userDataRoot = Path.GetFullPath(userDataRoot);
	}

	public string UserDataRoot => _userDataRoot;
	public string SaveDirectory => Path.Combine(_userDataRoot, "save");
	public string WorldsDirectory => Path.Combine(_userDataRoot, "worlds");

	public string CreateWorldId(string displayName) => CreateSluggedId(displayName, "world");

	public string CreateCharacterId(string displayName) => CreateSluggedId(displayName, "character");

	public WorldManifest CreateWorld(string displayName, WorldSettings settings)
	{
		var normalizedName = NormalizeDisplayName(displayName, "World");
		var manifest = new WorldManifest
		{
			WorldId = CreateWorldId(normalizedName),
			DisplayName = normalizedName,
			Settings = settings.Clone(),
			CreatedAtUtc = DateTimeOffset.UtcNow,
		};

		SaveWorld(manifest);
		return manifest;
	}

	public void SaveWorld(WorldManifest manifest)
	{
		Directory.CreateDirectory(GetWorldDirectory(manifest.WorldId));
		Directory.CreateDirectory(GetCharacterDirectory(manifest.WorldId));
		var json = JsonSerializer.Serialize(manifest, JsonOptions);
		File.WriteAllText(GetWorldManifestPath(manifest.WorldId), json);
	}

	public bool TryLoadWorld(string worldId, out WorldManifest manifest)
	{
		manifest = null!;
		var path = GetWorldManifestPath(worldId);
		if (!File.Exists(path))
			return false;

		try
		{
			var json = File.ReadAllText(path);
			var loaded = JsonSerializer.Deserialize<WorldManifest>(json, JsonOptions);
			if (loaded == null)
				return false;

			loaded.Settings ??= WorldSettings.CreateDefault();
			manifest = loaded;
			return true;
		}
		catch
		{
			return false;
		}
	}

	public IReadOnlyList<WorldManifest> ListWorlds()
	{
		if (!Directory.Exists(WorldsDirectory))
			return Array.Empty<WorldManifest>();

		var result = new List<WorldManifest>();
		foreach (var path in Directory.EnumerateFiles(WorldsDirectory, "world.json", SearchOption.AllDirectories))
		{
			try
			{
				var json = File.ReadAllText(path);
				var manifest = JsonSerializer.Deserialize<WorldManifest>(json, JsonOptions);
				if (manifest == null)
					continue;

				manifest.Settings ??= WorldSettings.CreateDefault();
				result.Add(manifest);
			}
			catch
			{
				// Ignore unreadable world manifests so one bad file does not break the browser.
			}
		}

		result.Sort(static (a, b) =>
		{
			var playedCompare = Nullable.Compare(b.LastPlayedAtUtc, a.LastPlayedAtUtc);
			if (playedCompare != 0)
				return playedCompare;

			var createdCompare = b.CreatedAtUtc.CompareTo(a.CreatedAtUtc);
			if (createdCompare != 0)
				return createdCompare;

			return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCulture);
		});
		return result;
	}

	public IReadOnlyList<WorldCharacterEntryInfo> ListWorldCharacters(string worldId, string worldName)
	{
		var characterDir = GetCharacterDirectory(worldId);
		if (!Directory.Exists(characterDir))
			return Array.Empty<WorldCharacterEntryInfo>();

		var result = new List<WorldCharacterEntryInfo>();
		foreach (var path in Directory.EnumerateFiles(characterDir, "*.json", SearchOption.TopDirectoryOnly))
		{
			if (SaveModule.TryReadSaveHeader(path, out var header) != SaveLoadStatus.Success || header == null)
				continue;

			var fileName = Path.GetFileNameWithoutExtension(path);
			result.Add(new WorldCharacterEntryInfo
			{
				WorldId = header.WorldId ?? worldId,
				WorldName = header.WorldName ?? worldName,
				CharacterId = header.CharacterId ?? fileName,
				CharacterName = header.CharacterName ?? header.Title ?? fileName,
				SavePath = Path.GetFullPath(path),
				SavedAtUtc = header.SavedAtUtc,
				ModifiedAtUtc = File.GetLastWriteTimeUtc(path),
				Turn = header.Turn,
				PlayerZ = header.PlayerZ,
				GeneratorId = header.GeneratorId,
			});
		}

		result.Sort(static (a, b) =>
		{
			var modifiedCompare = b.ModifiedAtUtc.CompareTo(a.ModifiedAtUtc);
			if (modifiedCompare != 0)
				return modifiedCompare;

			var savedCompare = b.SavedAtUtc.CompareTo(a.SavedAtUtc);
			if (savedCompare != 0)
				return savedCompare;

			return string.Compare(a.CharacterName, b.CharacterName, StringComparison.CurrentCulture);
		});
		return result;
	}

	public bool TryGetCharacterSavePath(string worldId, string characterId, out string path)
	{
		path = GetCharacterSavePath(worldId, characterId);
		return File.Exists(path);
	}

	public void UpdateWorldLastPlayed(string worldId, DateTimeOffset playedAtUtc, string? characterId)
	{
		if (!TryLoadWorld(worldId, out var manifest))
			return;

		manifest.LastPlayedAtUtc = playedAtUtc;
		manifest.LastPlayedCharacterId = characterId;
		SaveWorld(manifest);
	}

	public bool DeleteWorld(string worldId)
	{
		if (string.IsNullOrWhiteSpace(worldId))
			return false;

		var worldsRoot = Path.GetFullPath(WorldsDirectory);
		var worldDirectory = Path.GetFullPath(GetWorldDirectory(worldId));
		var expectedPrefix = worldsRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
		if (!worldDirectory.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
			return false;

		if (!Directory.Exists(worldDirectory))
			return false;

		try
		{
			Directory.Delete(worldDirectory, recursive: true);
			return true;
		}
		catch
		{
			return false;
		}
	}

	public string GetWorldDirectory(string worldId) => Path.Combine(WorldsDirectory, worldId);

	public string GetWorldManifestPath(string worldId) => Path.Combine(GetWorldDirectory(worldId), "world.json");

	public string GetCharacterDirectory(string worldId) => Path.Combine(GetWorldDirectory(worldId), "characters");

	public string GetCharacterSavePath(string worldId, string characterId) =>
		Path.Combine(GetCharacterDirectory(worldId), characterId + ".json");

	private static string CreateSluggedId(string displayName, string fallbackPrefix)
	{
		var slug = Slugify(displayName);
		if (string.IsNullOrWhiteSpace(slug))
			slug = fallbackPrefix;

		var suffix = Guid.NewGuid().ToString("N")[..8];
		return $"{slug}-{suffix}";
	}

	public static string Slugify(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
			return string.Empty;

		var builder = new StringBuilder(raw.Length);
		var pendingDash = false;
		foreach (var ch in raw.Trim())
		{
			if (char.IsLetterOrDigit(ch))
			{
				if (pendingDash && builder.Length > 0)
					builder.Append('-');

				builder.Append(char.ToLowerInvariant(ch));
				pendingDash = false;
				continue;
			}

			pendingDash = builder.Length > 0;
		}

		return builder.ToString().Trim('-');
	}

	private static string NormalizeDisplayName(string? raw, string fallback)
	{
		var trimmed = raw?.Trim();
		return string.IsNullOrWhiteSpace(trimmed)
			? fallback
			: trimmed;
	}
}
