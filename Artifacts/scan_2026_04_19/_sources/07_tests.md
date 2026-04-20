Now I have sufficient data to produce the complete audit report.

# Tests 测试覆盖与质量审计报告

## 0. 摘要：当前测试覆盖度地图

| 核心稳定边界（来自 架构现状.md） | 对应测试文件 | 覆盖状态 |
| --- | --- | --- |
| **会话与存档** GameSessionModule | `GameSessionModuleTests.cs` (20 个测试) | 良好：新建世界、读档、继续游戏、删除、多人快照、遗留迁移 |
| **会话与存档** SaveModule 序列化 | `SaveModuleTests.cs` (12 个测试) | 良好：round-trip、版本拒绝、遗留字段回填、设施、火灾、弹药、尸体 |
| **指令执行** ServerActionGateway | `ServerActionGatewayTests.cs` (16 个测试) | 良好：pickup/drop/equip/chest/trade/delegate/combat/modal |
| **回合推进** TimelineTurnManager | `TimelineTurnManagerTests.cs` (12 个测试) | 较好：reset、speed、sync、debug snapshot、skill/climb/cooldown |
| **事件后果** GameEventConsequenceRouter | `GameEventConsequenceRouterTests.cs` (5 个测试) | 中等：仅测 router 调度机制，不含具体 handler 业务逻辑 |
| **社交** RelationshipModule | `RelationshipModuleTests.cs` (12 个测试) | 良好 |
| **社交** ActorMemoryModule | `ActorMemoryModuleTests.cs` (11 个测试) | 良好 |
| **社交** RumorBus | `RumorBusTests.cs` (13 个测试) | 良好 |
| **AI** AIDispatcher | `AIDispatcherDeterminismTests.cs` (2 个测试) | **极弱**：仅测 Classify，未测决策管线 |
| **AI** UtilityBrain / InputResolver | 无独立测试 | **无覆盖** |
| **复活** ReviveService / RevivalCostModel | 无测试 | **无覆盖** |
| **设置面板** SettingsPanelModule | `SettingsPanelModuleTests.cs` (6 个测试) | 中等：测 SelectionModel + TextResolver，不含运行时流 |
| **多人 backend** | `MultiplayerSessionBackendTests.cs` (2 个), `MultiplayerRuntimeTests.cs` (7 个), `MultiplayerProtocolHardeningTests.cs` (5 个), `MultiplayerRegressionSuiteTests.cs`, `ENetTransportSmokeTests.cs` | 较好：覆盖协议、lobby、delegation、reservation，但无真两端模拟 |
| **渲染** IsometricVoxelRenderer | `IsometricRenderTests.cs` | 中等：测映射和 tile 解析，非视觉渲染 |
| **输入** InputModule | `InputModuleTests.cs` (4 个测试) | **弱**：仅测方向键映射 |
| **日历 / 日总结** CalendarService / DailySummaryBuilder | 无测试 | **无覆盖** |
| **死亡处理** ActiveActorDeathHandler | 无测试 | **无覆盖** |
| **战斗** CombatModule | `CombatModuleTests.cs` (14 个测试) | 较好：vital/dead/damage/attack/block |
| **事件呈现** GameEventPresentationRouter | `GameEventPresentationRouterTests.cs` (5 个测试) | 中等：测核心路由路径 |

**总览**: 131 个文件（含 2 个 helper: `TestSupport.cs`, `SkillCastingTestHelper.cs`），约 129 个测试类。核心会话/存档/指令/回合测试覆盖扎实；AI 决策管线、复活流程、日历系统存在关键空白。

---

## 1. 严重缺陷（必修：测试本身有 bug 或覆盖关键路径全无）

### S-1 全局测试并行禁用覆盖全 Assembly — 性能代价高且掩盖竞态
- **文件**: `SaveModuleTests.cs` 第 15 行
- **现象**: `[assembly: CollectionBehavior(DisableTestParallelization = true)]` 作用于全 assembly 的 131 个测试文件
- **证据**: `SaveModuleTests.cs` 中的 `ResetSaveCache()` 操纵静态 chunk 缓存是根因，但该标记阻止了对所有其他无状态测试的并行执行
- **风险**: 1) 测试运行时间膨胀 2) 掩盖了其他文件可能已经存在的共享状态竞态
- **建议**: 将 `DisableTestParallelization` 降级到 `[Collection("SaveModule")]` 仅覆盖需要串行的测试类

