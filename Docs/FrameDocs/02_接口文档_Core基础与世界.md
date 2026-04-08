# 02 接口文档: Core 基础与世界

## 范围与统计

- 范围：`Core/Config`、`Core/Data`、`Core/Map`、`Core/World`、`Core/Weather`
- 当前统计：61 个运行时 `.cs` 文件，209 个 `public` 类型
- 目标：梳理运行时事实源、世界/地图/存档/配置基础设施，以及这些基础类型被谁消费

## 关键行为型类型

| 类型 | 职责 | 主要公开成员 | 输入 / 输出 | 依赖对象 | 上游调用者 | 下游协作者 | 状态写入点 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `GameState` | 单一运行时事实源，聚合世界、角色、时间线、天气、识别状态、设施与经济域 | `Reset`、`EnsureDefaultEconomicDomains` 及一组公开属性 | 输入：各系统对 `GameState` 的读写；输出：被所有运行时系统消费 | `WorldMap`、`TimelineState`、`WeatherState`、`Actor`、`Quest` 等 | 所有运行时系统 | 所有运行时系统 | 几乎所有领域状态 |
| `LocalizationService` | 运行时本地化服务 | `Initialize`、`SetLocale`、`T`、`TOrFallback`、`LocalizeTree` | 输入：语言代码、翻译 key；输出：本地化文本与 `LocaleChanged` 事件 | Godot `TranslationServer`、本地资源 | `Main`、所有 UI 模块 | `GameLocalizer`、菜单/面板/HUD | 当前语言环境 |
| `AppSettingsStore` | 应用侧持久设置与继续状态持久化 | `LoadLocale`、`SaveLocale`、`LoadEnableKeyboardTargeting`、`SaveContinueState` | 输入：设置对象；输出：本地持久化内容 | 本地 JSON 配置文件 | `Main`、`GameSessionModule` | `LocalizationService`、设置流程 | 用户偏好、继续游戏目标 |
| `GameLocalizer` | 把已加载的预设和运行中状态重新本地化 | `CaptureBaseSnapshots`、`ApplyPresetTranslations`、`RelocalizeGameState` | 输入：当前 locale、预设数据库、`GameState`；输出：数据层名称字段被重写 | `PresetDB`、`LocalizationService` | `Main.HandleLanguageChanged` | `PresetDB`、`GameState` | 预设与运行时对象的显示名称 |
| `ActorModule` | 角色容器访问层，封装按 ID / 坐标查找与移动 | `Add`、`Remove`、`GetById`、`GetAllAt`、`MoveActor`、`GetPlayer` | 输入：坐标、角色 ID；输出：角色对象 | `GameState.Actors`、`WorldMap` | `ActionModule`、`AIDispatcher`、`Main` | `WorldMap.RegisterActor`、`WorldMap.UpdateActorChunk` | 角色坐标、角色索引 |
| `InventoryModule` | 背包、装备、使用、消耗、掉落等统一操作层 | `Add`、`RemoveAt`、`ConsumeAt`、`ToggleEquip`、`Use`、`Drop`、`List` | 输入：角色、物品索引；输出：`InventoryResult`、物品列表 | `Actor.Inventory`、`GameState` | `TradeModule`、UI 面板、`ActionModule` | `MapModule`、`NeedActionModule`、`HealthActionModule` | 背包、装备引用、世界掉落 |
| `InteractionModule` | 非战斗交互的查询与执行层 | `GetAvailableTargets`、`GetInteractions`、`Execute`、`DropItem`、`PickupItem`、`ExecuteDig` | 输入：发起者、目标、交互定义；输出：`GameEvent` 列表 | `GameState`、`InventoryModule`、`MapModule` | `ActionModule`、`Main.DoInteract`、各面板 | `Dialog/Trade` 交互、地面/背包操作 | 角色关系、物品位置、地形硬度 |
| `IdentificationModule` | 根据识别状态生成角色/物品显示名，并填充事件身份信息 | `GetActorDisplayName`、`GetItemDisplayName`、`PopulateInitiatorIdentity` 等 | 输入：`GameState`、角色/物品/事件；输出：文案所需身份字段 | `GameState.Identified*` | `CombatModule`、`TradeModule`、UI | `LogModule`、HUD、对话/交易 UI | 事件显示字段、识别集合 |
| `MapGenModule` | 地图生成入口，负责初始化世界、选生成器、找出生点、生成玩家 | `RegisterGenerator`、`AllGenerators`、`GetGenerator`、`InitializeWorld`、`SpawnPlayer`、`FindSpawnPoint` | 输入：`GameState`、生成器 ID、玩家创建参数；输出：已装配好的 `WorldMap` 与玩家 | `GameConfig`、`PresetDB`、`IMapGenerator` | `GameSessionModule` | `WorldMap`、`ActorModule`、`PlayerCreationOptions` | `GameState.World`、玩家坐标、世界种子 |
| `MapModule` | 基于 `GameState` 的地图门面，封装地形/设施/物品/楼梯查询与落点 | `SetTerrain`、`SetFixture`、`PlaceItem`、`PeekGroundItems`、`IsWalkable`、`GoDown/GoUp`、`PlacePlayerAtFixture` | 输入：坐标、实体/物品；输出：地图查询结果 | `GameState.World` | `Main`、`ActionModule`、`MapEditorSession`、`GameSessionModule` | `WorldMap` | 地形、设施、地面堆叠、玩家楼层 |
| `SaveModule` | 存档快照构建与应用、头信息读取、dirty chunk 缓存 | `SaveGame`、`LoadGame`、`TryReadSaveHeader`、`BuildSnapshot`、`ApplySnapshot`、`LoadChunkFromCache`、`SaveChunkToCache` | 输入：`GameState`、文件路径；输出：`SaveLoadStatus`、`SaveFile` | `JsonSerializer`、`NeedSystem`、`HealthSystem` | `GameSessionModule`、`WorldStore` | `ChunkManager`、快照 DTO、`PlayerAppearanceCatalog` | 存档文件、dirty chunk 缓存、`GameState` 恢复 |
| `WorldStore` | 世界目录、世界 manifest 与世界角色存档的持久化门面 | `CreateWorld`、`SaveWorld`、`TryLoadWorld`、`ListWorlds`、`ListWorldCharacters`、`TryGetCharacterSavePath`、`UpdateWorldLastPlayed` | 输入：世界名、设置、world/character ID；输出：`WorldManifest`、`WorldEntryInfo` | 文件系统、`SaveModule.TryReadSaveHeader` | `GameSessionModule`、`Main` | 存档目录结构、`ContinueState` | `world.json`、角色存档路径、最近游玩元数据 |
| `WorldMap` | 分块世界数据结构，提供地形、实体、设施、地面物品与 actor 索引访问 | `GetTerrainId`、`SetTerrain`、`GetEntities`、`PushEntity`、`TryGetFacilityAt`、`PlaceItem`、`RegisterActor` 等 | 输入：世界坐标；输出：地图块内容 | `ChunkManager`、设施索引 | `MapModule`、`TileMapRenderModule`、世界系统 | `ChunkManager`、`FacilityRegistry` | Chunk 内容、设施索引、actor 分块索引 |
| `ChunkManager` | 世界块按需加载、后台加载、卸载与模拟调度 | `GetOrLoad`、`UpdateLoadedChunks`、`ProcessPendingLoads`、`TickSimulation`、`UnloadAll` | 输入：中心坐标、当前回合；输出：已加载 `ChunkData` 集合 | `IMapGenerator`、`IChunkSimulator`、缓存回调 | `WorldMap`、`GameSessionModule`、`Main.FinalizeTimelineStepUi` | `SaveModule` dirty chunk 缓存、生成器 | 加载中的 chunk、卸载缓存 |
| `DigModule` | 地形可破坏性与采掘规则 | `CanApply`、`Execute` 等 | 输入：技能、地形、硬度；输出：是否可挖及结果事件 | `TerrainDef`、`InteractionDef` | `Main.StartDig`、`InteractionModule.ExecuteDig` | `MapModule` | 地形、硬度、掉落 |
| `IMapGenerator` | 世界生成器扩展点 | `Id`、`Name`、`GenerateChunk` / 同类接口成员 | 输入：chunk 坐标与世界种子；输出：`ChunkData` | 生成器实现类 | `MapGenModule`、`ChunkManager` | `BlankFloorGenerator` 等实现 | 生成出的 chunk 内容 |
| `IViewMode` | 渲染视图模式扩展点 | `GetVisibleLayers` / 同类接口成员 | 输入：玩家楼层与世界；输出：渲染层判定 | 视图模式实现类 | `GameSessionModule`、`TileMapRenderModule` | `SingleLayerViewMode`、`MultiLayerViewMode` | 视图模式选择 |
| `IChunkSimulator` | chunk 卸载时的后台模拟扩展点 | `Tick` / 同类接口成员 | 输入：chunk、当前回合、状态引用；输出：模拟副作用 | `ChunkManager` | `ChunkManager` | `NullSimulator` 或未来实现 | 卸载 chunk 的模拟状态 |

