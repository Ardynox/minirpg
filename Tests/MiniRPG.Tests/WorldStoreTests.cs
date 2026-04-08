using System;
using System.IO;
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
}
