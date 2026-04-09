# MiniRPG 架构文档

> 本文档记录当前代码事实和职责边界。它应该反映当前实现，而不是历史设计草稿。

---

## 文档定位

- 这份文档回答“当前代码是怎么组织的、入口在哪里、主路径怎么走”。
- 如果某次改动改变了职责边界、入口、模块关系或主调用链，应同步更新本文件。
- 如果你要看项目约定和默认决策规则，请先读 [`minirpg.md`](./minirpg.md)。
- 如果你只想快速找文件，请看 [`CODEBASE_MAP.md`](./CODEBASE_MAP.md)。

## 当前架构总览

```text
App/Main.tscn / App/Main.cs
  -> Menu / Session / Input / Log
  -> Panel UI framework
  -> TileMap render framework
  -> Core gameplay modules
  -> World / Save / AI / Data
```

## 1. 入口与胶水层

- [`App/Main.tscn`](../App/Main.tscn)
  入口场景。当前由 `MapPanel`、`StatusPanel`、`SkillManager`、`InventoryPanel`、`GroundPanel`、`LogPanel`、`InputBar`、`SettingsPanel`、`KeyBindingsPanel`、`MainMenu`、`SkillBar` 等子场景组成。
- [`App/Main.cs`](../App/Main.cs)
  胶水层。负责 `_Ready` 初始化、模块持有、命令路由、事件分发、面板注册、脏标记刷新、调试命令入口和地图刷新。
- [`Module/MenuModule.cs`](../Module/MenuModule.cs)
  主菜单与设置菜单切换。
- [`Module/GameSessionModule.cs`](../Module/GameSessionModule.cs)
  新开游戏、继续、读档、存档、楼层切换、视图模式同步。
- [`Module/InputModule.cs`](../Module/InputModule.cs)
  输入焦点状态机，负责 Action / Typing / Selection / Direction 四类输入模式。
- [`Module/InputBindingService.cs`](../Module/InputBindingService.cs)
  输入绑定解析，给 `InputModule` 和按键面板提供统一绑定表。
- [`Module/LogModule.cs`](../Module/LogModule.cs)
  日志写入和 `GameEvent` 到文本日志的翻译。

## 2. Panel/UI 框架

- [`Module/Panel/IPanel.cs`](../Module/Panel/IPanel.cs)
  所有可聚焦面板的统一接口。
- [`Module/Panel/PanelManager.cs`](../Module/Panel/PanelManager.cs)
  管理面板注册、焦点切换、焦点栈恢复、键盘路由和边框刷新。
- [`Module/Panel/PanelDragService.cs`](../Module/Panel/PanelDragService.cs)
  管理可拖拽面板、浮动层重挂载和布局持久化。
- [`Module/Panel/PanelLayoutStore.cs`](../Module/Panel/PanelLayoutStore.cs)
  负责拖拽布局保存和恢复。
- [`Module/Panel`](../Module/Panel)
  当前主要面板模块目录。包括 `StatusModule`、`SkillBarModule`、`SkillManagerModule`、`InventoryPanelModule`、`GroundPanelModule`、`ChestPanelModule`、`DialogPanelModule`、`TradePanelModule`、`QuestPanelModule`、`SettingsPanelModule` 以及若干 UI 辅助类。
- [`Module/CombatUIModule.cs`](../Module/CombatUIModule.cs)
  战斗交互流程 UI。
- [`Module/TradeUIModule.cs`](../Module/TradeUIModule.cs)
  交易交互流程 UI。
- [`Module/DialogUIModule.cs`](../Module/DialogUIModule.cs)
  对话流程 UI。
- [`Module/KeyBindingsUIModule.cs`](../Module/KeyBindingsUIModule.cs)
  按键绑定面板与输入绑定编辑。
- [`Module/LookModule.cs`](../Module/LookModule.cs)
  环境查看文本构建；当前会根据玩家视野分级限制信息暴露。

## 3. 渲染框架

- [`Module/Render/TileMapRenderModule.cs`](../Module/Render/TileMapRenderModule.cs)
  2D TileMap 主路径。负责 `Focused / Peripheral / Memory` 三态玩家视觉的多层 TileMap 渲染、TileSet 映射加载和主角动画挂接。
- [`Module/Render/IsometricVoxelRenderer.cs`](../Module/Render/IsometricVoxelRenderer.cs)
  2.5D Isometric 渲染路径。负责等轴投影下的体素/精灵绘制排序与可视表现拼接。
- [`Module/Render/IsoCoordUtil.cs`](../Module/Render/IsoCoordUtil.cs)
  2.5D 坐标转换工具，承担格点与屏幕投影间的正逆映射基准。
- [`Module/Render/FogOfWarTracker.cs`](../Module/Render/FogOfWarTracker.cs)
  负责玩家 `Focused / Peripheral / Memory / Unknown` 四态视野、朝向裁剪、已探索缓存，并通过 `WorldMap.BlocksSight` 共享遮挡真相。