## 高密度 DTO 索引

### `GameConfig.cs`

| 类型 | 归类 | 语义 / 关键字段组 | 主要消费方 |
| --- | --- | --- | --- |
| `GameConfig` | static class | 全局配置入口，聚合所有子配置实例 | `Main._Ready`、AI、渲染、天气、世界 |
| `PlayerVisionConfig` | config DTO | 玩家视野半径、后向视野、环境光等 | `FogOfWarTracker` |
| `AIVisionConfig` | config DTO | AI 激活视野、简化更新间隔等 | `AIDispatcher` |
| `WorldRuntimeConfig` | config DTO | chunk 加载半径、预算、缓存上限 | `ChunkManager.ApplyRuntimeConfig` |
| `WeatherConfig` | config DTO | 天气系统全局参数 | `WeatherRules`、`WeatherAccumulationSimulator` |
| `WeatherProfileConfig` | config DTO | 单气候档位参数 | `WeatherRules` |
| `FireConfig` | config DTO | 火焰扩散、伤害、衰减配置 | `FireSystem` |
| `DebugConfig` | config DTO | 调试开关与辅助配置 | `DebugModule`、`Main` |
| `GenerationConfigSet` | config DTO | 地图生成参数集合入口 | `MapGenModule`、各生成器 |
| `BspGenerationConfig` | config DTO | BSP 地图生成参数 | `BSPGenerator` |
| `CellularGenerationConfig` | config DTO | 细胞自动机参数 | `CellularAutomataGenerator` |
| `DrunkardWalkGenerationConfig` | config DTO | 蚯蚓挖掘生成参数 | `DrunkardWalkGenerator` |
| `PerlinGenerationConfig` | config DTO | Perlin 地图噪声参数 | `PerlinGenerator` |
| `RoomCorridorGenerationConfig` | config DTO | 房间走廊生成参数 | `RoomCorridorGenerator` |

