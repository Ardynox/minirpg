# MiniRPG 架构文档

> 本文档描述项目中每个类的职责、上游依赖和下游调用关系，以及完整的调用链。

---

## 一、各类职责说明

### 入口层

#### Main（`Main.cs`）

Godot 场景入口节点，充当胶水层。上游：Godot 引擎（`_Ready`/`_Process`/`_Input`/`_UnhandledInput`）。下游：初始化并持有所有 Module 实例，将用户输入路由到 `InputModule`，将 `InputModule` 产出的命令字符串分发到各功能模块，将 Core 层产出的 `GameEvent` 通过 `Dispatch` 路由到 `EventLogModule`（日志翻译）和 `CombatUIModule`/`TradeUIModule`（流程 UI），驱动 `MapRenderModule.Flush` 刷新画面。本身职责是生命周期管理、命令路由和事件分发，不包含具体游戏逻辑。

---

### Module 层（UI / 表现 / 流程编排）

#### InputModule（`Module/InputModule.cs`）

键盘输入状态机。上游：`Main._UnhandledInput` 传入 Godot 按键事件。下游：通过 `CommandReceived` 事件向 `Main.OnCommand` 发送命令字符串，通过 `OnStatusCommand`/`OnInventoryCommand`/`OnChestCommand` 委托直接调用对应面板模块的操作方法。本身职责是管理多种焦点模式（Action/Typing/Status/Inventory/Chest/Selection/Direction）下的按键映射与命令分发。

#### MenuModule（`Module/MenuModule.cs`）

主菜单与设置面板。上游：`Main._Ready` 创建并订阅事件。下游：通过 `OnContinue`/`OnNewGame`/`OnLoadGame`/`OnQuit`/`OnBackToMenu` 事件回调 `Main` 的菜单处理方法。本身职责是管理三态 UI 切换（MainMenu ↔ Settings ↔ InGame），连接 Godot 按钮信号到事件。

#### GameSessionModule（`Module/GameSessionModule.cs`）

游戏会话生命周期。上游：`Main` 在菜单回调和命令处理中调用。下游：调用 `MapGenModule.InitializeWorld`/`SpawnPlayer` 创建世界，调用 `SaveModule` 存读档，调用 `MapModule` 执行楼梯逻辑，调用 `FogOfWarTracker.Clear` 重置迷雾。本身职责是封装新建游戏、加载/保存存档、楼层切换、视图模式同步等会话级操作，不涉及 UI。

#### MapRenderModule（`Module/MapRenderModule.cs`）

地图渲染管线。上游：`Main.FlushMap` 每帧或每次状态变更时调用。下游：调用 `FogOfWarTracker.Update` 更新可见性，通过 `IViewMode.BuildDisplayMap` 获取字符矩阵，调用 `RenderModule.RenderMap` 生成 BBCode，调用 `MinimapModule.Render`/`FogMapModule.Render` 叠加小地图/大地图。本身职责是协调 FOV → 视图模式 → 渲染 → 输出到 RichTextLabel 的完整管线。

#### RenderModule（`Module/RenderModule.cs`）

地图文本渲染器。上游：`MapRenderModule.Flush` 调用。下游：无（纯输出）。本身职责是将二维字符矩阵转为 ASCII（BBCode 着色）或 Emoji 字符串，处理迷雾/记忆/周边态的色彩映射。

#### MinimapModule（`Module/MinimapModule.cs`）

小地图。上游：`MapRenderModule` 在 `Flush` 中调用。下游：读取 `FogOfWarTracker` 的可见性数据和 `GameState` 的 Actor 位置。本身职责是以 BBCode 彩色字符渲染玩家周围缩略区域。

#### FogMapModule（`Module/FogMapModule.cs`）

全屏大地图。上游：`MapRenderModule` 在 `Flush` 中调用。下游：读取 `FogOfWarTracker` 的已探索数据和 `GameState` 的世界数据。本身职责是以 BBCode 富文本显示已探索区域的全局缩略地形。