- [`Module/Render/ViewModes.cs`](../Module/Render/ViewModes.cs)
  渲染模式抽象与切换行为（当前包含 `SingleLayerViewMode` / `MultiLayerViewMode`，并承接 2D/2.5D 入口路由约束）。
- [`Module/Render`](../Module/Render)
  同时包含 `IAnimatable`、`SpineAnimatable`、`TileAnimatable`、`ResAccess` 等渲染支持类。
- 渲染相关外部资源当前集中在 [`Assets/Art/Tilesets/FantasyKingdom`](../Assets/Art/Tilesets/FantasyKingdom) 和 [`Assets/Characters/Spine/Balin`](../Assets/Characters/Spine/Balin)。

## 4. Core 逻辑分层

- [`Core/Data`](../Core/Data)
  运行时核心数据与数据驱动入口。包括 `GameState`、`Actor`、`GameEvent`、`Item`、`InteractionDef`、`PresetDB`、`SkillQuery`、`Quest`、`TagSystem`、`InventoryModule`、`InteractionModule`、`PartyModule`（队伍/多角色控制，详见 [`Docs/Systems/02_队伍系统.md`](./Systems/02_队伍系统.md)）等。
- [`Core/Config`](../Core/Config)
  统一运行时调参入口。`GameConfig` 在启动时读取 `Data/Config/*.json`，并向玩家视野、AI 视野、chunk 运行时、自动测试和地图生成器提供配置对象。
- [`Core/Combat`](../Core/Combat)
  战斗与回合主干。包括 `ActionModule`、`CombatModule`、`TurnModule`、`NestModule`。
- [`Core/Map`](../Core/Map)
  地图代理、世界初始化和存档入口。包括 `MapModule`、`MapGenModule`、`SaveModule`。
- [`Core/Trade`](../Core/Trade)
  交易逻辑。
- [`Core/Dialog`](../Core/Dialog)
  对话池、规则和模板渲染。
- [`Core/AI`](../Core/AI)
  AI 调度、批量视觉感知构建和当前大脑实现。当前入口包括 `AIDispatcher`、`PerceptionBuilder`、`AIVisionBatch`、`SimpleBrain`、`FollowerBrain`（队伍跟随 AI）。
- [`Core/Job`](../Core/Job)
  工作调度系统。`JobScheduler` 为工人分配任务，`JobExecutor` 执行具体动作，`JobBehaviorModule` 插入 AI 行为链。详见 [`Docs/Systems/01_工作系统.md`](./Systems/01_工作系统.md)。
- [`Core/Event`](../Core/Event)
  RimWorld 风格事件调度器。`Storyteller` 根据威胁等级动态选择事件（袭击/商队/流浪者等）。详见 [`Docs/Systems/03_事件系统.md`](./Systems/03_事件系统.md)。
- [`Core/Social`](../Core/Social)
  角色间关系与社交互动。非对称好感度、关系标签、社交冷却。详见 [`Docs/Systems/04_社交系统.md`](./Systems/04_社交系统.md)。
- [`Core/Zone`](../Core/Zone)
  通用区域管理框架（种植区/禁区/家区等 6 种类型）。详见 [`Docs/Systems/05_区域系统.md`](./Systems/05_区域系统.md)。
- [`Core/Farm`](../Core/Farm)
  农业系统：种植/生长/收获/枯萎/自动重种。详见 [`Docs/Systems/06_农业系统.md`](./Systems/06_农业系统.md)。
- [`Core/Debug`](../Core/Debug)
  调试命令实际执行逻辑。
- [`Core/World`](../Core/World)
  世界与 chunk 基础设施，包括 `WorldMap`、`ChunkManager`、`ChunkData`、`TerrainDef`、`ShadowcastFOV`、`VisibilityUtil`、`Pathfinding`、`DigModule`、生成器等。

## 5. 运行时事实与所有权

- [`Core/Data/GameState.cs`](../Core/Data/GameState.cs)
  唯一运行时事实源。持有玩家坐标、Actor 字典、Quest 列表、设置状态和 `WorldMap` 引用。
- [`Core/World/WorldMap.cs`](../Core/World/WorldMap.cs)
  世界访问统一入口，内部通过 `ChunkManager` 路由到具体 chunk；当前还负责 `BlocksSight` 这一层共享视线遮挡真相。
- `Actor` 在 `GameState.Actors` 字典中维护，不直接写进格子栈。
- 格子里的地形、设施、掉落物等在 `WorldMap` / `ChunkData` / `CellEntity` 里维护。
- Core 产出 `GameEvent`；UI 层由 [`App/Main.cs`](../App/Main.cs) 的 `Dispatch` 路由到日志、流程 UI 和面板刷新。

