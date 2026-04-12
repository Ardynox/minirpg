using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.World;
using MiniRPG.Core.World.Generators;
using Xunit;

namespace MiniRPG.Tests;

public sealed class DwarfFortressGeneratorTests
{
	private const int Seed = 12345;

	public DwarfFortressGeneratorTests()
	{
		TestSupport.EnsureGameplayDataLoaded();
	}

	[Fact]
	public void GenerateChunk_SurfaceZ0_ContainsMixedBiomes()
	{
		var gen = new DwarfFortressGenerator();
		var chunk = new ChunkData { Coord = new ChunkCoord { Cx = 0, Cy = 0, Cz = 0 } };
		gen.GenerateChunk(chunk, Seed);

		var terrainCounts = new Dictionary<string, int>();
		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			var id = chunk.GetTerrainId(lx, ly);
			var def = TerrainRegistry.Get(id);
			terrainCounts[def.StringId] = terrainCounts.GetValueOrDefault(def.StringId) + 1;
		}

		// Surface chunk should have terrain variety (not all one type)
		Assert.True(terrainCounts.Count > 1, $"Expected terrain variety, got {terrainCounts.Count} types");
	}

	[Fact]
	public void GenerateChunk_HighAbove_IsAllAir()
	{
		var gen = new DwarfFortressGenerator();
		var chunk = new ChunkData { Coord = new ChunkCoord { Cx = 0, Cy = 0, Cz = -10 } };
		gen.GenerateChunk(chunk, Seed);

		var airId = TerrainRegistry.GetId(Terrains.Air);
		var airCount = 0;
		for (var i = 0; i < ChunkData.Area; i++)
			if (chunk.TerrainIds[i] == airId) airCount++;

		// High above surface should be mostly air
		Assert.True(airCount > ChunkData.Area * 0.9, $"Expected >90% air at Z=-10, got {airCount}/{ChunkData.Area}");
	}

	[Fact]
	public void GenerateChunk_DeepUnderground_IsSolidRock()
	{
		var gen = new DwarfFortressGenerator();
		var chunk = new ChunkData { Coord = new ChunkCoord { Cx = 0, Cy = 0, Cz = 15 } };
		gen.GenerateChunk(chunk, Seed);

		var solidCount = 0;
		for (var i = 0; i < ChunkData.Area; i++)
		{
			var def = TerrainRegistry.Get(chunk.TerrainIds[i]);
			if (def.Solid) solidCount++;
		}

		// Deep underground should be mostly solid (rock/granite/ore) with some caves
		Assert.True(solidCount > ChunkData.Area * 0.5, $"Expected >50% solid at Z=15, got {solidCount}/{ChunkData.Area}");
	}

	[Fact]
	public void GenerateChunk_Bedrock_IsObsidian()
	{
		var gen = new DwarfFortressGenerator();
		var chunk = new ChunkData { Coord = new ChunkCoord { Cx = 0, Cy = 0, Cz = 30 } };
		gen.GenerateChunk(chunk, Seed);

		var obsidianId = TerrainRegistry.GetId(Terrains.WallObsidian);
		var obsidianCount = 0;
		for (var i = 0; i < ChunkData.Area; i++)
			if (chunk.TerrainIds[i] == obsidianId) obsidianCount++;

		// Bedrock layer should be mostly obsidian
		Assert.True(obsidianCount > ChunkData.Area * 0.8, $"Expected >80% obsidian at Z=30, got {obsidianCount}/{ChunkData.Area}");
	}

	[Fact]
	public void GenerateChunk_IsDeterministic()
	{
		var gen = new DwarfFortressGenerator();
		var coord = new ChunkCoord { Cx = 5, Cy = -3, Cz = 2 };

		var chunk1 = new ChunkData { Coord = coord };
		gen.GenerateChunk(chunk1, Seed);

		var chunk2 = new ChunkData { Coord = coord };
		gen.GenerateChunk(chunk2, Seed);

		for (var i = 0; i < ChunkData.Area; i++)
			Assert.Equal(chunk1.TerrainIds[i], chunk2.TerrainIds[i]);
	}

	[Fact]
	public void GenerateChunk_Underground_ContainsOres()
	{
		var gen = new DwarfFortressGenerator();
		var chunk = new ChunkData { Coord = new ChunkCoord { Cx = 0, Cy = 0, Cz = 7 } };
		gen.GenerateChunk(chunk, Seed);

		var oreIds = new HashSet<ushort>
		{
			TerrainRegistry.GetId(Terrains.OreCoal),
			TerrainRegistry.GetId(Terrains.OreIron),
			TerrainRegistry.GetId(Terrains.OreCopper),
			TerrainRegistry.GetId(Terrains.OreGold),
			TerrainRegistry.GetId(Terrains.OreCrystal),
		};

		var oreCount = 0;
		for (var i = 0; i < ChunkData.Area; i++)
			if (oreIds.Contains(chunk.TerrainIds[i])) oreCount++;

		// Underground should have some ore veins
		Assert.True(oreCount > 0, "Expected some ore veins at Z=7");
	}

	[Fact]
	public void PopulateChunk_Underground_PlacesFixtures()
	{
		var gen = new DwarfFortressGenerator();
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		// Search across multiple chunks to find one with caves (floor tiles)
		var hasFixture = false;
		for (var cx = 0; cx < 5 && !hasFixture; cx++)
		for (var cy = 0; cy < 5 && !hasFixture; cy++)
		for (var cz = 5; cz <= 12 && !hasFixture; cz++)
		{
			var chunk = new ChunkData { Coord = new ChunkCoord { Cx = cx, Cy = cy, Cz = cz } };
			gen.GenerateChunk(chunk, Seed);

			// Only populate chunks that have caves
			var hasFloor = false;
			for (var i = 0; i < ChunkData.Area && !hasFloor; i++)
				hasFloor = chunk.TerrainIds[i] == floorId;
			if (!hasFloor) continue;

			gen.PopulateChunk(chunk, Seed);

			for (var ly = 0; ly < ChunkData.Size && !hasFixture; ly++)
			for (var lx = 0; lx < ChunkData.Size && !hasFixture; lx++)
			{
				foreach (var e in chunk.GetEntities(lx, ly))
				{
					if (e.Type == CellEntityType.Fixture)
					{
						hasFixture = true;
						break;
					}
				}
			}
		}

		Assert.True(hasFixture, "Expected at least one fixture in underground chunks with caves");
	}

	[Fact]
	public void GenerateChunk_Underground_HasCavesAcrossMultipleChunks()
	{
		var gen = new DwarfFortressGenerator();
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		var totalFloorCount = 0;

		// Check a range of underground chunks — caves should appear somewhere
		for (var cx = 0; cx < 3; cx++)
		for (var cy = 0; cy < 3; cy++)
		for (var cz = 6; cz <= 12; cz++)
		{
			var chunk = new ChunkData { Coord = new ChunkCoord { Cx = cx, Cy = cy, Cz = cz } };
			gen.GenerateChunk(chunk, Seed);
			for (var i = 0; i < ChunkData.Area; i++)
				if (chunk.TerrainIds[i] == floorId) totalFloorCount++;
		}

		Assert.True(totalFloorCount > 0, $"Expected some cave floor tiles across underground chunks, got {totalFloorCount}");
	}
}
