# Core 职责边界全量分析（2026-04-12）

## 1. 范围与方法

- 扫描范围：
  - `D:\Godot\mini-rpg\Core\**\*.cs`
  - `D:\Godot\mini-rpg\MiniRPG.Shared\Core\**\*.cs`
- 本次统计结果：
  - 141 个运行时源码文件
  - 1407 个函数/方法（含静态方法、实例方法、轻量工厂方法；不含属性）
- 判定口径：
  - `边界内`：函数只处理单一主题，或者明确属于协调器/门面应承担的编排责任。
  - `边界内但过厚`：主题没跑偏，但一个文件里塞进了过多子职责，后续维护成本会升高。
  - `越界`：函数直接跨到别的层级，尤其是 `Core -> Module/UI/平台适配`，或者把持久化/显示逻辑硬耦合到领域逻辑里。

这份文档的目标不是挑格式问题，而是回答两个更实际的问题：

1. `Core` 里的函数大体有没有守住边界。
2. 真正值得马上拆的点在哪里。

## 2. 总体结论

- 结论先说：`Core` 的大多数函数仍然在职责边界内，尤其是 `AI / Combat / Health / Needs / Dialog / World generators / Social / Trade / Zone` 这些算法和规则型模块，边界总体是稳的。
- 真正明确越界的点不多，集中在 4 处：
  - `DebugModule` 直接依赖 `GameSessionModule`。
  - `Session` 目录里的 backend 实现直接依赖 `MiniRPG.Shared/Module`，职责更像应用层/基础设施层，而不是领域 core。
  - `WeatherSystem` 直接读取/写入 `SaveModule.DirtyChunkCache`，把天气系统和存档缓存绑死了。
  - `WorldMap / MapModule` 仍保留一批基于 `glyph` 的显示兼容 API，存在表现层语义回流到 world/core 的问题。
- 最大的风险不是“全局已经失控”，而是少数大文件持续变厚。当前最需要盯住的是：
  - `SaveModule`（64）
  - `WorldMap`（58）
  - `WeatherSystem`（47）
  - `MapModule`（43）
  - `GameLocalizer`（43）
  - `TimelineTurnManager`（40）
  - `DebugModule`（39）
  - `Protocol`（39）
  - `Actor`（34）
  - `WorldStore`（31）
  - `AppSettingsStore`（30）
  - `FireSystem`（30）
  - `SurgeryModule`（29）

换句话说：当前 `Core` 不是“全面越界”，而是“核心层总体健康，但已经出现几个典型厚模块和少量倒挂点”。

## 3. 明确越界与边界漂移

### 3.1 明确越界

| 文件 | 函数 | 判定 | 原因 |
| --- | --- | --- | --- |
| `MiniRPG.Shared/Core/Debug/DebugModule.cs` | `HandleCommand`、`MoveDownFloor`、`ExportPreset` | 越界 | `Core.Debug` 直接吃 `GameSessionModule`，已经碰到会话生命周期和应用层流程，不再是纯 debug 规则或数据。 |
| `MiniRPG.Shared/Core/Session/LocalSessionBackend.cs` | `StartAsync`、`SubmitCommandAsync`、`EmitFullSnapshot` | 越界（目录层面） | 这些函数本身职责清楚，但它们是本地会话适配器，直接依赖 `GameSessionModule` / `ServerActionGateway`，更像 `Module/Session` 或 `Infrastructure/Session`。 |
| `MiniRPG.Shared/Core/Session/MultiplayerSessionBackend.cs` | `ConnectAsync`、`SubmitCommandAsync`、`HandleMessageReceived`、`Poll` | 越界（目录层面） | 这是网络接入适配器，直接依赖 `ENetGameClient`，不属于纯领域 core。 |
| `MiniRPG.Shared/Core/Weather/WeatherSystem.cs` | `WeatherSurface.GetAccumulation`、`WeatherAccumulationSimulator.ClearAccumulation` | 越界 | 天气系统直接触碰 `SaveModule.DirtyChunkCache`，把领域规则和存档内部缓存结构绑在一起。 |

