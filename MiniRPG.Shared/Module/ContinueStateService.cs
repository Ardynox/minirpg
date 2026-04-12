using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniRPG.Core.Config;
using MiniRPG.Core.Map;

namespace MiniRPG.Module;

/// <summary>
/// Resolves, persists, and acts on the "continue game" target.
/// Extracted from GameSessionModule to reduce its responsibility surface.
/// </summary>
public sealed class ContinueStateService
{
    private const string ContinueKindWorldCharacter = "world_character";

    private readonly WorldStore _worldStore;
    private readonly Func<IReadOnlyList<WorldEntryInfo>> _listWorlds;
    private readonly Func<string, IReadOnlyList<WorldCharacterEntryInfo>> _listWorldCharacters;
    private readonly Func<IReadOnlyList<SaveSlotInfo>> _listLegacySaveEntries;
    private readonly Func<string, string, SaveLoadStatus> _loadWorldCharacter;
    private readonly Func<string, SaveLoadStatus> _loadGame;
    private readonly Func<string?> _getCurrentWorldId;
    private readonly Func<string?> _getCurrentCharacterId;

    public ContinueStateService(
        WorldStore worldStore,
        Func<IReadOnlyList<WorldEntryInfo>> listWorlds,
        Func<string, IReadOnlyList<WorldCharacterEntryInfo>> listWorldCharacters,
        Func<IReadOnlyList<SaveSlotInfo>> listLegacySaveEntries,
        Func<string, string, SaveLoadStatus> loadWorldCharacter,
        Func<string, SaveLoadStatus> loadGame,
        Func<string?> getCurrentWorldId,
        Func<string?> getCurrentCharacterId)
    {
        _worldStore = worldStore ?? throw new ArgumentNullException(nameof(worldStore));
        _listWorlds = listWorlds ?? throw new ArgumentNullException(nameof(listWorlds));
        _listWorldCharacters = listWorldCharacters ?? throw new ArgumentNullException(nameof(listWorldCharacters));
        _listLegacySaveEntries = listLegacySaveEntries ?? throw new ArgumentNullException(nameof(listLegacySaveEntries));
        _loadWorldCharacter = loadWorldCharacter ?? throw new ArgumentNullException(nameof(loadWorldCharacter));
        _loadGame = loadGame ?? throw new ArgumentNullException(nameof(loadGame));
        _getCurrentWorldId = getCurrentWorldId ?? throw new ArgumentNullException(nameof(getCurrentWorldId));
        _getCurrentCharacterId = getCurrentCharacterId ?? throw new ArgumentNullException(nameof(getCurrentCharacterId));
    }

    public bool TryContinue()
    {
        var target = ResolveContinueTarget();
        return target.Kind switch
        {
            ContinueTargetKind.WorldCharacter when !string.IsNullOrWhiteSpace(target.WorldId)
                && !string.IsNullOrWhiteSpace(target.CharacterId)
                => _loadWorldCharacter(target.WorldId!, target.CharacterId!) == SaveLoadStatus.Success,
            ContinueTargetKind.LegacySave when !string.IsNullOrWhiteSpace(target.SavePath)
                => _loadGame(target.SavePath!) == SaveLoadStatus.Success,
            _ => false,
        };
    }

    public bool TryLoadGame()
    {
        return _loadGame(GameSessionModule.ManualSavePath) == SaveLoadStatus.Success
            || _loadGame(GameSessionModule.QuickSavePath) == SaveLoadStatus.Success;
    }

    public ContinueTarget ResolveContinueTarget()
    {
        if (TryResolveStoredWorldContinueTarget(out var storedWorld))
            return storedWorld;

        var recentWorldCharacter = _listWorlds()
            .SelectMany(static world => world.Characters)
            .OrderByDescending(static character => character.ModifiedAtUtc)
            .ThenByDescending(static character => character.SavedAtUtc)
            .FirstOrDefault();
        if (recentWorldCharacter != null)
            return BuildWorldContinueTarget(recentWorldCharacter);

        if (TryResolveStoredLegacyContinueTarget(out var storedLegacy))
            return storedLegacy;

        var legacySave = _listLegacySaveEntries().FirstOrDefault();
        return legacySave != null
            ? BuildLegacyContinueTarget(legacySave)
            : ContinueTarget.None;
    }

