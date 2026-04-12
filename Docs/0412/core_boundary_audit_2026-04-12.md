# Core 职责边界全量分析（2026-04-12）

## 1. 范围与方法

- 扫描范围：`MiniRPG.Shared/Core/**/*.cs`
- 本次统计口径：
  - 140 个 C# 文件
  - 31,275 行源码
  - 1,428 个方法签名（不含属性）
- 判定口径：
  - `边界内`：函数围绕同一职责主题，或明确属于该模块应承担的编排责任
  - `边界内但偏厚`：主题没跑偏，但文件已经承载过多子流程/子主题
  - `边界漂移`：仍在同一大域里，但已经混入旧兼容层、表现层语义或临时过渡职责
  - `显式越界`：直接依赖 `App / Module / Godot / UI / 渲染` 等外层实现，或者把应用层/表现层细节硬塞进 Core
- 本次审计方法：
  - 先做依赖方向扫描，再读大文件和上一版审计中的风险点
  - 对全部文件按“文件内函数簇”判断
  - 对纯模型/DTO/常量文件不逐个展开函数细节，但仍纳入边界判断

## 2. 总体结论

- 结论先说：当前 `Core` 整体仍然守住了职责边界。
- 这次没有发现新的“显式越界”文件。
- 旧版审计里最明确的三处问题已经被收口：
  - `DebugModule` 不再直接依赖具体会话实现，而是通过 `IDebugSessionActions` 端口隔离
  - `WeatherSystem` 不再直接碰 `SaveModule.DirtyChunkCache`，而是通过 `IWeatherAccumulationStore` + `SaveModuleWeatherAdapter` 间接访问
  - 原先挂在 `Core` 下的 session backend 已经迁出当前扫描范围，`Core` 里不再保留这组会话适配器
- 当前最需要注意的不是“Core 全面串层”，而是两类更现实的问题：
  - 旧兼容逻辑和表现语义仍残留在 `MapModule / WorldMap`
  - 少数总控型文件继续变厚，后续维护成本会持续上升

## 3. 关键变化

### 3.1 依赖方向是干净的

- 本次对 `MiniRPG.Shared/Core` 做了直接依赖扫描，没有发现 `using Godot`、`using App.*`、`using Module.*`、`using MiniRPG.Shared.Module` 出现在 Core 文件里。
- 这意味着当前 `Core -> 外层实现` 的硬依赖已经基本被清空。
- 从职责边界角度看，这比上一版状态明显更稳。

### 3.2 Debug 的会话耦合已经从“实现依赖”变成“端口依赖”

- `MiniRPG.Shared/Core/Debug/DebugModule.cs`
  - `HandleCommand`、`MoveDownFloor`、`ExportPreset` 仍然会触发会话动作，但现在只依赖 `IDebugSessionActions`
- `MiniRPG.Shared/Core/Debug/IDebugSessionActions.cs`
  - 端口只有 `ChangeFloor` 和 `ExportPresetScenario`
- 这类依赖已经属于可接受的“Core 定义端口，外层提供实现”，不再算边界越界。
- 还需要保留一点警觉：`DebugModule` 现在主题是统一的，但已经是一个偏厚的调试总入口。

### 3.3 Weather 与 Save 的直接耦合已经被 adapter 收口

- `MiniRPG.Shared/Core/Weather/WeatherSystem.cs`
  - `WeatherSurface.GetAccumulation`
  - `WeatherAccumulationSimulator.ClearAccumulation`
  - 现在只通过 `IWeatherAccumulationStore` 访问未加载 chunk 的天气积累
- `MiniRPG.Shared/Core/Map/SaveModule.cs`
  - 静态构造里把 `WeatherSurface.AccumulationStore` 指向 `SaveModuleWeatherAdapter`
- `MiniRPG.Shared/Core/Map/SaveModuleWeatherAdapter.cs`
  - 明确承担 `DirtyChunkCache -> IWeatherAccumulationStore` 适配责任
- 这是一种正确的收口方式：天气系统不再知道存档缓存内部结构，跨子域接触点被收缩到一个显式 adapter。

### 3.4 当前最明显的边界漂移还在地图兼容层

- `MapModule` 文件头自己就说明了它是“作为 `WorldMap` 的代理层，提供与旧 API 兼容的接口”。
- `WorldMap` 里仍然保留 `GetDisplayCell`、`ResolveFixtureGlyph` 这类带显示语义的函数。
- 这些点不是严重越界，但会持续拖慢后续把 World/Render 彻底分开的工作。

