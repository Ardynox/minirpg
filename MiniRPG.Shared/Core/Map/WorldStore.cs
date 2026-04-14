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
	public string GeneratorId { get; set; } = "dwarf_fortress";

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

public sealed class WorldStorageMigrationItem
{
	public string WorldId { get; init; } = string.Empty;
	public string SourcePath { get; init; } = string.Empty;
	public bool Success { get; init; }
	public string Message { get; init; } = string.Empty;
}

public sealed class WorldStorageMigrationReport
{
	public IReadOnlyList<WorldStorageMigrationItem> Results { get; init; } = Array.Empty<WorldStorageMigrationItem>();
	public int SuccessCount => Results.Count(static item => item.Success);
	public int FailureCount => Results.Count(static item => !item.Success);
	public IReadOnlyList<WorldStorageMigrationItem> Failures => Results.Where(static item => !item.Success).ToArray();
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
	public string LegacyWorldsDirectory => Path.Combine(_userDataRoot, "worlds");
	public string WorldManifestsDirectory => Path.Combine(_userDataRoot, "world_manifests");
	public string WorldSavesDirectory => Path.Combine(_userDataRoot, "world_saves");
	public string WorldAssetsDirectory => Path.Combine(_userDataRoot, "world_assets");

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
		Directory.CreateDirectory(WorldManifestsDirectory);
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
		if (!Directory.Exists(WorldManifestsDirectory))
			return Array.Empty<WorldManifest>();

		var result = new List<WorldManifest>();
		foreach (var path in Directory.EnumerateFiles(WorldManifestsDirectory, "*.json", SearchOption.TopDirectoryOnly))
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
		var characterDir = GetWorldSaveDirectory(worldId);
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

	public bool HasWorldSaveData(string worldId)
	{
		var saveDirectory = GetWorldSaveDirectory(worldId);
		return Directory.Exists(saveDirectory)
			&& Directory.EnumerateFiles(saveDirectory, "*.json", SearchOption.TopDirectoryOnly).Any();
	}

	public bool HasWorldAssets(string worldId)
	{
		var assetDirectory = GetWorldAssetDirectory(worldId);
		return Directory.Exists(assetDirectory)
			&& Directory.EnumerateFileSystemEntries(assetDirectory, "*", SearchOption.AllDirectories).Any();
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

		if (!TryLoadWorld(worldId, out _))
			return false;

		if (!DeleteDirectoryIfExists(WorldSavesDirectory, GetWorldSaveDirectory(worldId)))
			return false;

		if (!DeleteDirectoryIfExists(WorldAssetsDirectory, GetWorldAssetDirectory(worldId)))
			return false;

		return DeleteFileIfExists(WorldManifestsDirectory, GetWorldManifestPath(worldId));
	}

	public bool DeleteWorldSaveData(string worldId)
	{
		if (string.IsNullOrWhiteSpace(worldId))
			return false;

		if (!TryLoadWorld(worldId, out var manifest))
			return false;

		var saveDirectory = GetWorldSaveDirectory(worldId);
		if (!DeleteDirectoryIfExists(WorldSavesDirectory, saveDirectory))
			return false;

		manifest.LastPlayedAtUtc = null;
		manifest.LastPlayedCharacterId = null;
		SaveWorld(manifest);
		return true;
	}

	public bool DeleteWorldAssets(string worldId)
	{
		if (string.IsNullOrWhiteSpace(worldId))
			return false;

		return DeleteDirectoryIfExists(WorldAssetsDirectory, GetWorldAssetDirectory(worldId));
	}

	public WorldStorageMigrationReport MigrateLegacyWorldLayout()
	{
		if (!Directory.Exists(LegacyWorldsDirectory))
			return new WorldStorageMigrationReport();

		var results = new List<WorldStorageMigrationItem>();
		var manifestPaths = Directory
			.EnumerateFiles(LegacyWorldsDirectory, "world.json", SearchOption.AllDirectories)
			.ToArray();
		foreach (var manifestPath in manifestPaths)
		{
			var sourceDirectory = Path.GetDirectoryName(manifestPath) ?? string.Empty;
			var sourceWorldId = Path.GetFileName(sourceDirectory);
			var targetWorldId = sourceWorldId;
			var migrationOwnsTarget = false;
			try
			{
				if (!TryLoadManifestAtPath(manifestPath, out var manifest))
				{
					results.Add(new WorldStorageMigrationItem
					{
						WorldId = sourceWorldId,
						SourcePath = sourceDirectory,
						Success = false,
						Message = "Failed to read legacy world manifest.",
					});
					continue;
				}

				targetWorldId = string.IsNullOrWhiteSpace(manifest.WorldId)
					? sourceWorldId
					: manifest.WorldId;
				manifest.WorldId = targetWorldId;

				if (HasSplitWorldLayoutData(targetWorldId))
				{
					results.Add(new WorldStorageMigrationItem
					{
						WorldId = targetWorldId,
						SourcePath = sourceDirectory,
						Success = false,
						Message = "Split-layout world data already exists for this worldId. Legacy data was left untouched to avoid overwriting newer data.",
					});
					continue;
				}

				CleanupMigratedWorldTargets(targetWorldId);
				migrationOwnsTarget = true;
				SaveWorld(manifest);
				CopyLegacyCharacterSaves(sourceDirectory, targetWorldId);
				if (!VerifyMigratedWorld(manifest, sourceDirectory))
				{
					if (migrationOwnsTarget)
						CleanupMigratedWorldTargets(targetWorldId);
					results.Add(new WorldStorageMigrationItem
					{
						WorldId = targetWorldId,
						SourcePath = sourceDirectory,
						Success = false,
						Message = "Migrated world verification failed.",
					});
					continue;
				}

				if (!DeleteDirectoryIfExists(LegacyWorldsDirectory, sourceDirectory))
				{
					if (migrationOwnsTarget)
						CleanupMigratedWorldTargets(targetWorldId);
					results.Add(new WorldStorageMigrationItem
					{
						WorldId = targetWorldId,
						SourcePath = sourceDirectory,
						Success = false,
						Message = "Failed to remove legacy world directory after migration.",
					});
					continue;
				}

				results.Add(new WorldStorageMigrationItem
				{
					WorldId = targetWorldId,
					SourcePath = sourceDirectory,
					Success = true,
					Message = "Legacy world migrated successfully.",
				});
			}
			catch (Exception ex)
			{
				if (migrationOwnsTarget)
					CleanupMigratedWorldTargets(targetWorldId);
				results.Add(new WorldStorageMigrationItem
				{
					WorldId = targetWorldId,
					SourcePath = sourceDirectory,
					Success = false,
					Message = ex.Message,
				});
			}
		}

		return new WorldStorageMigrationReport { Results = results };
	}

