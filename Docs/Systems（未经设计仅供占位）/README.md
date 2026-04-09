# 新增系统文档索引

> 本目录记录 2026-04-08 批量新增的 6 个核心系统的架构设计、接口说明和集成方式。

| 系统 | 文档 | 代码目录 | 测试文件 |
|------|------|----------|----------|
| 工作调度 | [01_工作系统.md](./01_工作系统.md) | `Core/Job/` | `JobSystemTests.cs` |
| 队伍/多角色 | [02_队伍系统.md](./02_队伍系统.md) | `Core/Data/PartyModule.cs`, `Core/AI/FollowerBrain.cs` | `PartyModuleTests.cs` |
| 事件/Storyteller | [03_事件系统.md](./03_事件系统.md) | `Core/Event/` | `StorytellerTests.cs` |
| 社交/关系 | [04_社交系统.md](./04_社交系统.md) | `Core/Social/` | `SocialModuleTests.cs` |
| 区域管理 | [05_区域系统.md](./05_区域系统.md) | `Core/Zone/` | — |
| 农业 | [06_农业系统.md](./06_农业系统.md) | `Core/Farm/` | `FarmModuleTests.cs` |

## 设计原则

所有新系统遵循项目既有约定：

- **纯函数 + GameState**：逻辑模块是 `static class`，无自身状态，所有运行时数据存在 `GameState` 上
- **数据驱动**：定义（Def）从 JSON 加载，运行时实例（Instance/State）可序列化
- **GameEvent 产出**：Core 层只产出 `GameEvent`，UI/渲染层消费
- **行为链插入**：AI 行为按 Health → Fire → Temperature → Needs → **Job** → Brain 的优先级链执行
- **向后兼容**：Party 为空时回退到 `state.PlayerId` 单角色模式，旧存档无需迁移