#### FogOfWarTracker（`Module/FogOfWarTracker.cs`）

战争迷雾追踪器。上游：`MapRenderModule.Flush` 中调用 `Update`，`GameSessionModule` 中调用 `Clear`。下游：调用 `ShadowcastFOV.Compute`/`ComputeDirectional` 计算视野，读取 `ActorModule.GetPlayer` 获取玩家位置和朝向。本身职责是维护每个格子的四态可见性（朝向可见 / 周边感知 / 已探索 / 未探索）。

#### LogModule（`Module/LogModule.cs`）

游戏日志。上游：`Main` 和各 UI Module 通过 `Add` 写入消息。下游：写入 Godot `RichTextLabel`。本身职责是维护带上限裁剪的日志行列表和显示。

#### EventLogModule（`Module/EventLogModule.cs`）

事件日志翻译。上游：`Main.Dispatch` 对每个 `GameEvent` 调用。下游：读取 `GameState` 和 `ActorModule` 获取名称信息。本身职责是纯函数，将 `GameEvent` 翻译为人类可读的中文日志文本，不持有状态。

#### CombatUIModule（`Module/CombatUIModule.cs`）

战斗 UI 流程。上游：`Main.Dispatch` 在 `combat_bump`/`actor_killed` 事件时调用。下游：通过 `IGameUI` 接口回调 `Main` 的 `AddLog`/`EnterSelection`/`Dispatch`/`FlushMap`，调用 `CombatModule` 计算伤害，调用 `SkillQuery` 查询可用攻击。本身职责是编排战斗碰撞、动作/肢体选择菜单、怪物反击和击杀掉落的完整 UI 流程。

#### TradeUIModule（`Module/TradeUIModule.cs`）

交易 UI 流程。上游：`Main.DispatchInteraction` 在 `trade` 类型交互事件时调用。下游：通过 `IGameUI` 接口回调 `Main`，调用 `TradeModule` 执行买卖。本身职责是编排商店浏览、购买、出售物品的文本选择交互流程。

#### InventoryUIModule（`Module/InventoryUIModule.cs`）

文本模式背包 UI（旧版）。上游：`Main` 在需要文本式物品选择时调用。下游：通过 `IGameUI` 调用 `Main`，调用 `InventoryModule` 操作物品。本身职责是基于文本数字选择的背包交互流程。

#### StatusPanelModule（`Module/StatusModule.cs`）

状态面板。上游：`Main.RefreshStatus` 每次地图刷新后调用 `Refresh`，`InputModule.OnStatusCommand` 驱动键盘导航。下游：读取 `Actor` 数据和 `PresetDB` 查询能力/种族信息。本身职责是多 Tab 页显示玩家肢体、能力、标记、Buff、装备详情。

#### SkillPanelModule（`Module/SkillPanelModule.cs`）

技能面板。上游：`Main.RefreshStatus` 调用 `Refresh`。下游：调用 `SkillQuery.GetAll` 获取玩家所有技能。本身职责是按战斗/实用/社交分类渲染技能列表。

#### InventoryPanelModule（`Module/InventoryPanelModule.cs`）

背包面板。上游：`Main.RefreshStatus` 在背包打开时调用 `Refresh`，`InputModule.OnInventoryCommand` 驱动键盘操作。下游：通过 `IHost` 接口回调 `Main` 的 `AddLog`/`Dispatch`/`FlushMap`/`OpenChestFromInventory`，调用 `InventoryModule`/`InteractionModule` 执行物品操作。本身职责是背包 UI 的分类过滤、排序、装备/使用/丢弃/打开容器。

#### GroundPanelModule（`Module/GroundPanelModule.cs`）