### `FacilityModels.cs`

| 类型 | 归类 | 语义 / 关键字段组 | 主要消费方 |
| --- | --- | --- | --- |
| `FacilityStage` | enum | 设施建造阶段 | 设施运行逻辑、保存 |
| `FacilityRotation` | enum | 设施旋转方向 | `WorldMap` 放置校验 |
| `FacilityUseSlotPurpose` | enum | 设施使用槽位用途 | 设施交互、作业分配 |
| `WorkTicketType` | enum | 作业票据类型 | `JobBoardState`、AI 工作流 |
| `OutputTargetStrategy` | enum | 产出物流策略 | 设施产出分发 |
| `EconomicDomainKind` | enum | 经济域种类 | `EconomicDomain` |
| `StockpilePriority` | enum | 库存区优先级 | 物流 / 产出逻辑 |
| `DomainIds` | const/static ids | 经济域常量 ID | 经济系统 |
| `WorkBrainIds` | const/static ids | 工作脑常量 ID | AI / 工作调度 |
| `FacilityIds` | const/static ids | 设施模板 ID | `PresetDB`、地图编辑 |
| `RoomRoleIds` | const/static ids | 房间角色 ID | 房间识别 |
| `FacilityInteractionModes` | const/static ids | 设施交互模式 ID | 交互逻辑 |
| `RecipeTagIds` | const/static ids | 配方标签 ID | 配方筛选 |
| `FacilityTags` | const/static ids | 设施标签 ID | 设施能力判断 |
| `ItemAmount` | DTO | 物品数量对 | 配方、账单、需求 |
| `ZoneCell` | DTO | 区域中的单格坐标 | stockpile / footprint |
| `RoomModifierSet` | DTO | 房间修饰集合 | 房间计算 |
| `FacilityFootprintCell` | DTO | 设施占地单元 | `WorldMap` 放置 |
| `FacilityUseSlot` | DTO | 设施使用插槽 | 设施交互、作业 |
| `FacilityDef` | 配置 DTO | 设施定义：占地、交互、产出、标签 | `FacilityRegistry`、`WorldMap` |
| `RecipeDef` | 配置 DTO | 配方定义：输入、输出、标签 | `RecipeRegistry` |
| `BillDef` | 配置 DTO | 生产单据定义 | 作业排程 |
| `EconomicDomain` | 运行时 DTO | 经济域状态 | `GameState`、存档 |
| `StockpileZone` | 运行时 DTO | 库存区状态 | `GameState`、物流 |
| `WorkTicket` | 运行时 DTO | 单次工作任务票据 | 工作 AI |
| `JobBoardState` | 运行时 DTO | 当前作业板状态 | `GameState`、设施系统 |
| `FacilityInstance` | 运行时 DTO | 地图上的设施实例 | `GameState.Facilities`、`WorldMap` |
| `RoomRoleDef` | 配置 DTO | 房间角色定义 | 房间判定 |
| `RoomSnapshot` | 快照 DTO | 房间状态快照 | 保存 / 诊断 |
| `FacilityRegistry` | registry | 设施定义注册表 | `PresetDB`、地图编辑、`WorldMap` |
| `RecipeRegistry` | registry | 配方注册表 | 设施生产、UI |
| `RoomRoleRegistry` | registry | 房间角色注册表 | 房间识别 |

