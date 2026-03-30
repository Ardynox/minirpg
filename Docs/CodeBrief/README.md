# MiniRPG C# 代码简明文档（给 AI 评估用）

这份文档面向"快速评估/接手"：按文件给出**职责一句话**、**关键数据结构/入口**、**重要约束与边界**、**运行时主流程**。

## 架构约束（摘自 `ARCHITECTURE.md`）

- **唯一数据源**：`Core/GameState.cs` 的 `GameState` 是所有游戏数据的唯一来源（可序列化 JSON）
- **逻辑/副作用分离（当前项目是 C# 版本）**：
  - 逻辑模块：`Core/*Module.cs` 以"纯数据 + 事件"的形式驱动状态变化
  - 副作用：Godot UI/输入/渲染集中在 `Main.cs` + `Module/*`
- **事件**：逻辑层产出 `Core/GameEvent.cs`，由 `Main.cs` 消费（日志/渲染等）

## 快速入口

- **运行入口（Godot 场景脚本）**：`Main.cs`
- **状态容器**：`Core/GameState.cs`
- **核心交互（移动）**：`Core/MapModule.cs` 的 `TryMovePlayer`
- **地图生成**：`Core/MapGenModule.cs`
- **巢穴刷怪**：`Core/NestModule.cs`
- **存档/读档 + 楼层缓存**：`Core/SaveModule.cs`
- **Actor + tag/动作系统**：`Core/Actor.cs`、`Core/TagSystem.cs`、`Core/ActionDefs.cs`
- **战斗系统**：`Core/CombatModule.cs`
- **交互系统**：`Core/InteractionModule.cs`、`Core/InteractionDefs.cs`
- **背包系统**：`Core/InventoryModule.cs`
- **交易系统**：`Core/TradeModule.cs`

## 文件索引

### Godot 驱动层 / 副作用层

- `Main.cs`：Godot 节点脚本；输入→命令→调用逻辑→消费事件→渲染/日志；菜单/设置/存读档/交互/交易/背包
- `Module/InputModule.cs`：键盘与文本输入统一成 command 事件；支持动作模式/打字模式/选择模式
- `Module/RenderModule.cs`：地图渲染（ASCII BBCode / Emoji 两种模式）

### 逻辑/状态层（纯数据优先）

- `Core/GameState.cs`：全局状态 + 楼层快照结构
- `Core/GameEvent.cs`：事件数据结构（包含战斗/交互/交易等多种事件类型）
- `Core/MapModule.cs`：四层地图读写/查询/玩家移动/楼层切换
- `Core/MapGenModule.cs`：随机房间地图生成 + 放置玩家/楼梯/巢穴/怪物/NPC
- `Core/NestModule.cs`：巢穴刷新怪物（按回合 Tick）
- `Core/SaveModule.cs`：全局存档（JSON）+ 楼层切换快照（内存）
- `Core/Actor.cs`：统一 Actor 数据结构；tag 由多个来源实时计算；包含背包、商店、Buff
- `Core/ActorTemplate.cs`：Actor 模板注册表（玩家/怪物/NPC 工厂）
- `Core/ActorModule.cs`：Actor 增删移动；同步地图 Objects 层
- `Core/TagSystem.cs`：tag 来源接口 + 动作定义 + 动作筛选（ActionQuery）
- `Core/ActionDefs.cs`：内置动作表（数据驱动）
- `Core/CombatModule.cs`：基于肢体耐久的伤害系统；怪物 AI
- `Core/InteractionDef.cs`、`Core/InteractionDefs.cs`：交互定义（对话/交易/攻击/驯服）
- `Core/InteractionModule.cs`：交互检测与执行
- `Core/InventoryModule.cs`：背包操作（添加/移除/装备/使用/丢弃）
- `Core/Item.cs`：物品数据结构；商店货架槽位
- `Core/TradeModule.cs`：交易系统（买/卖）

## 运行时主流程（简版）

- Godot `_Ready` 初始化 UI、`InputModule`、`RenderModule`，进入主菜单
- 游戏开始或读档：
  - `GameState.Reset()`（新开） → `MapGenModule.Generate()` → `EnsurePlayerActor()`
  - 或 `SaveModule.LoadGame()` → `EnsurePlayerActor()`
- 每次移动：
  - `MapModule.TryMovePlayer()` 产出事件（撞墙/移动/攻击击杀）
  - `state.Turn++`
  - `NestModule.Tick()` 可能刷怪并产出事件
  - `Main.Dispatch()` 消费事件写日志；`FlushMap()` 触发渲染
- 交互流程：
  - `F` 键触发 `DoInteract()`
  - 扫描玩家周围可交互目标
  - `InteractionModule.GetInteractions()` 过滤可用交互
  - `InteractionModule.Execute()` 执行并产出事件
  - `Main.DispatchInteraction()` 处理交易等副作用
- 战斗流程：
  - 撞击怪物 → `HandleCombatBump()` → `CombatModule.Attack()` → 怪物反击 → `TickAllBuffs()`

## 给评估 AI 的关注点（建议）

- **状态一致性**：`GameState.PlayerX/Y` 与 `Actors[player].X/Y` 与 `Objects` 层同步点在哪里、是否可能不同步
- **模块边界**：`MapModule`/`ActorModule`/`SaveModule` 的职责是否清晰，是否有耦合点（例如 `MapModule` 直接调用 `SaveModule`）
- **事件表达力**：`GameEvent` 当前字段覆盖战斗/交互/交易，但仍需扩展以支持更复杂的场景
- **性能**：`Actor.ComputeTags()` 每次现算；`ActionQuery` 过滤动作是否频繁调用
- **存档正确性**：楼层缓存与全局 JSON 两套结构是否一致；多态 JSON 的处理是否完备
- **战斗系统**：基于肢体耐久的伤害系统，肢体耐久归零会断裂，要害肢体断裂会导致死亡
- **交易系统**：商人拥有 ShopSlots，玩家可买可卖，售价为原价的一半