## 4. 当前明确的边界漂移

| 文件 | 函数簇 | 判断 | 原因 |
| --- | --- | --- | --- |
| `MiniRPG.Shared/Core/Map/MapModule.cs` | `SetTerrain` / `GetTerrain` / `SetFixture` / `GetFixture` / `GetDisplayCell` / `GoDown` / `GoUp` / `PlacePlayerAtFixture` / `GlyphToFixtureId` | 边界漂移 | 文件自己定义为旧 API 兼容层；其中既有世界访问代理，也混入了 glyph 兼容、显示字符查询和楼层导航 helper。 |
| `MiniRPG.Shared/Core/World/WorldMap.cs` | `GetDisplayCell` / `ResolveFixtureGlyph` | 边界漂移 | `WorldMap` 本应保持世界真相与空间查询，当前仍保留显示字符选择和 fixture glyph 映射，表现层语义还没完全退出。 |

这两处的共同点不是“已经坏掉”，而是“正在继续吃旧兼容债务”。  
如果要继续收紧边界，它们比别的文件更值得优先处理。

## 5. 当前偏厚但仍在边界内的热点

| 文件 | 方法数 | 行数 | 判断 | 说明 |
| --- | ---: | ---: | --- | --- |
| `MiniRPG.Shared/Core/Map/SaveModule.cs` | 63 | 1130 | 边界内但偏厚 | 同时承载快照装配、JSON 序列化、chunk cache、标题/header 生成和版本兼容。 |
| `MiniRPG.Shared/Core/Combat/TimelineTurnManager.cs` | 56 | 1167 | 边界内但偏厚 | 时间线调度、玩家输入提交、AI 自动推进、调试快照、profiling 都在一个文件。 |
| `MiniRPG.Shared/Core/Weather/WeatherSystem.cs` | 52 | 808 | 边界内但偏厚 | 枚举/状态/规则/采样/积累模拟/闪电伤害/数学工具全部堆在同一文件。 |
| `MiniRPG.Shared/Core/Debug/DebugModule.cs` | 39 | 668 | 边界内但偏厚 | 调试命令、天气调试、设施调试、生成/导出、会话端口调用集中在一起。 |
| `MiniRPG.Shared/Core/Config/GameLocalizer.cs` | 35 | 466 | 边界内但偏厚 | 既做静态文本本地化，又做运行时状态重写和 humanize fallback。 |
| `MiniRPG.Shared/Core/Health/SurgeryModule.cs` | 35 | 784 | 边界内但偏厚 | 尸体处理、肢体移除、安装/收获、掉落、死亡收尾都压在一个模块里。 |
| `MiniRPG.Shared/Core/Health/FireSystem.cs` | 34 | 730 | 边界内但偏厚 | 火焰 hazard、点燃、熄灭、燃烧扩散、环境伤害、掉落物处理全在一个系统里。 |
| `MiniRPG.Shared/Core/Config/AppSettingsStore.cs` | 30 | 468 | 边界内但偏厚 | locale、键位、调试开关、continue、multiplayer、路径与日志都归在一个 store。 |
| `MiniRPG.Shared/Core/Map/WorldStore.cs` | 30 | 627 | 边界内但偏厚 | manifest、目录结构、世界删除、角色索引、迁移、安全删除都在一个 store。 |
| `MiniRPG.Shared/Core/AI/AIDispatcher.cs` | 23 | 514 | 边界内但偏厚 | awareness、health、fire、temperature、need、job 行为链都由它串起来。 |
| `MiniRPG.Shared/Core/Data/FacilityModels.cs` | 28 | 656 | 边界内但偏厚 | DTO、运行时状态、注册表和 footprint 细节集中在一个文件。 |
| `MiniRPG.Shared/Core/Health/HealthSystem.cs` | 21 | 639 | 边界内但偏厚 | 伤害、感染、出血、湿度、温度、治疗等多种同步逻辑集中。 |

需要强调：这些文件的问题主要是“过厚”，不是“跑到别层”。  
这意味着后续更适合做小步拆分，而不是大洗牌。

## 6. 按目录全量盘点

下面的判断覆盖本次扫描到的全部 140 个 Core 文件。  
如果某个文件是混合状态，会单独点名函数簇；如果没有单独点名，表示该文件内函数整体都在职责边界内。

### 6.1 AI（9 文件 / 82 方法）