### `SaveSnapshot.cs`

| 类型 | 归类 | 语义 / 关键字段组 | 主要消费方 |
| --- | --- | --- | --- |
| `SaveLoadStatus` | enum | 读档结果：成功 / 不兼容 / 未找到等 | `GameSessionModule`、`WorldStore` |
| `SaveFile` | 根 DTO | `Version + Header + Payload` | `SaveModule` |
| `SaveHeader` | 头信息 DTO | 世界名、角色名、回合、楼层、时间等摘要 | `WorldStore.ListWorldCharacters`、世界浏览器 |
| `SaveHeaderContext` | DTO | 保存时额外上下文 | `GameSessionModule.BuildSaveHeaderContext` |
| `SavePayload` | 根 payload DTO | `GameState` 的绝大部分镜像 | `SaveModule` |
| `WeatherStateSnapshot` | DTO | 天气状态快照 | `SaveModule` |
| `TimelineSnapshot` | DTO | 回合队列快照 | `SaveModule` |
| `TimelineActorSnapshot` | DTO | 单角色时间线条目 | `TimelineSnapshot` |
| `ActorSnapshot` | DTO | 角色全量快照 | `SaveModule.CreateActor` |
| `NeedStateSnapshot` | DTO | 单需求状态快照 | `NeedSystem` |
| `ThoughtStateSnapshot` | DTO | 思想 / 心情快照 | `NeedSystem` |
| `HealthConditionStateSnapshot` | DTO | 健康状况快照 | `HealthSystem` |
| `ItemSnapshot` | DTO | 物品全量快照 | `SaveModule` |
| `ItemSurgerySnapshot` | DTO | 物品上的手术元数据快照 | `SurgeryModule`、保存 |
| `ItemCorpseSnapshot` | DTO | 尸体物品元数据快照 | `SurgeryModule`、保存 |
| `ShopSlotSnapshot` | DTO | 商店槽位快照 | `TradeModule` |
| `LimbSnapshot` | DTO | 肢体快照 | `HealthSystem`、战斗 |
| `EquipSlotSnapshot` | DTO | 装备槽位快照 | `InventoryModule` |
| `RaceSnapshot` | DTO | 种族快照 | `PresetDB`、本地化 |
| `ProfessionSnapshot` | DTO | 职业快照 | `PresetDB`、本地化 |
| `BuffSnapshot` | DTO | Buff 快照 | `Combat`、`NeedSystem` |
| `ExperienceSnapshot` | DTO | 经验快照 | 角色成长 |
| `QuestSnapshot` | DTO | 任务快照 | `QuestPanelModule` |
| `QuestObjectiveSnapshot` | DTO | 任务目标快照 | 任务系统 |
| `ChunkSnapshot` | DTO | dirty chunk 快照 | `ChunkManager`、`SaveModule` |
| `CellStackSnapshot` | DTO | 单格堆叠快照 | `ChunkSnapshot` |
| `CellEntitySnapshot` | DTO | 单个 cell entity 快照 | 地图/地面物品/设施 |
| `NestSnapshot` | DTO | 巢穴数据快照 | `NestModule` |

