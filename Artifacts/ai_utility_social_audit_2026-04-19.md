# AI Utility + Social 审计报告

## 0. 摘要

对 `MiniRPG.Shared/Core/AI/*`、`MiniRPG.Shared/Core/Social/*`、`Data/Config/utility_actions.json`、`Data/Config/personality_defaults.json` 做了全量代码审计，与 `Docs/涌现世界路线图.md` 做了交叉对账。

**核心发现**：涌现世界第二层的四个骨架模块（GameEventConsequenceRouter / RelationshipModule / ActorMemoryModule / RumorBus）已经存在且通过单元测试，但**存在两处严重接线断裂**导致它们在运行时完全不工作：

1. AI 事件（TickAll 产生的 combat_attack 等）绕过了 ConsequenceRouter，社交模块永远收不到 AI 生成的事件。
2. 社交模块实例未赋值给 AIDispatcher 的静态属性，即使有 InputResolver 也读不到数据。

加上 InputResolver 本身也缺少路线图要求的 7 个社交输入源，**整条"事件 → 社交数据积累 → AI 消费"管线从头到尾是断的**。

第一层 Utility AI 基线（27 个 Action、85+ InputResolver、28 个 Executor、三层缓存、性格 10 轴）本身是扎实的，没有发现会导致崩溃的缺陷。

| 严重度 | 数量 | 典型影响 |
|--------|------|----------|
| 严重缺陷 | 3 | 社交管线完全断裂 |
| 主要缺陷 | 6 | 数据无界增长、缓存失效遗漏、死代码 |
| 次要缺陷 | 6 | 性能细节、未来维护风险 |
| 已确认良好 | 10 | 第一层基线健康 |

---

## 1. 严重缺陷（必修：社交管线断裂 / AI 行为不可能读到社交数据）

### S-1: AI 事件绕过 ConsequenceRouter — 社交模块收不到 AI 生成的事件

- **文件**：`App/Main.Command.cs:210`、`App/Main.Timeline.cs:393-401`、`MiniRPG.Shared/Module/Session/LocalSessionBackend.cs:72-76`
- **现象**：`Dispatch()` 只调用 `_gameEventPresentationRouter.Dispatch` + `DispatchAudioEvents`，**不调用** `_consequenceRouter.DispatchConsequences`。`ConsequenceRouter` 仅在 `LocalSessionBackend.Execute`（即玩家指令路径）中被调用。AI 事件走的是 `TickAll → TimelineStepResult → ApplyTimelineStep → Dispatch`，从未经过 ConsequenceRouter。
- **证据**：

```csharp
// App/Main.Command.cs — Dispatch 只走 presentation
private void Dispatch(List<GameEvent> events)
{
    _gameEventPresentationRouter.Dispatch(events);
    DispatchAudioEvents(events);
}
```

```csharp
// App/Main.Timeline.cs — AI 事件从这里进入 Dispatch
private void ApplyTimelineStep(TimelineStepResult result)
{
    if (result.Events.Count > 0)
        Dispatch(result.Events);        // ← 只走 presentation
    TickSimulationSystems();             // ← decay 正常
}
```

```csharp
// LocalSessionBackend.cs — ConsequenceRouter 只在玩家指令路径触发
var result = ServerActionGateway.Execute(_state, command);
if (result.Events.Count > 0)
{
    _consequenceRouter?.DispatchConsequences(_state, result.Events);  // ← 只有这条路
    _dispatch(result.Events);
}
```

- **风险**：NPC 之间的战斗（占全部 combat_attack 事件的 >90%）**不会**触发 RelationshipModule / ActorMemoryModule / RumorBus。社交数据只能从玩家发起的攻击中积累，而玩家攻击在 Utility AI 评分中的权重极低。整条管线实质无效。
- **建议**：在 `ApplyTimelineStep` 里补一行 `_consequenceRouter?.DispatchConsequences(_state, result.Events)`，或在 `Dispatch` 中统一注入。需要注意插入顺序：ConsequenceRouter 应在 presentation dispatch **之前**执行（先改状态再渲染）。

---

### S-2: AIDispatcher 的社交模块静态属性从未赋值 — AI 永远读到 null

