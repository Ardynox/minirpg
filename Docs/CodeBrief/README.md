# MiniRPG C# 代码简明文档（给 AI 评估用）

这份文档面向“快速评估/接手”：按文件给出**职责一句话**、**关键数据结构/入口**、**重要约束与边界**、**运行时主流程**。

## 架构约束（摘自 `ARCHITECTURE.md`）

- **唯一数据源**：`Core/GameState.cs` 的 `GameState` 是所有游戏数据的唯一来源（可序列化 JSON）
- **逻辑/副作用分离（当前项目是 C# 版本）**：
  - 逻辑模块：`Core/*Module.cs` 以“纯数据 + 事件”的形式驱动状态变化
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

## 文件索引

### Godot 驱动层 / 副作用层

- `Main.cs`：Godot 节点脚本；输入→命令→调用逻辑→消费事件→渲染/日志；菜单/设置/存读档
- `Module/InputModule.cs`：键盘与文本输入统一成 command 事件
- `Module/RenderModule.cs`：地图渲染（ASCII BBCode / Emoji 两种模式）

### 逻辑/状态层（纯数据优先）

- `Core/GameState.cs`：全局状态 + 楼层快照结构
- `Core/GameEvent.cs`：事件数据结构
- `Core/MapModule.cs`：四层地图读写/查询/玩家移动/楼层切换
- `Core/MapGenModule.cs`：随机房间地图生成 + 放置玩家/楼梯/巢穴/怪物
- `Core/NestModule.cs`：巢穴刷新怪物（按回合 Tick）
- `Core/SaveModule.cs`：全局存档（JSON）+ 楼层切换快照（内存）
- `Core/Actor.cs`：统一 Actor 数据结构；tag 由多个来源实时计算
- `Core/ActorTemplate.cs`：Actor 模板注册表（玩家/怪物工厂）
- `Core/ActorModule.cs`：Actor 增删移动；同步地图 Objects 层
- `Core/TagSystem.cs`：tag 来源接口 + 动作定义 + 动作筛选（ActionQuery）
- `Core/ActionDefs.cs`：内置动作表（数据驱动）

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

## 给评估 AI 的关注点（建议）

- **状态一致性**：`GameState.PlayerX/Y` 与 `Actors[player].X/Y` 与 `Objects` 层同步点在哪里、是否可能不同步
- **模块边界**：`MapModule`/`ActorModule`/`SaveModule` 的职责是否清晰，是否有耦合点（例如 `MapModule` 直接调用 `SaveModule`）
- **事件表达力**：`GameEvent` 当前字段较少，是否足够承载后续扩展（伤害数值、来源、物品等）
- **性能**：`Actor.ComputeTags()` 每次现算；`ActionQuery` 过滤动作是否频繁调用
- **存档正确性**：楼层缓存与全局 JSON 两套结构是否一致；多态 JSON 的处理是否完备

