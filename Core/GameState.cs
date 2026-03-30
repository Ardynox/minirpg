using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 唯一数据源：所有游戏状态存在这里，可序列化为 JSON。
/// Core 层的模块只读写这个对象，不碰任何 Godot 节点。
/// </summary>
public class GameState
{
	public int Turn { get; set; }
	public int RngSeed { get; set; } = 42;

	// ── 地图（三层分离） ──
	public int MapWidth { get; set; }
	public int MapHeight { get; set; }
	/// <summary>第一层：地形（不可变地貌）—— "#" 墙, "." 地面</summary>
	public List<List<string>> Terrain { get; set; } = [];
	/// <summary>第二层：对象（动态实体）—— "P" 玩家, "M" 怪物, "N" 巢穴, "D" 门, "" 空</summary>
	public List<List<string>> Objects { get; set; } = [];
	/// <summary>第三层：元数据（标记/触发器）—— 每格一个字典，null 表示无</summary>
	public List<List<Dictionary<string, string>?>> Meta { get; set; } = [];

	// ── 巢穴 ──
	public List<NestData> Nests { get; set; } = [];

	// ── 玩家 ──
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
}
