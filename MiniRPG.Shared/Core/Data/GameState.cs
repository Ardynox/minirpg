using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Conversation;
using MiniRPG.Core.Event;
using MiniRPG.Core.Farm;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Social;
using MiniRPG.Core.World;
using MiniRPG.Core.Zone;

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
	public int RngSeed
	{
		get => WorldSeed;
		set => WorldSeed = value;
	}

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

	/// <summary>
	/// AI 过回合快照缓存：由 TimelineTurnManager.AdvanceAuto 在调用期间临时设置，
	/// 供 AIVisionBatch 复用 actor snapshot，避免每步重建。仅运行时字段，不序列化。
	/// </summary>
	[JsonIgnore]
	public TimelineSnapshotCache? SnapshotCache { get; set; }

	/// <summary>
	/// 回合级感知缓存：在一轮 AI 行动中批量构建所有敌人感知并缓存，
	/// 后续步骤直接复用，避免每步重建 O(N) 视觉扫描。仅运行时字段，不序列化。
	/// </summary>
	[JsonIgnore]
	public Dictionary<string, Perception>? PerceptionCache { get; set; }

	/// <summary>
	/// 回合级警觉上下文缓存：避免每步重新创建 AwarenessTurnContext。
	/// </summary>
	[JsonIgnore]
	public AwarenessTurnContext? AwarenessContextCache { get; set; }

	/// <summary>
	/// 回合级 AI 行为上下文缓存：避免每步重新分配 AIBehaviorContext 及其内部
	/// nearbyThreat / exposure 缓存字典。状态会跨步骤累积，但基于 (actorId, 坐标)
	/// 的缓存键保证正确性。
	/// </summary>
	[JsonIgnore]
	public AIBehaviorContext? BehaviorContextCache { get; set; }

	[JsonIgnore]
	public bool RuntimeFreeBuild { get; set; }

	[JsonIgnore]
	public bool RuntimeSurfaceFreeMove { get; set; }

	// ── 玩家三维坐标 ──
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
	public int PlayerZ { get; set; }

	// ── 生物 ──
	public Dictionary<string, Actor> Actors { get; set; } = new(StringComparer.Ordinal);
	public string PlayerId { get; set; } = DefaultPlayerId;
	public string PlayerAppearanceId { get; set; } = DefaultPlayerAppearanceId;
	public Dictionary<string, FacilityInstance> Facilities { get; set; } = new(StringComparer.Ordinal);
	public List<StockpileZone> StockpileZones { get; set; } = [];
	public Dictionary<string, EconomicDomain> EconomicDomains { get; set; } = new(StringComparer.Ordinal);
	public JobBoardState JobBoardState { get; set; } = new();
	public RoomRuntimeState Room { get; set; } = new();

	// ── 队伍 ──
	public PartyState Party { get; set; } = new();

	// ── 故事讲述者 ──
	public StorytellerState StorytellerState { get; set; } = new();

	// ── 社交 ──
	public SocialState SocialState { get; set; } = new();

	// ── 区域 ──
	public Dictionary<string, ZoneDef> Zones { get; set; } = new(StringComparer.Ordinal);

	// ── 农业 ──
	public Dictionary<string, CropInstance> Crops { get; set; } = new(StringComparer.Ordinal);

	// ── 任务 ──
	public List<Quest> Quests { get; set; } = [];

	// ── 对话（节点图，BG3 风格） ──
	/// <summary>当前活跃对话：键 = NPC actorId。权威状态，由 <see cref="ConversationModule"/> 维护。</summary>
	public Dictionary<string, ConversationState> ActiveConversations { get; set; }
		= new(StringComparer.Ordinal);

	// ── 人口学（demographics 跨 turn 状态）──
	/// <summary>上一次 <c>WorldDemographicsTick.Tick</c> 处理过的日历天索引。-1 = 从未处理过。
	/// 用于让"按天概率"的受孕 / 出生流程在 per-tick 调用环境下只在 day boundary 触发一次。
	/// 不入存档：读档后默认 -1 会让下一个 AdvanceWorldSystems 立即触发一次 OnNewDay，
	/// 由于每个 actor 状态独立检查（PregnancyTicksRemaining / Sex / 配对邻居），重复一次不会破坏一致性。</summary>
	[JsonIgnore]
	public int LastDemographicsDay { get; set; } = -1;

	// ── 设置 ──
	public int KillCount { get; set; }
	public TimelineState Timeline { get; set; } = new();
	public WeatherState Weather { get; set; } = WeatherState.CreateDefault(42);
	public bool WatchMode { get; set; }
	public HashSet<string> IdentifiedActorTypes { get; set; } = new(StringComparer.Ordinal);
	public HashSet<string> IdentifiedItemTypes { get; set; } = new(StringComparer.Ordinal);

	/// <summary>当前地图生成器 ID（"dwarf_fortress" / "room_corridor" / "perlin" / "cellular_automata" / "drunkard_walk" / "bsp" / "voxel_block_iso"）。</summary>
	public string GeneratorId { get; set; } = "dwarf_fortress";

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
		// 用全新的 ordinal 字典实例兜底：哪怕外部把 Actors 替换为非 ordinal dict
		// （历史上发生过），下一个会话也保证 actor id 用 ordinal 比较。
		Actors = new Dictionary<string, Actor>(StringComparer.Ordinal);
		// 顺手清掉跨会话的 NestModule.NestSpawnCounter，保证 actor id 序列从 0 起。
		// MapGenModule.InitializeWorld 也会再清一次，这里是状态层的对称。
		NestModule.ResetSpawnCounter();
		Facilities.Clear();
		StockpileZones.Clear();
		EconomicDomains.Clear();
		JobBoardState = new JobBoardState();
		Room = new RoomRuntimeState();
		Party = new PartyState();
		StorytellerState = new StorytellerState();
		SocialState = new SocialState();
		Zones.Clear();
		Crops.Clear();
		Quests.Clear();
		ActiveConversations.Clear();
		LastDemographicsDay = -1;
		KillCount = 0;
		Timeline.Reset();
		Weather = WeatherState.CreateDefault(WorldSeed);
		WatchMode = false;
		RuntimeFreeBuild = false;
		RuntimeSurfaceFreeMove = false;
		IdentifiedActorTypes.Clear();
		IdentifiedItemTypes.Clear();
		GeneratorId = "dwarf_fortress";
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
