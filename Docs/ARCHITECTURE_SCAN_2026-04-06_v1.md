# MiniRPG 架构全量扫描 v2026.04.06-r1

- 文档版本: `v2026.04.06-r1`
- 扫描日期: `2026-04-06`
- 扫描分支: `codex/turn-manager`
- 最近提交: `fa25c23 chore(checkpoint): before add timeline turn panel`
- 扫描基线: 当前工作树
- 备注: 本次扫描包含未提交的时间线回合面板接入改动，例如 `Module/Panel/TurnPanelModule.cs`、`Scene/TurnPanel.tscn` 以及 `App/Main.cs` / `App/Main.tscn` / `Core/Combat/TimelineTurnManager.cs` 的联动修改

## 1. 扫描范围与规模

- 主入口: `project.godot` -> `App/Main.tscn` -> `App/Main.cs`
- 技术栈: `Godot 4.6 + C# + .NET 8`
- 顶层扫描目录: `App/`、`Core/`、`Module/`、`Scene/`、`Data/`、`Tests/`、`Tools/`
- 代码与资源体量:
  - `App`: `3` 个 C# 文件, `1` 个主场景文件
  - `Core`: `67` 个 C# 文件
  - `Module`: `56` 个 C# 文件
  - `Scene`: `25` 个 `.tscn`
  - `Data`: `28` 个数据文件
  - `Tests`: `8` 个 C# 文件
  - `Tools`: `4` 个 C# 文件, `2` 个 `.tscn`
- 当前大文件:
  - `App/Main.cs`: `2443` 行
  - `App/ResourceCatalogEditor.cs`: `1745` 行
  - `Module/Render/TileMapRenderModule.cs`: `821` 行
  - `Core/Map/SaveModule.cs`: `531` 行

## 2. 当前架构总览

```text
project.godot
  -> App/Main.tscn
    -> HudLayer/UI
      -> MapPanel
      -> StatusPanel
      -> SkillManager
      -> InventoryPanel
      -> GroundPanel
      -> TurnPanel
      -> LogPanel
      -> InputBar
    -> OverlayLayer
      -> StartupOverlay
      -> SettingsPanel
      -> PauseMenuPanel
      -> MainMenu
      -> SkillBar
      -> LayoutEditBar
      -> SaveBrowser
      -> MapEditorBar
      -> SaveNameDialog
      -> CharacterCreationDialog
  -> App/Main.cs
    -> 启动装配
    -> 菜单与会话编排
    -> 输入路由
    -> 时间线驱动
    -> 事件分发
    -> 地图渲染与脏刷新
    -> 设置/布局/编辑器/存读档流程

Core/*
  -> 纯运行时规则、数据模型、世界状态、AI、存档

Module/*
  -> Godot UI 适配、流程编排、渲染适配、编辑器侧逻辑

Data/*
  -> 预设数据、配置、映射表、本地化文本

Tests/*
  -> xUnit 单元测试 + 运行时 AutoTest 回归入口
```

## 3. 分层与职责边界

### 3.1 App 层

- `App/Main.cs` 是当前唯一 Godot 运行时总编排层。
- 它负责:
  - 启动期加载配置、预设、本地化和渲染注册表
  - 构造 `GameSessionModule`、`MenuModule`、`InputModule`、`PanelManager`、`TileMapRenderModule`
  - 绑定菜单、设置、输入、面板、编辑器、角色创建、存读档事件
  - 作为 `IGameUI` 实现，把 Core 事件桥接到 UI 副作用
- `App/ResourceCatalogEditor.cs` 是另一个工具入口，职责独立于主游戏运行流。

### 3.2 Core 层

- `Core/Data`
  - 定义运行时数据模型与共享状态。
  - `GameState` 是唯一主状态容器。
  - `ActorModule`、`InventoryModule`、`InteractionModule` 等对 `GameState` 做纯逻辑操作。
- `Core/World`
  - `WorldMap` 统一封装地形、实体栈、地面物品、chunk 路由。
  - `ChunkManager` 负责按玩家中心加载/卸载区块。
  - 视野、遮挡、寻路、地图生成器也放在这一层。
