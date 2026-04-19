# WIP 文件承包

本文件是多 AI / 进程协作时的"正在动的文件"登记表。
开工前扫一遍，别碰不属于你这块的；动你这块之外的文件前，在这里登记。

## 格式

```
- <进程名> | <文件/目录 glob> | <预计完成时间 / 当前状态>
```

## 当前登记

<!-- 按开工时间倒序。完成后删除本条。 -->

- qtwx-mcp-6 | App/Main.Startup.cs(InitializeCoreServices) + MiniRPG.Shared/Core/AI/Utility/InputResolver.cs + MiniRPG.Shared/Core/Social/{ActorMemoryModule,RumorBus}.cs + Data/Config/utility_actions.json(flee_combat) + Tests/MiniRPG.Tests/InputResolverSocialTests.cs + Docs/涌现世界路线图.md | 涌现社交管线接通 P0-3/P0-4/P1-23（P0-2 已被 cursor-opus-focus@8d7a34eb 提前修了，下游 5 步继续） 2026-04-19
- qtwx-mcp-8 | Module/Render/IsometricVoxelRenderer*.cs + TerrainAtlas.cs + WeatherFxController.cs + ResAccess.cs + Module/Panel/RichTooltipLayer.cs + TurnPanelModule.cs + Module/WorldManagerModule.cs + MiniRPG.Shared/Module/Render/FogOfWarTracker.cs + Assets/Shaders/water/* + Scene/WaterPainterly*.bak | 渲染/UI 资源泄漏修复（P0-12/P1-18/P1-19/P1-20/P2-13/P2-14） 2026-04-19
- claude-mp-fix | MiniRPG.Shared/Core/Multiplayer/{ProtocolSerializer,HostedLobbyService,DedicatedGameServerHost}.cs + 新建 ServerSideConsequenceDispatcher.cs + MiniRPG.Shared/Module/Network/ENetGameServer.cs + 4 个新测试 | 多人四桩 P0-5/6/7/8（Climb 反序列化/脏包防崩/房间二人加入/服务端社交模拟）进行中 2026-04-19
- cursor-opus-focus | App/Main.cs(313-314) + App/Main.Timeline.cs(HandlePlayerDeath/ApplyTimelineStep) + App/RuntimeUi/{GameplayCommandCoordinator,GameEventPresentationRouter,MultiplayerRuntimeCoordinator}.cs + Module/LogModule.cs + 新增 MiniRPG.Shared/Core/Data/ActiveActorAccess.cs + PartyModule.TryGetActiveActor + Data/I18n/{zh_CN,en}.json + Tests/ActiveActorDeathHandlerIntegrationTests.cs + Docs/多人联机契约.md | 死亡-焦点链路打通（P0-9/P0-10/P1-5）进行中 2026-04-19
- cursor-opus | MiniRPG.Shared/Core/Map/SaveModule.cs + SaveSnapshot.cs + Core/Data/GameState.cs + NestModule.cs + MapGenModule.cs + 新建 5 个 Snapshot 类型 + Tests/SaveModuleTests.cs + Tests/SavePayloadCoverageTests.cs | 存档完整性（P0-1 5 子系统）+ 静态污染清理（P1-13/14/15）
- cursor-opus | Module/Panel/RichTooltipLayer.cs：line 167 一行 cast 修编译阻塞（Math.Ceiling 二义性，影响 Tests 运行）| 紧急 build fix（不动语义，不主动认领 P1-18）

## 已完成（最近）

- qtwx-mcp-5 | 草地 Wave 1 路 D：新文件 `Module/Render/Surface/GrassOverlayPass.cs`（接口 3 草稿：FNV-1a 衍生 hash 选 6 变体、cover==0 零分配 return、`object ctx` 占位 + Wave 2.2 接入清单写在 helper 注释里），主项目编译 0 警告 0 错误，分布手测 100 格 → `[17,13,18,18,15,19]`（均匀） | 完成 2026-04-19
- cursor-opus | AutoNav 多人 in-flight kind 准确化：DoMove 返回 bool 反映"实际走预测还是 TimelineAction"，AutoNavigationCoordinator 据此设 `_inFlightKind`（修掉 SFM+多人时 PredictedMove 错位的语义瑕疵）+ 6 个新单测，1092/1092 通过 | 完成 2026-04-19
- qtwx-mcp-3 | 草地 Wave 1 路 A：`ChunkData.GrassCover` 字段 + `SaveSnapshot.GrassCover` 可空字段 + `SaveModule` 写出/读入（照 SnowDepth 模式）+ `Tests/MiniRPG.Tests/ChunkGrassCoverTests.cs` 4 用例（默认值/全 0/round-trip/老快照兼容），SaveModule + GrassCover 共 20/20 通过 | 完成 2026-04-19
- qtwx-mcp-4 | 草地 Wave 1 路 B：`MiniRPG.Shared/Core/World/Surface/GrassCoverSampler.cs`（确定性 Perlin 2 频，dirt/grass_block + 暴露 + 非水下才出草）+ `Tests/MiniRPG.Tests/GrassCoverSamplerTests.cs`（10 case 全过） | 完成 2026-04-19
- qtwx-mcp-2 | Wave 0 协调：`Artifacts/grass-task-handoff.md`（草地任务 4 接口冻结 + 4 路文件分配 + Wave 1/2/3 计划） | 完成 2026-04-19
- qtwx-mcp-1 | TurnModule.AdvanceWorld 发 new_day_started / new_season_started GameEvent + IncidentDef.SeasonWeights + Storyteller 季节加权（cold_snap 冬季 ×3、heat_wave 夏季 ×3）+ 4 TurnModuleTests + 2 IncidentDef JSON 测试，1072/1072 通过 | 完成 2026-04-19
- claude | 全量代码扫描报告 Artifacts/代码扫描_2026-04-19.md（CRITICAL 8 + HIGH 多 + 建议 Sprint A/B/C） | 完成 2026-04-19
- cursor-opus | 自动寻路接入"地表自由移动"（抽 SurfaceFreeMoveTraversal + AutoNavigationPlanner.useSurfaceFreeMove + Coordinator 透传 RuntimeSurfaceFreeMove + 3 个 planner 单测） | 完成 2026-04-19
- codex | WIP 收口：玩家 glide / timeline 诊断、世界生成设置下传、TurnPanel 稳定化、音频清单管线、Settings 测试补齐 | 完成 2026-04-17
- qtwx-mcp-1 | Thirst MVP（Core + Data + HUD）| 完成 2026-04-17
- qtwx-mcp-1 | 死亡-复活数据骨架（PartyModule + ItemCorpseMetadata.SourceActorId + ReviveService + RevivalCostModel）| 完成 2026-04-17
- qtwx-mcp-1 | 需求能力乘数数据驱动化（NeedStageDef.CapacityMultipliers）| 完成 2026-04-17
- qtwx-mcp-1 | DailySummaryBuilder 数据层 | 完成 2026-04-17
- qtwx-mcp-1 | AGENTS.md 决策标尺 + 协作纪律 + wip 机制 | 完成 2026-04-17
- qtwx-mcp-1 | 性能基线文档 Artifacts/性能基线_2026-04-17.md | 完成 2026-04-17
- qtwx-mcp-1 | 开发计划 Artifacts/开发计划_2026-04-17.md | 完成 2026-04-17
- qtwx-mcp-1 | S1-3 ActiveActorDeathHandler（MiniRPG.Shared/Module/Session/）| 完成 2026-04-17
- qtwx-mcp-1 | S1-4 4 个新 IncidentWorker + thoughts.json 扩充 + storyteller_incidents.json workerId 填写 | 完成 2026-04-17
- qtwx-mcp-1 | S1-2 降级：starter kit 扩充（raw_meat + berries 足够做一次 cook_simple_meal）| 完成 2026-04-17
- qtwx-mcp-1 | S1-2 升级：B9 完整 MVP（FacilityConstructionModule.TryPlaceCompleted + MapGenModule.PlaceStarterFacilities 摆 stove + 挂 cook_simple_meal bill）| 完成 2026-04-17
- qtwx-mcp-1 | S1-1 Thirst 测试代码就绪（Tests/MiniRPG.Tests/ThirstSystemTests.cs，5 case，等 Tests 编译恢复自动跑）| 代码完成 2026-04-17
- qtwx-mcp-1 | Sprint 2 预热：Calendar / Season B1（Core/Calendar/CalendarService + DailySummaryBuilder 集成）| 完成 2026-04-17