    public string BuildContinueButtonText(ContinueTarget? target = null)
    {
        var resolvedTarget = target ?? ResolveContinueTarget();
        return resolvedTarget.Kind == ContinueTargetKind.None
            ? LocalizationService.T("ui.main_menu.continue")
            : LocalizationService.T("ui.main_menu.continue.with_target", ("target", resolvedTarget.Label));
    }

    public bool HasAnySave() => ResolveContinueTarget().Kind != ContinueTargetKind.None;

    public void SaveContinueStateForWorldCharacter()
    {
        var current = AppSettingsStore.LoadContinueState();
        AppSettingsStore.SaveContinueState(new ContinueState
        {
            LastContinueKind = ContinueKindWorldCharacter,
            LastWorldId = _getCurrentWorldId(),
            LastCharacterId = _getCurrentCharacterId(),
            LastLegacySavePath = current.LastLegacySavePath,
        });
    }

    public void SaveLegacyContinuePath(string path)
    {
        var current = AppSettingsStore.LoadContinueState();
        AppSettingsStore.SaveContinueState(new ContinueState
        {
            LastContinueKind = current.LastContinueKind,
            LastWorldId = current.LastWorldId,
            LastCharacterId = current.LastCharacterId,
            LastLegacySavePath = path,
        });
    }

    internal static string BuildWorldContinueTargetLabel(string characterName, string? worldName)
    {
        if (string.IsNullOrWhiteSpace(worldName))
            return characterName;

        return LocalizationService.T(
            "ui.continue_target.world_character",
            ("character", characterName),
            ("world", worldName));
    }

    internal bool TryResolveStoredWorldContinueTarget(out ContinueTarget target)
    {
        var state = AppSettingsStore.LoadContinueState();
        if (string.Equals(state.LastContinueKind, ContinueKindWorldCharacter, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(state.LastWorldId)
            && !string.IsNullOrWhiteSpace(state.LastCharacterId)
            && _worldStore.TryLoadWorld(state.LastWorldId, out var manifest))
        {
            var character = _listWorldCharacters(manifest.WorldId)
                .FirstOrDefault(entry => string.Equals(entry.CharacterId, state.LastCharacterId, StringComparison.Ordinal));
            if (character != null)
            {
                target = BuildWorldContinueTarget(character);
                return true;
            }
        }

        target = ContinueTarget.None;
        return false;
    }

    internal bool TryResolveStoredLegacyContinueTarget(out ContinueTarget target)
    {
        var state = AppSettingsStore.LoadContinueState();
        if (!string.IsNullOrWhiteSpace(state.LastLegacySavePath)
            && File.Exists(state.LastLegacySavePath))
        {
            var legacy = _listLegacySaveEntries()
                .FirstOrDefault(entry => string.Equals(
                    entry.SourcePath,
                    Path.GetFullPath(state.LastLegacySavePath),
                    StringComparison.OrdinalIgnoreCase));
            if (legacy != null)
            {
                target = BuildLegacyContinueTarget(legacy);
                return true;
            }
        }

        target = ContinueTarget.None;
        return false;
    }

    internal static ContinueTarget BuildWorldContinueTarget(WorldCharacterEntryInfo entry) => new()
    {
        Kind = ContinueTargetKind.WorldCharacter,
        WorldId = entry.WorldId,
        WorldName = entry.WorldName,
        CharacterId = entry.CharacterId,
        CharacterName = entry.CharacterName,
        SavePath = entry.SavePath,
        Label = BuildWorldContinueTargetLabel(entry.CharacterName, entry.WorldName),
    };

    internal static ContinueTarget BuildLegacyContinueTarget(SaveSlotInfo slot) => new()
    {
        Kind = ContinueTargetKind.LegacySave,
        SavePath = slot.SourcePath,
        Label = slot.DisplayName,
    };
}