- **文件**：`App/Main.Startup.cs:217-225`、`MiniRPG.Shared/Core/AI/AIDispatcher.cs:23-29`
- **现象**：`Main.InitializeCoreServices` 创建了 `_relationships`、`_actorMemories`、`_rumorBus` 并注册到 ConsequenceRouter，但**从未**将它们赋值给 `AIDispatcher.Relationships`、`AIDispatcher.ActorMemories`、`AIDispatcher.Rumors`。
- **证据**：

```csharp
// Main.Startup.cs:217-225 — 创建了但没赋值给 AIDispatcher
_relationships = new MiniRPG.Core.Social.RelationshipModule();
_actorMemories = new MiniRPG.Core.Social.ActorMemoryModule();
_rumorBus = new MiniRPG.Core.Social.RumorBus();
_consequenceRouter.Register(_relationships);
_consequenceRouter.Register(_actorMemories);
_consequenceRouter.Register(_rumorBus);
// 缺少：
// AIDispatcher.Relationships = _relationships;
// AIDispatcher.ActorMemories = _actorMemories;
// AIDispatcher.Rumors = _rumorBus;
```

```csharp
// AIDispatcher.cs:34-40 — CreateAmbientBehaviorContext 读取这些属性
private static AIBehaviorContext CreateAmbientBehaviorContext(GameState state) =>
    new(state)
    {
        Relationships = Relationships,   // ← 永远 null
        ActorMemories = ActorMemories,   // ← 永远 null
        Rumors = Rumors,                 // ← 永远 null
    };
```

- **风险**：即使补了 S-1 和 S-3，AIBehaviorContext 中的社交引用仍然全部为 null，InputResolver 读到的一定是默认值。
- **建议**：在 `InitializeCoreServices` 中补三行赋值。

---

### S-3: InputResolver 完全缺少路线图要求的 7 个社交输入源 — AI 不可能消费社交数据

- **文件**：`MiniRPG.Shared/Core/AI/Utility/InputResolver.cs`
- **现象**：路线图明确列出的"下一小步"InputResolver 全部不存在：

| 路线图要求 | InputResolver 中是否存在 | 状态 |
|-----------|-------------------------|------|
| `RelationshipTrust<target>` | 否 | ❌ 缺失 |
| `RelationshipFear<target>` | 否 | ❌ 缺失 |
| `MemoryFearTowards<target>` | 否 | ❌ 缺失 |
| `MemoryDebtOwedTo<target>` | 否 | ❌ 缺失 |
| `MemoryBetrayalBy<target>` | 否 | ❌ 缺失 |
| `NearbyRumorSeverity` | 否 | ❌ 缺失 |
| `HasRecentTheftRumor` | 否 | ❌ 缺失 |

- **风险**：即使 S-1 和 S-2 修复后社交模块能正常积累数据，AI 仍然无法读取。
- **建议**：按路线图顺序实现这 7 个 InputResolver。需要 `InputContext` 已有 `BehaviorContext` 引用来访问社交模块实例。

---

## 2. 主要缺陷

### M-1: RelationshipModule.Adjust 没有 clamp — Trust/Fear/Debt 可无限增长

- **文件**：`RelationshipModule.cs:89-92`
- **现象**：`Adjust` 直接做加法，无上下界限制。在一场持续战斗中，每次 combat_attack 给 Fear +0.10，10 次攻击后 Fear=1.0，100 次后 Fear=10.0。虽然 `Tick()` 的 decay（每回合 ×0.98）能防止长期无限增长，但单个战斗内的尖峰无法控制。
- **证据**：

```csharp
_graph[key] = new RelationshipState(
    current.Trust + trustDelta,    // ← 无 clamp
    current.Fear + fearDelta,      // ← 无 clamp
    current.Debt + debtDelta);     // ← 无 clamp
```

- **风险**：未来 InputResolver 如果直接读 Fear 原始值并用 Linear 曲线映射，超大值会被 Clamp01 压到 1.0，导致"打了 5 次和打了 100 次一样恐惧"的体感平顶。
- **建议**：在 Adjust 里加 `Math.Clamp(value, -MaxAxis, MaxAxis)`（如 MaxAxis=2.0），或在 InputResolver 侧用 Logistic 曲线做 soft-clamp。

---

### M-2: UtilityCache.HasInterrupt 缺少社交状态中断条件