### 3.2 边界漂移但暂可接受

| 文件 | 函数簇 | 判定 | 说明 |
| --- | --- | --- | --- |
| `MiniRPG.Shared/Core/World/WorldMap.cs` | `GetDisplayCell`、`ResolveFixtureGlyph` | 边界漂移 | `WorldMap` 本该是世界数据访问层，但这两个函数明显带有表现层语义。短期还能用，长期建议把显示选择移回 renderer / presenter。 |
| `MiniRPG.Shared/Core/Map/MapModule.cs` | `SetTerrain/GetTerrain`（glyph 兼容）、`SetFixture/GetFixture`、`GetDisplayCell` | 边界漂移 | 它现在是“旧文本地图 API 的兼容层”，本质上已经混入表现兼容逻辑。 |
| `MiniRPG.Shared/Core/Config/AppSettingsStore.cs` | 全文件 | 边界内但过厚 | 仍属于“应用设置持久化”这个大主题，但把 locale、UI、continue、multiplayer、路径解析、日志全塞进一个类里了。 |
| `MiniRPG.Shared/Core/Config/GameLocalizer.cs` | `ApplyPresetTranslations`、`RelocalizeGameState` + 一批 `Localize*` helper | 边界内但过厚 | 主题仍是本地化，但同时承担了数据重写器、显示文本 helper、humanize fallback。 |
| `MiniRPG.Shared/Core/Map/SaveModule.cs` | `BuildSnapshot`、`ApplySnapshot` | 边界内但过厚 | 作为存档聚合器跨多个子域是合理的，但目前“预同步 + 映射 + 文件 IO + cache”全堆在一个文件里。 |
| `MiniRPG.Shared/Core/Combat/TimelineTurnManager.cs` | `SubmitPlayerAction`、`AdvanceAuto`、`AdvanceAutoSingleStep`、`Finalize*` 系列 | 边界内但过厚 | 仍属于时间线调度器，但调度、调试、性能剖析、玩家提交、AI 自动步进都在一个文件里。 |

## 4. 按模块全量盘点

下面的判断是“文件内全部函数”的判断。若某文件是混合状态，会单独点名具体函数。

### 4.1 TopLevelCore

- `Core/Config/GodotConfigBridge.cs`（7）：
  - 全部函数边界内。
  - 它不是共享领域逻辑，而是 Godot 运行时适配桥，职责清晰。
  - 唯一要注意的是它和 `GameDataLocator` 存在平台探测逻辑重复，但这属于实现复用问题，不是职责错位。

### 4.2 Config（6 文件 / 112 方法）

- `GameConfig.cs`（1）、`GameDataLocator.cs`（19）、`LocalizationService.cs`（16）、`MultiplayerSettings.cs`（3）：
  - 全部函数边界内。
  - 分别承担配置加载、数据目录定位、本地化运行时服务、联机设置 DTO，没有明显串层。
- `AppSettingsStore.cs`（30）：
  - 全文件边界内但过厚。
  - 问题不在“越界”，而在“一个 store 装了太多设置子域”。
  - 建议未来拆成 `UiSettingsStore`、`ContinueStateStore`、`MultiplayerSettingsStore` 三块。
- `GameLocalizer.cs`（43）：
  - 大部分函数边界内。
  - `CaptureBaseSnapshots`、`ApplyPresetTranslations`、`RelocalizeGameState` 属于本地化重写器职责。
  - 一大批 `LocalizeEquipLayer`、`LocalizeDamageType`、`LocalizeWeatherName`、`LocalizeEffectType` 之类 helper 也还在“文本本地化”范围内，但和前面的数据重写职责混在同一文件，已经偏厚。

### 4.3 Data（29 文件 / 217 方法）