地面物品面板。上游：`Main.RefreshStatus` 调用 `Refresh`。下游：通过 `IHost` 接口回调 `Main` 的 `PickupGroundItem`/`OpenChestPanel`，调用 `MapModule.PeekGroundItems` 查询脚下物品。本身职责是展示脚下物品列表，支持拾取/全部拾取/打开容器，带位置缓存避免重复查询。

#### ChestPanelModule（`Module/ChestPanelModule.cs`）

宝箱面板。上游：`Main.OpenChestPanel` 打开，`InputModule.OnChestCommand` 驱动操作。下游：通过 `IHost` 接口回调 `Main` 的 `AddLog`/`FlushMap`/`CloseChestPanel`/`OpenPutIntoChestSelection`，调用 `InventoryModule` 操作物品转移。本身职责是宝箱内容展示和取出/放入物品操作。

#### DebugCommandHandler（`Module/DebugCommandHandler.cs`）

调试命令处理器。上游：`Main.HandleDebugCommand` 调用。下游：调用 `DebugModule` 的各作弊方法，调用 `GameSessionModule.ChangeFloor` 切换楼层。本身职责是解析 `/` 前缀调试命令并路由到对应的 Core 调试方法。

#### LookModule（`Module/LookModule.cs`）

环境查看。上游：`Main.DoLook` 调用。下游：读取 `GameState`、`ActorModule`、`MapModule` 获取周围信息。本身职责是构建玩家周围环境的描述文本（坐标、肢体状态、地面物品、相邻格子）。

#### IGameUI（`Module/IGameUI.cs`）

UI 通信契约接口。上游：`CombatUIModule`/`TradeUIModule`/`InventoryUIModule` 持有引用。下游：由 `Main` 实现。本身职责是定义 UI 模块与胶水层之间的通信能力（日志、选择、地图刷新、事件分发）。

#### 辅助工具类

| 类 | 职责 | 上游 | 下游 |
|---|---|---|---|
| `ItemFormatHelper` | 物品文本格式化（单行摘要 + BBCode 详情） | 面板模块 | `Item`、`InteractionDefs` |
| `RowStyleHelper` | Button 行统一样式和滚动定位 | 面板模块 | `UIColors`（缓存 StyleBoxFlat） |
| `PanelBorderHelper` | 面板焦点边框样式 | `Main.RefreshAllBorders` | `UIColors` |
| `UIColors` | 全局 UI 颜色常量 | 所有面板/辅助类 | 无 |
| `SingleLayerViewMode` | 单层视图模式 | `MapRenderModule` | `GameState` |
| `MultiLayerViewMode` | 多层透视视图模式 | `MapRenderModule` | `GameState` |

---

### Core 层（纯游戏逻辑，不依赖 Godot UI）

#### GameState（`Core/GameState.cs`）

全局可序列化状态。上游：所有 Core/Module 类读写。下游：持有 `WorldMap` 和 `Actors` 字典。本身职责是作为唯一数据源，集中管理所有游戏运行时状态（回合、玩家坐标、Actor 列表、世界地图等）。

#### GameEvent（`Core/GameEvent.cs`）

事件载体。上游：Core 层各 Module（`ActionModule`/`CombatModule`/`InteractionModule` 等）产出。下游：`Main.Dispatch` 消费并路由到 UI 层。本身职责是 Core → UI 的单向通信载体，携带事件类型和上下文数据。

#### Actor（`Core/Actor.cs`）

生物实体数据模型。上游：`ActorModule`/`PresetDB` 创建和管理。下游：被 `CombatModule`/`InventoryModule`/`SkillQuery`/`InteractionModule` 等读写。本身职责是统一的生物数据结构，通过 Tag 聚合系统（种族/职业/肢体/装备/Buff/经历）实时计算能力属性。

#### ActorModule（`Core/ActorModule.cs`）

Actor 增删查改。上游：几乎所有需要查找/移动 Actor 的模块。下游：操作 `GameState.Actors` 字典。本身职责是提供 Actor 的纯函数 CRUD 操作集。

#### ActionModule（`Core/ActionModule.cs`）

