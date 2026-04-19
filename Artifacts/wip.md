# WIP 文件承包

本文件是多 AI / 进程协作时的"正在动的文件"登记表。
开工前扫一遍，别碰不属于你这块的；动你这块之外的文件前，在这里登记。

## 格式

```
- <进程名> | <文件/目录 glob> | <预计完成时间 / 当前状态>
```

## 当前登记

<!-- 按开工时间倒序。完成后删除本条。 -->

- claude-mp-fix | MiniRPG.Shared/Core/Multiplayer/{ProtocolSerializer,HostedLobbyService,DedicatedGameServerHost}.cs + 新建 ServerSideConsequenceDispatcher.cs + MiniRPG.Shared/Module/Network/ENetGameServer.cs + 4 个新测试 | 多人四桩 P0-5/6/7/8（Climb 反序列化/脏包防崩/房间二人加入/服务端社交模拟）进行中 2026-04-19
- cursor-opus-focus | App/Main.cs(313-314) + App/Main.Timeline.cs(HandlePlayerDeath/ApplyTimelineStep) + App/RuntimeUi/{GameplayCommandCoordinator,GameEventPresentationRouter,MultiplayerRuntimeCoordinator}.cs + Module/LogModule.cs + 新增 MiniRPG.Shared/Core/Data/ActiveActorAccess.cs + PartyModule.TryGetActiveActor + Data/I18n/{zh_CN,en}.json + Tests/ActiveActorDeathHandlerIntegrationTests.cs + Docs/多人联机契约.md | 死亡-焦点链路打通（P0-9/P0-10/P1-5）进行中 2026-04-19

## 已完成（最近）

- qtwx-mcp-8 | 渲染/UI 资源泄漏修复 9 步：①IsometricVoxelRenderer entityMarkerCache + facilityFrameBoundsCache Init() 释放（P0-12）；②TerrainAtlas Build 末尾 _imageLoadCache.Clear()（P0-12）；③RichTooltipLayer signal callback 字典 + Detach 真解绑 + timer generation counter + minWidth 公式（P1-18 + P2-13）；④TurnPanelModule chip style 二实例懒缓存（P1-19）；⑤WorldManagerModule 行样式 (Color,Color,int) Dict 缓存（P1-19）；⑥FogOfWarTracker `_fullVisible/_directionalVisible.Clear()` 复用容量（P2-14）；⑦WeatherFxController 删 TryRenderWeatherShaderFx/ConfigureWeatherFxSprite/EnsureWeatherFxMaterial/ResolveWeatherFxShader/ResolveWeatherQuadTexture + 字段 + 删 weather_realtime_fx.gdshader（P1-20，路径 B）；⑧water shader 残留：删 painterly_water_block.gdshader.bak/WaterPainterly*.bak/seascape_top_surface.gdshader/WaterSeascapePreview.tscn/Assets/Shaders/water 整目录（P2-10）；⑨ResAccess null 资源不写缓存且不触发回调（P2-13）。dotnet test 1193/1193 通过 | 完成 2026-04-19
- cursor-opus | P0-1 SaveModule 漏存 5 子系统 + P1-13/14/15 静态污染：新增 Party/Social/Storyteller/Zones/Crops 5 个 Snapshot 类型并对称写读；MapGenModule.InitializeWorld 入口清 DirtyChunkCache + NestSpawnCounter；GameState.Actors 改 StringComparer.Ordinal + Reset 同步重建；SaveModule.CopyDictionary 透传 source comparer；新增 SavePayloadCoverageTests 反射守护 GameState→SavePayload 字段映射。dotnet test 1193/1193 通过 | 完成 2026-04-19
- cursor-opus | 紧急 build fix：Module/Panel/RichTooltipLayer.cs Math.Ceiling 二义性（一行 cast）+ Tests/MultiplayerProtocolHardeningTests.cs Random 命名参数 `seed`→`Seed` + Tests/GameEventPresentationRouterTests.cs Harness 缺 InfoToasts/WarningToasts/FlushMapCalls 字段定义 | 完成 2026-04-19
- qtwx-mcp-6 | 涌现社交管线接通：①P0-2 已被 cursor-opus-focus@8d7a34eb 提前修；②InitializeCoreServices 注入 AIDispatcher.Relationships/ActorMemories/Rumors（P0-3）；③InputResolver +7 社交 resolver + ActorMemoryModule.SumStrength 查询（P0-4）；④flee_combat 加 RelationshipFear<target> consideration；⑤RumorBus + ActorMemoryModule 响应 actor_killed → CasualtyReported rumor + 旁观者 CasualtyWitnessed memory（gift_given/theft 事件不存在跳过）；⑥InputResolverSocialTests 22 用例。dotnet test 1190/1190 通过。涌现世界路线图.md 第 1-4 步接线度 0%→约 35% | 完成 2026-04-19
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
