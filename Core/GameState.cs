using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 唯一数据源：所有游戏状态存在这里，可序列化为 JSON。
/// 地图四层：Terrain → Fixtures → Objects → Meta。
/// </summary>
public class GameState
{
	public int Turn { get; set; }
	public int RngSeed { get; set; } = 42;
	public int CurrentFloor { get; set; }

	// ── 当前楼层地图（四层分离） ──
	public int MapWidth { get; set; }
	public int MapHeight { get; set; }
	/// <summary>第一层：地形 —— "#" 墙, "." 地面</summary>
	public List<List<string>> Terrain { get; set; } = [];
	/// <summary>第二层：设施（静态/半静态）—— ">" 下行楼梯, "<" 上行楼梯, "N" 巢穴, "I" 道具, "" 空</summary>
	public List<List<string>> Fixtures { get; set; } = [];
	/// <summary>第三层：动态角色 —— "P" 玩家, "M" 怪物, "" 空</summary>
	public List<List<string>> Objects { get; set; } = [];
	/// <summary>第四层：元数据（标记/触发器）—— 每格一个字典，null 表示无</summary>
	public List<List<Dictionary<string, string>?>> Meta { get; set; } = [];

	// ── 巢穴 ──
	public List<NestData> Nests { get; set; } = [];

	// ── 生物（玩家 + 怪物共用同一结构） ──
	public Dictionary<string, Actor> Actors { get; set; } = new();
	public string PlayerId { get; set; } = "player";

	// ── 玩家坐标快捷方式（与 Actors["player"] 同步） ──
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }

	// ── 其他楼层缓存（当前楼层数据在上面的直属字段里，其余楼层在这里） ──
	public Dictionary<int, FloorData> Floors { get; set; } = new();

	/// <summary>重置为初始状态，用于开始新游戏。</summary>
	public void Reset()
	{
		Turn = 0;
		RngSeed = 42;
		CurrentFloor = 0;
		MapWidth = 0;
		MapHeight = 0;
		Terrain.Clear();
		Fixtures.Clear();
		Objects.Clear();
		Meta.Clear();
		Nests.Clear();
		Actors.Clear();
		PlayerX = 0;
		PlayerY = 0;
		Floors.Clear();
	}
}

/// <summary>一层楼的完整快照，用于楼层切换时的缓存。</summary>
public class FloorData
{
	public int Width { get; set; }
	public int Height { get; set; }
	public List<List<string>> Terrain { get; set; } = [];
	public List<List<string>> Fixtures { get; set; } = [];
	public List<List<string>> Objects { get; set; } = [];
	public List<List<Dictionary<string, string>?>> Meta { get; set; } = [];
	public List<NestData> Nests { get; set; } = [];
	public Dictionary<string, Actor> Actors { get; set; } = new();
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
}

/// <summary>巢穴数据：位置 + 刷怪间隔 + 距上次刷怪的回合数。</summary>
public class NestData
{
	public int X { get; set; }
	public int Y { get; set; }
	public int SpawnInterval { get; set; } = 5;
	public int TurnsSinceSpawn { get; set; }
	public int MaxSpawned { get; set; } = 3;
	/// <summary>指定刷出的模板 ID。空字符串 = 随机怪物。</summary>
	public string TemplateId { get; set; } = "";
}