- 纯模型/常量/轻量定义，全部函数边界内：
  - `ActorTemplate.cs`（1）
  - `CapacityDef.cs`（0）
  - `CellEntity.cs`（0）
  - `Const.cs`（0）
  - `DialogContext.cs`（1）
  - `DialogTypes.cs`（0）
  - `FixtureDef.cs`（3）
  - `GameEvent.cs`（0）
  - `GridDirections.cs`（0）
  - `InteractionDef.cs`（0）
  - `InteractionDefs.cs`（1）
  - `MaterialDef.cs`（2）
  - `PlayerAppearanceCatalog.cs`（5）
  - `PlayerCreationOptions.cs`（6）
  - `Quest.cs`（0）
- 运行时实体/服务，全部函数基本守住边界：
  - `Actor.cs`（34）：边界内。函数都围绕角色状态、tag/capacity、cooldown/home position 这些实体内规则，没有跑到别层。
  - `ActorModule.cs`（19）：边界内。就是对 `GameState.Actors` 和 `WorldMap` actor 索引的访问门面。
  - `GameState.cs`（2）：边界内。它是聚合根，本来就负责持有多个子域的运行时事实源，不算越界。
  - `IdentificationModule.cs`（21）：边界内。主题始终是“显示身份与识别状态”。
  - `InteractionModule.cs`（3）：边界内。虽然会碰 `TradeModule`、`DigModule`、`MapModule`，但这是“交互编排器”的应有职责。
  - `InventoryModule.cs`（15）：边界内。
  - `Item.cs`（15）：边界内。
  - `ItemConditionFormatter.cs`（3）：边界内。
  - `PartyModule.cs`（13）：边界内。
  - `SkillQuery.cs`（16）：边界内。
- 注册表/数据目录类，基本在边界内但有个别文件偏厚：
  - `FacilityModels.cs`（26）：边界内但偏厚。它把常量、DTO、运行时状态、注册表放在一个文件里，主题没跑，但组织粒度不够细。
  - `ItemContentDefinitions.cs`（17）：边界内。虽然内容多，但仍属于 item content 定义与注册。
  - `PresetDB.cs`（8）：边界内。是预设数据库入口。
  - `TagSystem.cs`（6）：边界内。

### 4.4 AI（9 文件 / 79 方法）

- `AIProfiling.cs`（6）、`AIVisionBatch.cs`（7）、`AwarenessModule.cs`（7）、`FireBehaviorModule.cs`（9）、`FollowerBrain.cs`（5）、`IBrainModule.cs`（1）、`PerceptionBuilder.cs`（2）、`SimpleBrain.cs`（21）：
  - 全部函数边界内。
  - 这些文件虽然会引用 `Combat/Health/World`，但都是为了完成 AI 感知、脑决策或行为短路链，本质仍属于 AI 子域。
- `AIDispatcher.cs`（21）：
  - 边界内但过厚。
  - `TickAllInternal`、`DecideAndExecuteOneResult`、`ExecuteDecision` 把 awareness / health / fire / temperature / needs / job 行为链都串到了一起。
  - 这仍是 AI 调度器的工作，不算越界，但已经很像“行为管线总装配器”，后续适合拆 stage。

### 4.5 Combat（7 文件 / 76 方法）

- `CombatModule.cs`（9）、`FirearmModule.cs`（6）、`NestModule.cs`（4）、`SkillCasting.cs`（0）、`TurnModule.cs`（6）：
  - 全部函数边界内。
  - `TurnModule` 会调用 `WeatherAccumulationSimulator`、`FireSystem`、`Storyteller`、`FarmModule`，这属于回合推进协调器本职。
- `ActionModule.cs`（11）：
  - 边界内但偏厚。
  - 统一玩家/AI 动作入口本来就会碰 `Map/Health/World/Interaction/Combat`。
  - 当前问题不是越界，而是 `TryCastSkill` 里的 effect-type 分支已经比较长，未来应继续按 effect 拆 handler。
- `TimelineTurnManager.cs`（40）：
  - 边界内但过厚。
  - 时间线调度、玩家输入提交、AI 自动推进、性能剖析、调试快照都在这里，主题仍统一，但文件过重。