### `WorldStore.cs`

Storage layout notes:
- Runtime persistence now uses three explicit layers: `world_manifests/<worldId>.json`, `world_saves/<worldId>/<characterId>.json`, and `world_assets/<worldId>/...`.
- Legacy `worlds/<worldId>/world.json + characters/` is migration input only and is not part of normal runtime reads or writes.
- `MigrateLegacyWorldLayout()` runs per world with copy, verify, and delete semantics, and reports failures without exposing half-migrated worlds.
- `DeleteWorldSaveData()` removes character/run saves but preserves the manifest shell so the world can stay in the list and accept new characters later.
- `DeleteWorldAssets()` removes only attached asset data and does not affect the manifest or character saves.

| 类型 | 归类 | 语义 / 关键字段组 | 主要消费方 |
| --- | --- | --- | --- |
| `WorldSettings` | DTO | 世界 seed、生成器、气候、季节、文明度、各项百分比参数 | `WorldSettingsDialogModule`、`WorldStore`、`GameSessionModule` |
| `WorldManifest` | DTO | 世界元数据：ID、显示名、设置、创建时间、最近游玩 | `WorldStore` |
| `WorldEntryInfo` | DTO | 世界浏览器展示项，含角色列表与摘要 | `GameSessionModule.ListWorlds`、`WorldManagerModule` |
| `WorldCharacterEntryInfo` | DTO | 世界角色展示项，含保存路径、回合、楼层、最近游玩标记 | `WorldManagerModule` |
| `WorldLaunchTab` | enum | 世界浏览器页签：世界 / 场景 / 旧存档 | `WorldManagerModule`、`Main` |
| `ContinueTargetKind` | enum | 继续游戏目标类别 | `GameSessionModule.ResolveContinueTarget` |
| `ContinueTarget` | DTO | 继续游戏解析结果 | 菜单 Continue 按钮文本与行为 |
| `WorldStore` | service | 世界/角色目录持久化门面 | `GameSessionModule` |

### `WeatherSystem.cs`