- `Core/Map`
  - `MapGenModule` 负责初始化 `WorldMap` 和生成器接线。
  - `MapModule` 是兼容层，保留大量 2D 风格 API，对外适配到 `WorldMap`。
  - `SaveModule` 负责运行时对象与存档 DTO 的双向映射。
- `Core/Combat`
  - `TimelineTurnManager` 是当前回合推进中枢。
  - `ActionModule`、`CombatModule`、`TurnModule` 共同组成玩家和 AI 共用的行动执行面。
- `Core/AI`
  - `AIDispatcher` 负责调度脑模块。
  - `PerceptionBuilder` / `AIVisionBatch` 负责感知构建。
- `Core/Dialog` / `Core/Trade`
  - 提供对话规则、模板渲染和交易规则。

### 3.3 Module 层

- `MenuModule`
  - 只管理主菜单与 HUD 显隐，不持有游戏状态。
- `GameSessionModule`
  - 管理新游戏、读档、存档、换层、世界流式加载。
- `InputBindingService` + `InputModule`
  - 提供输入绑定存储与输入焦点状态机。
- `Module/Panel/*`
  - 形成统一面板体系。
  - `PanelManager` 处理焦点栈和键盘路由。
  - `PanelDragService` 处理浮动拖拽和布局持久化。
  - `SettingsFlowCoordinator` 管理暂停菜单与设置页切换。
- `Module/Render/*`
  - `TileMapRenderModule` 负责地图多层渲染。
  - `FogOfWarTracker` 负责 Focused / Peripheral / Memory / Unknown 视野状态。
  - `ResAccess` 负责实体外观注册与异步资源轮询。
- `CombatUIModule`、`TradeUIModule`、`DialogUIModule`
  - 负责将 Core 事件转成具体 UI 流程。
- `Module/Editor/*`
  - 承载地图编辑器和资源目录编辑器的非场景逻辑。

### 3.4 Data / Scene / Tests / Tools 层

- `Scene/`
  - 存放具体 UI 场景资源，例如 `StatusPanel.tscn`、`TurnPanel.tscn`、`SaveBrowser.tscn`、`CharacterCreationDialog.tscn`。
- `Data/`
  - 根目录存放玩法预设、资源映射与本地化内容。
  - `Data/Config/` 存放运行时调参文件。
- `Tests/`
  - 当前显式覆盖的单元测试集中在时间线、存档、键位、设置流程、地面物品渲染注册表。
- `Tools/`
  - 存放离线脚本和辅助导入工具，不是主游戏运行时入口。

## 4. 运行时状态所有权

### 4.1 单一运行时事实源

- `Core/Data/GameState.cs`
  - 持有:
    - 玩家三维坐标
    - `Actors`
    - `Quests`
    - `Timeline`
    - 当前生成器 ID / 视图模式 ID
    - `WorldMap` 引用

### 4.2 世界状态所有权

- `Core/World/WorldMap.cs`
  - 持有 `ChunkManager`
  - 管理:
    - 地形 ID
    - 地形硬度
    - 格子实体栈
    - 地面物品
    - fixture
    - chunk 级 actor 注册表
- `Core/Data/ActorModule.cs`
  - Actor 真正存放在 `GameState.Actors`
  - `WorldMap.RegisterActor / UpdateActorChunk / UnregisterActor` 只是同步 chunk 级索引

### 4.3 回合状态所有权

- `Core/Combat/TimelineTurnManager.cs`
  - 持有 actor charge 队列、当前 actor、上一个 actor
  - 对外暴露:
    - `Reset`
    - `SyncActors`
    - `SubmitPlayerAction`
    - `AdvanceAuto`
    - `CreateDebugSnapshot`

### 4.4 存档所有权

- `Core/Map/SaveModule.cs`
  - 当前存档版本: `CurrentVersion = 3`
  - 保存范围:
    - `GameState` 标量状态
    - `Actors`
    - `Quests`
    - `Timeline`
    - 脏 chunk 快照
  - `WorldMap` 本体不直接序列化，而是通过 dirty chunk 缓存重建

