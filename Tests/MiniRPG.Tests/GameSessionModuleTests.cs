using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.World.Generators;
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
				MonsterDensityPercent = 180,
				NestIntensityPercent = 75,
			});

			var entry = session.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));
			var generationSettings = WorldGenerationSettingsRegistry.Resolve(state.WorldSeed, state.GeneratorId);

			Assert.True(session.GameStarted);
			Assert.Equal(424242, state.WorldSeed);
			Assert.Equal(generatorId, state.GeneratorId);
			Assert.Equal(180, generationSettings.MonsterDensityPercent);
			Assert.Equal(75, generationSettings.NestIntensityPercent);
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
			var manifest = sourceSession.CreateWorld("Alpha", new WorldSettings
			{
				Seed = 112233,
				MonsterDensityPercent = 220,
				NestIntensityPercent = 40,
			});
			var entry = sourceSession.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			var restoredState = new GameState();
			var restoredSession = new GameSessionModule(restoredState, new FogOfWarTracker(), root);
			var status = restoredSession.LoadWorldCharacter(manifest.WorldId, entry.CharacterId);
			var generationSettings = WorldGenerationSettingsRegistry.Resolve(restoredState.WorldSeed, restoredState.GeneratorId);

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
			Assert.Equal(220, generationSettings.MonsterDensityPercent);
			Assert.Equal(40, generationSettings.NestIntensityPercent);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void PrepareLoadGame_WhenPlayerIdMissingWithSingleCandidate_RequiresExplicitRecoveryCommit()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-load-recovery-single");
		try
		{
			var sourceState = new GameState();
			var sourceSession = new GameSessionModule(sourceState, new FogOfWarTracker(), root);
			sourceSession.NewGame(CreateOptions("Rook"));
			var path = WritePreparedLoadSave(root, "single-candidate.json", sourceState, saveFile =>
			{
				saveFile.Payload.PlayerId = "missing-player";
			});

			var restoredState = new GameState();
			var restoredSession = new GameSessionModule(restoredState, new FogOfWarTracker(), root);
			var prepared = restoredSession.PrepareLoadGame(path);

			Assert.Equal(SaveLoadStatus.Success, prepared.Status);
			var recovery = Assert.IsType<PreparedLoadRecovery>(prepared.Recovery);
			var candidate = Assert.Single(recovery.Candidates);
			Assert.Equal("missing-player", recovery.MissingPlayerId);
			Assert.Equal(SaveLoadStatus.Incompatible, restoredSession.CommitPreparedLoad(prepared));

			var status = restoredSession.CommitPreparedLoad(prepared, candidate.ActorId);

			Assert.Equal(SaveLoadStatus.Success, status);
			Assert.True(restoredSession.GameStarted);
			Assert.Equal(candidate.ActorId, restoredState.PlayerId);
			Assert.True(restoredState.Actors.ContainsKey(candidate.ActorId));
			Assert.Equal(candidate.X, restoredState.PlayerX);
			Assert.Equal(candidate.Y, restoredState.PlayerY);
			Assert.Equal(candidate.Z, restoredState.PlayerZ);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void PrepareLoadGame_WhenPlayerIdMissingWithMultipleCandidates_ExposesCandidatesAndRequiresSelectedId()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-load-recovery-multi");
		try
		{
			var sourceState = new GameState();
			var sourceSession = new GameSessionModule(sourceState, new FogOfWarTracker(), root);
			sourceSession.NewGame(CreateOptions("Rook"));
			var path = WritePreparedLoadSave(root, "multi-candidate.json", sourceState, saveFile =>
			{
				saveFile.Payload.PlayerId = "missing-player";
				AddPlayerRecoveryCandidate(saveFile, sourceState.PlayerId, "player_twin", "Rook Twin", 8, 7, 0);
			});

			var restoredState = new GameState();
			var restoredSession = new GameSessionModule(restoredState, new FogOfWarTracker(), root);
			var prepared = restoredSession.PrepareLoadGame(path);

			Assert.Equal(SaveLoadStatus.Success, prepared.Status);
			var recovery = Assert.IsType<PreparedLoadRecovery>(prepared.Recovery);
			Assert.Equal(2, recovery.Candidates.Count);
			Assert.Contains(recovery.Candidates, candidate => candidate.ActorId == sourceState.PlayerId);
			var selectedCandidate = Assert.Single(recovery.Candidates, candidate => candidate.ActorId == "player_twin");

			Assert.Equal(SaveLoadStatus.Incompatible, restoredSession.CommitPreparedLoad(prepared, "unknown-candidate"));
			Assert.Empty(restoredState.Actors);

			var status = restoredSession.CommitPreparedLoad(prepared, selectedCandidate.ActorId);

			Assert.Equal(SaveLoadStatus.Success, status);
			Assert.Equal(selectedCandidate.ActorId, restoredState.PlayerId);
			Assert.True(restoredState.Actors.ContainsKey(selectedCandidate.ActorId));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void PrepareLoadGame_WhenPlayerIdMissingAndNoCandidates_ReturnsIncompatible_AndLegacyLoadDoesNotSpawnFallback()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-load-recovery-none");
		try
		{
			var sourceState = new GameState();
			var sourceSession = new GameSessionModule(sourceState, new FogOfWarTracker(), root);
			sourceSession.NewGame(CreateOptions("Rook"));
			var path = WritePreparedLoadSave(root, "no-candidate.json", sourceState, saveFile =>
			{
				saveFile.Payload.PlayerId = "missing-player";
				foreach (var actor in saveFile.Payload.Actors)
				{
					if (actor.Faction == Factions.Player)
						actor.Faction = "neutral";
				}
			});

			var previewSession = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var prepared = previewSession.PrepareLoadGame(path);

			Assert.Equal(SaveLoadStatus.Incompatible, prepared.Status);
			Assert.Null(prepared.Recovery);

			var legacyState = new GameState();
			var legacySession = new GameSessionModule(legacyState, new FogOfWarTracker(), root);
			var status = legacySession.LoadGame(path);

			Assert.Equal(SaveLoadStatus.Incompatible, status);
			Assert.False(legacySession.GameStarted);
			Assert.Empty(legacyState.Actors);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void PrepareLoadGame_DoesNotMutateCurrentState_WhenRecoveryIsAbandoned()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-load-recovery-abandon");
		try
		{
			var currentState = new GameState();
			var currentSession = new GameSessionModule(currentState, new FogOfWarTracker(), root);
			currentSession.NewGame(CreateOptions("Current"));
			var originalPlayerId = currentState.PlayerId;
			var originalTurn = currentState.Turn;
			var originalActorCount = currentState.Actors.Count;

			var badSavePath = WritePreparedLoadSave(root, "abandon-candidate.json", currentState, saveFile =>
			{
				saveFile.Payload.PlayerId = "missing-player";
			});

			var prepared = currentSession.PrepareLoadGame(badSavePath);

			Assert.Equal(SaveLoadStatus.Success, prepared.Status);
			Assert.NotNull(prepared.Recovery);
			Assert.Equal(originalPlayerId, currentState.PlayerId);
			Assert.Equal(originalTurn, currentState.Turn);
			Assert.Equal(originalActorCount, currentState.Actors.Count);
			Assert.True(currentSession.GameStarted);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void LoadGame_StrictWrapper_DoesNotSilentlyRebindPlayerId()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-load-recovery-strict");
		try
		{
			var sourceState = new GameState();
			var sourceSession = new GameSessionModule(sourceState, new FogOfWarTracker(), root);
			sourceSession.NewGame(CreateOptions("Rook"));
			var path = WritePreparedLoadSave(root, "strict-single-candidate.json", sourceState, saveFile =>
			{
				saveFile.Payload.PlayerId = "missing-player";
			});

			var currentState = new GameState();
			var currentSession = new GameSessionModule(currentState, new FogOfWarTracker(), root);
			currentSession.NewGame(CreateOptions("Current"));
			var originalPlayerId = currentState.PlayerId;
			var originalActorCount = currentState.Actors.Count;

			var status = currentSession.LoadGame(path);

			Assert.Equal(SaveLoadStatus.Incompatible, status);
			Assert.Equal(originalPlayerId, currentState.PlayerId);
			Assert.Equal(originalActorCount, currentState.Actors.Count);
			Assert.True(currentState.Actors.ContainsKey(originalPlayerId));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorld_ReturnsActiveWorldLocked_WhenWorldIsCurrentlyLoaded()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-delete-active-world");
		try
		{
			var session = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = session.CreateWorld("Alpha");
			session.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));

			var status = session.DeleteWorld(manifest.WorldId);

			Assert.Equal(WorldDeletionStatus.ActiveWorldLocked, status);
			Assert.Contains(session.ListWorlds(), entry => entry.WorldId == manifest.WorldId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorld_RemovesWorldAndClearsStoredContinueState()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-delete-world-continue");
		try
		{
			var sourceSession = new GameSessionModule(new GameState(), new FogOfWarTracker(), root);
			var manifest = sourceSession.CreateWorld("Alpha");
			var entry = sourceSession.StartWorldCharacter(manifest.WorldId, CreateOptions("Rook"));
			var assetDirectory = Path.Combine(root, "world_assets", manifest.WorldId, "cache");
			Directory.CreateDirectory(assetDirectory);
			File.WriteAllText(Path.Combine(assetDirectory, "chunk-0.bin"), "cached");
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
			var status = session.DeleteWorld(manifest.WorldId);
			var continueState = AppSettingsStore.LoadContinueState();
			var continueTarget = session.ResolveContinueTarget();

			Assert.Equal(WorldDeletionStatus.Success, status);
			Assert.Null(continueState.LastContinueKind);
			Assert.Null(continueState.LastWorldId);
			Assert.Null(continueState.LastCharacterId);
			Assert.Equal(legacyPath, continueState.LastLegacySavePath);
			Assert.Equal(ContinueTargetKind.LegacySave, continueTarget.Kind);
			Assert.Equal(Path.GetFullPath(legacyPath), continueTarget.SavePath);
			Assert.Empty(session.ListWorlds());
			Assert.False(Directory.Exists(Path.Combine(root, "world_saves", manifest.WorldId)));
			Assert.False(Directory.Exists(Path.Combine(root, "world_assets", manifest.WorldId)));
			Assert.False(File.Exists(Path.Combine(root, "world_manifests", $"{manifest.WorldId}.json")));
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
			session.NewGame(CreateOptions("Legacy"));
			session.SaveGame(legacyPath);

			Assert.Equal(SaveLoadStatus.Success, session.LoadGame(legacyPath));
			Assert.Equal("Legacy Save: legacy-slot", session.DescribeCurrentSessionLabel());
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ApplyMultiplayerRoomSnapshot_RebindsPlayerAndDoesNotPersistContinueState()
	{
		TestSupport.EnsureGameplayDataLoaded();
		using var _ = new ContinueStateScope();
		var root = TestSupport.CreateTempDirectory("session-multiplayer-apply");
		try
		{
			var sourceState = new GameState();
			var sourceSession = new GameSessionModule(sourceState, new FogOfWarTracker(), root);
			sourceSession.NewGame(CreateOptions("Owner"));
			var snapshot = SaveModule.BuildSnapshot(sourceState);
			AddPlayerRecoveryCandidate(
				snapshot,
				sourceState.PlayerId,
				"scout",
				"Scout",
				sourceState.PlayerX + 4,
				sourceState.PlayerY + 2,
				sourceState.PlayerZ);

			var preservedContinueState = new ContinueState
			{
				LastContinueKind = "legacy_save",
				LastWorldId = "world-sentinel",
				LastCharacterId = "character-sentinel",
				LastLegacySavePath = "legacy-sentinel.json",
			};
			AppSettingsStore.SaveContinueState(preservedContinueState);

			var room = new RoomRuntimeState
			{
				RoomId = "room-alpha",
				RoomCode = "AB12CD",
			};
			room.Players["owner"] = new RoomPlayerState
			{
				PlayerSessionId = "owner",
				DisplayName = "Owner",
				PrimaryActorId = sourceState.PlayerId,
				Connected = true,
				IsRoomOwner = true,
			};
			room.Players["guest"] = new RoomPlayerState
			{
				PlayerSessionId = "guest",
				DisplayName = "Guest",
				PrimaryActorId = "scout",
				Connected = true,
				IsRoomOwner = false,
			};
			var roomState = new GameState { Room = room };
			RoomRuntimeModule.AssignPrimaryActor(roomState, "owner", sourceState.PlayerId);
			RoomRuntimeModule.AssignPrimaryActor(roomState, "guest", "scout");

			var restoredState = new GameState();
			var restoredSession = new GameSessionModule(restoredState, new FogOfWarTracker(), root);
			var status = restoredSession.ApplyMultiplayerRoomSnapshot(
				room,
				playerSessionId: "guest",
				displayName: "Guest",
				primaryActorId: "scout",
				snapshot);

			Assert.Equal(SaveLoadStatus.Success, status);
			Assert.True(restoredSession.GameStarted);
			Assert.True(restoredSession.IsMultiplayerRoomSession);
			Assert.Null(restoredSession.CurrentSavePath);
			Assert.Null(restoredSession.CurrentWorldId);
			Assert.Null(restoredSession.CurrentCharacterId);
			Assert.Equal("scout", restoredState.PlayerId);
			Assert.Equal(restoredState.Actors["scout"].X, restoredState.PlayerX);
			Assert.Equal(restoredState.Actors["scout"].Y, restoredState.PlayerY);
			Assert.Equal(restoredState.Actors["scout"].Z, restoredState.PlayerZ);
			Assert.Equal("Guest", restoredState.Room.Players["guest"].DisplayName);

			var continueState = AppSettingsStore.LoadContinueState();
			Assert.Equal(preservedContinueState.LastContinueKind, continueState.LastContinueKind);
			Assert.Equal(preservedContinueState.LastWorldId, continueState.LastWorldId);
			Assert.Equal(preservedContinueState.LastCharacterId, continueState.LastCharacterId);
			Assert.Equal(preservedContinueState.LastLegacySavePath, continueState.LastLegacySavePath);
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
		};
	}

	private static string WritePreparedLoadSave(
		string root,
		string fileName,
		GameState state,
		Action<SaveFile> mutate)
	{
		var saveFile = SaveModule.BuildSnapshot(state);
		mutate(saveFile);
		var path = Path.Combine(root, fileName);
		SaveModule.WriteSaveFile(saveFile, path);
		return path;
	}

	private static void AddPlayerRecoveryCandidate(
		SaveFile saveFile,
		string sourceActorId,
		string newActorId,
		string displayName,
		int x,
		int y,
		int z)
	{
		var clone = SaveModule.DeserializeSaveFile(SaveModule.SerializeSaveFile(saveFile));
		Assert.NotNull(clone);

		var candidate = clone!.Payload.Actors.First(snapshot => snapshot.Id == sourceActorId);
		candidate.Id = newActorId;
		candidate.DisplayName = displayName;
		candidate.X = x;
		candidate.Y = y;
		candidate.Z = z;
		saveFile.Payload.Actors.Add(candidate);

		var timelineActor = clone.Payload.Timeline.Actors.FirstOrDefault(snapshot => snapshot.ActorId == sourceActorId);
		if (timelineActor == null)
			return;

		timelineActor.ActorId = newActorId;
		saveFile.Payload.Timeline.Actors.Add(timelineActor);
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
		  "version": 8,
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