| 类型 | 归类 | 语义 / 关键字段组 | 主要消费方 |
| --- | --- | --- | --- |
| `WeatherType` | enum | 天气类别 | `WeatherRules`、渲染 |
| `WeatherIntensity` | enum | 天气强度 | `WeatherRules` |
| `WeatherState` | 运行时 DTO | 世界当前天气状态 | `GameState`、渲染、速度修正 |
| `WeatherDebugOverride` | DTO | 调试强制天气 | `DebugModule` |
| `WeatherLocalSnapshot` | DTO | 局部天气观测值 | `WeatherRules`、渲染 |
| `WeatherSample` | DTO | 采样结果 | `WeatherFieldSampler` |
| `WeatherAccumulation` | DTO | 地表积累状态 | `WeatherAccumulationSimulator` |
| `WeatherSurfaceState` | DTO | 单地表表面状态 | `TileMapRenderModule`、环境伤害 |
| `WeatherIds` | const/static ids | 天气 ID 常量 | 配置与调试 |
| `WeatherRules` | static class | 天气规则与局部采样决策 | `TimelineTurnManager`、渲染 |
| `WeatherSurface` | static class / DTO | 地表天气帮助类型 | 天气系统 |
| `WeatherFieldSampler` | service/static | 场采样器 | `WeatherRules` |
| `WeatherAccumulationSimulator` | static class | 回合推进时更新积雪/积雨/闪电等 | `TurnModule.AdvanceWorld` |
| `WeatherAccumulationDelta` | DTO | 单次天气累积增量 | `WeatherAccumulationSimulator` |

## 其他基础模型簇

| 模型簇 | 代表类型 | 语义 | 主要消费方 |
| --- | --- | --- | --- |
| 角色与战斗基础 | `Actor`、`AwarenessState`、`CapacityDef`、`RacePreset`、`ProfessionPreset` | 角色身体、属性、职业、种族与标签基底 | `Combat`、`AI`、UI |
| 物品与内容定义 | `Item`、`ShopSlot`、`ItemCategoryDef`、`MaterialDef`、`FixtureDef`、`TerrainDef` | 世界实体、装备、材料、地形与设施模板 | `InventoryModule`、`TradeModule`、`TileMapRenderModule` |
| 对话与交互定义 | `DialogContext`、`DialogEntry`、`DialogOption`、`DialogEffect`、`InteractionDef`、`InteractionDefs` | 非战斗交互与对话规则数据 | `DialogRuleEngine`、`ActionModule`、UI |
| 标签与成长 | `Limb`、`EquipSlot`、`Buff`、`Experience`、`NeedStateSnapshot` 等 | 身体部位、装备层、Buff、经验与派生标签 | `HealthSystem`、`NeedSystem`、`CombatModule` |
| 任务与目标 | `Quest`、`QuestObjective`、`QuestStatus` | 任务状态与目标 | `QuestPanelModule`、存档 |
| 世界坐标与几何 | `WorldCoord`、`ChunkCoord`、`CoordUtil`、`ZoneCell`、`Room` | 世界坐标、chunk 坐标与生成几何 | `ChunkManager`、生成器、编辑器 |

## 附录: 穷举索引