## 5. 主调用链

### 5.1 启动链

```text
project.godot
  -> run/main_scene = App/Main.tscn
  -> Main._Ready()
    -> GameConfig.Load()
    -> PresetDB.Load()
    -> LocalizationService.Initialize()
    -> TerrainRegistry.Load()
    -> DialogPool.Load()
    -> ResAccess.Load()
    -> PlayerAppearanceCatalog.LoadProjectCatalog()
    -> new GameSessionModule(...)
    -> new MenuModule(this)
    -> new PanelManager / PanelDragService / PanelLayoutService
    -> new InputBindingService(...)
    -> new InputModule(...)
    -> new CombatUIModule(this)
    -> BeginHeavyStartupLoad()
```

补充说明:

- `_Ready()` 先完成逻辑装配，再进入重资源异步加载。
- `_Process()` 中持续轮询:
  - `ResAccess.PollAsyncLoads()`
  - `PollHeavyStartupLoad()`
  - `PollPostStartupTasks()`
  - `_session.ProcessWorldStreaming()`
- 重资源完成后:
  - `FinalizeHeavyStartupLoad()`
  - 构造 `TileMapRenderModule`
  - `QueuePostStartupTasks()`
  - 延迟执行 `SeedPresetSaves()` 与 `PrewarmDeferredUiScenes()`

### 5.2 菜单到开局链

```text
Main._Ready()
  -> 菜单按钮事件绑定
  -> MenuModule.OnNewGame += HandleMenuNewGame
  -> HandleMenuNewGame()
    -> OpenCharacterCreationDialog()
    -> CharacterCreationModule.Open()
      -> 用户确认
      -> HandleCharacterCreationConfirmed(options)
        -> PrepareSessionTransition(clearLogs: true)
        -> _session.NewGame(options)
          -> GameState.Reset()
          -> FogOfWarTracker.Clear()
          -> InitializeWorld(options)
            -> MapGenModule.InitializeWorld(state)
            -> MapGenModule.FindSpawnPoint(state)
            -> MapGenModule.SpawnPlayer(state, options)
          -> TimelineTurnManager.Reset(state)
        -> FinalizeNewGameStart()
        -> DoEnterGame()
        -> FlushMap()
```

主菜单“继续”和“地图编辑器”链路与此类似，只是分别走 `TryContinue()` 和 `NewBlankEditorMap()`。

### 5.3 输入链

```text
Godot InputEvent
  -> Main._UnhandledInput(InputEventKey)
    -> SettingsFlowCoordinator.HandleKeyInput()
    -> PanelManager.HandleKey()
    -> InputModule.HandleKeyInput()
      -> 按当前 InputFocus 分发
        -> Action
        -> Typing
        -> Selection
        -> Direction
      -> CommandReceived(cmd)
        -> Main.OnCommand(cmd)
```

鼠标链:

```text
Main._Input(InputEvent)
  -> PanelHoverChromeService.HandleInput()
  -> PanelDragService.HandleGlobalInput()
  -> InputModule.HandleMouseButtonInput()
```

`InputBindingService` 负责:

- 默认绑定定义
- `user://keybindings.json` 的持久化
- Action / Typing / Selection / Direction 四个上下文的输入解析

### 5.4 命令到玩家行动链

```text
Main.OnCommand("w" / "a" / "s" / "d")
  -> DoMove(dx, dy)
  -> SubmitPlayerAction(TimelinePlayerAction.Move(dx, dy))
  -> TimelineTurnManager.SubmitPlayerAction(state, action)
    -> TryExecutePlayerAction(...)
      -> Move
        -> ActionModule.TryMove(...)
      -> Dig
        -> InteractionModule.ExecuteDig(...)
      -> Attack
        -> ActionModule.TryAttack(...)
    -> FinalizeConsumedAction(...)
      -> TurnModule.AdvanceWorld(state)
      -> ConsumeTurn(...)
  -> Main.ApplyTimelineStep(result)
    -> Dispatch(result.Events)
    -> FinalizeTimelineStepUi()
    -> SyncTimelineAutoAdvanceState()
    -> FlushMap()
```

