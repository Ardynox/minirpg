# WIP 文件承包

本文件是多 AI / 进程协作时的"正在动的文件"登记表。
开工前扫一遍，别碰不属于你这块的；动你这块之外的文件前，在这里登记。

## 格式

```
- <进程名> | <文件/目录 glob> | <预计完成时间 / 当前状态>
```

## 当前登记

<!-- 按开工时间倒序。完成后删除本条。 -->

- qtwx-mcp-5 | 路 D：新文件 `Module/Render/Surface/GrassOverlayPass.cs`（独立草稿，object ctx 占位） | 进行中 2026-04-19
- cursor-opus | AutoNav 多人 in-flight kind 准确化（DoMove 返回值通道：GameplayCommandCoordinator/Main.Multiplayer/AutoNavigationCoordinator/Startup + 测试 Harness） | 进行中 2026-04-19

## 已完成（最近）

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