- **文件**：`UtilityCache.cs:74-104`
- **现象**：HasInterrupt 只检查 OnFire / AwarenessState / HP / EnemyCount / MentalBreak 过期。不检查：
  - 社交状态变化（新 Fear 增长、新 Rumor 出现）
  - `mood_crisis` 阈值突破（mood 从 16 降到 14，mental break 应触发但被 3-6 回合缓存延迟）
- **风险**：当 S-1/S-2/S-3 修复后，社交数据变化无法触发决策重评估，NPC 反应会有 3-6 回合延迟。
- **建议**：在 UtilityDecisionCache 中加 `MoodAtDecision` 字段，在 HasInterrupt 中检查 mood 是否跨过 crisis 阈值。社交中断可等 InputResolver 上线后再加。

---

### M-3: 三个社交模块只响应 combat_attack — 事件规则半成品

- **文件**：`RelationshipModule.cs:145-166`、`ActorMemoryModule.cs:147-167`、`RumorBus.cs:148-170`
- **现象**：路线图第 2 步说"下一小步：增加事件规则（gift given / debt repaid / betrayed）"，但三个模块的 `OnEvent` 都只匹配 `combat_attack`。没有：
  - `gift_given` → Trust 增加、KindnessReceived 记忆
  - `debt_repaid` → Debt 减少
  - `betrayed` → Trust 大幅下降、BetrayalBy 记忆
  - `combat_death` → CasualtyReported rumor
  - `theft` → TheftWitnessed rumor
- **风险**：即使事件管线修通，社交数据只能反映"谁被谁打了"，无法产生路线图预期的"记仇"、"感恩"、"背叛"体感。
- **建议**：优先补 `combat_death` → CasualtyReported（已有 RumorKind 枚举成员），其次 `gift_given` → Trust/KindnessReceived。

---

### M-4: UtilityCache.HasInterrupt 有副作用 — 查询方法修改游戏状态

- **文件**：`UtilityCache.cs:96-101`
- **现象**：在 `HasInterrupt`（语义上是只读查询）内部直接修改了 `actor.MentalBreak = null` 和 `actor.MoodValue += 10`。
- **证据**：

```csharp
if (actor.MentalBreak != null && !actor.MentalBreak.IsActive(state.Turn))
{
    actor.MentalBreak = null;                               // ← 改状态
    actor.MoodValue = Math.Min(100f, actor.MoodValue + 10f); // ← 改状态
    return true;
}
```

- **风险**：如果 `HasInterrupt` 在同一 tick 被多次调用（当前代码路径中不会，但重构后可能），会产生重复 mood 奖励。违反查询/命令分离原则。
- **建议**：将 MentalBreak 清理逻辑移到 `AIDispatcher` 的执行阶段或 `TickSimulationSystems` 中。

---

### M-5: ResponseCurve 的 Boolean / Step 类型不对输出做 Clamp01

- **文件**：`ResponseCurve.cs:33,38`
- **现象**：Linear / Exponential / Logistic / Inverse 都调用 `Clamp01`，但 Boolean 直接返回 `TrueValue`，Step 直接返回 `High`/`Low`。如果 JSON 配置了 `trueValue: 1.5`，UtilityBrain 的乘积链会被膨胀。
- **当前风险**：所有 JSON 数据使用 [0,1] 值，没有现存问题。但缺少运行时防御。
- **建议**：给 Boolean 和 Step 的返回值也加 Clamp01，或在 `UtilityActionRegistry.Load` 中校验。

---

### M-6: UtilityActionDef.CooldownTurns 已定义但从未被检查

- **文件**：`UtilityActionDef.cs:14`、`UtilityBrain.cs`（无 cooldown 逻辑）
- **现象**：JSON 数据模型有 `cooldownTurns` 字段（全部设为 0），但 `UtilityBrain.ComputeScore` 和 `EvaluateAction` 没有任何代码检查这个值。如果未来数据作者设 `cooldownTurns: 5`，不会有任何效果。
- **建议**：要么在 ComputeScore 入口加 cooldown 检查（需要 per-actor-per-action 的 lastUsedTurn 记录），要么从数据模型中移除这个字段以避免误导。

---

## 3. 次要缺陷

### N-1: ActorMemoryModule FIFO 使用 `List.RemoveAt(0)` — O(n) 但影响小