统一行动入口。上游：`Main.DoMove` 调用 `TryMove`，`AIDispatcher` 调用 `TryMove`/`TryAttack`/`TryInteract`。下游：调用 `MapModule` 检查地形，调用 `CombatModule` 执行攻击，调用 `InteractionModule` 判断碰撞交互。本身职责是作为玩家和 AI 共用的移动/攻击/交互入口，产出 `GameEvent` 列表。

#### TurnModule（`Core/TurnModule.cs`）

回合推进引擎。上游：`Main.DoMove` 和 `WatchModeTick` 调用。下游：调用 `NestModule.Tick` 刷怪，调用 `AIDispatcher.TickAll` 驱动 AI。本身职责是推进回合计数、触发巢穴刷怪和 AI 行动。

#### CombatModule（`Core/CombatModule.cs`）

战斗伤害计算。上游：`ActionModule.TryAttack`、`CombatUIModule`、`AIDispatcher` 调用。下游：调用 `InventoryModule` 处理掉落，调用 `ActorModule` 移除死亡 Actor，读取 `PresetDB` 查询能力。本身职责是基于肢体耐久/材质/护甲的伤害结算系统，包含致命状态检查。

#### InteractionModule（`Core/InteractionModule.cs`）

交互执行引擎。上游：`Main.DoInteract`/`HandleDigDirection`、`ActionModule` 调用。下游：调用 `MapModule` 操作地图实体，调用 `InventoryModule` 处理物品，调用 `DigModule` 执行挖掘。本身职责是目标扫描、条件过滤、执行交互/拾取/丢弃/挖掘并产出事件。

#### InventoryModule（`Core/InventoryModule.cs`）

背包与装备管理。上游：面板模块和 `InteractionModule`/`CombatModule`/`TradeModule` 调用。下游：操作 `Actor.Inventory` 和 `EquipSlot`。本身职责是物品增删、装备/卸下、使用消耗品、肢体摧毁时掉落。

#### TradeModule（`Core/TradeModule.cs`）

交易系统。上游：`TradeUIModule` 调用。下游：调用 `InventoryModule` 转移物品，操作 `Actor.Gold` 和 `ShopSlots`。本身职责是买卖物品和金币结算。

#### MapModule（`Core/MapModule.cs`）

地图代理层。上游：几乎所有需要读写地图的模块。下游：将调用转发到 `WorldMap` API。本身职责是封装 `WorldMap` 的 chunk-based API，提供兼容旧代码的简化 2D/3D 接口。

#### MapGenModule（`Core/MapGenModule.cs`）

地图生成适配器。上游：`GameSessionModule.NewGame` 调用。下游：调用 `WorldMap`/`ChunkManager` 初始化世界，注册并调用各 `IMapGenerator` 实现。本身职责是注册地图生成器、初始化 WorldMap、生成玩家出生点。

#### SaveModule（`Core/SaveModule.cs`）

存档系统。上游：`GameSessionModule.SaveGame`/`LoadGame` 调用。下游：序列化/反序列化 `GameState`/`Actor`/`WorldMap`/`ChunkData` 到 JSON 文件。本身职责是全局状态持久化和 dirty chunk 增量保存/加载。

#### NestModule（`Core/NestModule.cs`）

巢穴刷怪。上游：`TurnModule.Tick` 每回合调用。下游：调用 `ActorModule.Add` 添加怪物，调用 `ActorTemplates.Spawn` 实例化。本身职责是遍历已加载 chunk 中的巢穴，按间隔刷出怪物。

#### SkillQuery（`Core/SkillQuery.cs`）

技能查询引擎。上游：`CombatUIModule`/`CombatModule`/`SkillPanelModule`/`Main.StartDig` 调用。下游：读取 `Actor` 的 Tag 和 `InteractionDefs` 注册表。本身职责是根据 Actor 的 Tag/能力/装备筛选可用交互技能。

#### DebugModule（`Core/DebugModule.cs`）

