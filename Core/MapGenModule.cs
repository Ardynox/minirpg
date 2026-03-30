using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>房间数据：左上角坐标 + 宽高。</summary>
public class Room
{
	public int X { get; set; }
	public int Y { get; set; }
	public int W { get; set; }
	public int H { get; set; }
	public int CenterX => X + W / 2;
	public int CenterY => Y + H / 2;
}

/// <summary>
/// 地图生成模块：随机房间 + 走廊连接 + 放置玩家/巢穴/楼梯。
/// 纯函数，不引用 Godot 节点。
/// </summary>
public static class MapGenModule
{
	private const int MinRoomSize = 4;
	private const int MaxRoomSize = 8;
	private const int MaxAttempts = 60;

	/// <summary>
	/// 生成一张新地图并写入 state。floor 决定是否放置上行楼梯。
	/// </summary>
	public static List<Room> Generate(GameState state, int width, int height,
		int floor = 0, int? seed = null)
	{
		var rng = new Random(seed ?? Environment.TickCount);
		state.RngSeed = seed ?? rng.Next();
		state.MapWidth = width;
		state.MapHeight = height;
		state.Turn = 0;

		ActorModule.ClearAll(state);
		state.Nests.Clear();
		InitLayers(state, width, height);
		var rooms = PlaceRooms(state, rng);
		ConnectRooms(state, rooms, rng);
		Populate(state, rooms, rng, floor);
		if (floor > 0)
			NestModule.RegisterNests(state);

		return rooms;
	}

	private static void InitLayers(GameState state, int w, int h)
	{
		state.Terrain.Clear();
		state.Fixtures.Clear();
		state.Objects.Clear();
		state.Meta.Clear();
		for (var y = 0; y < h; y++)
		{
			var tRow = new List<string>();
			var fRow = new List<string>();
			var oRow = new List<string>();
			var mRow = new List<Dictionary<string, string>?>();
			for (var x = 0; x < w; x++)
			{
				tRow.Add("#");
				fRow.Add("");
				oRow.Add("");
				mRow.Add(null);
			}
			state.Terrain.Add(tRow);
			state.Fixtures.Add(fRow);
			state.Objects.Add(oRow);
			state.Meta.Add(mRow);
		}
	}

	private static List<Room> PlaceRooms(GameState state, Random rng)
	{
		var rooms = new List<Room>();
		for (var i = 0; i < MaxAttempts; i++)
		{
			var w = rng.Next(MinRoomSize, MaxRoomSize + 1);
			var h = rng.Next(MinRoomSize, MaxRoomSize + 1);
			var x = rng.Next(1, state.MapWidth - w - 1);
			var y = rng.Next(1, state.MapHeight - h - 1);
			var room = new Room { X = x, Y = y, W = w, H = h };

			if (Overlaps(rooms, room))
				continue;

			CarveRoom(state, room);
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

	private static void CarveRoom(GameState state, Room room)
	{
		for (var dy = 0; dy < room.H; dy++)
		for (var dx = 0; dx < room.W; dx++)
			MapModule.SetTerrain(state, room.X + dx, room.Y + dy, ".");
	}

	private static void ConnectRooms(GameState state, List<Room> rooms, Random rng)
	{
		for (var i = 1; i < rooms.Count; i++)
		{
			var a = rooms[i - 1];
			var b = rooms[i];
			if (rng.Next(2) == 0)
			{
				CarveHCorridor(state, a.CenterX, b.CenterX, a.CenterY);
				CarveVCorridor(state, a.CenterY, b.CenterY, b.CenterX);
			}
			else
			{
				CarveVCorridor(state, a.CenterY, b.CenterY, a.CenterX);
				CarveHCorridor(state, a.CenterX, b.CenterX, b.CenterY);
			}
		}
	}

	private static void CarveHCorridor(GameState state, int x1, int x2, int y)
	{
		for (var x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
			MapModule.SetTerrain(state, x, y, ".");
	}

	private static void CarveVCorridor(GameState state, int y1, int y2, int x)
	{
		for (var y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
			MapModule.SetTerrain(state, x, y, ".");
	}

	private static int _monsterCounter;
	private static int _npcCounter;

	private static void Populate(GameState state, List<Room> rooms, Random rng, int floor)
	{
		if (rooms.Count == 0) return;

		PlacePlayer(state, rooms[0], floor);

		if (floor == 0)
			PopulateSurface(state, rooms, rng);
		else
			PopulateDungeon(state, rooms, rng, floor);
	}

	private static void PlacePlayer(GameState state, Room first, int floor)
	{
		var player = ActorTemplates.Spawn("player", "player");
		player.X = first.CenterX;
		player.Y = first.CenterY;
		ActorModule.Add(state, player);
		state.PlayerX = first.CenterX;
		state.PlayerY = first.CenterY;

		if (floor > 0)
			MapModule.SetFixture(state, first.CenterX, first.CenterY, "<");
	}

	/// <summary>地表：安全村庄，无怪物无巢穴。有商人、村长、村民。</summary>
	private static void PopulateSurface(GameState state, List<Room> rooms, Random rng)
	{
		if (rooms.Count > 1)
		{
			var last = rooms[^1];
			MapModule.SetFixture(state, last.CenterX, last.CenterY, ">");
		}

		var npcAssignments = new (string Template, string Label)[]
		{
			("merchant", "商人小屋"),
			("elder",    "村长小屋"),
			("villager", "村民房"),
		};

		for (var i = 1; i < rooms.Count - 1 && i - 1 < npcAssignments.Length; i++)
		{
			var r = rooms[i];
			var (template, _) = npcAssignments[i - 1];
			var npc = ActorTemplates.Spawn(template, $"npc_{_npcCounter++}");
			npc.X = r.CenterX;
			npc.Y = r.CenterY;
			ActorModule.Add(state, npc);

			MapModule.SetFixture(state, r.CenterX, r.CenterY, "H");
			state.Nests.Add(new NestData
			{
				X = r.CenterX, Y = r.CenterY,
				TemplateId = template,
				SpawnInterval = 1, MaxSpawned = 1,
			});
		}
	}

	/// <summary>地下城：巢穴 + 怪物 + 楼梯。</summary>
	private static void PopulateDungeon(GameState state, List<Room> rooms, Random rng, int floor)
	{
		var monsterTemplates = ActorTemplates.MonsterIds.ToArray();

		if (rooms.Count > 1)
		{
			var last = rooms[^1];
			MapModule.SetFixture(state, last.CenterX, last.CenterY, ">");
		}

		for (var i = 1; i < rooms.Count - 1; i++)
		{
			var r = rooms[i];

			if (rng.Next(100) < 60)
			{
				var nx = r.X + rng.Next(1, r.W - 1);
				var ny = r.Y + rng.Next(1, r.H - 1);
				if (MapModule.IsWalkable(state, nx, ny) && ActorModule.GetAt(state, nx, ny) == null)
					MapModule.SetFixture(state, nx, ny, "N");
			}

			if (rng.Next(100) < 40)
			{
				var mx = r.X + rng.Next(1, r.W - 1);
				var my = r.Y + rng.Next(1, r.H - 1);
				if (MapModule.IsWalkable(state, mx, my) && ActorModule.GetAt(state, mx, my) == null)
				{
					var templateId = monsterTemplates[rng.Next(monsterTemplates.Length)];
					var monster = ActorTemplates.Spawn(templateId, $"mon_{_monsterCounter++}");
					monster.X = mx;
					monster.Y = my;
					ActorModule.Add(state, monster);
				}
			}
		}
	}
}