### 4.6 Dialog（3 文件 / 25 方法）

- `DialogPool.cs`（7）、`DialogRuleEngine.cs`（3）、`TemplateRenderer.cs`（15）：
  - 全部函数边界内。
  - `DialogPool` 负责加载，`DialogRuleEngine` 负责规则筛选，`TemplateRenderer` 负责模板替换，分工清楚。

### 4.7 Event（6 文件 / 28 方法）

- `IIncidentWorker.cs`（0）、`IncidentDef.cs`（0）、`StorytellerDefLoader.cs`（2）、`StorytellerState.cs`（2）、`Workers/BuiltinIncidentWorkers.cs`（9）：
  - 全部函数边界内。
- `Storyteller.cs`（15）：
  - 边界内。
  - 它是事件调度器，本来就要跨 incident 规则、冷却、权重、worker 执行。

### 4.8 Facility（1 文件 / 17 方法）

- `FacilityConstructionModule.cs`（17）：
  - 全部函数边界内。
  - 该文件会碰材料交付、建造阶段、ticket 重建，但这些都还是设施建造子域本身。

### 4.9 Farm（2 文件 / 15 方法）

- `CropModels.cs`（4）、`FarmModule.cs`（11）：
  - 全部函数边界内。

### 4.10 Health（11 文件 / 116 方法）

- 纯模型/目录类：
  - `HealthModels.cs`（0）
  - `HealthCatalog.cs`（7）
  - 全部函数边界内。
- 行为与系统类：
  - `DefaultEnvironmentExposureProvider.cs`（13）：边界内。虽然读取 `Weather/World/Config`，但这正是“给健康系统提供环境暴露快照”的职责。
  - `HealthActionModule.cs`（5）：边界内。
  - `HealthBehaviorModule.cs`（3）：边界内。
  - `HealthSystem.cs`（12）：边界内但偏厚。集中了承伤、感染、疼痛、湿度、温度等同步逻辑，但仍属于健康系统中心。
  - `HeatActionModule.cs`（3）：边界内。
  - `RoomContextAnalyzer.cs`（7）：边界内。
  - `SurgeryModule.cs`（29）：边界内但偏厚。尸体、活体、收获、安装都在一个文件里，不过主题仍是手术/解剖。
  - `TemperatureBehaviorModule.cs`（7）：边界内。
  - `FireSystem.cs`（30）：边界内但偏厚。火焰 hazard、actor 点燃、扩散、熄灭、危险格评估都在同一个系统里，主题一致但实现体量很大。

### 4.11 Needs（6 文件 / 48 方法）

- `NeedModels.cs`（3）、`NeedCatalog.cs`（8）：
  - 全部函数边界内。
- `NeedSystem.cs`（14）、`NeedActionModule.cs`（8）、`NeedBehaviorModule.cs`（7）、`MentalBreakModule.cs`（8）：
  - 全部函数边界内。
  - `NeedActionModule.TryRest` 会触碰 `HealthSystem` 和 `DefaultEnvironmentExposureProvider`，但这是“休息动作要更新湿度/思想/需求”的自然边界，不算越界。

### 4.12 Job（3 文件 / 38 方法）

- `JobBehaviorModule.cs`（2）、`JobScheduler.cs`（16）、`JobExecutor.cs`（20）：
  - 全部函数边界内。
  - `JobScheduler` 和 `JobExecutor` 会碰设施、背包、路径、stockpile，这是工作系统必须做的跨子域协作。
  - 当前问题主要是两文件都偏厚，不是职责跑偏。

### 4.13 Map（7 文件 / 164 方法）

- `ItemSnapshotMapper.cs`（8）、`MapGenModule.cs`（9）、`PresetScenarioCatalog.cs`（9）、`SaveSnapshot.cs`（0）、`WorldStore.cs`（31）：
  - 全部函数边界内。
  - `WorldStore` 体量较大，但始终围绕世界清单、角色存档路径、迁移、删除、安全删除做持久化门面。