调试功能。上游：`DebugCommandHandler` 调用。下游：调用 `ActorModule`/`ActorTemplates`/`PresetDB` 生成实体和物品。本身职责是提供生成宝箱、加金币、满血、刷怪、无敌模式等作弊方法。

#### PresetDB（`Core/PresetDB.cs`）

预设数据库。上游：`Main._Ready` 调用 `Load` 初始化。下游：从 JSON 文件加载种族/肢体/职业/物品/Actor/交互/能力定义。本身职责是游戏数据的中央注册表，提供模板实例化和 ID 查询 API。

#### 数据结构类

| 类 | 职责 |
|---|---|
| `Item` | 物品数据模型（可交易/装备/嵌套容器），装备后参与 Tag 聚合 |
| `InteractionDef` | 交互行为定义（战斗/社交/工具），含条件和效果参数 |
| `InteractionDefs` | 交互定义注册表，代理 PresetDB |
| `ActorTemplate` | Actor 模板入口，委托 PresetDB 实例化 |
| `CapacityDef` | 能力定义（视觉/操纵等），含致命阈值和依赖乘数 |
| `CellEntity` | 格子栈实体（地形/设施/物品等分层信息） |
| `MaterialDef` / `MaterialRegistry` | 材质定义与注册表（硬度/可燃/耐腐蚀） |
| `Const.cs` 各常量类 | 全局字符串/枚举常量（阵营/实体/地形/能力/伤害类型等） |
| `TagSystem.cs` 各类 | Tag 聚合系统的数据类型（`Limb`/`Race`/`Profession`/`Buff`/`Experience`/`EquipSlot`） |

---

### Core/World 层（世界地图系统）

#### WorldMap（`Core/World/WorldMap.cs`）

三维世界统一 API。上游：`MapModule` 转发所有地图操作。下游：将读写路由到 `ChunkManager`/`ChunkData`。本身职责是暴露地形/实体/Actor 的完整 3D 访问接口，屏蔽 chunk 分割细节。

#### ChunkManager（`Core/World/ChunkManager.cs`）

Chunk 生命周期管理。上游：`WorldMap` 在地图操作时调用 `GetOrLoad`，`Main.DoMove` 后调用 `UpdateLoadedChunks`。下游：调用 `IMapGenerator` 生成新 chunk，调用 `SaveModule` 缓存卸载的 chunk，调用 `IChunkSimulator` 模拟远处 chunk。本身职责是按需生成、LRU 缓存、加载/卸载 chunk。

#### ChunkData（`Core/World/ChunkData.cs`）

单个 32×32 chunk 数据容器。上游：`ChunkManager`/`WorldMap` 读写。下游：无（纯数据）。本身职责是存储地形 ID、硬度、稀疏实体栈和 Actor 引用。

#### ShadowcastFOV（`Core/World/ShadowcastFOV.cs`）

视野计算算法。上游：`FogOfWarTracker.Update` 调用。下游：无（纯算法，通过委托 `IsOpaqueFunc` 解耦）。本身职责是递归对称 Shadowcasting，支持全向和朝向两种 FOV 模式。

#### Pathfinding（`Core/World/Pathfinding.cs`）

寻路算法。上游：`SimpleBrain.Decide` 调用。下游：无（纯算法，通过委托 `IsWalkableFunc` 解耦）。本身职责是统一寻路入口，短距离 A*，长距离 JPS。

#### DigModule（`Core/World/DigModule.cs`）

地形破坏。上游：`InteractionModule.ExecuteDig` 调用。下游：操作 `WorldMap` 修改地形。本身职责是通过硬度系统逐步破坏并转变地形。

#### TerrainDef / TerrainRegistry（`Core/World/TerrainDef.cs`）

地形定义与注册表。上游：`Main._Ready` 调用 `Load` 初始化。下游：从 JSON 加载地形数据。本身职责是维护 ushort ID 与 StringId 的双向映射和地形属性查询。