- **文件**：`ActorMemoryModule.cs:98`
- `MaxMemoriesPerActor=32`，O(32) 的复制开销可忽略。如果未来提升容量，应改用 `Queue<T>` 或环形缓冲。

### N-2: PersonalityModule 在极端种族+职业组合下有 ceiling/floor 效应

- **文件**：`PersonalityModule.cs:70`
- **现象**：`baseValue = default(0.5) + raceOffset(≤0.35) + profOffset(≤0.2) + random(±0.15)` → 理论最大 1.2，被 `Math.Clamp` 压到 1.0。这意味着 orc soldier 的 bravery 几乎总是 1.0，性格多样性被削减。
- **建议**：考虑将 offset 改为乘性调整或在 clamp 前做 sigmoid 软压缩。

### N-3: InputResolver.Resolve 对未注册输入静默返回 0

- **文件**：`InputResolver.cs:33-36`
- 如果 JSON 引用了拼写错误的 inputId（如 `has_enemy_in_vison`），不会有任何警告，该 consideration 恒等于 0 → 对应 action 永不可选。调试极其困难。
- **建议**：加 `#if DEBUG` 日志或在 `UtilityActionRegistry.Load` 后做一次 input 交叉校验。

### N-4: RumorBus 没有 SpawnTurn 字段 — 无法按年龄差异化衰减

- **文件**：`RumorBus.cs:31-39`
- Tick decay 是全局等比衰减（所有 rumor 每回合 ×0.96），无法实现"旧谣言衰减快、新谣言衰减慢"。路线图提到"生成时记 SpawnTurn，按 TimelineTurnManager tick 钩子淘汰"，但 SpawnTurn 字段不存在。
- **建议**：在 Rumor record 中加 `int SpawnTurn`，在 Emit 时从 `state.Turn` 填充。

### N-5: SocialModule 与 RelationshipModule 是平行系统，无整合桥

- **文件**：`SocialModule.cs`（GameState 持久化、int Opinion）vs `RelationshipModule.cs`（内存、float Trust/Fear/Debt）
- InputResolver `relationship_opinion` 读的是 SocialModule，不是 RelationshipModule。两套数据各自独立积累。
- **建议**：短期可接受（两者关注不同维度），但应在路线图明确二者的合并或分工边界。

### N-6: Executor 之间大量重复的 MoveToward 辅助方法

- **文件**：`NeedExecutors.cs`、`CombatExecutors.cs`、`SocialExecutors.cs`、`SurvivalExecutors.cs`、`IdleExecutors.cs`
- 10+ 个 executor 各自定义了几乎相同的 `MoveToward` private 方法（Pathfinding.NextStep + ActionModule.TryMove）。
- **建议**：提取到 `ExecutorHelper.MoveToward` 静态方法。

---

## 4. 涌现世界路线图实际进度对账（重点）

### 第 1 步：GameEventConsequenceRouter ⚠️ 骨架落地，接线有严重断裂

| 项目 | 路线图要求 | 实际状态 |
|------|-----------|---------|
| Router 类 | Shared 层 | ✅ `Core/Events/GameEventConsequenceRouter.cs` |
| Handler 接口 | 存在 | ✅ `IGameEventConsequenceHandler.cs` |
| 注册 IncidentStatistics | handler #0 | ✅ Main.Startup 注册 |
| 注册 RelationshipModule | handler #1 | ✅ Main.Startup 注册 |
| 注册 ActorMemoryModule | handler #2 | ✅ Main.Startup 注册 |
| 注册 RumorBus | handler #3 | ✅ Main.Startup 注册 |
| 玩家指令路径触发 | 走 Gateway | ✅ LocalSessionBackend.Execute 调用 |
| **AI 事件路径触发** | **走 Timeline** | **❌ ApplyTimelineStep 不调用 ConsequenceRouter**（S-1） |
| 容错（handler 异常不 abort） | 要求 | ✅ try-catch + errorSink |
| 单元测试 | 覆盖 | ✅ 5 个测试 |

**缺什么**：在 `ApplyTimelineStep` 或 `Dispatch` 中补调 `_consequenceRouter.DispatchConsequences`。

---

### 第 2 步：RelationshipModule ⚠️ 骨架落地，上游断 + 下游断

