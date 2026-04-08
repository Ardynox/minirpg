using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;

namespace MiniRPG.Module;

/// <summary>
/// 游戏会话生命周期管理：新建游戏、世界/角色开局、读取、保存与继续游戏。
/// </summary>
public class GameSessionModule
{
	private const string ContinueKindWorldCharacter = "world_character";

	private static readonly string DefaultUserDataRoot = ResolveUserDataDir();
	private static readonly string DefaultSaveDir = Path.Combine(DefaultUserDataRoot, "save");

	private readonly GameState _state;
	private readonly FogOfWarTracker _fogTracker;
	private readonly WorldStore _worldStore;

	private ActiveSessionKind _activeSessionKind = ActiveSessionKind.None;
	private IViewMode _viewMode = new SingleLayerViewMode();

	private enum ActiveSessionKind
	{
		None,
		WorldCharacter,
		LegacySave,
		PresetScenario,
		BlankEditor,
	}

	public static string QuickSavePath => Path.Combine(DefaultSaveDir, "quicksave.json");
	public static string ManualSavePath => Path.Combine(DefaultSaveDir, "save.json");

	public bool GameStarted { get; private set; }
	public string? CurrentSavePath { get; private set; }
	public string? CurrentPresetScenarioId { get; private set; }
	public string? CurrentWorldId { get; private set; }
	public string? CurrentWorldName { get; private set; }
	public string? CurrentCharacterId { get; private set; }
	public string? CurrentCharacterName { get; private set; }
	public string UserDataRoot => _worldStore.UserDataRoot;
	public string SaveDirectory => _worldStore.SaveDirectory;
	public IViewMode ViewMode => _viewMode;
	public bool RequiresSwitchConfirmation => GameStarted && _activeSessionKind != ActiveSessionKind.None;
	public bool CanSaveAndSwitchCurrentSession => _activeSessionKind == ActiveSessionKind.WorldCharacter;

	public GameSessionModule(GameState state, FogOfWarTracker fogTracker, string? userDataRoot = null)
	{
		_state = state;
		_fogTracker = fogTracker;
		_worldStore = new WorldStore(userDataRoot ?? ResolveUserDataDir());
	}

	public void NewGame(PlayerCreationOptions? options)
	{
		var resolvedOptions = options ?? PlayerCreationOptions.CreateDefault();
		_state.Reset();
		_state.WorldSeed = System.Environment.TickCount;
		_state.PlayerAppearanceId = resolvedOptions.ResolveAppearanceId();
		_state.Weather.ResetForWorld(_state.WorldSeed);
		_fogTracker.Clear();
		InitializeWorld(resolvedOptions);
		TimelineTurnManager.Reset(_state);
		ActorDerivedStateUpdater.SyncAllActorsForSession(_state);
		ResetSessionContext(ActiveSessionKind.None);
		GameStarted = true;
	}

	public void NewGame() => NewGame(PlayerCreationOptions.CreateDefault());