#### WorldCoord / ChunkCoord / CoordUtil（`Core/World/WorldCoord.cs`）

坐标系统。上游：所有涉及坐标转换的模块。下游：无（纯数据/纯函数）。本身职责是定义世界/chunk 坐标结构体和坐标系转换工具。

#### IMapGenerator 与各生成器

| 类 | 风格 |
|---|---|
| `RoomCorridorGenerator` | 经典矩形房间 + L 形走廊 |
| `BSPGenerator` | BSP 二叉空间分割，规整对称布局 |
| `CellularAutomataGenerator` | Perlin + 细胞自动机平滑，有机洞穴 |
| `DrunkardWalkGenerator` | 多随机行走者在岩石中开辟隧道 |
| `PerlinGenerator` | Perlin 噪声高度图（地表）/ 3D 密度场（地下） |
| `SurfaceGenerator`（共享） | z=0 地表生成，双层 Perlin 分类地形 |

上游：`ChunkManager.GetOrLoad` 触发生成。下游：写入 `ChunkData`。所有地下生成器在 z=0 时委托给 `SurfaceGenerator`。

#### 其他接口

| 接口 | 职责 |
|---|---|
| `IViewMode` | 视图投影契约，决定如何将 3D 世界投影为 2D 字符矩阵 |
| `IChunkSimulator` | 远距离 chunk 简化模拟契约（当前 `NullSimulator` 空操作） |

---

### Core/AI 层（人工智能）

#### AIDispatcher（`Core/AI/AIDispatcher.cs`）

AI 调度中枢。上游：`TurnModule.Tick` 调用 `TickAll`。下游：调用 `PerceptionBuilder.Build` 构造感知，调用 `IBrainModule.Decide` 获取决策，调用 `ActionModule` 执行动作。本身职责是每回合按距离分级为所有 NPC 构造感知、调用大脑决策、执行动作。

#### SimpleBrain（`Core/AI/SimpleBrain.cs`）

MVP 优先级大脑。上游：`AIDispatcher` 调用 `Decide`。下游：调用 `Pathfinding.NextStep` 寻路，调用 `CombatModule.MonsterChooseAction` 选择攻击。本身职责是简单状态机：相邻敌人→攻击，感知范围有敌人→寻路，否则→随机漫步。

#### PerceptionBuilder（`Core/AI/PerceptionBuilder.cs`）

感知构造器。上游：`AIDispatcher` 调用。下游：读取 `GameState` 和 `MapModule` 获取周围信息。本身职责是根据模拟精度从世界状态中为指定 Actor 构造局部感知数据。

#### IBrainModule / 数据模型（`Core/AI/IBrainModule.cs`）

AI 核心数据模型。定义 `Decision`（决策）、`Perception`（感知）、`SimDetail`（模拟精度）、`DecisionType`（决策类型）和 `FactionRelation`（阵营敌对关系）。

---

## 二、调用链总览

### 1. 主循环

```
Godot Engine
  │
  ├─ _Ready()
  │    ├─ PresetDB.Load()
  │    ├─ TerrainRegistry.Load()
  │    ├─ new GameSessionModule(state, fogTracker)
  │    ├─ new MenuModule(this)
  │    ├─ new MapRenderModule(state, fogTracker, renderModule, ...)
  │    ├─ new InputModule(lineEdit)
  │    ├─ new StatusPanelModule / SkillPanelModule / InventoryPanelModule / ...
  │    ├─ new CombatUIModule / TradeUIModule / InventoryUIModule
  │    └─ MenuModule.ShowMainMenu()
  │
  ├─ _Process(delta)
  │    └─ WatchModeTick() → TurnModule.TickWatchMode → Dispatch → FlushMap
  │
  ├─ _UnhandledInput(event)
  │    └─ InputModule.HandleKeyInput()
  │         └─ CommandReceived → Main.OnCommand()
  │
  └─ _Input(event)
       └─ 鼠标点击面板焦点切换
```

