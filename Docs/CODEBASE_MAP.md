# MiniRPG 代码导航

> 这份文档只回答“代码在哪、主入口在哪、改某类功能先看什么文件”。它不是约定文档，也不是逐类架构说明。

---
## Render Notes

- `TileMapRenderModule` now renders focused/peripheral/memory player vision using multiple TileMap layers.
- Terrain tile mappings may provide a single base tile or `base + optional overlay`.

## Config Notes

- `Core/Config/GameConfig.cs` is the single startup loader for runtime tuning JSON.
- `Data/Config/` now holds restart-to-apply tuning files for player vision, AI vision, world runtime, auto-test, and map generators.
- `Data/` root still holds authored content presets and resource mappings such as `actors.json`, `terrains.json`, `tile_mapping.json`, and `entity_render.json`.
- Placement rules for new data files live in [`../Data/README.md`](../Data/README.md).


## 文档定位

- 看项目约定和默认决策规则，请先读 [`./minirpg.md`](./minirpg.md)。
- 看当前职责边界和主路径，请读 [`./ARCHITECTURE.md`](./ARCHITECTURE.md)。
- 看遗留问题和技术债，请读 [`./TODO.md`](./TODO.md)。
- 看游戏内自动化巡检器（AutoTest）的 AI 接手流程，请读 [`./AUTOTEST_AI_GUIDE.md`](./AUTOTEST_AI_GUIDE.md)。

## 顶层目录

```text
mini-rpg/
├── App/
│   ├── GlobalUsings.cs
│   ├── Main.cs
│   └── Main.tscn
├── project.godot
├── Core/
│   ├── AI/
│   ├── Combat/
│   ├── Config/
│   ├── Data/
│   ├── Debug/
│   ├── Dialog/
│   ├── Event/
│   ├── Farm/
│   ├── Health/
│   ├── Job/
│   ├── Map/
│   ├── Needs/
│   ├── Social/
│   ├── Trade/
│   ├── Weather/
│   ├── World/
│   └── Zone/
├── Module/
│   ├── Panel/
│   └── Render/
├── Scene/
├── Data/
├── Assets/
├── Artifacts/
├── Docs/
└── Tools/
```

## 先从哪里看

