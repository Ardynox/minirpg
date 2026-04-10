using System;

namespace MiniRPG.Core.World.Generators;

internal static class GeneratorPopulateHelper
{
	public static void PlaceDungeonFixtures(
		ChunkData chunk,
		Random rng,
		ushort floorId,
		int stairDownChancePercent,
		int stairUpChancePercent,
		bool allowStairUp,
		int nestChancePercent,
		int nestSpawnInterval,
		int nestMaxSpawned)
	{
		var downPlaced = false;
		var upPlaced = false;

		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			if (!downPlaced && rng.Next(100) < Math.Clamp(stairDownChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				downPlaced = true;
			}
			else if (allowStairUp && !upPlaced && rng.Next(100) < Math.Clamp(stairUpChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "<", EntityId = Entities.StairUp });
				upPlaced = true;
			}
			else if (rng.Next(100) < Math.Clamp(nestChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "N", EntityId = Entities.Nest });
				chunk.Nests.Add(new NestData
				{
					X = chunk.Coord.Cx * ChunkData.Size + lx,
					Y = chunk.Coord.Cy * ChunkData.Size + ly,
					SpawnInterval = nestSpawnInterval,
					MaxSpawned = nestMaxSpawned,
				});
			}
		}
	}

	public static void PlaceStairs(
		ChunkData chunk,
		Random rng,
		ushort floorId,
		int stairDownChancePercent,
		int stairUpChancePercent,
		bool allowStairUp)
	{
		var downPlaced = false;
		var upPlaced = false;
		for (var ly = 0; ly < ChunkData.Size && (!downPlaced || !upPlaced); ly++)
		for (var lx = 0; lx < ChunkData.Size && (!downPlaced || !upPlaced); lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			if (!downPlaced && rng.Next(100) < Math.Clamp(stairDownChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				downPlaced = true;
			}
			else if (allowStairUp && !upPlaced && rng.Next(100) < Math.Clamp(stairUpChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "<", EntityId = Entities.StairUp });
				upPlaced = true;
			}
		}
	}
}
