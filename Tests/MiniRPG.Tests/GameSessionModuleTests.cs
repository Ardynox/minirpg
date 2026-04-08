using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class GameSessionModuleTests
{
	[Fact]
	public void StartWorldCharacter_UsesWorldSeedAndGenerator()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-world-start");
		try
		{
			var state = new GameState();
			var session = new GameSessionModule(state, new FogOfWarTracker(), root);
			var generatorId = MapGenModule.AllGenerators.Keys.First(id =>
				id != "blank_floor" && id != "room_corridor");
			var manifest = session.CreateWorld("Alpha", new WorldSettings
			{
				Seed = 424242,
				GeneratorId = generatorId,
			});

			var entry = session.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			Assert.True(session.GameStarted);
			Assert.Equal(424242, state.WorldSeed);
			Assert.Equal(generatorId, state.GeneratorId);
			Assert.Equal(manifest.WorldId, session.CurrentWorldId);
			Assert.Equal(entry.CharacterId, session.CurrentCharacterId);
			Assert.True(File.Exists(entry.SavePath));
			Assert.Equal(SaveLoadStatus.Success, SaveModule.TryReadSaveHeader(entry.SavePath, out var header));
			Assert.NotNull(header);
			Assert.Equal(manifest.WorldId, header!.WorldId);
			Assert.Equal(manifest.DisplayName, header.WorldName);
			Assert.Equal(entry.CharacterId, header.CharacterId);
			Assert.Equal("Rook", header.CharacterName);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void LoadWorldCharacter_RestoresWorldAndCharacterContext()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-world-load");
		try
		{
			var sourceSession = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = sourceSession.CreateWorld("Alpha");
			var entry = sourceSession.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			var restoredState = new GameState();
			var restoredSession = new GameSessionModule(restoredState, new FogOfWarTracker(), root);
			var status = restoredSession.LoadWorldCharacter(manifest.WorldId, entry.CharacterId);

			Assert.Equal(SaveLoadStatus.Success, status);
			Assert.True(restoredSession.GameStarted);
			Assert.Equal(manifest.WorldId, restoredSession.CurrentWorldId);
			Assert.Equal(manifest.DisplayName, restoredSession.CurrentWorldName);
			Assert.Equal(entry.CharacterId, restoredSession.CurrentCharacterId);
			Assert.Equal("Rook", restoredSession.CurrentCharacterName);
			Assert.Equal(entry.SavePath, restoredSession.CurrentSavePath);
			Assert.Equal(entry.SavePath, restoredSession.GetPreferredSavePath());
			Assert.Equal(entry.SavePath, restoredSession.GetQuickSavePath());
			Assert.Equal(restoredState.PlayerId, restoredState.Actors[restoredState.PlayerId].Id);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorldSaveData_ReturnsActiveWorldLocked_WhenWorldIsCurrentlyLoaded()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-delete-active-world-save-data");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = session.CreateWorld("Alpha");
			session.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			var status = session.DeleteWorldSaveData(manifest.WorldId);

			Assert.Equal(WorldSaveDataDeletionStatus.ActiveWorldLocked, status);
			Assert.Contains(session.ListWorlds(), entry => entry.WorldId == manifest.WorldId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorldSaveData_ClearsStoredContinueStateAndKeepsWorldShell()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-delete-save-data-continue");
		try
		{
			var sourceSession = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = sourceSession.CreateWorld("Alpha");
			var entry = sourceSession.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));
			var legacyPath = Path.Combine(sourceSession.SaveDirectory, "legacy-slot.json");
			Directory.CreateDirectory(sourceSession.SaveDirectory);
			SaveModule.WriteSaveFile(CreateMinimalSaveFile("legacy-slot"), legacyPath);

			AppSettingsStore.SaveContinueState(new ContinueState
			{
				LastContinueKind = "world_character",
				LastWorldId = manifest.WorldId,
				LastCharacterId = entry.CharacterId,
				LastLegacySavePath = legacyPath,
			});

			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var status = session.DeleteWorldSaveData(manifest.WorldId);
			var continueState = AppSettingsStore.LoadContinueState();
			var continueTarget = session.ResolveContinueTarget();
			var world = Assert.Single(session.ListWorlds());

			Assert.Equal(WorldSaveDataDeletionStatus.Success, status);
			Assert.Null(continueState.LastContinueKind);
			Assert.Null(continueState.LastWorldId);
			Assert.Null(continueState.LastCharacterId);
			Assert.Equal(legacyPath, continueState.LastLegacySavePath);
			Assert.Equal(ContinueTargetKind.LegacySave, continueTarget.Kind);
			Assert.Equal(Path.GetFullPath(legacyPath), continueTarget.SavePath);
			Assert.Equal(manifest.WorldId, world.WorldId);
			Assert.Equal(0, world.CharacterCount);
			Assert.Empty(world.Characters);
			Assert.Null(world.LastPlayedAtUtc);
			Assert.Null(world.LastPlayedCharacterId);
			Assert.False(Directory.Exists(Path.Combine(root, "world_saves", manifest.WorldId)));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorldAssets_ReturnsActiveWorldLocked_WhenWorldIsCurrentlyLoaded()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-delete-active-world-assets");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = session.CreateWorld("Alpha");
			var assetDirectory = Path.Combine(root, "world_assets", manifest.WorldId, "cache");
			Directory.CreateDirectory(assetDirectory);
			File.WriteAllText(Path.Combine(assetDirectory, "chunk-0.bin"), "cached");
			session.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			var status = session.DeleteWorldAssets(manifest.WorldId);

			Assert.Equal(WorldAssetCleanupStatus.ActiveWorldLocked, status);
			Assert.True(Directory.Exists(Path.Combine(root, "world_assets", manifest.WorldId)));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorldAssets_ReturnsNoAssets_WhenWorldHasNoAssets()
	{
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-delete-world-assets-empty");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = session.CreateWorld("Alpha");

			var status = session.DeleteWorldAssets(manifest.WorldId);

			Assert.Equal(WorldAssetCleanupStatus.NoAssets, status);
			Assert.Contains(session.ListWorlds(), entry => entry.WorldId == manifest.WorldId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void Constructor_MigratesLegacyWorldLayoutBeforeResolvingContinueTarget()
	{
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-legacy-world-migration");
		try
		{
			var legacyWorldDirectory = Path.Combine(root, "worlds", "alpha-00000001");
			WriteLegacyWorldManifest(legacyWorldDirectory, "alpha-00000001", "Alpha");
			WriteWorldCharacterSave(
				Path.Combine(legacyWorldDirectory, "characters", "rook-00000001.json"),
				"alpha-00000001",
				"Alpha",
				"rook-00000001",
				"Rook");
			AppSettingsStore.SaveContinueState(new ContinueState
			{
				LastContinueKind = "world_character",
				LastWorldId = "alpha-00000001",
				LastCharacterId = "rook-00000001",
			});

			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var continueTarget = session.ResolveContinueTarget();
			var world = Assert.Single(session.ListWorlds());

			Assert.Equal(ContinueTargetKind.WorldCharacter, continueTarget.Kind);
			Assert.Equal("alpha-00000001", continueTarget.WorldId);
			Assert.Equal("rook-00000001", continueTarget.CharacterId);
			Assert.Equal("alpha-00000001", world.WorldId);
			Assert.False(Directory.Exists(legacyWorldDirectory));
			Assert.True(File.Exists(Path.Combine(root, "world_manifests", "alpha-00000001.json")));
			Assert.True(File.Exists(Path.Combine(root, "world_saves", "alpha-00000001", "rook-00000001.json")));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ResolveContinueTarget_PrefersStoredWorldCharacterOverMoreRecentWorld()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-continue-stored");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var alpha = session.CreateWorld("Alpha");
			var alphaCharacter = session.StartWorldCharacter(alpha.WorldId, CreateOptions("Alpha"));
			File.SetLastWriteTimeUtc(alphaCharacter.SavePath, new DateTime(2026, 4, 7, 8, 0, 0, DateTimeKind.Utc));

			var beta = session.CreateWorld("Beta");
			var betaCharacter = session.StartWorldCharacter(beta.WorldId, CreateOptions("Beta"));
			File.SetLastWriteTimeUtc(betaCharacter.SavePath, new DateTime(2026, 4, 7, 9, 0, 0, DateTimeKind.Utc));

			AppSettingsStore.SaveContinueState(new ContinueState
			{
				LastContinueKind = "world_character",
				LastWorldId = alpha.WorldId,
				LastCharacterId = alphaCharacter.CharacterId,
			});

			var target = new GameSessionModule(new GameState(), new FogOfWarTracker(), root).ResolveContinueTarget();

			Assert.Equal(ContinueTargetKind.WorldCharacter, target.Kind);
			Assert.Equal(alpha.WorldId, target.WorldId);
			Assert.Equal(alphaCharacter.CharacterId, target.CharacterId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ResolveContinueTarget_FallsBackToMostRecentWorldCharacter()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-continue-world");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var alpha = session.CreateWorld("Alpha");
			var alphaCharacter = session.StartWorldCharacter(alpha.WorldId, CreateOptions("Alpha"));
			File.SetLastWriteTimeUtc(alphaCharacter.SavePath, new DateTime(2026, 4, 7, 8, 0, 0, DateTimeKind.Utc));

			var beta = session.CreateWorld("Beta");
			var betaCharacter = session.StartWorldCharacter(beta.WorldId, CreateOptions("Beta"));
			File.SetLastWriteTimeUtc(betaCharacter.SavePath, new DateTime(2026, 4, 7, 9, 0, 0, DateTimeKind.Utc));

			AppSettingsStore.SaveContinueState(new ContinueState
			{
				LastContinueKind = "world_character",
				LastWorldId = "missing-world",
				LastCharacterId = "missing-character",
			});

			var target = new GameSessionModule(new GameState(), new FogOfWarTracker(), root).ResolveContinueTarget();

			Assert.Equal(ContinueTargetKind.WorldCharacter, target.Kind);
			Assert.Equal(beta.WorldId, target.WorldId);
			Assert.Equal(betaCharacter.CharacterId, target.CharacterId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ResolveContinueTarget_FallsBackToLegacySaveWhenNoWorldCharactersExist()
	{
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-continue-legacy");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var legacyPath = Path.Combine(session.SaveDirectory, "legacy-slot.json");
			Directory.CreateDirectory(session.SaveDirectory);
			SaveModule.WriteSaveFile(CreateMinimalSaveFile("legacy-slot"), legacyPath);
			File.SetLastWriteTimeUtc(legacyPath, new DateTime(2026, 4, 7, 9, 0, 0, DateTimeKind.Utc));

			AppSettingsStore.SaveContinueState(new ContinueState
			{
				LastContinueKind = "world_character",
				LastWorldId = "missing-world",
				LastCharacterId = "missing-character",
			});

			var target = new GameSessionModule(new GameState(), new FogOfWarTracker(), root).ResolveContinueTarget();

			Assert.Equal(ContinueTargetKind.LegacySave, target.Kind);
			Assert.Equal(Path.GetFullPath(legacyPath), target.SavePath);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ResolveContinueTarget_FormatsWorldCharacterLabelWithWorldName()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-continue-label");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = session.CreateWorld("Alpha");
			var entry = session.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			var target = session.ResolveContinueTarget();

			Assert.Equal(ContinueTargetKind.WorldCharacter, target.Kind);
			Assert.Equal("Rook @ Alpha", target.Label);
			Assert.Equal("Continue: Rook @ Alpha", session.BuildContinueButtonText(target));
			Assert.Equal(entry.CharacterId, target.CharacterId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DescribeCurrentSessionLabel_UsesWorldCharacterContext()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-label-world");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = session.CreateWorld("Alpha");
			session.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			Assert.Equal("Rook @ Alpha", session.DescribeCurrentSessionLabel());
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DescribeCurrentSessionLabel_UsesScenarioDisplayNameWhenScenarioLoaded()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-label-scenario");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var scenario = session.ListScenarioEntries().First();

			Assert.Equal(SaveLoadStatus.Success, session.LoadPresetScenario(scenario.Id));
			Assert.Equal($"Scenario: {scenario.DisplayName}", session.DescribeCurrentSessionLabel());
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DescribeCurrentSessionLabel_UsesLegacySaveLabelWhenLegacySaveLoaded()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-label-legacy");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var legacyPath = Path.Combine(session.SaveDirectory, "legacy-slot.json");
			Directory.CreateDirectory(session.SaveDirectory);
			SaveModule.WriteSaveFile(CreateMinimalSaveFile("legacy-slot"), legacyPath);

			Assert.Equal(SaveLoadStatus.Success, session.LoadGame(legacyPath));
			Assert.Equal("Legacy Save: legacy-slot", session.DescribeCurrentSessionLabel());
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void LoadLegacySave_DoesNotOverrideWorldCharacterContinuePriority()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-legacy-priority");
		try
		{
			var worldState = new GameState();
			var worldSession = new GameSessionModule(worldState, new FogOfWarTracker(), root);
			var manifest = worldSession.CreateWorld("Alpha");
			var entry = worldSession.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			var legacyPath = Path.Combine(worldSession.SaveDirectory, "legacy-slot.json");
			Directory.CreateDirectory(worldSession.SaveDirectory);
			SaveModule.SaveGame(worldState, legacyPath);
			Assert.Equal(SaveLoadStatus.Success, new GameSessionModule(new GameState(), new FogOfWarTracker(), root).LoadGame(legacyPath));

			var target = new GameSessionModule(new GameState(), new FogOfWarTracker(), root).ResolveContinueTarget();

			Assert.Equal(ContinueTargetKind.WorldCharacter, target.Kind);
			Assert.Equal(manifest.WorldId, target.WorldId);
			Assert.Equal(entry.CharacterId, target.CharacterId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	private static PlayerCreationOptions CreateOptions(string name)
	{
		var defaults = PlayerCreationOptions.CreateDefault();
		return new PlayerCreationOptions
		{
			DisplayName = name,
			RaceId = defaults.RaceId,
			ProfessionId = defaults.ProfessionId,
			AppearanceId = defaults.AppearanceId,
		};
	}

	private static SaveFile CreateMinimalSaveFile(string title) => new()
	{
		Version = SaveModule.CurrentVersion,
		Header = new SaveHeader
		{
			Title = title,
			SavedAtUtc = new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
			Turn = 3,
			PlayerZ = 0,
			GeneratorId = "blank_floor",
			ViewModeId = "single_layer",
		},
		Payload = new SavePayload
		{
			WorldSeed = 123,
			Turn = 3,
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			PlayerId = "player",
			PlayerAppearanceId = null,
			KillCount = 0,
			GeneratorId = "blank_floor",
			ViewModeId = "single_layer",
			Actors = [],
			Quests = [],
			DirtyChunks = [],
			Timeline = new TimelineSnapshot
			{
				Actors = [],
			},
		},
	};

	private static void WriteLegacyWorldManifest(string worldDirectory, string worldId, string displayName)
	{
		Directory.CreateDirectory(worldDirectory);
		File.WriteAllText(
			Path.Combine(worldDirectory, "world.json"),
			JsonSerializer.Serialize(new WorldManifest
			{
				WorldId = worldId,
				DisplayName = displayName,
				Settings = WorldSettings.CreateDefault(),
				CreatedAtUtc = new DateTimeOffset(2026, 4, 7, 8, 0, 0, TimeSpan.Zero),
				LastPlayedAtUtc = new DateTimeOffset(2026, 4, 7, 9, 0, 0, TimeSpan.Zero),
				LastPlayedCharacterId = "rook-00000001",
			}));
	}

	private static void WriteWorldCharacterSave(
		string path,
		string worldId,
		string worldName,
		string characterId,
		string characterName)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, $$"""
		{
		  "version": 5,
		  "header": {
		    "title": "{{characterName}}",
		    "savedAtUtc": "2026-04-07T10:15:00+00:00",
		    "turn": 12,
		    "playerZ": 0,
		    "generatorId": "room_corridor",
		    "viewModeId": "single_layer",
		    "worldId": "{{worldId}}",
		    "worldName": "{{worldName}}",
		    "characterId": "{{characterId}}",
		    "characterName": "{{characterName}}"
		  },
		  "payload": {
		    "unexpected": "shape"
		  }
		}
		""");
	}
}