| 源码文件 | public 类型 |
| --- | --- |
| `Core/Config/AppSettingsStore.cs` | `AppSettingsStore`, `ContinueState` |
| `Core/Config/GameConfig.cs` | `GameConfig`, `PlayerVisionConfig`, `AIVisionConfig`, `WorldRuntimeConfig`, `WeatherConfig`, `WeatherProfileConfig`, `FireConfig`, `DebugConfig`, `GenerationConfigSet`, `BspGenerationConfig`, `CellularGenerationConfig`, `DrunkardWalkGenerationConfig`, `PerlinGenerationConfig`, `RoomCorridorGenerationConfig` |
| `Core/Config/GameDataLocator.cs` | `GameDataLocator` |
| `Core/Config/GameLocalizer.cs` | `GameLocalizer` |
| `Core/Config/LocalizationService.cs` | `LocalizationService` |
| `Core/Data/Actor.cs` | `AwarenessState`, `Actor` |
| `Core/Data/ActorModule.cs` | `ActorModule` |
| `Core/Data/ActorTemplate.cs` | `ActorTemplates` |
| `Core/Data/CapacityDef.cs` | `CapacityDef` |
| `Core/Data/CellEntity.cs` | `CellEntityType`, `CellEntity` |
| `Core/Data/Const.cs` | `Factions`, `Entities`, `Terrains`, `Caps`, `SkillTags`, `BodyParts`, `EquipLayer`, `DamageTypes`, `ItemCategories`, `ItemTags` |
| `Core/Data/DialogContext.cs` | `DialogContext` |
| `Core/Data/DialogTypes.cs` | `DialogEntry`, `DialogCondition`, `DialogOption`, `DialogEffect`, `DialogSet` |
| `Core/Data/FacilityModels.cs` | `FacilityStage`, `FacilityRotation`, `FacilityUseSlotPurpose`, `WorkTicketType`, `OutputTargetStrategy`, `EconomicDomainKind`, `StockpilePriority`, `DomainIds`, `WorkBrainIds`, `FacilityIds`, `RoomRoleIds`, `FacilityInteractionModes`, `RecipeTagIds`, `FacilityTags`, `ItemAmount`, `ZoneCell`, `RoomModifierSet`, `FacilityFootprintCell`, `FacilityUseSlot`, `FacilityDef`, `RecipeDef`, `BillDef`, `EconomicDomain`, `StockpileZone`, `WorkTicket`, `JobBoardState`, `FacilityInstance`, `RoomRoleDef`, `RoomSnapshot`, `FacilityRegistry`, `RecipeRegistry`, `RoomRoleRegistry` |
| `Core/Data/FixtureDef.cs` | `FixtureDef`, `FixtureRegistry` |
| `Core/Data/GameEvent.cs` | `GameEvent` |
| `Core/Data/GameState.cs` | `GameState`, `NestData` |
| `Core/Data/IdentificationModule.cs` | `IdentificationModule` |
| `Core/Data/InteractionDef.cs` | `InteractionDef` |
| `Core/Data/InteractionDefs.cs` | `InteractionDefs` |
| `Core/Data/InteractionModule.cs` | `InteractionModule` |
| `Core/Data/InventoryModule.cs` | `InventoryResult`, `InventoryModule` |
| `Core/Data/Item.cs` | `Item`, `ShopSlot`, `ItemCategoryDef` |
| `Core/Data/ItemConditionFormatter.cs` | `ItemConditionFormatter` |
| `Core/Data/ItemContentDefinitions.cs` | `ItemSubcategoryDef`, `ItemSubcategoryRegistry`, `AmmoProfileDef`, `AmmoProfileRegistry`, `ButcherYieldDef`, `CorpseProfileDef`, `CorpseProfileRegistry`, `SurgeryOperationDef`, `SurgeryOperationModes`, `SurgeryOperationRegistry`, `ItemSurgeryMetadata`, `ItemCorpseMetadata` |
| `Core/Data/MaterialDef.cs` | `MaterialDef`, `MaterialRegistry` |
| `Core/Data/PlayerAppearanceCatalog.cs` | `PlayerAppearanceCatalogEntry`, `PlayerAppearanceCatalog` |
| `Core/Data/PlayerCreationOptions.cs` | `PlayerCreationOptions` |
| `Core/Data/PresetDB.cs` | `RacePreset`, `LimbPreset`, `ProfessionPreset`, `ItemPreset`, `ShopSlotPreset`, `ActorPreset`, `ItemCategoryPreset`, `InteractionPreset`, `PresetDB` |
| `Core/Data/Quest.cs` | `QuestStatus`, `Quest`, `QuestObjective` |
| `Core/Data/SkillQuery.cs` | `SkillQuery` |
| `Core/Data/TagSystem.cs` | `ITagSource`, `Limb`, `EquipSlot`, `Race`, `Profession`, `Buff`, `Experience`, `ITagSourcePolymorphic` |
| `Core/Map/MapGenModule.cs` | `MapGenModule` |
| `Core/Map/MapModule.cs` | `MapModule` |
| `Core/Map/PresetScenarioCatalog.cs` | `PresetScenarioDef`, `PresetScenarioCatalog` |
| `Core/Map/SaveModule.cs` | `SaveModule` |
| `Core/Map/SaveSnapshot.cs` | `SaveLoadStatus`, `SaveFile`, `SaveHeader`, `SaveHeaderContext`, `SavePayload`, `WeatherStateSnapshot`, `TimelineSnapshot`, `TimelineActorSnapshot`, `ActorSnapshot`, `NeedStateSnapshot`, `ThoughtStateSnapshot`, `HealthConditionStateSnapshot`, `ItemSnapshot`, `ItemSurgerySnapshot`, `ItemCorpseSnapshot`, `ShopSlotSnapshot`, `LimbSnapshot`, `EquipSlotSnapshot`, `RaceSnapshot`, `ProfessionSnapshot`, `BuffSnapshot`, `ExperienceSnapshot`, `QuestSnapshot`, `QuestObjectiveSnapshot`, `ChunkSnapshot`, `CellStackSnapshot`, `CellEntitySnapshot`, `NestSnapshot` |
| `Core/Map/WorldStore.cs` | `WorldSettings`, `WorldManifest`, `WorldEntryInfo`, `WorldCharacterEntryInfo`, `WorldLaunchTab`, `ContinueTargetKind`, `ContinueTarget`, `WorldStore` |
| `Core/Weather/WeatherSystem.cs` | `WeatherType`, `WeatherIntensity`, `WeatherState`, `WeatherDebugOverride`, `WeatherLocalSnapshot`, `WeatherSample`, `WeatherAccumulation`, `WeatherSurfaceState`, `WeatherIds`, `WeatherRules`, `WeatherSurface`, `WeatherFieldSampler`, `WeatherAccumulationSimulator`, `WeatherAccumulationDelta` |
| `Core/World/ChunkData.cs` | `ChunkData` |
| `Core/World/ChunkManager.cs` | `ChunkManager` |
| `Core/World/DigModule.cs` | `DigModule` |
| `Core/World/Generators/BlankFloorGenerator.cs` | `BlankFloorGenerator` |
| `Core/World/Generators/BSPGenerator.cs` | `BSPGenerator` |
| `Core/World/Generators/CellularAutomataGenerator.cs` | `CellularAutomataGenerator` |
| `Core/World/Generators/DrunkardWalkGenerator.cs` | `DrunkardWalkGenerator` |
| `Core/World/Generators/PerlinGenerator.cs` | `PerlinGenerator` |
| `Core/World/Generators/RoomCorridorGenerator.cs` | `RoomCorridorGenerator`, `Room` |
| `Core/World/Generators/SurfaceGenerator.cs` | `SurfaceGenerator` |
| `Core/World/IChunkSimulator.cs` | `IChunkSimulator`, `NullSimulator` |
| `Core/World/IMapGenerator.cs` | `IMapGenerator` |
| `Core/World/IViewMode.cs` | `IViewMode` |
| `Core/World/Noise/PerlinNoise.cs` | `PerlinNoise` |
| `Core/World/Pathfinding.cs` | `Pathfinding` |
| `Core/World/ShadowcastFOV.cs` | `ShadowcastFOV` |
| `Core/World/TerrainDef.cs` | `TerrainDef`, `TerrainRegistry` |
| `Core/World/VisibilityUtil.cs` | `VisibilityUtil` |
| `Core/World/VisionRangeScaler.cs` | `DirectionalVisionRange`, `VisionRangeScaler` |
| `Core/World/WorldCoord.cs` | `WorldCoord`, `ChunkCoord`, `CoordUtil` |
| `Core/World/WorldMap.cs` | `WorldMap` |