| 任务类型 | 先看这些文件 |
|---|---|
| 游戏内自动化巡检 / 读 `test_results.log` / AutoTest 扩展 | [`./AUTOTEST_AI_GUIDE.md`](./AUTOTEST_AI_GUIDE.md), [`../App/Main.AutoTest.cs`](../App/Main.AutoTest.cs), [`../Module/AutoTestModule.cs`](../Module/AutoTestModule.cs), [`../Module/AutoTestLogWriter.cs`](../Module/AutoTestLogWriter.cs), [`../Data/Config/debug.json`](../Data/Config/debug.json) |
| 项目入口、初始化、命令路由 | [`../App/Main.cs`](../App/Main.cs), [`../App/Main.tscn`](../App/Main.tscn) |
| 菜单、继续、读档、楼层切换 | [`../Module/GameSessionModule.cs`](../Module/GameSessionModule.cs), [`../Module/MenuModule.cs`](../Module/MenuModule.cs), [`../Core/Map/SaveModule.cs`](../Core/Map/SaveModule.cs) |
| 输入、快捷键、输入焦点 | [`../Module/InputModule.cs`](../Module/InputModule.cs), [`../Module/InputBindingService.cs`](../Module/InputBindingService.cs), [`../Module/KeyBindingsUIModule.cs`](../Module/KeyBindingsUIModule.cs) |
| 面板焦点、拖拽、布局 | [`../Module/Panel/IPanel.cs`](../Module/Panel/IPanel.cs), [`../Module/Panel/PanelManager.cs`](../Module/Panel/PanelManager.cs), [`../Module/Panel/PanelDragService.cs`](../Module/Panel/PanelDragService.cs), [`../Module/Panel/PanelLayoutStore.cs`](../Module/Panel/PanelLayoutStore.cs) |
| 状态、背包、地面、宝箱、技能等面板 | [`../Module/Panel`](../Module/Panel) |
| 地图渲染、迷雾、视图模式 | [`../Module/Render/TileMapRenderModule.cs`](../Module/Render/TileMapRenderModule.cs), [`../Module/Render/FogOfWarTracker.cs`](../Module/Render/FogOfWarTracker.cs), [`../Module/Render/ViewModes.cs`](../Module/Render/ViewModes.cs), [`../Module/LookModule.cs`](../Module/LookModule.cs) |
| 战斗、交互、回合、AI | [`../Core/Combat`](../Core/Combat), [`../Core/AI`](../Core/AI), [`../Module/CombatUIModule.cs`](../Module/CombatUIModule.cs) |
| 世界数据、地图代理、chunk、生成器 | [`../Core/Map`](../Core/Map), [`../Core/World`](../Core/World) |
| 运行时状态、Actor、Item、任务、预设数据 | [`../Core/Data`](../Core/Data) |
| 交易、对话 | [`../Core/Trade`](../Core/Trade), [`../Core/Dialog`](../Core/Dialog), [`../Module/TradeUIModule.cs`](../Module/TradeUIModule.cs), [`../Module/DialogUIModule.cs`](../Module/DialogUIModule.cs) |
| 调试命令 | [`../App/Main.cs`](../App/Main.cs), [`../Core/Debug/DebugModule.cs`](../Core/Debug/DebugModule.cs) |
| 工作调度、NPC 自动工作 | [`../Core/Job`](../Core/Job), [`Docs/Systems/01_工作系统.md`](./Systems/01_工作系统.md) |
| 队伍、多角色控制 | [`../Core/Data/PartyModule.cs`](../Core/Data/PartyModule.cs), [`../Core/AI/FollowerBrain.cs`](../Core/AI/FollowerBrain.cs), [`Docs/Systems/02_队伍系统.md`](./Systems/02_队伍系统.md) |
| 随机事件、Storyteller | [`../Core/Event`](../Core/Event), [`Docs/Systems/03_事件系统.md`](./Systems/03_事件系统.md) |
| 社交、好感度、关系 | [`../Core/Social`](../Core/Social), [`Docs/Systems/04_社交系统.md`](./Systems/04_社交系统.md) |
| 区域划定（种植区/禁区等） | [`../Core/Zone`](../Core/Zone), [`Docs/Systems/05_区域系统.md`](./Systems/05_区域系统.md) |
| 农业、种植、收获 | [`../Core/Farm`](../Core/Farm), [`Docs/Systems/06_农业系统.md`](./Systems/06_农业系统.md) |
| Tile 映射和资源数据 | [`../Data`](../Data), [`../Assets/README.md`](../Assets/README.md), [`../Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres`](../Assets/Art/Tilesets/FantasyKingdom/FantasyKingdomTileSet.tres), [`../Tools/tile_name_to_id.json`](../Tools/tile_name_to_id.json) |

## 当前目录分工

### `App/`

- 启动入口层。当前包含 `Main.tscn`、`Main.cs` 和 `GlobalUsings.cs`，负责项目启动场景、主脚本胶水层和全局 using 声明。

### `Core/`

- `Config/`: startup-time runtime tuning loader and DTOs. `GameConfig.Load()` reads `Data/Config/*.json` once, and gameplay/render systems consume the resulting config objects.
- `Data/`: `GameState`、`Actor`、`GameEvent`、`PresetDB`、`Item`、`InteractionDef`、`Quest`、`TagSystem` 等运行时核心数据与数据驱动入口。
- `Combat/`: `ActionModule`、`CombatModule`、`TurnModule`、`NestModule`。
- `Map/`: `MapModule`、`MapGenModule`、`SaveModule`。
- `World/`: `WorldMap`、`ChunkManager`、`ChunkData`、`TerrainDef`、视野、LOS、寻路、挖掘、生成器。
- `AI/`: `AIDispatcher`、`AIVisionBatch`、`SimpleBrain`、`FollowerBrain`、`PerceptionBuilder`。
- `Job/`: 工作调度系统。`JobScheduler`、`JobExecutor`、`JobBehaviorModule`。
- `Event/`: 事件调度器。`Storyteller`、`IncidentDef`、`IIncidentWorker`、内置 Workers。
- `Social/`: 社交系统。`SocialModule`、`RelationEntry`、`SocialInteractionDef`。
- `Zone/`: 区域管理。`ZoneModule`、`ZoneDef`（6 种区域类型）。
- `Farm/`: 农业系统。`FarmModule`、`CropDef`、`CropRegistry`。
- `Trade/`: 交易逻辑。
- `Dialog/`: 对话规则与模板渲染。
- `Debug/`: 调试命令实际执行。