- `SaveModule.cs`（64）：
  - 边界内但过厚。
  - `BuildSnapshot` 和 `ApplySnapshot` 本质是跨子域的“存档装配器”，因此看到 `Need/Health/Facility/Weather/Room` 很正常。
  - 真问题是“映射 + 文件 IO + cache + header/title 生成”没有继续下拆。
- `MapModule.cs`（43）：
  - 混合状态。
  - `PlaceItem/PickupItem/PeekGroundItems/IsWalkable/IsWall` 这类函数边界内。
  - `SetTerrain/GetTerrain/SetFixture/GetFixture/GetDisplayCell` 明显带着旧 `glyph` 文本地图表现兼容，属于边界漂移。
  - `GoDown/GoUp/PlacePlayerAtFixture` 更像 session/navigation helper，也说明这个兼容层正在承担额外职责。

### 4.14 Multiplayer（15 文件 / 134 方法）

- 协议与模型层，全部函数边界内：
  - `Protocol.cs`（39）
  - `ProtocolErrorCodeExtensions.cs`（2）
  - `ProtocolSerializer.cs`（6）
  - `RoomModels.cs`（8）
  - `ServerAuditLog.cs`（0）
  - `TransportError.cs`（0）
- 房间规则与目录层，全部函数边界内：
  - `RoomActorCatalog.cs`（2）
  - `RoomCombatRules.cs`（2）
  - `RoomRuntimeModule.cs`（17）
- Lobby / host / telemetry / launcher，全部函数基本边界内：
  - `DedicatedGameServerHost.cs`（15）
  - `HostedLobbyService.cs`（11）
  - `ILocalServerLauncher.cs`（2）
  - `LobbyHttpService.cs`（8）
  - `LobbyService.cs`（13）
  - `MultiplayerTelemetryStore.cs`（9）
  - 这些文件更偏联机基础设施，但仍在 multiplayer 子域内部，没有出现再向 UI/Godot 表现层倒挂的问题。

### 4.15 Session（3 文件 / 22 方法）

- `IGameSessionBackend.cs`（4）：
  - 作为 port/interface，函数职责清楚。
  - 但从架构分层看，它更像应用层会话端口，而不是领域 core。
- `LocalSessionBackend.cs`（6）、`MultiplayerSessionBackend.cs`（12）：
  - 函数责任本身清楚，分别对应本地 backend 和远端 backend。
  - 问题不在函数内容，而在目录归位：这两个文件应该下沉到 `Module/Session` 或 `Infrastructure`，而不应继续挂在 `Core/Session`。

### 4.16 Social（3 文件 / 20 方法）

- `SocialInteractionLoader.cs`（1）、`SocialModels.cs`（1）、`SocialModule.cs`（18）：
  - 全部函数边界内。

### 4.17 Trade（1 文件 / 4 方法）

- `TradeModule.cs`（4）：
  - 全部函数边界内。

### 4.18 Weather（1 文件 / 47 方法）

- `WeatherSystem.cs`（47）整体判断：
  - 文件主题仍然统一，都是天气域：`WeatherState`、`WeatherRules`、`WeatherSurface`、`WeatherFieldSampler`、`WeatherAccumulationSimulator`。
  - 其中绝大多数函数边界内。
  - 需要单独拎出来的只有：
    - `WeatherSurface.GetAccumulation`
    - `WeatherAccumulationSimulator.ClearAccumulation`
  - 这两个函数直接依赖 `SaveModule.DirtyChunkCache`，导致天气系统知道了存档缓存实现细节，这是本文件当前最明确的边界破口。
  - 另外，这个文件已经太大，后续至少可以拆成：
    - `WeatherRules`
    - `WeatherFieldSampler`
    - `WeatherAccumulationSimulator`
    - `WeatherSurface`

### 4.19 World（25 文件 / 190 方法）

