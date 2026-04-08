using System;
using System.IO;
using System.Text.Json;
using MiniRPG.Core.Map;
using Xunit;

namespace MiniRPG.Tests;

public sealed class WorldStoreTests
{
	[Fact]
	public void CreateWorldId_UsesSlugAndEightCharacterSuffix()
	{
		var root = TestSupport.CreateTempDirectory("world-store-id");
		try
		{
			var store = new WorldStore(root);

			var id = store.CreateWorldId("My First World");

			Assert.Matches("^my-first-world-[a-f0-9]{8}$", id);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void SaveWorld_RoundTripsManifest()
	{
		var root = TestSupport.CreateTempDirectory("world-store-roundtrip");
		try
		{
			var store = new WorldStore(root);
			var manifest = new WorldManifest
			{
				WorldId = "alpha-00000001",
				DisplayName = "Alpha",
				Settings = new WorldSettings
				{
					Seed = 424242,
					GeneratorId = "perlin",
					ClimateId = "temperate",
					StartSeasonId = "spring",
					CivilizationLevelId = "frontier",
					MonsterDensityPercent = 120,
					NpcDensityPercent = 80,
					LootAbundancePercent = 110,
					NestIntensityPercent = 95,
					WeatherVolatilityPercent = 130,
				},
				CreatedAtUtc = new DateTimeOffset(2026, 4, 7, 8, 0, 0, TimeSpan.Zero),
				LastPlayedAtUtc = new DateTimeOffset(2026, 4, 7, 9, 30, 0, TimeSpan.Zero),
				LastPlayedCharacterId = "rook-00000001",
			};

			store.SaveWorld(manifest);
			var loaded = store.TryLoadWorld(manifest.WorldId, out var roundTrip);

			Assert.True(loaded);
			Assert.Equal(manifest.DisplayName, roundTrip.DisplayName);
			Assert.Equal(manifest.Settings.Seed, roundTrip.Settings.Seed);
			Assert.Equal(manifest.Settings.GeneratorId, roundTrip.Settings.GeneratorId);
			Assert.Equal(manifest.LastPlayedAtUtc, roundTrip.LastPlayedAtUtc);
			Assert.Equal(manifest.LastPlayedCharacterId, roundTrip.LastPlayedCharacterId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ListWorlds_SortsByLastPlayedThenCreatedAt()
	{
		var root = TestSupport.CreateTempDirectory("world-store-sort");
		try
		{
			var store = new WorldStore(root);
			store.SaveWorld(new WorldManifest
			{
				WorldId = "alpha-00000001",
				DisplayName = "Alpha",
				Settings = WorldSettings.CreateDefault(),
				CreatedAtUtc = new DateTimeOffset(2026, 4, 6, 10, 0, 0, TimeSpan.Zero),
				LastPlayedAtUtc = new DateTimeOffset(2026, 4, 7, 8, 0, 0, TimeSpan.Zero),
			});
			store.SaveWorld(new WorldManifest
			{
				WorldId = "beta-00000002",
				DisplayName = "Beta",
				Settings = WorldSettings.CreateDefault(),
				CreatedAtUtc = new DateTimeOffset(2026, 4, 6, 9, 0, 0, TimeSpan.Zero),
				LastPlayedAtUtc = new DateTimeOffset(2026, 4, 7, 9, 0, 0, TimeSpan.Zero),
			});
			store.SaveWorld(new WorldManifest
			{
				WorldId = "gamma-00000003",
				DisplayName = "Gamma",
				Settings = WorldSettings.CreateDefault(),
				CreatedAtUtc = new DateTimeOffset(2026, 4, 7, 7, 0, 0, TimeSpan.Zero),
				LastPlayedAtUtc = null,
			});

			var worlds = store.ListWorlds();

			Assert.Collection(worlds,
				entry => Assert.Equal("beta-00000002", entry.WorldId),
				entry => Assert.Equal("alpha-00000001", entry.WorldId),
				entry => Assert.Equal("gamma-00000003", entry.WorldId));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorldSaveData_RemovesCharacterSavesButKeepsManifest()
	{
		var root = TestSupport.CreateTempDirectory("world-store-delete-saves");
		try
		{
			var store = new WorldStore(root);
			store.SaveWorld(new WorldManifest
			{
				WorldId = "alpha-00000001",
				DisplayName = "Alpha",
				Settings = WorldSettings.CreateDefault(),
				CreatedAtUtc = DateTimeOffset.UtcNow,
				LastPlayedAtUtc = new DateTimeOffset(2026, 4, 7, 9, 0, 0, TimeSpan.Zero),
				LastPlayedCharacterId = "rook-00000001",
			});

			var characterPath = store.GetCharacterSavePath("alpha-00000001", "rook-00000001");
			WriteWorldCharacterSave(characterPath, "alpha-00000001", "Alpha", "rook-00000001", "Rook");

			var deleted = store.DeleteWorldSaveData("alpha-00000001");

			Assert.True(deleted);
			Assert.True(File.Exists(store.GetWorldManifestPath("alpha-00000001")));
			Assert.False(Directory.Exists(store.GetWorldSaveDirectory("alpha-00000001")));
			Assert.True(store.TryLoadWorld("alpha-00000001", out var manifest));
			Assert.NotNull(manifest);
			Assert.Null(manifest.LastPlayedAtUtc);
			Assert.Null(manifest.LastPlayedCharacterId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void DeleteWorldAssets_RemovesOnlyAssetDirectory()
	{
		var root = TestSupport.CreateTempDirectory("world-store-delete-assets");
		try
		{
			var store = new WorldStore(root);
			store.SaveWorld(new WorldManifest
			{
				WorldId = "alpha-00000001",
				DisplayName = "Alpha",
				Settings = WorldSettings.CreateDefault(),
				CreatedAtUtc = DateTimeOffset.UtcNow,
			});

			var characterPath = store.GetCharacterSavePath("alpha-00000001", "rook-00000001");
			WriteWorldCharacterSave(characterPath, "alpha-00000001", "Alpha", "rook-00000001", "Rook");
			var assetDirectory = Path.Combine(store.GetWorldAssetDirectory("alpha-00000001"), "chunks");
			Directory.CreateDirectory(assetDirectory);
			File.WriteAllText(Path.Combine(assetDirectory, "chunk-0.bin"), "cached");

			var deleted = store.DeleteWorldAssets("alpha-00000001");

			Assert.True(deleted);
			Assert.True(File.Exists(store.GetWorldManifestPath("alpha-00000001")));
			Assert.True(File.Exists(characterPath));
			Assert.False(Directory.Exists(store.GetWorldAssetDirectory("alpha-00000001")));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void MigrateLegacyWorldLayout_MovesManifestAndCharacterSavesIntoSplitDirectories()
	{
		var root = TestSupport.CreateTempDirectory("world-store-migrate-success");
		try
		{
			var store = new WorldStore(root);
			var legacyWorldDirectory = Path.Combine(store.LegacyWorldsDirectory, "alpha-00000001");
			WriteLegacyWorldManifest(legacyWorldDirectory, "alpha-00000001", "Alpha");
			WriteWorldCharacterSave(
				Path.Combine(legacyWorldDirectory, "characters", "rook-00000001.json"),
				"alpha-00000001",
				"Alpha",
				"rook-00000001",
				"Rook");

			var report = store.MigrateLegacyWorldLayout();

			Assert.Equal(1, report.SuccessCount);
			Assert.Equal(0, report.FailureCount);
			Assert.False(Directory.Exists(legacyWorldDirectory));
			Assert.True(File.Exists(store.GetWorldManifestPath("alpha-00000001")));
			Assert.True(File.Exists(store.GetCharacterSavePath("alpha-00000001", "rook-00000001")));
			var world = Assert.Single(store.ListWorlds());
			Assert.Equal("alpha-00000001", world.WorldId);
			var character = Assert.Single(store.ListWorldCharacters("alpha-00000001", "Alpha"));
			Assert.Equal("rook-00000001", character.CharacterId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void MigrateLegacyWorldLayout_KeepsFailedLegacyWorldsHiddenWhileMigratingOthers()
	{
		var root = TestSupport.CreateTempDirectory("world-store-migrate-partial");
		try
		{
			var store = new WorldStore(root);
			var migratedWorldDirectory = Path.Combine(store.LegacyWorldsDirectory, "alpha-00000001");
			WriteLegacyWorldManifest(migratedWorldDirectory, "alpha-00000001", "Alpha");
			WriteWorldCharacterSave(
				Path.Combine(migratedWorldDirectory, "characters", "rook-00000001.json"),
				"alpha-00000001",
				"Alpha",
				"rook-00000001",
				"Rook");

			var failedWorldDirectory = Path.Combine(store.LegacyWorldsDirectory, "broken-00000002");
			Directory.CreateDirectory(failedWorldDirectory);
			File.WriteAllText(Path.Combine(failedWorldDirectory, "world.json"), "{not-json}");

			var report = store.MigrateLegacyWorldLayout();

			Assert.Equal(1, report.SuccessCount);
			Assert.Equal(1, report.FailureCount);
			var failure = Assert.Single(report.Failures);
			Assert.Equal("broken-00000002", failure.WorldId);
			Assert.False(Directory.Exists(migratedWorldDirectory));
			Assert.True(Directory.Exists(failedWorldDirectory));
			Assert.True(File.Exists(store.GetWorldManifestPath("alpha-00000001")));
			Assert.False(File.Exists(store.GetWorldManifestPath("broken-00000002")));
			var world = Assert.Single(store.ListWorlds());
			Assert.Equal("alpha-00000001", world.WorldId);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void MigrateLegacyWorldLayout_SkipsLegacyWorldWhenSplitLayoutDataAlreadyExists()
	{
		var root = TestSupport.CreateTempDirectory("world-store-migrate-conflict");
		try
		{
			var store = new WorldStore(root);
			var legacyWorldDirectory = Path.Combine(store.LegacyWorldsDirectory, "alpha-00000001");
			WriteLegacyWorldManifest(legacyWorldDirectory, "alpha-00000001", "Alpha Legacy");
			WriteWorldCharacterSave(
				Path.Combine(legacyWorldDirectory, "characters", "rook-00000001.json"),
				"alpha-00000001",
				"Alpha Legacy",
				"rook-00000001",
				"Rook");

			store.SaveWorld(new WorldManifest
			{
				WorldId = "alpha-00000001",
				DisplayName = "Alpha Current",
				Settings = new WorldSettings
				{
					Seed = 777,
					GeneratorId = "perlin",
					ClimateId = "tropical",
					StartSeasonId = "summer",
					CivilizationLevelId = "city_state",
					MonsterDensityPercent = 90,
					NpcDensityPercent = 140,
					LootAbundancePercent = 120,
					NestIntensityPercent = 60,
					WeatherVolatilityPercent = 80,
				},
				CreatedAtUtc = new DateTimeOffset(2026, 4, 8, 8, 0, 0, TimeSpan.Zero),
				LastPlayedAtUtc = new DateTimeOffset(2026, 4, 8, 12, 0, 0, TimeSpan.Zero),
				LastPlayedCharacterId = "mage-00000002",
			});
			var currentSavePath = store.GetCharacterSavePath("alpha-00000001", "mage-00000002");
			WriteWorldCharacterSave(
				currentSavePath,
				"alpha-00000001",
				"Alpha Current",
				"mage-00000002",
				"Mage");

			var report = store.MigrateLegacyWorldLayout();

			Assert.Equal(0, report.SuccessCount);
			Assert.Equal(1, report.FailureCount);
			var failure = Assert.Single(report.Failures);
			Assert.Equal("alpha-00000001", failure.WorldId);
			Assert.Contains("left untouched", failure.Message);
			Assert.True(Directory.Exists(legacyWorldDirectory));
			Assert.True(store.TryLoadWorld("alpha-00000001", out var manifest));
			Assert.Equal("Alpha Current", manifest.DisplayName);
			Assert.Equal(777, manifest.Settings.Seed);
			Assert.Equal("mage-00000002", manifest.LastPlayedCharacterId);
			Assert.True(File.Exists(currentSavePath));
			Assert.False(File.Exists(store.GetCharacterSavePath("alpha-00000001", "rook-00000001")));
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

	[Fact]
	public void ListWorldCharacters_ReadsHeaderOnlyWithoutDeserializingPayload()
	{
		var root = TestSupport.CreateTempDirectory("world-store-characters");
		try
		{
			var store = new WorldStore(root);
			store.SaveWorld(new WorldManifest
			{
				WorldId = "alpha-00000001",
				DisplayName = "Alpha",
				Settings = WorldSettings.CreateDefault(),
				CreatedAtUtc = DateTimeOffset.UtcNow,
			});

			var validPath = store.GetCharacterSavePath("alpha-00000001", "rook-00000001");
			File.WriteAllText(validPath, """
			{
			  "version": 5,
			  "header": {
			    "title": "Rook",
			    "savedAtUtc": "2026-04-07T10:15:00+00:00",
			    "turn": 12,
			    "playerZ": 0,
			    "generatorId": "room_corridor",
			    "viewModeId": "single_layer",
			    "worldId": "alpha-00000001",
			    "worldName": "Alpha",
			    "characterId": "rook-00000001",
			    "characterName": "Rook"
			  },
			  "payload": {
			    "unexpected": "shape"
			  }
			}
			""");

			File.WriteAllText(store.GetCharacterSavePath("alpha-00000001", "broken"), "{\"turn\":1}");

			var entries = store.ListWorldCharacters("alpha-00000001", "Alpha");

			var entry = Assert.Single(entries);
			Assert.Equal("rook-00000001", entry.CharacterId);
			Assert.Equal("Rook", entry.CharacterName);
			Assert.Equal("Alpha", entry.WorldName);
			Assert.Equal(12, entry.Turn);
			Assert.Equal(validPath, entry.SavePath);
		}
		finally
		{
			TestSupport.TryDeleteDirectory(root);
		}
	}

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