- `边界内`：
  - `AIProfiling.cs`
  - `AIVisionBatch.cs`
  - `AwarenessModule.cs`
  - `FireBehaviorModule.cs`
  - `FollowerBrain.cs`
  - `IBrainModule.cs`
  - `PerceptionBuilder.cs`
  - `SimpleBrain.cs`
- `边界内但偏厚`：
  - `AIDispatcher.cs`
- 结论：
  - AI 子域的函数都仍然围绕“感知 -> 决策 -> 执行”展开。
  - `AIDispatcher` 虽然会串 `Health / Need / Job / Fire / Temperature`，但它承担的是 AI 行为管线调度器职责，这种跨子域接触属于应有编排。

### 6.2 Combat（7 文件 / 98 方法）

- `边界内`：
  - `CombatModule.cs`
  - `FirearmModule.cs`
  - `NestModule.cs`
  - `SkillCasting.cs`
  - `TurnModule.cs`
- `边界内但偏厚`：
  - `ActionModule.cs`
  - `TimelineTurnManager.cs`
- 结论：
  - `Combat` 没有越界到 UI、会话或平台层。
  - `ActionModule` 作为统一动作入口天然会接触 `Map / Health / Surgery / Interaction`，但仍属于动作执行域。
  - `TimelineTurnManager` 是当前时间线玩法的最大复杂度中心，建议继续拆 player action handler、auto step、debug snapshot、profiling。

### 6.3 Config（6 文件 / 104 方法）

- `边界内`：
  - `GameConfig.cs`
  - `GameDataLocator.cs`
  - `LocalizationService.cs`
  - `MultiplayerSettings.cs`
- `边界内但偏厚`：
  - `AppSettingsStore.cs`
  - `GameLocalizer.cs`
- 结论：
  - 这组文件虽然包含路径解析、磁盘持久化和文本本地化，但都仍在“配置/本地化”这个大主题内部。
  - `AppSettingsStore` 与 `GameLocalizer` 的主要风险是主题过大，不是串层。

### 6.4 Data（29 文件 / 223 方法）

- `边界内`：
  - `Actor.cs`
  - `ActorModule.cs`
  - `ActorTemplate.cs`
  - `CapacityDef.cs`
  - `CellEntity.cs`
  - `Const.cs`
  - `DialogContext.cs`
  - `DialogTypes.cs`
  - `FixtureDef.cs`
  - `GameEvent.cs`
  - `GameState.cs`
  - `GridDirections.cs`
  - `IdentificationModule.cs`
  - `InteractionDef.cs`
  - `InteractionDefs.cs`
  - `InteractionModule.cs`
  - `InventoryModule.cs`
  - `Item.cs`
  - `ItemConditionFormatter.cs`
  - `ItemContentDefinitions.cs`
  - `MaterialDef.cs`
  - `PartyModule.cs`
  - `PlayerAppearanceCatalog.cs`
  - `PlayerCreationOptions.cs`
  - `PresetDB.cs`
  - `Quest.cs`
  - `SkillQuery.cs`
  - `TagSystem.cs`
- `边界内但偏厚`：
  - `FacilityModels.cs`
- 结论：
  - 这一组大多是实体、定义、运行时数据访问和识别/交互 helper，整体很稳。
  - `Actor.cs` 方法不少，但都还围绕实体内部状态、装备、tag、capacity 和缓存。
  - `FacilityModels.cs` 把 DTO、运行时状态、registry 与 footprint 细节放在一起，属于组织粒度偏粗，不是职责越界。

### 6.5 Debug（2 文件 / 39 方法）

- `边界内`：
  - `IDebugSessionActions.cs`
- `边界内但偏厚`：
  - `DebugModule.cs`
- 结论：
  - 旧版最明显的问题已经修掉了，`DebugModule` 不再直接抓具体会话实现。
  - 当前 `MoveDownFloor`、`ExportPreset` 通过端口跨边界，是合理依赖，不再算越界。
  - 但 `DebugModule` 已经是一个偏厚的调试总控，后续再加命令时建议按主题拆子处理器。

### 6.6 Dialog（3 文件 / 22 方法）

- `边界内`：
  - `DialogPool.cs`
  - `DialogRuleEngine.cs`
  - `TemplateRenderer.cs`
- 结论：
  - 加载、规则筛选、模板渲染三层分工清楚，没有看到职责跑偏。

### 6.7 Event（6 文件 / 24 方法）

- `边界内`：
  - `IIncidentWorker.cs`
  - `IncidentDef.cs`
  - `Storyteller.cs`
  - `StorytellerDefLoader.cs`
  - `StorytellerState.cs`
  - `Workers/BuiltinIncidentWorkers.cs`
