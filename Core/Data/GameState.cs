using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Event;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Data;

/// <summary>
/// 唯一数据源：所有可序列化的游戏运行时状态集中于此。
/// 地图使用三维无限 chunk 世界：WorldMap 管理所有地形和实体数据。
/// Actor 存在全局字典中，不存在 chunk 内。
/// </summary>
public class GameState
{
	public const string DefaultPlayerId = "player";
	public const string DefaultPlayerAppearanceId = "player1";
	private int _worldSeed = 42;
	private WorldMap? _world;

	public int Turn { get; set; }
	public int WorldSeed
	{
		get => _worldSeed;
		set
		{
			_worldSeed = value;
			Weather = WeatherState.CreateDefault(value);
		}
	}

	/// <summary>兼容旧模块的 RNG 种子属性，等同于 WorldSeed。</summary>
	public int RngSeed => WorldSeed;

	/// <summary>三维无限世界。不可序列化——存档时由 SaveModule 单独处理 dirty chunk。</summary>
	[JsonIgnore]
	public WorldMap? World
	{
		get => _world;
		set
		{
			_world = value;
			_world?.AttachFacilityState(Facilities);
		}
	}

	// ── 玩家三维坐标 ──
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
	public int PlayerZ { get; set; }

	// ── 生物 ──
	public Dictionary<string, Actor> Actors { get; set; } = new();
	public string PlayerId { get; set; } = DefaultPlayerId;
	public string PlayerAppearanceId { get; set; } = DefaultPlayerAppearanceId;
	public Dictionary<string, FacilityInstance> Facilities { get; set; } = new(StringComparer.Ordinal);
	public List<StockpileZone> StockpileZones { get; set; } = [];
	public Dictionary<string, EconomicDomain> EconomicDomains { get; set; } = new(StringComparer.Ordinal);
	public JobBoardState JobBoardState { get; set; } = new();

	// ── 队伍 ──
	public PartyState Party { get; set; } = new();

	// ── 故事讲述者 ──
	public StorytellerState StorytellerState { get; set; } = new();

	// ── 任务 ──
	public List<Quest> Quests { get; set; } = [];

	// ── 设置 ──
	public int KillCount { get; set; }
	public TimelineState Timeline { get; set; } = new();
	public WeatherState Weather { get; set; } = WeatherState.CreateDefault(42);
	public bool WatchMode { get; set; }
	public HashSet<string> IdentifiedActorTypes { get; set; } = new(StringComparer.Ordinal);
	public HashSet<string> IdentifiedItemTypes { get; set; } = new(StringComparer.Ordinal);

	/// <summary>当前地图生成器 ID（"room_corridor" / "perlin" / "cellular_automata" / "drunkard_walk" / "bsp"）。</summary>
	public string GeneratorId { get; set; } = "room_corridor";

	/// <summary>当前视图模式 ID（"single_layer" / "multi_layer"）。</summary>
	public string ViewModeId { get; set; } = "single_layer";

	public GameState()
	{
		EnsureDefaultEconomicDomains();
	}

	public void Reset()
	{
		Turn = 0;
		WorldSeed = 42;
		PlayerX = 0;
		PlayerY = 0;
		PlayerZ = 0;
		PlayerId = DefaultPlayerId;
		PlayerAppearanceId = DefaultPlayerAppearanceId;
		Actors.Clear();
		Facilities.Clear();
		StockpileZones.Clear();
		EconomicDomains.Clear();
		JobBoardState = new JobBoardState();
		Party = new PartyState();
		StorytellerState = new StorytellerState();
		Quests.Clear();
		KillCount = 0;
		Timeline.Reset();
		Weather = WeatherState.CreateDefault(WorldSeed);
		WatchMode = false;
		IdentifiedActorTypes.Clear();
		IdentifiedItemTypes.Clear();
		GeneratorId = "room_corridor";
		ViewModeId = "single_layer";
		World = null;
		EnsureDefaultEconomicDomains();
	}

	public void EnsureDefaultEconomicDomains()
	{
		if (!EconomicDomains.ContainsKey(DomainIds.Player))
		{
			EconomicDomains[DomainIds.Player] = new EconomicDomain
			{
				Id = DomainIds.Player,
				Name = "Player",
				Kind = EconomicDomainKind.Player,
			};
		}

		if (!EconomicDomains.ContainsKey(DomainIds.Public))
		{
			EconomicDomains[DomainIds.Public] = new EconomicDomain
			{
				Id = DomainIds.Public,
				Name = "Public",
				Kind = EconomicDomainKind.Public,
				IsPublic = true,
			};
		}
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
