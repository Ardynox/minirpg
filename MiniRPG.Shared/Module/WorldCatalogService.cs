using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Config;
using MiniRPG.Core.Map;

namespace MiniRPG.Module;

/// <summary>
/// Manages the world/character catalog: listing, deletion, and summary building.
/// Extracted from GameSessionModule to reduce its responsibility surface.
/// </summary>
public sealed class WorldCatalogService
{
    private readonly WorldStore _worldStore;
    private readonly Func<string, bool> _isWorldLocked;

    /// <param name="worldStore">Underlying world storage.</param>
    /// <param name="isWorldLocked">
    /// Returns <c>true</c> when the given <paramref name="worldId"/> is the currently-active
    /// world-character session and must not be deleted.
    /// </param>
    public WorldCatalogService(WorldStore worldStore, Func<string, bool> isWorldLocked)
    {
        _worldStore = worldStore ?? throw new ArgumentNullException(nameof(worldStore));
        _isWorldLocked = isWorldLocked ?? throw new ArgumentNullException(nameof(isWorldLocked));
    }

    public IReadOnlyList<WorldEntryInfo> ListWorlds()
    {
        var manifests = _worldStore.ListWorlds();
        var result = new List<WorldEntryInfo>(manifests.Count);
        foreach (var manifest in manifests)
        {
            var characters = ListWorldCharacters(manifest.WorldId);
            result.Add(new WorldEntryInfo
            {
                WorldId = manifest.WorldId,
                DisplayName = manifest.DisplayName,
                Summary = BuildWorldSummary(manifest, characters),
                Settings = manifest.Settings.Clone(),
                CreatedAtUtc = manifest.CreatedAtUtc,
                LastPlayedAtUtc = manifest.LastPlayedAtUtc,
                LastPlayedCharacterId = manifest.LastPlayedCharacterId,
                Characters = characters,
                CharacterCount = characters.Count,
            });
        }

        return result;
    }

    public IReadOnlyList<WorldCharacterEntryInfo> ListWorldCharacters(string worldId)
    {
        if (!_worldStore.TryLoadWorld(worldId, out var manifest))
            return Array.Empty<WorldCharacterEntryInfo>();

        var rawCharacters = _worldStore.ListWorldCharacters(worldId, manifest.DisplayName);
        var result = new List<WorldCharacterEntryInfo>(rawCharacters.Count);
        foreach (var entry in rawCharacters)
        {
            var isLastPlayedCharacter = string.Equals(
                entry.CharacterId,
                manifest.LastPlayedCharacterId,
                StringComparison.Ordinal);
            result.Add(new WorldCharacterEntryInfo
            {
                WorldId = entry.WorldId,
                WorldName = entry.WorldName,
                CharacterId = entry.CharacterId,
                CharacterName = entry.CharacterName,
                SavePath = entry.SavePath,
                Summary = BuildCharacterSummary(entry.Turn, entry.PlayerZ, entry.SavedAtUtc, isLastPlayedCharacter),
                SavedAtUtc = entry.SavedAtUtc,
                ModifiedAtUtc = entry.ModifiedAtUtc,
                Turn = entry.Turn,
                PlayerZ = entry.PlayerZ,
                GeneratorId = entry.GeneratorId,
                IsLastPlayedCharacter = isLastPlayedCharacter,
            });
        }

        return result;
    }

    public WorldSaveDataDeletionStatus DeleteWorldSaveData(string worldId)
    {
        if (string.IsNullOrWhiteSpace(worldId))
            return WorldSaveDataDeletionStatus.NotFound;

        if (_isWorldLocked(worldId))
            return WorldSaveDataDeletionStatus.ActiveWorldLocked;

        if (!_worldStore.TryLoadWorld(worldId, out _))
            return WorldSaveDataDeletionStatus.NotFound;

        if (!_worldStore.DeleteWorldSaveData(worldId))
            return WorldSaveDataDeletionStatus.Failed;

        ClearContinueStateForDeletedWorld(worldId);
        return WorldSaveDataDeletionStatus.Success;
    }

    public WorldAssetCleanupStatus DeleteWorldAssets(string worldId)
    {
        if (string.IsNullOrWhiteSpace(worldId))
            return WorldAssetCleanupStatus.NotFound;

        if (_isWorldLocked(worldId))
            return WorldAssetCleanupStatus.ActiveWorldLocked;

        if (!_worldStore.TryLoadWorld(worldId, out _))
            return WorldAssetCleanupStatus.NotFound;

        if (!_worldStore.HasWorldAssets(worldId))
            return WorldAssetCleanupStatus.NoAssets;

        return _worldStore.DeleteWorldAssets(worldId)
            ? WorldAssetCleanupStatus.Success
            : WorldAssetCleanupStatus.Failed;
    }

    internal static void ClearContinueStateForDeletedWorld(string worldId)
    {
        var current = AppSettingsStore.LoadContinueState();
        if (!string.Equals(current.LastWorldId, worldId, StringComparison.Ordinal))
            return;

        AppSettingsStore.SaveContinueState(new ContinueState
        {
            LastContinueKind = null,
            LastWorldId = null,
            LastCharacterId = null,
            LastLegacySavePath = current.LastLegacySavePath,
        });
    }

    private static string BuildWorldSummary(WorldManifest manifest, IReadOnlyList<WorldCharacterEntryInfo> characters)
    {
        var lastPlayedCharacter = characters
            .FirstOrDefault(entry => string.Equals(entry.CharacterId, manifest.LastPlayedCharacterId, StringComparison.Ordinal))
            ?.CharacterName
            ?? LocalizationService.T("ui.common.none");
        var lastPlayed = manifest.LastPlayedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            ?? LocalizationService.T("ui.common.none");
        return LocalizationService.T(
            "ui.world_manager.world_summary",
            ("lastCharacter", lastPlayedCharacter),
            ("generator", DescribeGenerator(manifest.Settings.GeneratorId)),
            ("lastPlayed", lastPlayed));
    }

    internal static string BuildCharacterSummary(int turn, int playerZ, DateTimeOffset savedAtUtc, bool isLastPlayedCharacter)
    {
        var timestamp = savedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        return LocalizationService.T(
            "ui.world_manager.character_summary",
            ("marker", isLastPlayedCharacter
                ? LocalizationService.T("ui.world_manager.character_marker.last_played") + " | "
                : string.Empty),
            ("turn", turn),
            ("floor", playerZ),
            ("timestamp", timestamp));
    }

    private static string DescribeGenerator(string generatorId)
    {
        if (MapGenModule.AllGenerators.TryGetValue(generatorId, out var generator))
            return generator.Name;

        return GameLocalizer.HumanizeId(generatorId);
    }
}