| 项目 | 路线图要求 | 实际状态 |
|------|-----------|---------|
| 数据结构 | `(observer, subject) → (Trust, Fear, Debt)` | ✅ |
| 事件规则：hostile combat_attack → Fear | 首版 | ✅ 代码存在 |
| 事件规则触发 | 收到 AI 事件 | ❌ 因 S-1 断裂（只收到玩家指令事件） |
| Reset() | session 切换 | ✅ Main.SessionFlow 调用 |
| Tick() decay | per-turn | ✅ Main.Timeline.TickSimulationSystems 调用 |
| clamp 上下界 | 路线图未要求但应有 | ❌ 无限增长（M-1） |
| AIDispatcher 注入 | 赋值静态属性 | ❌ 从未赋值（S-2） |
| `RelationshipTrust<target>` InputResolver | "下一小步" | ❌ 不存在（S-3） |
| `RelationshipFear<target>` InputResolver | "下一小步" | ❌ 不存在（S-3） |
| 事件规则：gift given / debt repaid / betrayed | "下一小步" | ❌ 不存在（M-3） |
| 持久化 | "故意未做" | ✅ 符合路线图 |
| 单元测试 | 覆盖 | ✅ RelationshipModuleTests |

**接线度**：0% — 数据能写入但 AI 无法读取。

---

### 第 3 步：ActorMemoryModule ⚠️ 骨架落地，上游断 + 下游断

| 项目 | 路线图要求 | 实际状态 |
|------|-----------|---------|
| 数据结构 | `actorId → List<ActorMemory>` | ✅ |
| FIFO MaxMemoriesPerActor=32 | 容量控制 | ✅ |
| 事件规则：hostile combat_attack → HostileAttackBy | 首版 | ✅ 代码存在 |
| 事件规则触发 | 收到 AI 事件 | ❌ 因 S-1 断裂 |
| Reset() | session 切换 | ✅ |
| Tick() decay | per-turn | ✅ 代码存在且被 TickSimulationSystems 调用 |
| AIDispatcher 注入 | 赋值静态属性 | ❌ 从未赋值（S-2） |
| `MemoryFearTowards<target>` InputResolver | "下一小步" | ❌ 不存在（S-3） |
| `MemoryDebtOwedTo<target>` InputResolver | "下一小步" | ❌ 不存在（S-3） |
| `MemoryBetrayalBy<target>` InputResolver | "下一小步" | ❌ 不存在（S-3） |
| 持久化 | "故意未做" | ✅ 符合路线图 |
| 单元测试 | 覆盖 | ✅ ActorMemoryModuleTests |

**接线度**：0% — 同上。

---

### 第 4 步：RumorBus ⚠️ 骨架落地，上游断 + 下游断

| 项目 | 路线图要求 | 实际状态 |
|------|-----------|---------|
| 数据结构 | `Rumor = (Id, SubjectId, Kind, ...)` | ✅ |
| RumorKind 枚举 | AttackWitnessed / TheftWitnessed / CasualtyReported | ✅ |
| QueryAudibleAt（Chebyshev + Z 过滤） | 审听模型 | ✅ 实现正确 |
| 事件规则：hostile combat_attack → AttackWitnessed | 首版 | ✅ 代码存在 |
| 事件规则触发 | 收到 AI 事件 | ❌ 因 S-1 断裂 |
| Reset() | session 切换 | ✅ |
| Tick() decay | per-turn credibility 衰减 | ✅ |
| SpawnTurn 字段 | "生成时记 SpawnTurn" | ❌ 不存在（N-4） |
| NpcKnowledge per-actor | "引入 NpcKnowledge" | ❌ 不存在（路线图标注为下一小步） |
| AIDispatcher 注入 | 赋值静态属性 | ❌ 从未赋值（S-2） |
| `NearbyRumorSeverity` InputResolver | "下一小步" | ❌ 不存在（S-3） |
| `HasRecentTheftRumor` InputResolver | "下一小步" | ❌ 不存在（S-3） |
| 持久化 | "故意未做" | ✅ 符合路线图 |
| 单元测试 | 覆盖 | ✅ RumorBusTests |

**接线度**：0% — 同上。`QueryAudibleAt` 的 Chebyshev + Z 过滤实现正确（`dx <= radius && dy <= radius && sourceZ == z`）。

---

### "下一小步" 按风险从低到高的建议顺序