	public string GetWorldManifestPath(string worldId) => Path.Combine(WorldManifestsDirectory, worldId + ".json");

	public string GetWorldSaveDirectory(string worldId) => Path.Combine(WorldSavesDirectory, worldId);

	public string GetWorldAssetDirectory(string worldId) => Path.Combine(WorldAssetsDirectory, worldId);

	public string GetCharacterSavePath(string worldId, string characterId) =>
		Path.Combine(GetWorldSaveDirectory(worldId), characterId + ".json");

	private bool VerifyMigratedWorld(WorldManifest manifest, string sourceDirectory)
	{
		if (!TryLoadWorld(manifest.WorldId, out _))
			return false;

		var legacyCharacterDirectory = Path.Combine(sourceDirectory, "characters");
		if (!Directory.Exists(legacyCharacterDirectory))
			return true;

		foreach (var sourcePath in Directory.EnumerateFiles(legacyCharacterDirectory, "*.json", SearchOption.TopDirectoryOnly))
		{
			var targetPath = Path.Combine(GetWorldSaveDirectory(manifest.WorldId), Path.GetFileName(sourcePath));
			if (!File.Exists(targetPath))
				return false;

			var sourceLength = new FileInfo(sourcePath).Length;
			var targetLength = new FileInfo(targetPath).Length;
			if (sourceLength != targetLength)
				return false;
		}

		return true;
	}

	private void CopyLegacyCharacterSaves(string sourceDirectory, string worldId)
	{
		var legacyCharacterDirectory = Path.Combine(sourceDirectory, "characters");
		if (!Directory.Exists(legacyCharacterDirectory))
			return;

		var targetSaveDirectory = GetWorldSaveDirectory(worldId);
		Directory.CreateDirectory(targetSaveDirectory);
		foreach (var sourcePath in Directory.EnumerateFiles(legacyCharacterDirectory, "*.json", SearchOption.TopDirectoryOnly))
		{
			var targetPath = Path.Combine(targetSaveDirectory, Path.GetFileName(sourcePath));
			File.Copy(sourcePath, targetPath, overwrite: true);
		}
	}

	private bool HasSplitWorldLayoutData(string worldId)
	{
		if (File.Exists(GetWorldManifestPath(worldId)))
			return true;

		return HasWorldSaveData(worldId);
	}

	private void CleanupMigratedWorldTargets(string worldId)
	{
		var manifestPath = GetWorldManifestPath(worldId);
		if (File.Exists(manifestPath))
			File.Delete(manifestPath);

		var saveDirectory = GetWorldSaveDirectory(worldId);
		if (Directory.Exists(saveDirectory))
			Directory.Delete(saveDirectory, recursive: true);
	}

	private static bool TryLoadManifestAtPath(string manifestPath, out WorldManifest manifest)
	{
		manifest = null!;
		if (!File.Exists(manifestPath))
			return false;

		try
		{
			var json = File.ReadAllText(manifestPath);
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

	private static bool DeleteDirectoryIfExists(string rootDirectory, string targetDirectory)
	{
		if (!IsPathWithinRoot(rootDirectory, targetDirectory))
			return false;

		if (!Directory.Exists(targetDirectory))
			return true;

		try
		{
			Directory.Delete(targetDirectory, recursive: true);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool DeleteFileIfExists(string rootDirectory, string targetPath)
	{
		if (!IsPathWithinRoot(rootDirectory, targetPath))
			return false;

		if (!File.Exists(targetPath))
			return true;

		try
		{
			File.Delete(targetPath);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsPathWithinRoot(string rootDirectory, string targetDirectory)
	{
		var normalizedRoot = Path.GetFullPath(rootDirectory)
			.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
			+ Path.DirectorySeparatorChar;
		var normalizedTarget = Path.GetFullPath(targetDirectory);
		return normalizedTarget.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
	}

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
