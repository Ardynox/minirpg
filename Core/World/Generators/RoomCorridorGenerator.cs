using System;
using System.Collections.Generic;

namespace MiniRPG.Core.World.Generators;

/// <summary>
/// 房间+走廊生成器：移植自旧 MapGenModule。
/// 在 chunk 内随机放置不重叠的矩形房间，用 L 形走廊连接。
/// 地表(z=0)生成村庄风格，地下生成地城风格。
/// </summary>
public class RoomCorridorGenerator : IMapGenerator
{
	public string Id => "room_corridor";
	public string Name => "房间走廊";

	public void GenerateChunk(ChunkData chunk, int worldSeed)
	{
		if (chunk.Coord.Cz == 0)
		{
			SurfaceGenerator.Generate(chunk, worldSeed);
			return;
		}
		if (chunk.Coord.Cz < 0) { chunk.Fill(TerrainRegistry.GetId(Terrains.Floor)); return; }

		var seed = HashSeed(worldSeed, chunk.Coord);
		var rng = new Random(seed);
		var wallId = GetWallTerrain(chunk.Coord.Cz);
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		chunk.Fill(wallId);

		var rooms = PlaceRooms(chunk, rng, floorId);
		ConnectRooms(chunk, rooms, rng, floorId);
	}

	public void PopulateChunk(ChunkData chunk, int worldSeed)
	{
		var seed = HashSeed(worldSeed, chunk.Coord) ^ 0x50505050;
		var rng = new Random(seed);
		var z = chunk.Coord.Cz;
		var floorId = TerrainRegistry.GetId(Terrains.Floor);

		if (z > 0)
			PopulateDungeon(chunk, rng);
		else if (z == 0)
			PopulateSurface(chunk, rng);

		PlaceStairs(chunk, rng, floorId);
	}

	private static ushort GetWallTerrain(int z)
	{
		if (z <= 0) return TerrainRegistry.GetId(Terrains.WallStone);
		if (z <= 3) return TerrainRegistry.GetId(Terrains.WallSoil);
		if (z <= 8) return TerrainRegistry.GetId(Terrains.WallStone);
		if (z <= 15) return TerrainRegistry.GetId(Terrains.WallGranite);
		return TerrainRegistry.GetId(Terrains.WallObsidian);
	}

	private static List<Room> PlaceRooms(ChunkData chunk, Random rng, ushort floorId)
	{
		var config = GameConfig.Generation.RoomCorridor;
		var rooms = new List<Room>();
		for (var i = 0; i < config.MaxAttempts; i++)
		{
			var w = rng.Next(config.MinRoomSize, config.MaxRoomSize + 1);
			var h = rng.Next(config.MinRoomSize, config.MaxRoomSize + 1);
			var x = rng.Next(1, ChunkData.Size - w - 1);
			var y = rng.Next(1, ChunkData.Size - h - 1);
			var room = new Room { X = x, Y = y, W = w, H = h };

			if (Overlaps(rooms, room)) continue;

			CarveRoom(chunk, room, floorId);
			rooms.Add(room);
		}
		return rooms;
	}

	private static bool Overlaps(List<Room> rooms, Room r)
	{
		foreach (var other in rooms)
		{
			if (r.X - 1 < other.X + other.W && r.X + r.W + 1 > other.X &&
				r.Y - 1 < other.Y + other.H && r.Y + r.H + 1 > other.Y)
				return true;
		}
		return false;
	}

	private static void CarveRoom(ChunkData chunk, Room room, ushort floorId)
	{
		for (var dy = 0; dy < room.H; dy++)
		for (var dx = 0; dx < room.W; dx++)
			chunk.SetTerrain(room.X + dx, room.Y + dy, floorId);
	}