- 结论：
  - 全部函数都集中在事件定义、权重挑选、worker 执行与 storyteller 状态维护上，边界清晰。

### 6.8 Facility（1 文件 / 18 方法）

- `边界内`：
  - `FacilityConstructionModule.cs`
- 结论：
  - 蓝图放置、材料投递、施工票据和设施状态归一都属于设施建造域自身，没有发现串到会话/UI 层。

### 6.9 Farm（2 文件 / 14 方法）

- `边界内`：
  - `CropModels.cs`
  - `FarmModule.cs`
- 结论：
  - 全部函数边界内。

### 6.10 Health（11 文件 / 140 方法）

- `边界内`：
  - `DefaultEnvironmentExposureProvider.cs`
  - `HealthActionModule.cs`
  - `HealthBehaviorModule.cs`
  - `HealthCatalog.cs`
  - `HealthModels.cs`
  - `HeatActionModule.cs`
  - `RoomContextAnalyzer.cs`
  - `TemperatureBehaviorModule.cs`
- `边界内但偏厚`：
  - `FireSystem.cs`
  - `HealthSystem.cs`
  - `SurgeryModule.cs`
- 结论：
  - 这组文件会自然接触 `Map / Item / Combat / Need / Weather`，但这些接触都服务于“健康/火焰/手术”规则本身。
  - `FireSystem`、`HealthSystem`、`SurgeryModule` 需要持续拆薄，但还没有掉出 Health 子域。

### 6.11 Job（3 文件 / 37 方法）

- `边界内`：
  - `JobBehaviorModule.cs`
  - `JobExecutor.cs`
  - `JobScheduler.cs`
- 结论：
  - 工作系统会读取设施、背包、路径和施工票据，这是应有职责。
  - `JobExecutor`、`JobScheduler` 都不算轻，但仍在同一职责面内。

### 6.12 Map（8 文件 / 163 方法）

- `边界内`：
  - `ItemSnapshotMapper.cs`
  - `MapGenModule.cs`
  - `PresetScenarioCatalog.cs`
  - `SaveModuleWeatherAdapter.cs`
  - `SaveSnapshot.cs`
- `边界内但偏厚`：
  - `SaveModule.cs`
  - `WorldStore.cs`
- `边界漂移`：
  - `MapModule.cs`
- 结论：
  - `SaveModule` 作为跨子域快照装配器，看到 `Need / Health / Room / Facility / Weather` 并不意外，关键问题是它太厚。
  - `SaveModuleWeatherAdapter` 是显式 adapter，属于正向设计，不是越界。
  - `WorldStore` 仍然在“世界目录与存储布局”边界内，但已经承担了 manifest、迁移、索引、安全删除等多块工作。
  - `MapModule` 是当前唯一需要明确标记为边界漂移的 Map 文件。

### 6.13 Multiplayer（15 文件 / 136 方法）

- `边界内`：
  - `DedicatedGameServerHost.cs`
  - `HostedLobbyService.cs`
  - `ILocalServerLauncher.cs`
  - `LobbyHttpService.cs`
  - `MultiplayerTelemetryStore.cs`
  - `Protocol.cs`
  - `ProtocolErrorCodeExtensions.cs`
  - `ProtocolSerializer.cs`
  - `RoomActorCatalog.cs`
  - `RoomCombatRules.cs`
  - `RoomModels.cs`
  - `ServerAuditLog.cs`
  - `TransportError.cs`
- `边界内但偏厚`：
  - `LobbyService.cs`
  - `RoomRuntimeModule.cs`
- 结论：
  - 这组文件更偏联机基础设施与房间规则，但都还留在 `multiplayer` 子域内部，没有倒挂到 App/UI。
  - `LobbyService`、`RoomRuntimeModule` 的复杂度已经偏高，不过仍是单一主题。

### 6.14 Needs（6 文件 / 53 方法）

- `边界内`：
  - `MentalBreakModule.cs`
  - `NeedActionModule.cs`
  - `NeedBehaviorModule.cs`
  - `NeedCatalog.cs`
  - `NeedModels.cs`
  - `NeedSystem.cs`
- 结论：
  - 全部函数边界内。
  - `NeedActionModule` 和 `NeedBehaviorModule` 接触 `Health / Map / ActionModule` 属于需求驱动行为的自然边界。

### 6.15 Social（3 文件 / 20 方法）

