using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 唯一数据源（Single Source of Truth）：所有可序列化的游戏运行时状态集中于此。
/// 地图使用格子栈模型：Cells[y][x] = List&lt;CellEntity&gt;，从底到顶排列。
/// Actor 不存在栈中，渲染时从 Actors 字典动态查询。
///
/// 设计约束：
/// - 仅包含数据，不包含行为逻辑（逻辑分散在各 Module 中）。
/// - 所有字段可 JSON 序列化，用于存档和楼层缓存。
/// - Core/ 层不依赖 Godot。
/// </summary>
public class GameState
{
	public int Turn { get; set; }
	public int RngSeed { get; set; } = 42;
	public int CurrentFloor { get; set; }

	// ── 当前楼层地图（格子栈） ──
	public int MapWidth { get; set; }
	public int MapHeight { get; set; }
	/// <summary>地图格子：Cells[y][x] = 实体栈，按 CellEntityType 从底到顶排列。</summary>
	public List<List<List<CellEntity>>> Cells { get; set; } = [];

	// ── 巢穴 ──
	public List<NestData> Nests { get; set; } = [];

	// ── 生物（玩家 + 怪物共用同一结构） ──
	public Dictionary<string, Actor> Actors { get; set; } = new();
	public string PlayerId { get; set; } = "player";

	// ── 玩家坐标快捷方式（与 Actors["player"] 同步） ──
	// REVIEW: PlayerX/Y 与 Actors[PlayerId].X/Y 是冗余数据，需要手动保持同步。
	//         每次 MoveActor 都要双写。如果某处遗漏，会导致渲染与逻辑位置不一致。
	//         考虑改为计算属性或移除该快捷字段。
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }

	// ── 设置 ──
	/// <summary>向敌对目标移动时视为攻击（默认开启）。</summary>
	public bool BumpAttack { get; set; } = true;

	/// <summary>看海模式：玩家由 AI 自动控制，世界自动推进。</summary>
	// REVIEW: WatchMode 是运行时 UI 状态，不属于「游戏逻辑状态」。
	//         序列化后加载会恢复 WatchMode，但 UI 端的 BrainId 未被存档同步，
	//         可能导致加载存档后看海模式状态不一致。
	public bool WatchMode { get; set; }

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
		Cells.Clear();
		Nests.Clear();
		Actors.Clear();
		PlayerX = 0;
		PlayerY = 0;
		BumpAttack = true;
		WatchMode = false;
		Floors.Clear();
	}
}

/// <summary>
/// 一层楼的完整快照，用于楼层切换时的缓存。
/// 与 GameState 中当前楼层的字段一一对应，SaveModule 负责双向拷贝。
/// </summary>
// REVIEW: FloorData 与 GameState 的地图字段高度重复，
//         每次新增地图字段都要同步修改两处 + SaveModule 的拷贝逻辑。
//         考虑让 GameState 直接持有一个 FloorData 引用来消除重复。
public class FloorData
{
	public int Width { get; set; }
	public int Height { get; set; }
	public List<List<List<CellEntity>>> Cells { get; set; } = [];
	public List<NestData> Nests { get; set; } = [];
	public Dictionary<string, Actor> Actors { get; set; } = new();
	public int PlayerX { get; set; }
	public int PlayerY { get; set; }
}

/// <summary>
/// 巢穴数据：记录地图上一个怪物刷新点的位置、刷新间隔和状态。
/// 由 NestModule.RegisterNests() 从地图 Fixture 扫描生成，
/// 每回合由 NestModule.Tick() 驱动刷怪逻辑。
/// </summary>
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