| 步骤 | 改动 | 风险 | 理由 |
|------|------|------|------|
| 0 | **修 S-1**：`ApplyTimelineStep` 补 `DispatchConsequences` 调用 | 极低 | 一行代码，立即让三个社交模块开始收到 AI 事件 |
| 1 | **修 S-2**：`InitializeCoreServices` 补 `AIDispatcher.Relationships/ActorMemories/Rumors = ...` | 极低 | 三行代码，无行为变化（因为还没有 InputResolver 消费） |
| 2 | 加 `RelationshipFear<target>` InputResolver | 低 | 最简单的社交 InputResolver，只读 `ctx.BehaviorContext?.Relationships?.Get()` |
| 3 | 给 `flee_combat` 或 `chase_enemy` 的 considerations 里加一条 `RelationshipFear<target>` | 低 | 第一个 AI 真正消费社交数据的闭环 |
| 4 | 加 `NearbyRumorSeverity` InputResolver | 低 | 读 RumorBus.QueryAudibleAt，聚合 Credibility |
| 5 | 补 `combat_death` → CasualtyReported rumor | 低 | RumorKind 枚举已有成员 |
| 6 | 补 RelationshipModule 的 gift_given / betrayed 规则 | 中 | 需要确认上游是否已有对应 GameEvent type |
| 7 | 加 `MemoryFearTowards<target>` / `MemoryBetrayalBy<target>` InputResolver | 中 | 涉及 target-aware 聚合逻辑 |
| 8 | 加 Rumor SpawnTurn + age-based decay | 中 | 需修改 Rumor record 结构 |
| 9 | 给 RelationshipModule.Adjust 加 clamp | 低 | 防御性改动 |
| 10 | 引入 NpcKnowledge per-actor | 高 | 路线图标为"信息不对称分水岭"，改动面大 |

**判定标准（何时接入持久化）**：当步骤 0-3 完成且 InputResolver 在实际游戏中被消费后，社交数据开始影响 AI 行为。此时如果玩家 save/load 后发现"NPC 不记仇了"，就是接入 SaveFile 的信号。预计在步骤 3 完成后立即需要。

---

## 5. 已确认良好的实践

| 项目 | 状态 | 说明 |
|------|------|------|
| **配置 vs 代码双向对账** | ✅ | 27 个 Action 的 executor 全部在 ExecutorRegistry 注册；所有 JSON 引用的 inputId 全部在 InputResolver 注册。零漂移。 |
| **UtilityBrain 补偿公式** | ✅ | `MathF.Pow(product, 1f/n)` 几何平均补偿 + ±5% 随机扰动，防止 consideration 数量不同的 action 之间不公平比较 |
| **ResponseCurve 6 种曲线** | ✅ | Boolean / Linear / Exponential / Logistic / Inverse / Step 覆盖常见映射需求，参数边界处理正确（Logistic 中点 0.5 / Steepness 10 在 [0,1] 输入下输出范围 [0.007, 0.993]） |
| **UtilityCache 三层模型** | ✅ | Quick(3) / Full(6) / Simplified(10) 的间隔分层 + HasInterrupt 中断条件设计合理（OnFire / Awareness / HP drop / Enemy count 变化） |
| **TargetResolver Full vs Simplified** | ✅ | Full 模式逐目标评分（HP + distance + vital limb）；Simplified 模式选最近敌人。退化条件清晰。 |
| **PersonalityModule 10 轴 + 种族/职业偏移** | ✅ | 继承 + 变异机制完整；`Math.Clamp(value, axis.Min, axis.Max)` 确保不爆出 [0,1]（虽有 ceiling 效应但不是 bug） |
| **AwarenessModule 4 状态 FSM** | ✅ | Idle → Suspicious → Alerted → Searching 的状态转换清晰；Reset 在无敌对目标时正确触发 |
| **AIVisionBatch 性能优化** | ✅ | 快照缓存 + LOS 缓存 + BlocksSight 缓存 + partial sort top-K + 方向性视野（前/后不同范围） |
| **GameEventConsequenceRouter 容错** | ✅ | handler 异常不 abort 批次，错误转发到 errorSink |
| **Session 生命周期** | ✅ | Reset() 在四个模块上全部正确调用（Main.SessionFlow.cs:19-22）；Tick() decay 在 TickSimulationSystems 中统一调用 |
