using System;
using System.Collections.Generic;

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

		InitLayers(state, width, height);
		var rooms = PlaceRooms(state, rng);
		ConnectRooms(state, rooms, rng);
		Populate(state, rooms, rng, floor);
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

	/// <summary>
	/// 放置玩家（第一个房间）、楼梯、巢穴。
	/// floor > 0 时在第一个房间放上行楼梯，最后一个房间放下行楼梯。
	/// </summary>
	private static void Populate(GameState state, List<Room> rooms, Random rng, int floor)
	{
		if (rooms.Count == 0) return;

		var first = rooms[0];
		state.PlayerX = first.CenterX;
		state.PlayerY = first.CenterY;
		MapModule.SetObject(state, first.CenterX, first.CenterY, "P");

		// 非底层 → 第一个房间放上行楼梯（玩家脚下）
		if (floor > 0)
			MapModule.SetFixture(state, first.CenterX, first.CenterY, "<");

		// 最后一个房间放下行楼梯
		if (rooms.Count > 1)
		{
			var last = rooms[^1];
			MapModule.SetFixture(state, last.CenterX, last.CenterY, ">");
		}

		// 中间房间随机放巢穴
		for (var i = 1; i < rooms.Count - 1; i++)
		{
			if (rng.Next(100) < 60)
			{
				var r = rooms[i];
				var nx = r.X + rng.Next(1, r.W - 1);
				var ny = r.Y + rng.Next(1, r.H - 1);
				if (MapModule.IsWalkable(state, nx, ny))
					MapModule.SetFixture(state, nx, ny, "N");
			}
		}
	}
}
