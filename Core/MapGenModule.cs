using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 房间数据：左上角坐标 (X,Y) + 宽高 (W,H)，供 MapGenModule 使用。
/// </summary>
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
/// 程序化地图生成模块：随机房间布局 + L 形走廊连接 + 放置玩家/NPC/巢穴/楼梯。
/// 纯函数，不引用 Godot。
///
/// 生成流程：
///   1. InitCells → 全墙底图
///   2. PlaceRooms → 随机放置不重叠的矩形房间
///   3. ConnectRooms → 用 L 形走廊依次连接相邻房间
///   4. Populate → 放置玩家 + NPC/怪物/巢穴/楼梯
///   5. RegisterNests → 扫描地图 Fixture "nest" 注册到 state.Nests
/// </summary>
public static class MapGenModule
{
	private const int MinRoomSize = 4;
	private const int MaxRoomSize = 8;
	private const int MaxAttempts = 60;

	/// <summary>
	/// 生成一张新地图并写入 state。
	/// floor=0 → 地表村庄（安全区），floor>0 → 地下城（有怪物和巢穴）。
	/// 返回生成的房间列表。
	/// </summary>
	public static List<Room> Generate(GameState state, int width, int height,
		int floor = 0, int? seed = null)
	{
		var rng = new Random(seed ?? Environment.TickCount);
		// REVIEW: 当 seed 为 null 时，先用 TickCount 构造 rng，
		//         再用 rng.Next() 覆盖 RngSeed。这导致 RngSeed 无法完整重现地图。
		//         如果需要 replay，应在此保存实际使用的 seed。
		state.RngSeed = seed ?? rng.Next();
		state.MapWidth = width;
		state.MapHeight = height;
		state.Turn = 0;

		ActorModule.ClearAll(state);
		state.Nests.Clear();
		MapModule.InitCells(state, width, height);
		var rooms = PlaceRooms(state, rng);
		ConnectRooms(state, rooms, rng);
		Populate(state, rooms, rng, floor);
		if (floor > 0)
			NestModule.RegisterNests(state);

		return rooms;
	}

	/// <summary>随机尝试放置房间，最多 MaxAttempts 次，跳过重叠的。</summary>
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

	/// <summary>检查候选房间是否与已有房间重叠（含 1 格间距）。</summary>
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

	/// <summary>将房间区域的地形从墙替换为地板。</summary>
	private static void CarveRoom(GameState state, Room room)
	{
		for (var dy = 0; dy < room.H; dy++)
		for (var dx = 0; dx < room.W; dx++)
			MapModule.SetTerrain(state, room.X + dx, room.Y + dy, ".");
	}

	/// <summary>用 L 形走廊依次连接相邻房间的中心点。</summary>
	// REVIEW: 只连接 rooms[i-1] → rooms[i]，生成的是链式拓扑。
	//         如果 PlaceRooms 的顺序碰巧跨越远距离，走廊会很长。
	//         可考虑用最小生成树连接以获得更自然的布局。
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

	/// <summary>水平走廊：将 (x1,y) 到 (x2,y) 一行全部设为地板。</summary>
	private static void CarveHCorridor(GameState state, int x1, int x2, int y)
	{
		for (var x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++)
			MapModule.SetTerrain(state, x, y, ".");
	}

	/// <summary>垂直走廊：将 (x,y1) 到 (x,y2) 一列全部设为地板。</summary>
	private static void CarveVCorridor(GameState state, int y1, int y2, int x)
	{
		for (var y = Math.Min(y1, y2); y <= Math.Max(y1, y2); y++)
			MapModule.SetTerrain(state, x, y, ".");
	}

	// REVIEW: _monsterCounter / _npcCounter 是 static 字段，
	//         在整个进程生命周期内递增，不会随 Reset() 或新游戏归零。
	//         这意味着多次新建游戏后 Id 会越来越大（"mon_47" "npc_12"），
	//         虽然功能上不影响正确性，但不利于调试。
	//         考虑在 Generate() 入口处重置。
	private static int _monsterCounter;
	private static int _npcCounter;

	/// <summary>在房间中放置玩家、NPC、怪物、楼梯等。根据 floor 区分地表/地下城。</summary>
	private static void Populate(GameState state, List<Room> rooms, Random rng, int floor)
	{
		if (rooms.Count == 0) return;

		PlacePlayer(state, rooms[0], floor);

		if (floor == 0)
			PopulateSurface(state, rooms, rng);
		else
			PopulateDungeon(state, rooms, rng, floor);
	}

	/// <summary>在第一个房间中心放置玩家。地下城层还会在该位置放上行楼梯。</summary>
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

	/// <summary>
	/// 地表村庄：最后一个房间放下行楼梯，中间房间放 NPC + 房屋。
	/// NPC 用 NestData 记录以便离开再回来时重新生成。
	/// </summary>
	// REVIEW: NPC 房屋使用 NestData（SpawnInterval=1, MaxSpawned=1）实现「重回时刷新」，
	//         这是对巢穴系统的 hack 复用。NestData 语义是「怪物刷新点」，
	//         用于 NPC 可能导致 NPC 被 NestModule.Tick 在回合中重复刷出。
	//         应为 NPC 驻守点设计独立机制。
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

	/// <summary>
	/// 地下城：中间房间 60% 几率放巢穴，40% 几率放初始怪物，最后房间放下行楼梯。
	/// </summary>
	// REVIEW: floor 参数传入但未使用——不会根据层数调整难度/密度。
	//         如果未来要做「深层更难」，需要在此利用 floor。
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