### S-2 `SkillCastingTestHelper._initialized` 共享可变静态门控
- **文件**: `SkillCastingTestHelper.cs` 第 13 行
- **现象**: `private static bool _initialized` 用于避免重复调用 `GameConfig.Load()` / `PresetDB.Load()`。`TestSupport.EnsureGameplayDataLoaded()` 有类似逻辑但无显式门控
- **证据**: 两个 helper 各自独立初始化部分重叠的全局单例（`LocalizationService`、`TerrainRegistry`、`UtilityActionRegistry` 等）
- **风险**: 如果任一全局单例不是线程安全的，恢复并行化后会立即 flaky
- **建议**: 统一为一个 `GlobalTestFixture : ICollectionFixture<>` 模式，在 xUnit collection fixture 中初始化一次

### S-3 `ENetTransportSmokeTests` / `LobbyHttpClientTests` — 端口选取依赖 Random.Shared
- **文件**: `ENetTransportSmokeTests.cs` 第 178 行, `LobbyHttpClientTests.cs` 第 106 行
- **现象**: `Random.Shared.Next(0, 2000)` 选端口，无种子固定
- **风险**: CI 环境端口冲突时 flaky；两文件可能选中同一端口
- **建议**: 使用 `TcpListener(IPAddress.Loopback, 0)` 获取系统分配端口

### S-4 ReviveService / RevivalCostModel 全无测试
- **文件**: `MiniRPG.Shared/Core/Revival/ReviveService.cs`, `RevivalCostModel.cs`（git status 显示已修改）
- **现象**: 搜索全测试目录，无任何对 `ReviveService` 或 `RevivalCostModel` 的引用
- **风险**: 复活是核心死亡-恢复闭环，费用计算错误直接破坏游戏经济
- **建议**: P0 补测：复活条件判断、费用计算边界、多次复活累加、资源不足拒绝

### S-5 CalendarService / DailySummaryBuilder 全无测试
- **文件**: `MiniRPG.Shared/Core/Calendar/CalendarService.cs`, `MiniRPG.Shared/Core/Event/DailySummaryBuilder.cs`（git status 显示已修改）
- **现象**: 搜索无匹配
- **风险**: 日历驱动事件触发和日总结影响游戏叙事节奏
- **建议**: P1 补测

### S-6 ActiveActorDeathHandler 全无测试
- **文件**: `MiniRPG.Shared/Module/Session/ActiveActorDeathHandler.cs`（git status 显示已修改）
- **现象**: 搜索无匹配
- **风险**: 玩家死亡后的会话状态切换是关键路径
- **建议**: P1 补测

---

## 2. 主要缺陷

### M-1 AI 决策管线几乎零覆盖
- **文件**: `AIDispatcherDeterminismTests.cs` — 仅 2 个测试，且只测 `Classify` 方法
- **缺失**: `UtilityBrain.Evaluate`、`InputResolver`（~60 个输入源）、`ResponseCurve`、`TargetResolver`、`ExecutorRegistry`（~25 个 executor）均无独立单测
- `TimelineTurnCyclePerfTests.cs` 中 `new UtilityBrain().Evaluate(...)` 是唯一调用，但是性能基准，不含行为断言
- **建议**: 为 `UtilityBrain` 写确定性评分测试（固定状态 + 固定 Random 种子 → 断言选中的 action）

### M-2 MultiplayerSessionBackendTests 依赖反射调用私有方法
- **文件**: `MultiplayerSessionBackendTests.cs` 第 68-70 行
- **现象**: `typeof(MultiplayerSessionBackend).GetMethod("HandleMessageReceived", BindingFlags.Instance | BindingFlags.NonPublic)` — 如果方法改名测试静默跳过（Assert.NotNull 会报错，但错误信息不明确）
- **风险**: 重构时无编译期保护
- **建议**: 引入 `internal` 可见性 + `InternalsVisibleTo` 或改用 public dispatch 入口

