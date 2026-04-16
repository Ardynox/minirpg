# Utility AI

这个目录负责数据驱动的 Utility AI 评分与执行。
它不直接负责整轮 AI 调度；整轮入口在 `MiniRPG.Shared/Core/AI/AIDispatcher.cs`。

## 从哪开始读

- 先看 `MiniRPG.Shared/Core/AI/AIDispatcher.cs`：这里负责 Tick、感知批处理、缓存复用和执行入口
- Action 定义加载：`UtilityActionRegistry.cs`
- 评分引擎：`UtilityBrain.cs`
- 输入解析：`InputResolver.cs`
- 目标选择：`TargetResolver.cs`
- 决策缓存：`UtilityCache.cs`
- 执行器注册：`Executors/ExecutorRegistry.cs`
- 性格生成与默认值：`PersonalityModule.cs`

## 常见改动去哪里

- 调整已有行为分数、曲线、tag、target type：`Data/Config/utility_actions.json`
- 新增输入源：`InputResolver.cs`
- 改评分逻辑或曲线解释：`UtilityBrain.cs`、`ResponseCurve.cs`
- 改目标选择策略：`TargetResolver.cs`
- 新增执行器或改执行落地：`Executors/ExecutorRegistry.cs` 与对应 `Executors/*.cs`
- 改性格轴、种族/职业偏移：`PersonalityModule.cs`、`Data/Config/personality_defaults.json`

## 不要在这里解决什么

- 不在 `AIDispatcher` 里硬编码行为优先级，优先走数据和现有评分入口
- 不把感知层 `AwarenessModule` 当成决策引擎
- 不把 UI、渲染或多人同步逻辑塞进 utility executor

## 什么时候更新这份 README

- 只有当 Utility AI 的稳定入口、评分分层、配置落点或常见改动入口发生变化时才更新