	public WorldManifest CreateWorld(string displayName, WorldSettings? settings = null)
	{
		var resolvedSettings = NormalizeWorldSettings(settings ?? WorldSettings.CreateDefault());
		return _worldStore.CreateWorld(displayName, resolvedSettings);
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

	public IReadOnlyList<SaveSlotInfo> ListScenarioEntries()
	{
		var entries = new List<SaveSlotInfo>();
		foreach (var scenario in PresetScenarioCatalog.List())
		{
			entries.Add(new SaveSlotInfo
			{
				Id = scenario.Id,
				Kind = SaveSlotKind.PresetScenario,
				SourcePath = scenario.TemplatePath,
				FileName = Path.GetFileName(scenario.TemplatePath),
				DisplayName = LocalizationService.TOrFallback(
					scenario.DisplayNameKey,
					GameLocalizer.HumanizeId(scenario.Id)),
				Summary = LocalizationService.TOrFallback(
					scenario.DescriptionKey,
					GameLocalizer.HumanizeId(scenario.Id)),
				ModifiedAt = PresetScenarioCatalog.GetModifiedAt(scenario),
				Writable = false,
			});
		}

		return entries;
	}

	public IReadOnlyList<SaveSlotInfo> ListLegacySaveEntries()
	{
		Directory.CreateDirectory(SaveDirectory);

		var entries = new List<SaveSlotInfo>();
		foreach (var path in Directory.EnumerateFiles(SaveDirectory, "*.json", SearchOption.TopDirectoryOnly))
		{
			var normalizedPath = Path.GetFullPath(path);
			var fileName = Path.GetFileName(normalizedPath);
			var modifiedAt = File.GetLastWriteTime(normalizedPath);

			var headerStatus = SaveModule.TryReadSaveHeader(normalizedPath, out var header);
			if (headerStatus == SaveLoadStatus.Success && !string.IsNullOrWhiteSpace(header?.WorldId))
				continue;

			entries.Add(new SaveSlotInfo
			{
				Id = Path.GetFileNameWithoutExtension(fileName),
				Kind = SaveSlotKind.UserSave,
				SourcePath = normalizedPath,
				FileName = fileName,
				DisplayName = DescribeSavePath(normalizedPath),
				Summary = BuildSummary(normalizedPath, modifiedAt),
				ModifiedAt = modifiedAt,
				Writable = true,
			});
		}

		entries.Sort(static (a, b) =>
		{
			var modifiedCompare = b.ModifiedAt.CompareTo(a.ModifiedAt);
			if (modifiedCompare != 0)
				return modifiedCompare;

			return string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase);
		});

		return entries;
	}

	public IReadOnlyList<SaveSlotInfo> ListSaveSlots() => ListLegacySaveEntries();

	public IReadOnlyList<SaveSlotInfo> ListBrowserEntries()
	{
		var entries = new List<SaveSlotInfo>();
		entries.AddRange(ListScenarioEntries());
		entries.AddRange(ListLegacySaveEntries());
		return entries;
	}

	public WorldCharacterEntryInfo StartWorldCharacter(string worldId, PlayerCreationOptions options)
	{
		if (!_worldStore.TryLoadWorld(worldId, out var manifest))
			throw new InvalidOperationException($"Unknown world id: {worldId}");

		var resolvedOptions = options ?? PlayerCreationOptions.CreateDefault();
		var characterName = resolvedOptions.ResolveDisplayName(PlayerCreationOptions.CreateDefault().DisplayName);
		var characterId = _worldStore.CreateCharacterId(characterName);
		var canonicalPath = _worldStore.GetCharacterSavePath(worldId, characterId);

		_state.Reset();
		_state.WorldSeed = NormalizeWorldSettings(manifest.Settings).Seed;
		_state.GeneratorId = ResolveGeneratorId(manifest.Settings.GeneratorId);
		_state.PlayerAppearanceId = resolvedOptions.ResolveAppearanceId();
		_state.Weather.ResetForWorld(_state.WorldSeed);
		_fogTracker.Clear();
		InitializeWorld(resolvedOptions);
		TimelineTurnManager.Reset(_state);
		ActorDerivedStateUpdater.SyncAllActorsForSession(_state);
		SetWorldCharacterContext(manifest.WorldId, manifest.DisplayName, characterId, characterName, canonicalPath);
		_activeSessionKind = ActiveSessionKind.WorldCharacter;
		GameStarted = true;
		SaveGame(canonicalPath);

		return new WorldCharacterEntryInfo
		{
			WorldId = manifest.WorldId,
			WorldName = manifest.DisplayName,
			CharacterId = characterId,
			CharacterName = characterName,
			SavePath = canonicalPath,
			Summary = BuildCharacterSummary(_state.Turn, _state.PlayerZ, DateTimeOffset.UtcNow, isLastPlayedCharacter: true),
			SavedAtUtc = DateTimeOffset.UtcNow,
			ModifiedAtUtc = DateTimeOffset.UtcNow,
			Turn = _state.Turn,
			PlayerZ = _state.PlayerZ,
			GeneratorId = _state.GeneratorId,
			IsLastPlayedCharacter = true,
		};
	}

	public void NewBlankEditorMap()
	{
		_state.Reset();
		_state.WorldSeed = System.Environment.TickCount;
		_state.GeneratorId = "blank_floor";
		_state.Weather.ResetForWorld(_state.WorldSeed);
		_fogTracker.Clear();
		InitializeWorld();
		TimelineTurnManager.Reset(_state);
		ActorDerivedStateUpdater.SyncAllActorsForSession(_state);
		ResetSessionContext(ActiveSessionKind.BlankEditor);
		GameStarted = true;
	}

