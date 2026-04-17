# WIP 文件承包

本文件是多 AI / 进程协作时的"正在动的文件"登记表。
开工前扫一遍，别碰不属于你这块的；动你这块之外的文件前，在这里登记。

## 格式

```
- <进程名> | <文件/目录 glob> | <预计完成时间 / 当前状态>
```

## 当前登记

<!-- 按开工时间倒序。完成后删除本条。 -->

- codex-player-glide-logfile | Artifacts/wip.md, App/Main.ActorMotion.cs, App/Main.Timeline.cs, Module/Render/ActorMotionPresentation.cs, Module/Render/ActorMotionTracker.cs, Module/Render/IsometricVoxelRenderer.cs, Tests/MiniRPG.Tests/MainTimelineMotionTests.cs, Tests/MiniRPG.Tests/IsometricRenderTests.cs | 正在把玩家连续移动改成 glide，并把诊断日志落到 Artifacts/turn
- codex-timeline-motion-log | Artifacts/wip.md, App/Main.ActorMotion.cs, App/Main.Timeline.cs, Tests/MiniRPG.Tests/MainTimelineMotionTests.cs | 正在补过回合/动作 lane 诊断日志，帮助观察“为什么不丝滑”
- codex-wip-triage | Artifacts/wip.md, Artifacts/wip_plan.md | 正在整理未登记改动并补分流计划
- needs-owner | Module/Panel/TurnPanelModule.cs, Scene/TurnPanel.tscn | TurnPanel 单行布局 / 队列名片压缩改动已存在；建议独立冒烟后单独提交
- needs-owner | Assets/Audio/**, Tools/extract_samples.py, Tools/validate_audio_manifest.py | 音频资源管线重组已通过 python Tools/validate_audio_manifest.py；建议独立成一条 lane 收口
- codex-baseline | Artifacts/wip.md, Tests/MiniRPG.Tests/SettingsFlowCoordinatorTests.cs, Tests/MiniRPG.Tests/SettingsFlowModalInputAdapterTests.cs, Tests/MiniRPG.Tests/SettingsPanelModuleTests.cs | Tests 基线已恢复（dotnet test 1030 通过）；这些测试文件在提交前暂勿并改
- needs-owner | Artifacts/render_mapping_audit_report.md | git status 检出未登记改动；认领前勿覆盖
- qtwx-mcp-1 | A 赛道（Shared/Core）| Sprint 1 本通道任务已完成：S1-3 / S1-4 落地；S1-2 降级为 starter kit 扩充。S1-1 阻塞等 Tests 编译恢复。

## 已完成（最近）

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