- `边界内`：
  - `SocialInteractionLoader.cs`
  - `SocialModels.cs`
  - `SocialModule.cs`
- 结论：
  - 全部函数边界内。

### 6.16 Trade（1 文件 / 4 方法）

- `边界内`：
  - `TradeModule.cs`
- 结论：
  - 全部函数边界内。

### 6.17 Weather（2 文件 / 52 方法）

- `边界内`：
  - `IWeatherAccumulationStore.cs`
- `边界内但偏厚`：
  - `WeatherSystem.cs`
- 结论：
  - 当前天气域的职责边界比上一版明显更稳。
  - `WeatherSurface.GetAccumulation`、`WeatherAccumulationSimulator.ClearAccumulation` 已经改为依赖端口，不再直接触碰 Save 内部缓存。
  - 剩下的问题主要是文件过大，建议未来拆出 `WeatherRules`、`WeatherFieldSampler`、`WeatherAccumulationSimulator`、`WeatherMath` 独立文件。

### 6.18 World（25 文件 / 191 方法）

- `边界内`：
  - `BlockPlaceModule.cs`
  - `ChunkData.cs`
  - `ChunkManager.cs`
  - `ClimbingService.cs`
  - `DigModule.cs`
  - `Generators/BlankFloorGenerator.cs`
  - `Generators/BSPGenerator.cs`
  - `Generators/CellularAutomataGenerator.cs`
  - `Generators/DrunkardWalkGenerator.cs`
  - `Generators/GeneratorPopulateHelper.cs`
  - `Generators/PerlinGenerator.cs`
  - `Generators/RoomCorridorGenerator.cs`
  - `Generators/SurfaceGenerator.cs`
  - `Generators/VoxelBlockIsometricGenerator.cs`
  - `IChunkSimulator.cs`
  - `IMapGenerator.cs`
  - `IViewMode.cs`
  - `Noise/PerlinNoise.cs`
  - `Pathfinding.cs`
  - `ShadowcastFOV.cs`
  - `TerrainDef.cs`
  - `VisibilityUtil.cs`
  - `VisionRangeScaler.cs`
  - `WorldCoord.cs`
- `边界漂移`：
  - `WorldMap.cs`
- 结论：
  - 世界层的大多数函数都很纯，尤其是 chunk、pathfinding、FOV、generator 这一块边界非常干净。
  - `WorldMap` 的主要问题不是空间真相部分，而是里面仍残留显示字符和 glyph 映射语义。
  - `WorldMap` 还顺带承担了 ground item snapshot/meta 读写，这让它更厚，但还没到越界程度。

### 6.19 Zone（1 文件 / 8 方法）

- `边界内`：
  - `ZoneModule.cs`
- 结论：
  - 全部函数边界内。

## 7. 优先修整顺序

如果只做最值钱的边界收口，建议按下面顺序推进：

1. 收掉 `MapModule / WorldMap` 的 legacy glyph 与 display helper
   - 先把 `GetDisplayCell`、`ResolveFixtureGlyph`、glyph 兼容 setter/getter 逐步推回 renderer 或 compatibility adapter
   - 目标是让 `WorldMap` 更像世界真相层，让 `MapModule` 更像薄代理
2. 拆 `SaveModule`
   - 优先把 snapshot mapper、header/title 生成、chunk cache 适配、JSON 版本兼容继续外提
3. 拆 `TimelineTurnManager`
   - 按 player action handler、auto advance、timeline debug/profiling 分层
4. 拆 `WeatherSystem`
   - 文件内已经天然形成多个子主题，继续分文件会很直接
5. 控制几个“还没越界但会继续膨胀”的总控文件
   - `DebugModule`
   - `GameLocalizer`
   - `AppSettingsStore`
   - `WorldStore`
   - `FireSystem`
   - `SurgeryModule`

## 8. 结论

- 站在职责边界维度看，当前 `Core` 是健康的，且比上一版更稳。
- 旧版最明确的几处越界点已经被端口/adapter/目录归位处理掉了。
- 当前真正需要继续处理的不是“跨层乱串”，而是：
  - 地图兼容层里还没清完的 legacy/display 语义
  - 少数总控型文件持续变厚
- 所以下一步最合适的策略不是推倒重来，而是继续做小步、精准、可验证的边界收口：
  - 先清 `MapModule / WorldMap`
  - 再拆 `SaveModule / TimelineTurnManager / WeatherSystem`
  - 其余厚文件按增量需求逐个削薄