	public SaveLoadStatus LoadGame(string path)
	{
		_ = SaveModule.TryReadSaveHeader(path, out var header);
		var status = SaveModule.LoadGame(_state, path);
		if (status != SaveLoadStatus.Success)
			return status;

		return FinalizeLoadedGame(path, presetScenarioId: null, header);
	}

	public SaveLoadStatus LoadWorldCharacter(string worldId, string characterId)
	{
		if (!_worldStore.TryGetCharacterSavePath(worldId, characterId, out var path))
			return SaveLoadStatus.NotFound;

		_ = SaveModule.TryReadSaveHeader(path, out var header);
		var status = SaveModule.LoadGame(_state, path);
		if (status != SaveLoadStatus.Success)
			return status;

		_worldStore.TryLoadWorld(worldId, out var manifest);
		return FinalizeLoadedGame(path, presetScenarioId: null, header, manifest, characterId);
	}

	public SaveLoadStatus LoadPresetScenario(string id)
	{
		if (!PresetScenarioCatalog.TryGet(id, out var scenario))
			return SaveLoadStatus.NotFound;

		var saveFile = SaveModule.DeserializeSaveFile(PresetScenarioCatalog.ReadText(scenario.TemplatePath));
		if (saveFile == null)
			return SaveLoadStatus.Incompatible;

		SaveModule.ApplySnapshot(_state, saveFile);
		return FinalizeLoadedGame(currentSavePath: null, presetScenarioId: scenario.Id, saveFile.Header);
	}

	public bool TryContinue()
	{
		var target = ResolveContinueTarget();
		return target.Kind switch
		{
			ContinueTargetKind.WorldCharacter when !string.IsNullOrWhiteSpace(target.WorldId)
				&& !string.IsNullOrWhiteSpace(target.CharacterId)
				=> LoadWorldCharacter(target.WorldId!, target.CharacterId!) == SaveLoadStatus.Success,
			ContinueTargetKind.LegacySave when !string.IsNullOrWhiteSpace(target.SavePath)
				=> LoadGame(target.SavePath!) == SaveLoadStatus.Success,
			_ => false,
		};
	}

	public bool TryLoadGame()
	{
		return LoadGame(ManualSavePath) == SaveLoadStatus.Success
			|| LoadGame(QuickSavePath) == SaveLoadStatus.Success;
	}

