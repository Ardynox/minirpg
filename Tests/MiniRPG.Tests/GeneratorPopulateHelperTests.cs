using System;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;
using Xunit;

namespace MiniRPG.Tests;

public sealed class GeneratorPopulateHelperTests
{
	[Fact]
	public void PlaceDungeonFixtures_PlacesOnlyOnFloorAndEmptyCells()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var chunk = CreateChunk(new ChunkCoord { Cx = 3, Cy = -2, Cz = 4 }, Terrains.WallStone);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		chunk.SetTerrain(0, 0, floorId);
		chunk.SetTerrain(1, 0, floorId);
		chunk.SetTerrain(2, 0, floorId);
		chunk.PushEntity(1, 0, new CellEntity { Type = CellEntityType.Item, Glyph = "i", EntityId = "occupied" });

		GeneratorPopulateHelper.PlaceDungeonFixtures(
			chunk,
			new Random(123),
			floorId,
			stairDownChancePercent: 100,
			stairUpChancePercent: 100,
			allowStairUp: true,
			nestChancePercent: 100,
			nestSpawnInterval: 7,
			nestMaxSpawned: 3);

		Assert.Equal(Entities.StairDown, chunk.GetEntities(0, 0).Single().EntityId);
		Assert.Equal("occupied", chunk.GetEntities(1, 0).Single().EntityId);
		Assert.Equal(Entities.StairUp, chunk.GetEntities(2, 0).Single().EntityId);
		Assert.Empty(chunk.GetEntities(3, 0));
	}

	[Fact]
	public void PlaceDungeonFixtures_PlacesAtMostOneUpAndDownStair()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var chunk = CreateChunk(new ChunkCoord { Cx = 0, Cy = 0, Cz = 6 }, Terrains.Floor);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		GeneratorPopulateHelper.PlaceDungeonFixtures(
			chunk,
			new Random(456),
			floorId,
			stairDownChancePercent: 100,
			stairUpChancePercent: 100,
			allowStairUp: true,
			nestChancePercent: 100,
			nestSpawnInterval: 5,
			nestMaxSpawned: 2);

		Assert.Equal(1, CountFixtures(chunk, Entities.StairDown));
		Assert.Equal(1, CountFixtures(chunk, Entities.StairUp));
		Assert.True(CountFixtures(chunk, Entities.Nest) > 0);
	}

	[Fact]
	public void PlaceDungeonFixtures_UsesScanOrderAndPriority()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var chunk = CreateChunk(new ChunkCoord { Cx = 1, Cy = 1, Cz = 5 }, Terrains.WallStone);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		chunk.SetTerrain(0, 0, floorId);
		chunk.SetTerrain(1, 0, floorId);
		chunk.SetTerrain(2, 0, floorId);

		GeneratorPopulateHelper.PlaceDungeonFixtures(
			chunk,
			new Random(789),
			floorId,
			stairDownChancePercent: 100,
			stairUpChancePercent: 100,
			allowStairUp: true,
			nestChancePercent: 100,
			nestSpawnInterval: 9,
			nestMaxSpawned: 4);

		Assert.Equal(Entities.StairDown, chunk.GetEntities(0, 0).Single().EntityId);
		Assert.Equal(Entities.StairUp, chunk.GetEntities(1, 0).Single().EntityId);
		Assert.Equal(Entities.Nest, chunk.GetEntities(2, 0).Single().EntityId);

		var nest = Assert.Single(chunk.Nests);
		Assert.Equal(chunk.Coord.Cx * ChunkData.Size + 2, nest.X);
		Assert.Equal(chunk.Coord.Cy * ChunkData.Size, nest.Y);
		Assert.Equal(9, nest.SpawnInterval);
		Assert.Equal(4, nest.MaxSpawned);
	}

	[Fact]
	public void PlaceStairs_DoesNotPlaceUpStairWhenDisabled()
	{
		TestSupport.EnsureGameplayDataLoaded();
		var chunk = CreateChunk(new ChunkCoord { Cx = -1, Cy = 2, Cz = 0 }, Terrains.Floor);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		GeneratorPopulateHelper.PlaceStairs(
			chunk,
			new Random(321),
			floorId,
			stairDownChancePercent: 100,
			stairUpChancePercent: 100,
			allowStairUp: false);

		Assert.Equal(1, CountFixtures(chunk, Entities.StairDown));
		Assert.Equal(0, CountFixtures(chunk, Entities.StairUp));
	}

	[Fact]
	public void WorldGenerationSettingsRegistry_ScalesNestParametersFromWorldSettings()
	{
		const int worldSeed = 24680;
		const string generatorId = "generator_scaling_probe";
		WorldGenerationSettingsRegistry.Register(worldSeed, generatorId, new WorldSettings
		{
			MonsterDensityPercent = 250,
			NestIntensityPercent = 50,
		});

		Assert.Equal(5, WorldGenerationSettingsRegistry.ScaleNestChancePercent(worldSeed, generatorId, 10));
		Assert.Equal(8, WorldGenerationSettingsRegistry.ScaleNestSpawnInterval(worldSeed, generatorId, 20));
		Assert.Equal(10, WorldGenerationSettingsRegistry.ScaleNestMaxSpawned(worldSeed, generatorId, 4));
	}

	[Fact]
	public void WorldGenerationSettingsRegistry_DisablesNestSpawns_WhenMonsterDensityIsZero()
	{
		const int worldSeed = 86420;
		const string generatorId = "generator_scaling_zero";
		WorldGenerationSettingsRegistry.Register(worldSeed, generatorId, new WorldSettings
		{
			MonsterDensityPercent = 0,
			NestIntensityPercent = 300,
		});

		Assert.Equal(0, WorldGenerationSettingsRegistry.ScaleNestChancePercent(worldSeed, generatorId, 10));
		Assert.Equal(int.MaxValue, WorldGenerationSettingsRegistry.ScaleNestSpawnInterval(worldSeed, generatorId, 20));
		Assert.Equal(0, WorldGenerationSettingsRegistry.ScaleNestMaxSpawned(worldSeed, generatorId, 4));
	}

	[Fact]
	public void WorldGenerationSettingsRegistry_NestIntensityAbove100_AlsoBoostsSpawnCadence()
	{
		const int worldSeed = 97531;
		const string generatorId = "generator_scaling_over_100";
		WorldGenerationSettingsRegistry.Register(worldSeed, generatorId, new WorldSettings
		{
			MonsterDensityPercent = 100,
			NestIntensityPercent = 250,
		});

		Assert.Equal(25, WorldGenerationSettingsRegistry.ScaleNestChancePercent(worldSeed, generatorId, 10));
		Assert.Equal(8, WorldGenerationSettingsRegistry.ScaleNestSpawnInterval(worldSeed, generatorId, 20));
		Assert.Equal(10, WorldGenerationSettingsRegistry.ScaleNestMaxSpawned(worldSeed, generatorId, 4));
	}

	private static ChunkData CreateChunk(ChunkCoord coord, string terrainId)
	{
		var chunk = new ChunkData { Coord = coord };
		chunk.Fill(TerrainRegistry.GetId(terrainId));
		return chunk;
	}

	private static int CountFixtures(ChunkData chunk, string entityId) =>
		chunk.Entities.Values
			.SelectMany(static entities => entities)
			.Count(entity => entity.Type == CellEntityType.Fixture && entity.EntityId == entityId);
}