## 6. 关键运行路径

### 启动

- `Main._Ready` 先调用 `GameConfig.Load()`，再加载 `PresetDB`、`TerrainRegistry`、`DialogPool`、`ResAccess`，然后创建 `GameSessionModule`、渲染模块、面板模块，并接入输入和菜单事件。
- `Main._Ready` 当前直接加载 [`Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres`](../Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres)；角色 Spine 资源路径则来自 [`Data/entity_render.json`](../Data/entity_render.json)。

### 输入到命令

- Godot 输入事件先进入 `InputModule`。
- `InputModule` 解析当前焦点模式，并通过 `CommandReceived` 把命令交给 `Main.OnCommand`。
- `Main.OnCommand` 负责把命令路由到移动、交互、设置、存档、调试或面板操作。

### 行动到事件

- 玩家和 AI 共用 [`Core/Combat/ActionModule.cs`](../Core/Combat/ActionModule.cs)。
- `ActionModule` 调 `MapModule`、`CombatModule`、`InteractionModule` 等 Core 模块并产出 `GameEvent`。
- `TurnModule` 负责推进回合，并驱动巢穴刷新、AI 行动、事件调度（`Storyteller.Tick`）和作物生长（`FarmModule.TickGrowth`）。

### 事件到 UI

- `Main.Dispatch` 先把通用事件交给 `LogModule`。
- 战斗、交易、对话等特殊事件再交给对应流程 UI。
- 地面物品、面板内容等通过脏标记和 `FlushIfDirty` 在刷新阶段更新。

### 地图刷新

- `Main.FlushMap` 负责触发渲染模块与各面板刷新。
- 2D 路径主链：`FogOfWarTracker.Update` + `TileMapRenderModule.Flush`。
- 2.5D 路径主链：`FogOfWarTracker.Update` + `IsometricVoxelRenderer` 对应刷新入口。
- `LookModule` 复用同一套玩家视野分级控制信息暴露，要求两条渲染路径在可见性语义上保持一致。

## 7. 渲染入口、切换点与职责边界（2D/2.5D 并存）

### 7.1 渲染入口/切换点

- 渲染切换入口由 `Main` 层命令路由与 `ViewModes` 协同控制。
- 切换时需要保证：
  - 当前可见层状态与模式一致（避免“显示层残留”）。
  - 视野分级（Focused/Peripheral/Memory）语义不因模式变化而改变。
  - 交互拾取坐标在新模式下仍可逆映射到世界格点。

### 7.2 `TileMapRenderModule` vs `IsometricVoxelRenderer` 边界

- `TileMapRenderModule`
  - 负责 2D TileMap 图层绘制与图层可见性控制。
  - 负责 TileSet 映射驱动下的地表/覆盖层铺设。
  - 不承担 2.5D 透视/排序策略实现。
- `IsometricVoxelRenderer`
  - 负责 2.5D 等轴投影绘制、排序 key 与遮挡呈现。
  - 依赖 `IsoCoordUtil` 做坐标正逆变换，不重复维护独立坐标数学。
  - 不承担 2D TileMap 图层组织逻辑。
- 共享能力（跨模块一致）
  - 视野状态来源与 tint 语义。
  - 实体/地形映射数据来源（`Data/*.json`）。
  - 模式切换时的可见性与交互正确性约束。

### 7.3 渲染改动 checklist

每次渲染改动（尤其涉及 2D/2.5D 切换）至少检查：

1. 切模式：切换前后可见层状态正确、无残留。
2. 拾取：屏幕点 -> 世界格点映射稳定，边界格无明显偏移。
3. 视野：Focused/Peripheral/Memory tint 与信息暴露保持一致。
4. 性能：关注帧耗时均值、活跃 sprite 数、draw command 数是否异常回退。

## 8. 当前扩展入口

- 新增可聚焦面板：优先实现 `IPanel`，并接入 `PanelManager` / `PanelDragService`。
- 新增快捷键或输入语义：优先改 `InputBindingService`、`InputModule`、`Main.OnCommand`。
- 新增玩法行动：优先落在现有 Core 模块，必要时扩 `ActionModule` 主路径。
- 新增世界或地图行为：优先改 `MapModule`、`WorldMap`、`ChunkData`、生成器体系。
- 新增渲染表现：优先沿用 `TileMapRenderModule` 和现有 Tile 映射数据，不新开一套平行渲染管线。

## 9. 当前不应再依赖的旧认知

- 当前渲染主路径不是旧版 `RenderModule.cs` 文本渲染。
- 当前 UI 也不是“一个 `RichTextLabel` + 一个 `LineEdit`”的极简原型。
- 当前文件布局已经按 `Core/Data`、`Core/Combat`、`Core/Map` 等目录拆分，不再是所有核心文件都放在 `Core/` 根目录。