	public ContinueTarget ResolveContinueTarget()
	{
		if (TryResolveStoredWorldContinueTarget(out var storedWorld))
			return storedWorld;

		var recentWorldCharacter = ListWorlds()
			.SelectMany(static world => world.Characters)
			.OrderByDescending(static character => character.ModifiedAtUtc)
			.ThenByDescending(static character => character.SavedAtUtc)
			.FirstOrDefault();
		if (recentWorldCharacter != null)
			return BuildWorldContinueTarget(recentWorldCharacter);

		if (TryResolveStoredLegacyContinueTarget(out var storedLegacy))
			return storedLegacy;

		var legacySave = ListLegacySaveEntries().FirstOrDefault();
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

	public string DescribeCurrentSessionLabel()
	{
		return _activeSessionKind switch
		{
			ActiveSessionKind.WorldCharacter => BuildWorldContinueTargetLabel(
				CurrentCharacterName ?? LocalizationService.T("ui.common.none"),
				CurrentWorldName),
			ActiveSessionKind.LegacySave => LocalizationService.T(
				"ui.session_label.legacy_save",
				("name", DescribeSavePath(CurrentSavePath ?? ManualSavePath))),
			ActiveSessionKind.PresetScenario => LocalizationService.T(
				"ui.session_label.scenario",
				("name", ResolvePresetScenarioDisplayName(CurrentPresetScenarioId))),
			ActiveSessionKind.BlankEditor => LocalizationService.T("ui.session_label.map_editor"),
			_ => LocalizationService.T("ui.session_label.current_session"),
		};
	}

	public void SaveGame(string path)
	{
		var resolvedPath = ResolveSavePath(path);
		SaveModule.SaveGame(_state, resolvedPath, BuildSaveHeaderContext());
		CurrentSavePath = resolvedPath;

		switch (_activeSessionKind)
		{
			case ActiveSessionKind.WorldCharacter when !string.IsNullOrWhiteSpace(CurrentWorldId)
				&& !string.IsNullOrWhiteSpace(CurrentCharacterId):
				_worldStore.UpdateWorldLastPlayed(CurrentWorldId!, DateTimeOffset.UtcNow, CurrentCharacterId);
				SaveContinueStateForWorldCharacter();
				break;

			case ActiveSessionKind.LegacySave:
			case ActiveSessionKind.None:
				_activeSessionKind = ActiveSessionKind.LegacySave;
				SaveLegacyContinuePath(resolvedPath);
				break;
		}
	}

	public string BuildPresetScenarioJson(string id)
	{
		var normalizedId = NormalizePresetScenarioId(id);
		var saveFile = SaveModule.BuildSnapshot(_state);
		saveFile.Header.Title = normalizedId;
		saveFile.Header.SavedAtUtc = PresetScenarioCatalog.ExportTimestamp;
		return SaveModule.SerializeSaveFile(saveFile);
	}

	public string ExportPresetScenario(string id)
	{
		var normalizedId = NormalizePresetScenarioId(id);
		var json = BuildPresetScenarioJson(normalizedId);
		var exportPath = PresetScenarioCatalog.ResolveExportPath(normalizedId);
		var directory = Path.GetDirectoryName(exportPath);
		if (!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);
		File.WriteAllText(exportPath, json);
		return exportPath;
	}

	public string BuildNamedSavePath(string rawName)
	{
		Directory.CreateDirectory(SaveDirectory);
		var trimmed = rawName.Trim();
		if (trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
			trimmed = trimmed[..^5];

		var builder = new StringBuilder(trimmed.Length);
		foreach (var ch in trimmed)
		{
			if (char.IsWhiteSpace(ch))
			{
				builder.Append('_');
				continue;
			}

			builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
		}

		var safeName = builder.ToString().Trim('_');
		if (string.IsNullOrWhiteSpace(safeName))
			safeName = "editor_map";

		return Path.Combine(SaveDirectory, safeName + ".json");
	}

	public bool HasAnySave() => ResolveContinueTarget().Kind != ContinueTargetKind.None;

	public void ProcessWorldStreaming()
	{
		_state.World?.Chunks.ProcessPendingLoads(_state.Turn);
	}

	public string GetPreferredSavePath()
	{
		if (_activeSessionKind == ActiveSessionKind.WorldCharacter && !string.IsNullOrWhiteSpace(CurrentSavePath))
			return CurrentSavePath!;

		return CurrentSavePath ?? ManualSavePath;
	}

	public string GetQuickSavePath() =>
		_activeSessionKind == ActiveSessionKind.WorldCharacter
			? GetPreferredSavePath()
			: QuickSavePath;

	public string DescribeSavePath(string path)
	{
		if (_activeSessionKind == ActiveSessionKind.WorldCharacter
			&& string.Equals(CurrentSavePath, path, StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrWhiteSpace(CurrentCharacterName))
		{
			return CurrentCharacterName!;
		}

		if (SaveModule.TryReadSaveHeader(path, out var header) == SaveLoadStatus.Success)
		{
			if (!string.IsNullOrWhiteSpace(header?.CharacterName))
				return header.CharacterName!;

			if (!string.IsNullOrWhiteSpace(header?.Title))
				return header.Title;
		}

		var fileName = Path.GetFileName(path);
		return fileName switch
		{
			"quicksave.json" => LocalizationService.T("ui.save_slot.quicksave"),
			"save.json" => LocalizationService.T("ui.save_slot.manual"),
			_ => Path.GetFileNameWithoutExtension(path),
		};
	}

	public bool TryUseStairs(out string? logMessage)
	{
		logMessage = null;
		var px = _state.PlayerX;
		var py = _state.PlayerY;
		var dirs = new (int Dx, int Dy)[] { (0, 0), (0, -1), (0, 1), (-1, 0), (1, 0) };

		foreach (var (dx, dy) in dirs)
		{
			if (MapModule.HasFixture(_state, px + dx, py + dy, Entities.StairDown))
			{
				ChangeFloor(goDown: true);
				logMessage = LocalizationService.T("log.floor.enter_down", ("floor", _state.PlayerZ));
				return true;
			}

			if (MapModule.HasFixture(_state, px + dx, py + dy, Entities.StairUp))
			{
				ChangeFloor(goDown: false);
				logMessage = LocalizationService.T("log.floor.enter_up", ("floor", _state.PlayerZ));
				return true;
			}
		}

		return false;
	}

	public void ChangeFloor(bool goDown)
	{
		if (goDown) MapModule.GoDown(_state);
		else MapModule.GoUp(_state);
		var center = new WorldCoord(_state.PlayerX, _state.PlayerY, _state.PlayerZ);
		_state.World?.Chunks.UpdateLoadedChunks(center, _state.Turn);
		MapModule.PlacePlayerAtFixture(_state, goDown ? Entities.StairUp : Entities.StairDown);
	}

	private void InitializeWorld(PlayerCreationOptions? options = null)
	{
		MapGenModule.InitializeWorld(_state);
		MapGenModule.FindSpawnPoint(_state);
		MapGenModule.SpawnPlayer(_state, options);
		SyncViewMode();
	}

	private bool TryResolveStoredWorldContinueTarget(out ContinueTarget target)
	{
		var state = AppSettingsStore.LoadContinueState();
		if (string.Equals(state.LastContinueKind, ContinueKindWorldCharacter, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(state.LastWorldId)
			&& !string.IsNullOrWhiteSpace(state.LastCharacterId)
			&& _worldStore.TryLoadWorld(state.LastWorldId, out var manifest))
		{
			var character = ListWorldCharacters(manifest.WorldId)
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

	private bool TryResolveStoredLegacyContinueTarget(out ContinueTarget target)
	{
		var state = AppSettingsStore.LoadContinueState();
		if (!string.IsNullOrWhiteSpace(state.LastLegacySavePath)
			&& File.Exists(state.LastLegacySavePath))
		{
			var legacy = ListLegacySaveEntries()
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

	private ContinueTarget BuildWorldContinueTarget(WorldCharacterEntryInfo entry) => new()
	{
		Kind = ContinueTargetKind.WorldCharacter,
		WorldId = entry.WorldId,
		WorldName = entry.WorldName,
		CharacterId = entry.CharacterId,
		CharacterName = entry.CharacterName,
		SavePath = entry.SavePath,
		Label = BuildWorldContinueTargetLabel(entry.CharacterName, entry.WorldName),
	};

	private static ContinueTarget BuildLegacyContinueTarget(SaveSlotInfo slot) => new()
	{
		Kind = ContinueTargetKind.LegacySave,
		SavePath = slot.SourcePath,
		Label = slot.DisplayName,
	};

	private string BuildSummary(string path, DateTime modifiedAt)
	{
		var status = SaveModule.TryReadSaveHeader(path, out var header);
		if (status != SaveLoadStatus.Success || header == null)
		{
			var fallbackTimestamp = modifiedAt.ToString("yyyy-MM-dd HH:mm");
			return LocalizationService.T("ui.save_browser.summary.incompatible", ("timestamp", fallbackTimestamp));
		}

		var timestamp = header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

		return LocalizationService.T("ui.save_browser.summary.standard",
			("turn", header.Turn),
			("floor", header.PlayerZ),
			("timestamp", timestamp));
	}

	private string BuildWorldSummary(WorldManifest manifest, IReadOnlyList<WorldCharacterEntryInfo> characters)
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

	private string BuildCharacterSummary(int turn, int playerZ, DateTimeOffset savedAtUtc, bool isLastPlayedCharacter)
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

	private SaveLoadStatus FinalizeLoadedGame(
		string? currentSavePath,
		string? presetScenarioId,
		SaveHeader? header,
		WorldManifest? knownWorld = null,
		string? fallbackCharacterId = null)
	{
		_state.Weather ??= WeatherState.CreateDefault(_state.WorldSeed);
		MapGenModule.InitializeWorld(_state);
		EnsurePlayerActor();
		ActorModule.InitializeMissingHomePositions(_state);
		_state.World?.RebuildLoadedActorIndex(_state.Actors);
		SyncViewMode();
		GameLocalizer.RelocalizeGameState(_state);
		if (_state.Timeline.Actors.Count == 0)
			TimelineTurnManager.Reset(_state);
		else
			TimelineTurnManager.SyncActors(_state);
		ActorDerivedStateUpdater.SyncAllActorsForSession(_state);

		CurrentSavePath = currentSavePath;
		CurrentPresetScenarioId = presetScenarioId;
		if (!string.IsNullOrWhiteSpace(presetScenarioId))
		{
			ClearWorldCharacterContext();
			_activeSessionKind = ActiveSessionKind.PresetScenario;
		}
		else if (!string.IsNullOrWhiteSpace(header?.WorldId))
		{
			var worldId = header.WorldId!;
			if (knownWorld == null)
				_worldStore.TryLoadWorld(worldId, out knownWorld);

			var worldName = header.WorldName ?? knownWorld?.DisplayName ?? worldId;
			var characterId = header.CharacterId
				?? fallbackCharacterId
				?? Path.GetFileNameWithoutExtension(currentSavePath ?? "character");
			var characterName = header.CharacterName
				?? (!string.IsNullOrWhiteSpace(header.Title) ? header.Title : characterId);
			var resolvedPath = currentSavePath ?? _worldStore.GetCharacterSavePath(worldId, characterId);
			SetWorldCharacterContext(worldId, worldName, characterId, characterName, resolvedPath);
			_activeSessionKind = ActiveSessionKind.WorldCharacter;
			_worldStore.UpdateWorldLastPlayed(worldId, DateTimeOffset.UtcNow, characterId);
			SaveContinueStateForWorldCharacter();
		}
		else if (!string.IsNullOrWhiteSpace(currentSavePath))
		{
			ClearWorldCharacterContext();
			_activeSessionKind = ActiveSessionKind.LegacySave;
			SaveLegacyContinuePath(currentSavePath);
		}
		else
		{
			ResetSessionContext(ActiveSessionKind.None);
		}

		GameStarted = true;
		return SaveLoadStatus.Success;
	}

	private void SyncViewMode()
	{
		_viewMode = _state.ViewModeId switch
		{
			"multi_layer" => new MultiLayerViewMode(),
			_ => new SingleLayerViewMode(),
		};
	}

	private SaveHeaderContext? BuildSaveHeaderContext()
	{
		if (_activeSessionKind != ActiveSessionKind.WorldCharacter
			|| string.IsNullOrWhiteSpace(CurrentWorldId)
			|| string.IsNullOrWhiteSpace(CurrentWorldName)
			|| string.IsNullOrWhiteSpace(CurrentCharacterId)
			|| string.IsNullOrWhiteSpace(CurrentCharacterName))
		{
			return null;
		}

		return new SaveHeaderContext
		{
			WorldId = CurrentWorldId,
			WorldName = CurrentWorldName,
			CharacterId = CurrentCharacterId,
			CharacterName = CurrentCharacterName,
		};
	}

	private string ResolveSavePath(string requestedPath)
	{
		if (_activeSessionKind == ActiveSessionKind.WorldCharacter
			&& !string.IsNullOrWhiteSpace(CurrentWorldId)
			&& !string.IsNullOrWhiteSpace(CurrentCharacterId))
		{
			return _worldStore.GetCharacterSavePath(CurrentWorldId!, CurrentCharacterId!);
		}

		return requestedPath;
	}

	private void SaveContinueStateForWorldCharacter()
	{
		var current = AppSettingsStore.LoadContinueState();
		AppSettingsStore.SaveContinueState(new ContinueState
		{
			LastContinueKind = ContinueKindWorldCharacter,
			LastWorldId = CurrentWorldId,
			LastCharacterId = CurrentCharacterId,
			LastLegacySavePath = current.LastLegacySavePath,
		});
	}

	private void SaveLegacyContinuePath(string path)
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

	private void SetWorldCharacterContext(
		string worldId,
		string worldName,
		string characterId,
		string characterName,
		string savePath)
	{
		CurrentWorldId = worldId;
		CurrentWorldName = worldName;
		CurrentCharacterId = characterId;
		CurrentCharacterName = characterName;
		CurrentSavePath = savePath;
		CurrentPresetScenarioId = null;
	}

	private void ResetSessionContext(ActiveSessionKind kind)
	{
		_activeSessionKind = kind;
		CurrentSavePath = null;
		CurrentPresetScenarioId = null;
		ClearWorldCharacterContext();
	}

	private void ClearWorldCharacterContext()
	{
		CurrentWorldId = null;
		CurrentWorldName = null;
		CurrentCharacterId = null;
		CurrentCharacterName = null;
	}

	private static WorldSettings NormalizeWorldSettings(WorldSettings settings)
	{
		var normalized = settings.Clone();
		if (normalized.Seed == 0)
			normalized.Seed = System.Environment.TickCount;

		normalized.GeneratorId = ResolveGeneratorId(normalized.GeneratorId);
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

	private static string ResolveGeneratorId(string? generatorId)
	{
		var normalized = generatorId?.Trim();
		if (string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "blank_floor", StringComparison.Ordinal))
			return "room_corridor";

		return normalized;
	}

	private static string BuildWorldContinueTargetLabel(string characterName, string? worldName)
	{
		if (string.IsNullOrWhiteSpace(worldName))
			return characterName;

		return LocalizationService.T(
			"ui.continue_target.world_character",
			("character", characterName),
			("world", worldName));
	}

	private static string ResolvePresetScenarioDisplayName(string? presetScenarioId)
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

	private static string DescribeGenerator(string generatorId)
	{
		if (MapGenModule.AllGenerators.TryGetValue(generatorId, out var generator))
			return generator.Name;

		return GameLocalizer.HumanizeId(generatorId);
	}

	private static string NormalizePresetScenarioId(string id)
	{
		var normalized = (id ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
			throw new ArgumentException("Preset scenario id cannot be empty.", nameof(id));

		return normalized;
	}

	private static string ResolveUserDataDir()
	{
		const string appName = "MiniRPG";

		if (OperatingSystem.IsWindows())
		{
			var root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
			return Path.Combine(root, "Godot", "app_userdata", appName);
		}

		if (OperatingSystem.IsMacOS())
		{
			var root = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
			return Path.Combine(root, "Godot", "app_userdata", appName);
		}

		var xdgData = System.Environment.GetEnvironmentVariable("XDG_DATA_HOME");
		var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
		var rootDir = string.IsNullOrWhiteSpace(xdgData)
			? Path.Combine(home, ".local", "share")
			: xdgData;
		return Path.Combine(rootDir, "godot", "app_userdata", appName);
	}

	private void EnsurePlayerActor()
	{
		if (_state.Actors.ContainsKey(_state.PlayerId))
			return;

		foreach (var actor in _state.Actors.Values)
		{
			if (actor.Faction != Factions.Player)
				continue;

			LogWarning($"EnsurePlayerActor: PlayerId '{_state.PlayerId}' missing, recovered existing player Actor '{actor.Id}'");
			_state.PlayerId = actor.Id;
			_state.PlayerX = actor.X;
			_state.PlayerY = actor.Y;
			_state.PlayerZ = actor.Z;
			return;
		}

		LogWarning("EnsurePlayerActor: no player Actor found at all, creating minimal fallback");
		var player = ActorTemplates.Spawn("player", _state.PlayerId);
		player.X = _state.PlayerX;
		player.Y = _state.PlayerY;
		player.Z = _state.PlayerZ;
		ActorModule.Add(_state, player);
	}

	private static void LogWarning(string message)
	{
		Debug.WriteLine(message);
		Console.Error.WriteLine(message);
	}
}

public sealed class SaveSlotInfo
{
	public string Id { get; init; } = string.Empty;
	public SaveSlotKind Kind { get; init; }
	public string SourcePath { get; init; } = string.Empty;
	public string FileName { get; init; } = string.Empty;
	public string DisplayName { get; init; } = string.Empty;
	public string Summary { get; init; } = string.Empty;
	public DateTime ModifiedAt { get; init; }
	public bool Writable { get; init; }
}

public enum SaveSlotKind
{
	PresetScenario,
	UserSave,
}
