using System.Collections.Generic;
using System.Text.Json.Serialization;
using MiniRPG.Core.World;

namespace MiniRPG.Core;

/// <summary>
/// 唯一数据源：所有可序列化的游戏运行时状态集中于此。
/// 地图使用三维无限 chunk 世界：WorldMap 管理所有地形和实体数据。
/// Actor 存在全局字典中，不存在 chunk 内。
/// </summary>
public class GameState
{
	public int Turn { get; set; }
	public int WorldSeed { get; set; } = 42;

	/// <summary>兼容旧模块的 RNG 种子属性，等同于 WorldSeed。</summary>
	public int RngSeed => WorldSeed;

	/// <summary>三维无限世界。不可序列化——存档时由 SaveModule 单独处理 dirty chunk。</summary>
	[JsonIgnore]
	public WorldMap? World { get; set; }

	// ── 玩家三维坐标 ──
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
	public int PlayerZ { get; set; }

	// ── 生物 ──
	public Dictionary<string, Actor> Actors { get; set; } = new();
	public string PlayerId { get; set; } = "player";

	// ── 设置 ──
	public bool BumpAttack { get; set; } = true;
	public bool WatchMode { get; set; }
	public int KillCount { get; set; }

	/// <summary>当前地图生成器 ID（"room_corridor" / "perlin" / "cellular_automata" / "drunkard_walk" / "bsp"）。</summary>
	public string GeneratorId { get; set; } = "room_corridor";

	/// <summary>当前视图模式 ID（"single_layer" / "multi_layer"）。</summary>
	public string ViewModeId { get; set; } = "single_layer";

	public void Reset()
	{
		Turn = 0;
		WorldSeed = 42;
		PlayerX = 0;
		PlayerY = 0;
		PlayerZ = 0;
		Actors.Clear();
		BumpAttack = true;
		WatchMode = false;
		KillCount = 0;
		World = null;
	}
}

/// <summary>
/// 巢穴数据：记录地图上一个怪物刷新点的位置、刷新间隔和状态。
/// 现在存储在 ChunkData.Nests 中而非全局。
/// </summary>
public class NestData
{
	public int X { get; set; }
	public int Y { get; set; }
	public int SpawnInterval { get; set; } = 5;
	public int TurnsSinceSpawn { get; set; }
	public int MaxSpawned { get; set; } = 3;
	public string TemplateId { get; set; } = "";
}