### `Module/`

- 根目录下是胶水型或流程型模块，如 `InputModule`、`GameSessionModule`、`MenuModule`、`LogModule`、`CombatUIModule`、`TradeUIModule`、`DialogUIModule`。
- `Panel/` 是当前面板 UI 框架和具体面板实现。
- `Render/` 是当前 TileMap 渲染、迷雾和视图模式实现。

### `Scene/`

- 各个 Godot 子场景资源。`App/Main.tscn` 通过实例化这些场景拼装当前 UI。

### `Data/`

- `Data/` root keeps authored presets and resource mappings such as `actors.json`, `items.json`, `terrains.json`, `dialogs.json`, `tile_mapping.json`, and `entity_render.json`.
- `Data/Config/` is only for runtime tuning JSON that is expected to change frequently during balancing and debugging.
- See [`../Data/README.md`](../Data/README.md) for the split between root data files and `Data/Config/`.

### `Assets/`

- 项目资源统一入口。当前 `Branding/` 放项目图标，`UI/Themes/` 放共用主题，`Art/Tilesets/FantasyKingdom/` 放 Fantasy Kingdom tileset 包和 `FantasyKingdomTileSet.tres`，`Characters/Spine/Balin/` 放 Balin Spine 角色资源。
- 更细的资源分层、哪些资源故意不进版本库、以及新增资源时的落点规则，统一写在 [`../Assets/README.md`](../Assets/README.md)。

### `Artifacts/`

- 非运行时源文件的产物目录。`Recovery/` 预留给人工保留的恢复快照压缩包，避免以后继续堆在项目根目录。

### `Tools/`

- 工具脚本、辅助导表、渲染资源映射等，不是主运行时逻辑入口。

## 当前高频修改路径

### 新增或调整面板

1. 改 [`../Scene`](../Scene) 里的对应 `.tscn`。
2. 改 [`../Module/Panel`](../Module/Panel) 里的对应模块。
3. 必要时在 [`../App/Main.cs`](../App/Main.cs) 注册面板、拖拽或焦点行为。

### 改输入或快捷键

1. 改 [`../Module/InputBindingService.cs`](../Module/InputBindingService.cs)。
2. 改 [`../Module/InputModule.cs`](../Module/InputModule.cs)。
3. 必要时补 [`../App/Main.cs`](../App/Main.cs) 的命令路由。

### 改地图渲染

1. 改 [`../Module/Render/TileMapRenderModule.cs`](../Module/Render/TileMapRenderModule.cs)。
2. 改 [`../Module/Render/FogOfWarTracker.cs`](../Module/Render/FogOfWarTracker.cs) 或 [`../Module/Render/ViewModes.cs`](../Module/Render/ViewModes.cs)。
3. 如果改动影响玩家信息暴露，同时检查 [`../Module/LookModule.cs`](../Module/LookModule.cs)。

### 改玩法逻辑

1. 优先看 [`../Core/Combat`](../Core/Combat)、[`../Core/Data`](../Core/Data)、[`../Core/Map`](../Core/Map)。
2. 如果需要 UI 反馈，再补 `Module/*`。
3. 不要先从 `App/Main.cs` 里堆玩法细节。

### 改存档或世界数据

1. 改 [`../Core/Data/GameState.cs`](../Core/Data/GameState.cs)。
2. 改 [`../Core/Map/SaveModule.cs`](../Core/Map/SaveModule.cs)。
3. 必要时改 [`../Core/World`](../Core/World) 下的 world/chunk 数据结构。

## 当前不该再找的旧文件名

- 当前主渲染路径不是 `Module/RenderModule.cs`。
- 当前地图渲染主模块不是 `Module/MapRenderModule.cs`。
- 当前多数面板实现不在 `Module/InventoryPanelModule.cs` 这种旧根目录路径，而是在 [`../Module/Panel`](../Module/Panel)。
- 当前 Core 文件也不再集中放在 `Core/` 根目录，而是按子目录分组。