这里的关键点:

- 玩家行动和 AI 行动共用 `ActionModule` / `CombatModule`
- 回合推进已经不再依赖旧式“每输入一次就全量 Tick 全世界”的模型
- `TurnPanelModule` 通过 `TimelineTurnManager.CreateDebugSnapshot()` 显示当前回合状态

### 5.5 自动推进与 AI 链

```text
Main._Process()
  -> 如果当前不是玩家可输入阶段
    -> AdvanceTimelineAutoStep()
      -> TimelineTurnManager.AdvanceAuto(state)
        -> EnsureCurrentActor()
        -> 如果当前 actor 是 AI
          -> AIDispatcher.DecideAndExecuteAny(state, actor)
            -> PerceptionBuilder.Build(...)
            -> brain.Decide(...)
            -> ActionModule.TryMove / TryAttack
        -> FinalizeConsumedAction(...)
      -> Main.ApplyTimelineStep(result)
```

额外还有旧式批量 AI 路径:

```text
TurnModule.Tick(state)
  -> AdvanceWorld(state)
  -> AIDispatcher.TickAll(state, playerX, playerY, viewRange)
```

当前主游戏输入链更偏向 `TimelineTurnManager`，`TurnModule.Tick()` 仍主要被测试和部分旧调用使用。

### 5.6 事件分发链

```text
Core 模块产生 List<GameEvent>
  -> Main.Dispatch(events)
    -> LogModule.DispatchEvent(e, state)
    -> 根据 e.Type 做 UI 副作用路由
      -> combat_bump -> CombatUIModule
      -> actor_killed -> HandlePlayerDeath / 掉落 / 奖励
      -> interaction + trade -> EnsureTradeUI().OpenTradeMenu(...)
      -> interaction + talk -> EnsureDialogUI().OpenDialog(...)
      -> 其他交互 -> 直接写日志
    -> 标记地图或面板为 dirty
```

当前事件模型特征:

- `GameEvent.Type` 仍是字符串
- `Main.Dispatch()` 是运行时 UI 副作用唯一汇总入口
- `LogModule` 只做日志翻译，不负责流程切换

### 5.7 渲染链

```text
Main.FlushMap()
  -> TileMapRenderModule.SetEditorView(...)
  -> TileMapRenderModule.Flush()
    -> FogOfWarTracker.Update(state)
    -> 逐格读取 WorldMap / Actor / GroundItems
    -> 绘制 Focused / Peripheral / Memory / Fog 多层 TileMap
    -> 更新玩家可动画实体
  -> MarkUIDirty()
    -> StatusPanelModule.Dirty = true
    -> TurnPanelModule.Dirty = true
    -> Inventory / Ground / SkillBar dirty
```

随后在下一帧:

```text
Main._Process()
  -> ProcessDirtyPanels()
    -> StatusPanelModule.Refresh(...)
    -> TurnPanelModule.FlushIfDirty(...)
    -> SkillBar.Refresh(...)
    -> InventoryPanel.FlushIfDirty()
    -> GroundPanel.FlushIfDirty()
```

### 5.8 视野与观察链

```text
TileMapRenderModule.Flush()
  -> FogOfWarTracker.Update(state)
    -> 基于 PlayerVisionConfig
    -> 结合 WorldMap.BlocksSight()
    -> 生成 Focused / Peripheral / Memory / Unknown
  -> TileMapRenderModule 根据视野层级绘图

Main.DoLook()
  -> LookModule.BuildLookText(state, fogTracker)
    -> FocusedCellLabel / PeripheralCellLabel / MemoryCellLabel
    -> 输出受视野约束的文本观察结果
```

因此，图形渲染和文字观察共享同一套可见性规则，不是两套独立逻辑。

### 5.9 交互、交易、对话链

交互入口:

```text
Main.OnCommand(":interact" / "interact")
  -> DoInteract()
    -> InteractionModule.GetAvailableTargets(...)
    -> ShowInteractionsFor(player, target)
    -> InteractionModule.Execute(...)
    -> Dispatch(events)
```