### M-3 `RuntimeHelpers.GetUninitializedObject` 大量使用绕过 Godot 构造函数
- **文件**: `DebugPanelControllerTests.cs`, `RuntimeStatusPanelControllerTests.cs`, `GameEventPresentationRouterTests.cs`, `AutoNavigationCoordinatorTests.cs`, `MainStartupTests.cs`
- **现象**: 用 `GetUninitializedObject` 创建 `PanelContainer`、`HBoxContainer`、`Node2D`、`CombatUIModule` 等 Godot 节点
- **风险**: Godot 节点未经 `_Ready` 初始化，如果产品代码依赖 constructor/init 数据，测试不会发现
- **建议**: 提取可测接口或引入 thin wrapper，让核心逻辑不依赖 Godot 节点实例

### M-4 `ResAccess` 全局静态状态操纵
- **文件**: `GameEventPresentationRouterTests.cs` 第 208、213 行
- **现象**: `ResAccess.RegisterAnimatable` / `ResAccess.Reset()` 操纵全局注册表
- **风险**: 若 Dispose 被跳过（测试抛异常且无 `using`），后续测试可能读到脏状态。当前用了 `using var harness`，但 `ResAccess.Reset()` 在 `Dispose` 内，若 `Harness` 构造函数后半段抛异常则泄漏
- **建议**: `ResAccess` 改为可注入的 scope 实例

### M-5 SaveModule 缺少版本迁移兼容测试
- **文件**: `SaveModuleTests.cs`
- **现象**: 测了 v4 拒绝、legacy root 拒绝、当前版本 round-trip，但无 "v5 → v7 迁移" 或 "v6 → v7 字段回填" 的系统性版本兼容矩阵
- **风险**: 每次 bump `CurrentVersion` 都可能默默丢失旧存档兼容
- **建议**: 维护一组固化 JSON fixture（每个历史 version 一个），断言 load + migrate 成功

### M-6 JSON 配置文件无 schema 守护测试
- **文件**: 无
- **缺失**: `utility_actions.json`（~27 个 action 定义）、`personality_defaults.json`、`terrains.json`、`crops.json` 等运行时 JSON 配置文件没有结构性测试
- 搜索 `utility_actions.json` 和 `personality_defaults.json` 在测试中零引用
- **建议**: 添加 schema test：反序列化 → 断言必需字段非空、ID 无重复、curve 参数合法范围

---

## 3. 次要缺陷（一行一条）

- `FlatFloorGenerator` / `StubGenerator` 在 10+ 个测试文件中重复定义（`SaveModuleTests`, `ServerActionGatewayTests`, `MultiplayerRuntimeTests`, `AutoTestRegressionTests`, `TimelineTurnCyclePerfTests`, `SkillCastingTestHelper` 等），应提取到 `TestSupport`
- `CreateActor` 辅助方法有 4+ 种签名分散在不同测试类中，字段组合各异，维护成本高
- `ContinueStateScope` 保存/恢复 `AppSettingsStore` 全局状态，但测试间如果中断可能泄漏
- `TimelineTurnCyclePerfTests` 是纯性能日志测试（无失败阈值断言），混在 unit test 中 → 应标记 `[Trait("Category","Perf")]` 以便 CI 分离
- `InputModuleTests` 仅测方向键映射（4 个测试），未覆盖 action 模式、面板焦点切换、组合键等关键输入路径
- `SettingsPanelModuleTests.ModuleBindingRoots_MatchCurrentSceneHierarchy` 用文件扫描 `File.ReadAllText` 检查 .cs 和 .tscn 的字符串匹配，而非结构化校验 — 易因格式变化误报
- `AutoTestRegressionTests.WeatherAwarePartialSightProbe_ShrinksSurfaceRange_AndMatchesFogTracker` 单测含 199 行、6 个子场景 — 粒度过粗
- `CombatModuleTests.PickPreferredTargetLimb_NoVital_PrefersTorso` 中 `if (torso != null)...else Assert.NotNull(picked)` — 条件断言会掩盖失败路径
- `CombatModuleTests.CalcDamage_BasicAttack_ReturnsPositive` 断言 `Assert.True(damage >= 1)` — 过于宽松，未检验伤害计算公式的合理范围
- 多个测试构造复杂 `Actor`（含 Limbs、Capacities、EquipSlots）行内展开 30+ 行，缺少 `ActorBuilder` 或 fixture 类