	private static void ConnectRooms(ChunkData chunk, List<Room> rooms, Random rng, ushort floorId)
	{
		for (var i = 1; i < rooms.Count; i++)
		{
			var a = rooms[i - 1];
			var b = rooms[i];
			if (rng.Next(2) == 0)
			{
				CarveHCorridor(chunk, a.CenterX, b.CenterX, a.CenterY, floorId);
				CarveVCorridor(chunk, a.CenterY, b.CenterY, b.CenterX, floorId);
			}
			else
			{
				CarveVCorridor(chunk, a.CenterY, b.CenterY, a.CenterX, floorId);
				CarveHCorridor(chunk, a.CenterX, b.CenterX, b.CenterY, floorId);
			}
		}
	}

	private static void CarveHCorridor(ChunkData chunk, int x1, int x2, int y, ushort floorId)
	{
		for (var x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
			if (x >= 0 && x < ChunkData.Size && y >= 0 && y < ChunkData.Size)
				chunk.SetTerrain(x, y, floorId);
	}

	private static void CarveVCorridor(ChunkData chunk, int y1, int y2, int x, ushort floorId)
	{
		for (var y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
			if (x >= 0 && x < ChunkData.Size && y >= 0 && y < ChunkData.Size)
				chunk.SetTerrain(x, y, floorId);
	}

	private static void PopulateSurface(ChunkData chunk, Random rng)
	{
		var config = GameConfig.Generation.RoomCorridor;
		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != TerrainRegistry.GetId(Terrains.Floor)) continue;
			if (rng.Next(100) < Math.Clamp(config.SurfaceHouseChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "H", EntityId = Entities.House });
			}
		}
	}

	private static void PopulateDungeon(ChunkData chunk, Random rng)
	{
		var config = GameConfig.Generation.RoomCorridor;
		var floorId = TerrainRegistry.GetId(Terrains.Floor);
		for (var ly = 0; ly < ChunkData.Size; ly++)
		for (var lx = 0; lx < ChunkData.Size; lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (rng.Next(100) < Math.Clamp(config.DungeonNestChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "N", EntityId = Entities.Nest });
				chunk.Nests.Add(new NestData
				{
					X = chunk.Coord.Cx * ChunkData.Size + lx,
					Y = chunk.Coord.Cy * ChunkData.Size + ly,
					SpawnInterval = config.DungeonNestSpawnInterval,
					MaxSpawned = config.DungeonNestMaxSpawned,
				});
			}
		}
	}

	private static void PlaceStairs(ChunkData chunk, Random rng, ushort floorId)
	{
		var config = GameConfig.Generation.RoomCorridor;
		var downPlaced = false;
		var upPlaced = false;
		for (var ly = 0; ly < ChunkData.Size && (!downPlaced || !upPlaced); ly++)
		for (var lx = 0; lx < ChunkData.Size && (!downPlaced || !upPlaced); lx++)
		{
			if (chunk.GetTerrainId(lx, ly) != floorId) continue;
			if (chunk.GetEntities(lx, ly).Count > 0) continue;

			if (!downPlaced && rng.Next(100) < Math.Clamp(config.StairDownChancePercent, 0, 100))
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = ">", EntityId = Entities.StairDown });
				downPlaced = true;
			}
			else if (!upPlaced && rng.Next(100) < Math.Clamp(config.StairUpChancePercent, 0, 100) && chunk.Coord.Cz > 0)
			{
				chunk.PushEntity(lx, ly, new CellEntity
					{ Type = CellEntityType.Fixture, Glyph = "<", EntityId = Entities.StairUp });
				upPlaced = true;
			}
		}
	}

	private static int HashSeed(int worldSeed, ChunkCoord c) =>
		unchecked(worldSeed * 73856093 ^ c.Cx * 19349663 ^ c.Cy * 83492791 ^ c.Cz * 41729387);
}

/// <summary>房间数据：左上角坐标 + 宽高。供生成器内部使用。</summary>
public class Room
{
	public int X { get; set; }
	public int Y { get; set; }
	public int W { get; set; }
	public int H { get; set; }
	public int CenterX => X + W / 2;
	public int CenterY => Y + H / 2;
}