交易链:

```text
Dispatch(interaction trade)
  -> EnsureTradeUI().OpenTradeMenu(e)
  -> TradeUIModule.OpenTradeMenu(...)
    -> TradePanelModule.Open(...)
    -> PanelManager.PushFocus(panel)
  -> 用户执行买卖
    -> TradeModule.Buy / Sell
    -> panel.RefreshData(...)
```

对话链:

```text
Dispatch(interaction talk)
  -> EnsureDialogUI().OpenDialog(npc)
  -> DialogUIModule.OpenDialog(...)
    -> DialogContext.Build(...)
    -> DialogPool.GetGreetCandidates()
    -> DialogRuleEngine.SelectEntry(...)
    -> TemplateRenderer.Render(...)
    -> DialogPanelModule.Show(...)
```

### 5.10 存档 / 读档链

```text
Main.DoSave(path, label)
  -> _session.SaveGame(path)
    -> SaveModule.SaveGame(state, path)
      -> BuildSnapshot(state)
      -> JsonSerializer.Serialize(...)
```

```text
Main.DoLoad(path, label)
  -> _session.LoadGame(path)
    -> SaveModule.LoadGame(state, path)
      -> TryReadSaveFile(...)
      -> ApplySnapshot(state, saveFile)
    -> MapGenModule.InitializeWorld(state)
    -> EnsurePlayerActor()
    -> SyncViewMode()
    -> GameLocalizer.RelocalizeGameState(state)
    -> TimelineTurnManager.Reset / SyncActors
  -> FlushMap()
```

关键约束:

- 存档格式版本当前是 `v3`
- `TryReadSaveHeader()` 支持只读头信息来驱动存档浏览器
- dirty chunk 通过 `SaveModule.DirtyChunkCache` 与 `ChunkManager.OnChunkLoad/OnChunkUnload` 接口联动

### 5.11 地图编辑器链

```text
主菜单地图编辑器 / 设置里的地图编辑器开关
  -> Main.HandleMenuMapEditor() 或 ToggleMapEditor()
  -> EnterMapEditor(...)
    -> MapEditorSession.Enter(...)
    -> MapEditorBarModule 打开
    -> TileMapRenderModule.SetEditorView(active: true, ...)
  -> 鼠标或快捷键修改格子
    -> MapEditorSession.ApplyBrush / EraseBrush
    -> state.World.SetTerrain / SetFixture
    -> FlushMap()
```

这条链没有单独的编辑器世界副本，仍直接编辑当前 `GameState.World`。

### 5.12 资源目录编辑器链

```text
Scene/ResourceCatalogEditor.tscn
  -> App/ResourceCatalogEditor.cs
    -> LocalizationService.Initialize()
    -> ResourceCatalogStore.Load(...)
    -> ResourcePreviewControl 预览
    -> 批量导入 / 切片 / 标注 / 保存
    -> ResourceCatalogStore.Save(...)
```

这条链和主游戏运行流分离，但仍处于同一解决方案与同一代码仓库中。

## 6. 关键模块图

### 6.1 主游戏运行流

```text
Main
  -> MenuModule
  -> GameSessionModule
  -> InputModule
  -> PanelManager / PanelDragService / SettingsFlowCoordinator
  -> TileMapRenderModule / FogOfWarTracker / ResAccess
  -> CombatUIModule / TradeUIModule / DialogUIModule
  -> Core modules
       -> GameState
       -> TimelineTurnManager
       -> ActionModule / CombatModule / TurnModule
       -> InteractionModule / TradeModule / DialogRuleEngine
       -> WorldMap / ChunkManager / SaveModule
```

### 6.2 世界数据依赖

```text
GameState
  -> WorldMap
    -> ChunkManager
      -> IMapGenerator
      -> SaveModule.LoadChunkFromCache / SaveChunkToCache
  -> Actors
  -> Quests
  -> Timeline
```

### 6.3 UI 框架依赖