---

## 4. 关键模块缺测试清单（按业务风险排序）

| 模块 | 产品代码文件 | 已有测试 | 缺什么 | 建议优先级 |
| --- | --- | --- | --- | --- |
| ReviveService | `Core/Revival/ReviveService.cs` | 无 | 复活条件、费用计算、多次累加、资源不足拒绝 | **P0** |
| RevivalCostModel | `Core/Revival/RevivalCostModel.cs` | 无 | 费用公式边界：0 次、N 次、溢出 | **P0** |
| UtilityBrain | `Core/AI/Utility/UtilityBrain.cs` | 仅性能调用 | 确定性评分、action 选择、空 action 列表 | **P0** |
| InputResolver | `Core/AI/Utility/InputResolver.cs` | 无 | ~60 个输入源的单元映射验证 | **P1** |
| ResponseCurve | `Core/AI/Utility/ResponseCurve.cs` | 无 | 6 种曲线类型的边界值（0、1、负数） | **P1** |
| CalendarService | `Core/Calendar/CalendarService.cs` | 无 | 日历推进、事件触发时机 | **P1** |
| DailySummaryBuilder | `Core/Event/DailySummaryBuilder.cs` | 无 | 日总结内容正确性 | **P1** |
| ActiveActorDeathHandler | `Module/Session/ActiveActorDeathHandler.cs` | 无 | 玩家死亡后会话状态变更 | **P1** |
| TargetResolver | `Core/AI/Utility/TargetResolver.cs` | 无 | Full vs Simplified 目标选择 | **P2** |
| ExecutorRegistry | `Core/AI/Utility/Executors/*` | 无独立测试 | ~25 个 executor 的执行边界 | **P2** |
| PersonalityModule | `Core/AI/Utility/PersonalityModule.cs` | 仅 `EnsureLoaded()` | 10 轴生成、种族/职业偏移、遗传 | **P2** |
| FogOfWarTracker | `Module/Render/FogOfWarTracker.cs` | 间接覆盖 | 缺独立单测：多层、视野缩减、脏标记 | **P2** |
| utility\_actions.json | `Data/Config/utility_actions.json` | 无 schema test | ID 唯一性、必需字段非空、curve 合法 | **P2** |
| personality\_defaults.json | `Data/Config/personality_defaults.json` | 无 schema test | 轴名匹配代码常量 | **P2** |

---

## 5. 测试基础设施建议

### 5.1 新建 fixture / helper

1. **`SharedTestState` collection fixture**: 统一 `TestSupport.EnsureGameplayDataLoaded()` 和 `SkillCastingTestHelper.EnsureGameDataLoaded()` 为一个 `ICollectionFixture<GameDataFixture>`，消除双路初始化和 `static bool _initialized` 模式。
2. **`TestMapGenerator`**: 从 10+ 文件中提取 `FlatFloorGenerator`、`StubGenerator`、`MultiLayerGenerator` 到 `TestSupport.cs` 或独立文件。
3. **`ActorBuilder`**: 提供 fluent API 构造 `Actor`（含 Limbs、Capacities、Inventory），替换当前分散在各测试中的 30+ 行行内构造。
4. **`GameStateBuilder`**: 封装 `GameState` + `WorldMap` + `WeatherState` + 玩家/敌人 actor 的标准组合。
5. **降级全局串行化**: 将 `[assembly: CollectionBehavior(DisableTestParallelization = true)]` 替换为 `[Collection("StatefulSave")]`，仅对 `SaveModuleTests` 等操纵静态缓存的测试串行。

### 5.2 AutoTestModule 与 xUnit 协作

- **当前状态**: `AutoTestCliTests.cs` 仅测试 CLI 入口点；`AutoTestRegressionTests.cs` 和 `AutoTestProfilingTests.cs` 测试运行时场景下的 AI/视野/存档 round-trip，本质是 "用 xUnit 驱动的集成回归测试"。
- **与 `AutoTestModule` (Godot 内置 smoke test) 的关系**: `AutoTestModule` 在 Godot 运行时执行，能测到 scene tree、渲染、输入管线等 xUnit 无法触及的层面；xUnit 测试则能测纯逻辑、并行化、CI 友好。
- **建议**: 文档化边界 — `AutoTestModule` 负责 "端到端冒烟（含 Godot 场景树）"，xUnit 负责 "逻辑回归 + 性能基线"；两者共享 `PresetScenario`（如 `qa_combat_arena`）作为测试入口。