### 2. 命令处理链

```
InputModule.CommandReceived
  └─ Main.OnCommand(cmd)
       ├─ "w/a/s/d"  → DoMove(dx,dy)
       │                  ├─ ActionModule.TryMove() → List<GameEvent>
       │                  ├─ TurnModule.Tick()      → List<GameEvent>
       │                  ├─ Dispatch(events)
       │                  ├─ ChunkManager.UpdateLoadedChunks()
       │                  └─ FlushMap()
       │
       ├─ ":interact" → DoInteract()
       │                  ├─ InteractionModule.GetAvailableTargets()
       │                  ├─ ShowInteractionsFor() → InteractionModule.Execute()
       │                  └─ GroundPanel.DoInteractSelected()
       │
       ├─ ":dig"      → StartDig() → HandleDigDirection()
       │                  ├─ SkillQuery.GetCellSkills()
       │                  ├─ InteractionModule.ExecuteDig()
       │                  └─ TurnModule.Tick()
       │
       ├─ "enter"     → DoEnterStairs()
       │                  └─ GameSessionModule.TryUseStairs()
       │
       ├─ ":settings" → MenuModule.ToggleSettings()
       ├─ ":inventory"→ ToggleInventory()
       ├─ ":quicksave"→ GameSessionModule.SaveGame()
       ├─ ":quickload"→ GameSessionModule.LoadGame()
       └─ "/xxx"      → DebugCommandHandler.Handle()
                          └─ DebugModule.xxx()
```

### 3. 事件分发链

```
Core 逻辑产出 List<GameEvent>
  └─ Main.Dispatch(events)
       ├─ EventLogModule.ToLogLines(event)  → LogModule.Add()
       │
       ├─ "combat_bump"       → CombatUIModule.HandleCombatBump()
       │                          ├─ CombatModule.Attack()
       │                          └─ MonsterCounterAttack()
       │
       ├─ "actor_killed"      → CombatUIModule.HandleActorKilled()
       │   (玩家死亡时)        → Main.HandlePlayerDeath()
       │
       ├─ "interaction"       → DispatchInteraction()
       │                          ├─ "trade"  → TradeUIModule.OpenTradeMenu()
       │                          ├─ "combat" → CombatUIModule.OpenCombatMenu()
       │                          └─ "talk"/"tame" → LogModule.Add()
       │
       └─ "item_picked_up/dropped" → GroundPanelModule.Invalidate()
```

### 4. 渲染管线

```
Main.FlushMap()
  └─ MapRenderModule.Flush()
       ├─ FogOfWarTracker.Update(state)
       │    ├─ ShadowcastFOV.Compute()          → fullVisible
       │    └─ ShadowcastFOV.ComputeDirectional() → directionalVisible
       │
       ├─ IViewMode.BuildDisplayMap(state)
       │    ├─ SingleLayerViewMode  (当前层)
       │    └─ MultiLayerViewMode   (当前层 + 下层透视)
       │
       ├─ RenderModule.RenderMap(displayMap)    → BBCode/Emoji
       │
       ├─ MinimapModule.Render(state)           → 小地图叠加
       └─ FogMapModule.Render(state)            → 大地图叠加
  │
  └─ Main.RefreshStatus()
       ├─ StatusPanelModule.Refresh()
       ├─ SkillPanelModule.Refresh()
       ├─ InventoryPanelModule.Refresh()
       ├─ GroundPanelModule.Refresh()
       └─ PanelBorderHelper.Apply() × N
```

### 5. AI 回合