```text
PanelManager
  -> IPanel implementations
    -> StatusPanelModule
    -> SkillBarModule
    -> SkillManagerModule
    -> InventoryPanelModule
    -> GroundPanelModule
    -> TradePanelModule
    -> DialogPanelModule
    -> QuestPanelModule
    -> PauseMenuPanelModule
    -> SettingsPanelModule

PanelDragService
  -> PanelLayoutStore
  -> floatingRoot

SettingsFlowCoordinator
  -> PauseMenuPanelModule
  -> SettingsPanelModule
  -> PanelManagerSettingsFlowFocusHost
```

## 7. 测试与验证覆盖

当前显式单元测试文件:

- `Tests/MiniRPG.Tests/TimelineTurnManagerTests.cs`
  - 覆盖时间线重置、速度计算、actor 同步
- `Tests/MiniRPG.Tests/SaveModuleTests.cs`
  - 覆盖存档 round-trip、版本检查、header 读取
- `Tests/MiniRPG.Tests/KeyBindingsViewTests.cs`
  - 覆盖键位界面相关逻辑
- `Tests/MiniRPG.Tests/SettingsFlowCoordinatorTests.cs`
  - 覆盖设置流程协调器
- `Tests/MiniRPG.Tests/ItemWorldRenderRegistryTests.cs`
  - 覆盖地面物品渲染映射

运行时回归:

- `Module/AutoTestModule.cs`
  - 以真实游戏流程串联会话、渲染、命令、事件、AI、主循环进行 smoke / regression 检查

## 8. 本次扫描的结构结论

### 8.1 已稳定的结构骨架

- 主入口已经稳定为 `Main.tscn + Main.cs`
- 运行时事实源已经集中到 `GameState`
- 世界访问已经统一收口到 `WorldMap`
- UI 面板已经形成 `IPanel + PanelManager + PanelDragService` 体系
- 回合推进已经形成 `TimelineTurnManager` 中心模型
- 存档已经有版本化 DTO 和脏 chunk 机制

### 8.2 当前架构上的真实重心

- `Main` 仍然是总编排中枢，而不是薄壳
- `MapModule` 仍是兼容层，尚未完全退到边缘
- `TileMapRenderModule` 同时承担地图绘制、地面物品绘制、玩家实体可视化和相机缩放
- `ResourceCatalogEditor` 已经是独立工具应用规模
- `TurnPanelModule` 说明当前工作树正在把时间线调试信息正式纳入常驻 UI

### 8.3 当前代码事实里最需要记住的边界

- 玩家和 AI 的行动执行已经共享 `ActionModule`
- 图形视野与文字观察共用 `FogOfWarTracker` / `WorldMap.BlocksSight`
- 存档浏览器依赖 `SaveModule.TryReadSaveHeader()`，不是直接全量读 payload
- 地图编辑器直接改运行时世界，不存在独立副本
- `DialogUIModule` / `TradeUIModule` 不是 Core 逻辑，而是 `Main.Dispatch()` 触发的流程层

## 9. 后续维护建议

- 任何修改 `Main` 启动流程、输入路由、事件分发、地图刷新、存读档流程时，都应同步更新本文件。
- 任何新增常驻面板、编辑器入口、时间线状态展示、世界状态所有权迁移时，都应新建下一版扫描文档。
- 推荐命名延续:
  - `ARCHITECTURE_SCAN_YYYY-MM-DD_vN.md`

## 10. 本版文档结论摘要

本仓库当前不是“无架构”状态，而是已经形成了较清晰的四段式结构:

```text
Godot 入口编排
  -> Module 流程与 UI 适配
    -> Core 纯逻辑与状态
      -> Data / Scene / Tests / Tools 支撑层
```

真正的中轴有三个:

- `GameState` 作为运行时事实源
- `WorldMap` 作为世界访问入口
- `TimelineTurnManager` 作为行动推进中轴

真正的编排汇聚点只有一个:

- `App/Main.cs`

这意味着后续所有结构演进，优先应该围绕“继续收敛 Main 的编排密度”和“保持 Core 状态所有权不外溢”展开，而不是重新引入平行框架。