---

## 6. 已确认良好的实践

- **GameSessionModuleTests**: 20 个测试覆盖新建世界、读档、遗留迁移、继续游戏解析、多人快照应用、删除世界/存档/资产、标签格式化等核心路径，且每个测试都有 `try/finally` 清理临时目录
- **SaveModuleTests**: 全量 round-trip 测试覆盖了 actor、inventory（含嵌套 contents）、limbs、buffs、quests、timeline、weather、facilities、经济域、火灾、枪械/弹药/尸体等复杂数据结构
- **ServerActionGatewayTests**: 每个命令类型都测 happy path + 错误分支，断言 status/errorCode/events/logs 四元组
- **RelationshipModule / ActorMemoryModule / RumorBus**: 三个社交子系统测试覆盖全面，包括 CRUD、衰减、阈值清理、事件驱动写入、边界输入（空 ID、自引用）
- **StateWriteBoundaryTests**: 架构约束测试 — 扫描产品代码确保 `SaveModule.LoadGame` 只在 `GameSessionModule` 中调用，是优秀的架构守护实践
- **LocalizationCatalogTests**: 验证 en/zh_CN key 一致性、mojibake 检测、受控文本文件扫描 — 国际化质量守护的典范
- **MultiplayerProtocolHardeningTests**: 协议序列化 round-trip + 遗留兼容降级 + wire error code 映射测试
- **TimelineTurnManagerTests**: `AdvanceAuto_FriendlyMerchantDoesNotStallTimeline` 测试了真实 30 轮循环中商人不卡死时间线，是有效的回归防护
- **`ContinueStateScope`**: 利用 `IDisposable` 模式保存/恢复全局 continue state，避免测试间状态泄漏
- 所有测试类均标记 `sealed`（仅 `PartyModuleTests` 和 `JobSchedulerTests` 例外），符合 xUnit 推荐

Now I have enough information to produce a comprehensive audit. Let me compile the final report.

Key findings:

**Code Defects:**
1. `SkillCastingTestHelper._initialized` - shared mutable static state guarding initialization. `TestSupport.EnsureGameplayDataLoaded()` similarly mutates global singletons. Both are mitigation problems because test parallelization is disabled assembly-wide via `DisableTestParallelization = true`, but this is a heavy sledgehammer.
2. `ENetTransportSmokeTests.PickAvailablePort()` uses `Random.Shared` without fixed seed - flaky port selection


3. `LobbyHttpClientTests` has the same port selection issue with `Random.Shared`, and `MultiplayerSessionBackendTests` uses reflection to invoke private methods, creating fragile coupling that breaks on refactoring.
4. `RuntimeHelpers.GetUninitializedObject` bypasses constructors for Godot types throughout the tests, which is risky if those types add initialization logic, and `GameEventPresentationRouterTests` manipulates static `ResAccess` state without guaranteed cleanup.

**Functional Coverage Gaps:**
`ReviveService` and `RevivalCostModel` have no test coverage at all.
`InputResolver` with 60+ input sources, `ResponseCurve`, `UtilityBrain.Evaluate`, and schema validation for `utility_actions.json` and `personality_defaults.json` are all untested.
`CalendarService`, `DailySummaryBuilder`, `ActiveActorDeathHandler`, and save version migrations are missing coverage, while `AIDispatcherDeterminismTests` only has 2 trivial tests for the `Classify` method, and there's no multiplayer simulation across two endpoints.

Now looking at the structural issues: `FlatFloorGenerator` and `CreateActor` helpers are duplicated across test files, initialization logic overlaps between `TestSupport` and `SkillCastingTestHelper`, perf tests are mixed with unit tests, and `ContinueStateScope` relies on fragile global state restoration that depends on test ordering.

The process itself has problems too—no integration tests crossing module boundaries, assembly-wide parallelization is disabled for performance, `LocalizationCatalogTests` doesn't verify all source code keys have translations, and the boundaries between `AutoTestModule` and xUnit aren't clearly defined.

Now I'm ready to write the final report.