```
TurnModule.Tick(state)
  ├─ state.Turn++
  ├─ NestModule.Tick()
  │    └─ ActorTemplates.Spawn() → ActorModule.Add()
  │
  └─ AIDispatcher.TickAll()
       └─ per NPC actor:
            ├─ PerceptionBuilder.Build(state, actor, detail)
            ├─ SimpleBrain.Decide(perception, rng)
            │    ├─ 相邻敌人 → Decision.Attack
            │    ├─ 感知敌人 → Pathfinding.NextStep() → Decision.Move
            │    └─ 否则     → Decision.Wander
            │
            └─ ActionModule.TryMove / TryAttack()
                 └─ → List<GameEvent>
```

### 6. 会话生命周期

```
MenuModule 事件
  ├─ OnNewGame  → Main.DoStartNewGame()
  │                 └─ GameSessionModule.NewGame()
  │                      ├─ GameState.Reset()
  │                      ├─ MapGenModule.InitializeWorld()
  │                      ├─ MapGenModule.SpawnPlayer()
  │                      └─ FogOfWarTracker.Clear()
  │
  ├─ OnContinue → GameSessionModule.TryContinue()
  │                 └─ SaveModule.LoadGame(quicksave)
  │
  ├─ OnLoadGame → GameSessionModule.TryLoadGame()
  │                 └─ SaveModule.LoadGame(manual)
  │
  └─ OnBackToMenu → GameSessionModule.SaveGame(quicksave)
                     └─ SaveModule.SaveGame()
```

### 7. 分层架构

```
┌─────────────────────────────────────────────────────┐
│                    Godot Engine                      │
├─────────────────────────────────────────────────────┤
│  Main.cs (胶水层)                                    │
│  ├─ 生命周期 (_Ready/_Process/_Input)                │
│  ├─ 命令路由 (OnCommand)                             │
│  └─ 事件分发 (Dispatch)                              │
├─────────────────────────────────────────────────────┤
│  Module 层 (UI / 表现 / 流程编排)                     │
│  ├─ 流程: Menu / GameSession / Input                 │
│  ├─ 渲染: MapRender / Render / Minimap / FogMap      │
│  ├─ 面板: Status / Skill / Inventory / Ground / Chest│
│  ├─ 战斗UI: CombatUI / TradeUI / InventoryUI        │
│  ├─ 日志: Log / EventLog / Look                      │
│  └─ 工具: RowStyle / PanelBorder / ItemFormat / UIColors│
├─────────────────────────────────────────────────────┤
│  Core 层 (纯游戏逻辑，不依赖 Godot UI)               │
│  ├─ 行动: Action / Turn / Combat / Interaction       │
│  ├─ 实体: Actor / ActorModule / Inventory / Trade    │
│  ├─ 数据: GameState / GameEvent / Item / PresetDB    │
│  ├─ 地图: MapModule / MapGen / Save / Nest           │
│  ├─ AI:   AIDispatcher / SimpleBrain / Perception    │
│  └─ 世界: WorldMap / ChunkManager / Pathfinding / FOV│
├─────────────────────────────────────────────────────┤
│  Core/World 层 (地图基础设施)                         │
│  ├─ 存储: ChunkData / WorldCoord / TerrainDef        │
│  ├─ 生成: IMapGenerator × 5 + SurfaceGenerator      │
│  └─ 算法: ShadowcastFOV / Pathfinding / DigModule    │
└─────────────────────────────────────────────────────┘
```

---

## 三、数据流向

```
用户输入 → InputModule → Main.OnCommand
                            │
                            ▼
                     Core 层纯函数
              (Action/Combat/Interaction/Turn)
                            │
                            ▼
                    List<GameEvent>
                            │
                            ▼
                     Main.Dispatch
                     ┌──────┴──────┐
                     ▼             ▼
              EventLogModule   CombatUI / TradeUI
              (日志翻译)       (流程编排)
                     │             │
                     ▼             ▼
               LogModule      IGameUI 回调
              (显示日志)      (选择/日志/刷新)
                                   │
                                   ▼
                            Main.FlushMap
                                   │
                                   ▼
                          MapRenderModule
                     (FOV → ViewMode → Render)
                                   │
                                   ▼
                          RichTextLabel 显示
```
