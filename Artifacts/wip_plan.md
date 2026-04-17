# 当前 WIP 分流计划

目标：先把“谁在改什么”说清，再按低冲突顺序收口，避免并行协作时互相覆盖。

## 当前分流

- `render-damping`
  - 文件：`Module/Render/ActorMotionTracker.cs`、`Tests/MiniRPG.Tests/IsometricRenderTests.cs`
  - 状态：已有 owner；不要并改
  - 备注：最新提交信息显示这条线在做移动补间阻尼
- `tests-baseline`
  - 文件：`Tests/MiniRPG.Tests/SettingsFlowCoordinatorTests.cs`、`Tests/MiniRPG.Tests/SettingsFlowModalInputAdapterTests.cs`、`Tests/MiniRPG.Tests/SettingsPanelModuleTests.cs`
  - 状态：已有 owner；不要并改
  - 备注：`dotnet test 1030` 已恢复，这几份测试先等 owner 收口
- `turn-panel-layout`
  - 文件：`Module/Panel/TurnPanelModule.cs`、`Scene/TurnPanel.tscn`
  - 状态：未登记，需认领
  - 建议验证：运行 TurnPanel 相关场景，检查自动推进时高度是否稳定、队列 chip 是否仍可读
- `audio-pipeline`
  - 文件：`Assets/Audio/**`、`Tools/extract_samples.py`、`Tools/validate_audio_manifest.py`
  - 状态：未登记，但边界清楚，适合独立收口
  - 已验证：`python Tools/validate_audio_manifest.py` 通过
  - 风险：大量新增 `.import` 和资源目录，提交时要和 UI / 渲染改动分开
- `render-mapping-audit-report`
  - 文件：`Artifacts/render_mapping_audit_report.md`
  - 状态：未登记，认领前勿覆盖

## 建议处理顺序

1. 先等 `render-damping` 和 `tests-baseline` owner 各自收口，不要再向这些文件叠加 unrelated 修改。
2. `audio-pipeline` 单独成一条 lane 处理，因为已经有清晰验证命令，最容易独立提交。
3. `turn-panel-layout` 由单独 owner 冒烟确认后再交，避免和 `Module/Panel/*` 其他改动撞车。
4. `render-mapping-audit-report` 最后认领；它不阻塞构建，也不该和功能改动混提。

## 本次判断

- 先收口哪条最划算：`audio-pipeline`
- 原因：不和当前已登记的渲染 / Settings 测试 WIP 交叉，且已有本地校验通过