- 纯算法/基础数据，全部函数边界内：
  - `ChunkData.cs`（10）
  - `IChunkSimulator.cs`（1）
  - `IMapGenerator.cs`（0）
  - `IViewMode.cs`（0）
  - `Noise/PerlinNoise.cs`（8）
  - `Pathfinding.cs`（5）
  - `ShadowcastFOV.cs`（3）
  - `VisibilityUtil.cs`（1）
  - `VisionRangeScaler.cs`（0）
  - `WorldCoord.cs`（13）
- world service / rule files，全部函数基本边界内：
  - `BlockPlaceModule.cs`（3）
  - `ChunkManager.cs`（13）
  - `ClimbingService.cs`（7）
  - `DigModule.cs`（8）
  - `TerrainDef.cs`（7）
- generators，全部函数边界内：
  - `Generators/BSPGenerator.cs`（11）
  - `Generators/BlankFloorGenerator.cs`（2）
  - `Generators/CellularAutomataGenerator.cs`（6）
  - `Generators/DrunkardWalkGenerator.cs`（7）
  - `Generators/GeneratorPopulateHelper.cs`（0）
  - `Generators/PerlinGenerator.cs`（5）
  - `Generators/RoomCorridorGenerator.cs`（14）
  - `Generators/SurfaceGenerator.cs`（4）
  - `Generators/VoxelBlockIsometricGenerator.cs`（4）
- `WorldMap.cs`（58）：
  - 大部分函数边界内，尤其是 terrain/entity/facility occupancy/actor index 相关函数。
  - 明显边界漂移的函数：
    - `GetDisplayCell`
    - `ResolveFixtureGlyph`
  - 这两个函数说明 `WorldMap` 仍残留表现层选择逻辑。
  - 另外 `BuildItemMeta`、`RestoreItemFromEntity`、`TryMergeGroundItem` 也让 `WorldMap` 同时承担了 ground item 的存储映射职责。它还没错，但已经过厚。

### 4.20 Zone（1 文件 / 9 方法）

- `ZoneModule.cs`（9）：
  - 全部函数边界内。

## 5. 优先修整顺序

如果只做最值钱的整理，建议按这个顺序：

1. `DebugModule` 去会话化
   - 把 `MoveDownFloor`、`ExportPreset` 一类依赖 `GameSessionModule` 的命令移到 `MiniRPG.Shared/Module` 或 App 层。
   - `Core.Debug` 只保留纯状态变更和查询。
2. `Session` 目录重新归位
   - `LocalSessionBackend`、`MultiplayerSessionBackend` 明确迁到应用层/基础设施层。
   - `Core` 只保留协议 DTO 或会话端口定义。
3. 去掉 `WeatherSystem -> SaveModule.DirtyChunkCache` 直接耦合
   - 最简单的做法是给 `WorldMap` 或 `ChunkManager` 补只读/只写接口，让天气系统不再知道存档缓存的存在。
4. 收缩 `WorldMap / MapModule` 的 glyph 兼容 API
   - 把 `GetDisplayCell`、`ResolveFixtureGlyph`、`glyph -> terrain/fixture` 这类函数逐步移回 renderer / presenter / compatibility adapter。
5. 拆厚模块
   - `SaveModule`
   - `TimelineTurnManager`
   - `GameLocalizer`
   - `AppSettingsStore`
   - `WorldMap`

## 6. 结论

- 站在“职责边界”这个维度看，当前 `Core` 仍然是可控的。
- 绝大多数函数没有乱串层，真正的问题集中在少量厚模块和几处倒挂点。
- 最明确的问题不是算法层，而是“应用层/表现层兼容逻辑继续留在 core 里”：
  - `DebugModule`
  - `Session backends`
  - `glyph 兼容 API`
  - `Weather` 对 `SaveModule` 内部 cache 的直接触碰
- 所以接下来不需要“大洗牌”，更适合做小而准的收口：
  - 先移除倒挂
  - 再把几个最厚的协调器拆到更细的 stage / mapper / adapter

这会比全面重构更稳，也更符合当前工程状态。
