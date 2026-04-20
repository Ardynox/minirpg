报告已输出到 `Artifacts/ai_utility_social_audit_2026-04-19.md`。

**核心发现概要：**

**三处严重断裂构成了一条完全不通的管线**：

1. **S-1**：AI 事件（NPC 之间的战斗，占 >90%）从 `ApplyTimelineStep → Dispatch` 只走了 presentation 路由，**从未经过 ConsequenceRouter**。社交模块虽然注册了 handler，但只能收到玩家发起的指令事件。修复只需一行代码。

2. **S-2**：`Main.Startup` 创建了 RelationshipModule / ActorMemoryModule / RumorBus 并注册到 ConsequenceRouter，但**从未赋值给 `AIDispatcher.Relationships/ActorMemories/Rumors`**。三个静态属性永远是 null，`AIBehaviorContext` 中的社交引用也永远是 null。修复只需三行代码。

3. **S-3**：路线图列出的 7 个社交 InputResolver（`RelationshipTrust/Fear`, `MemoryFear/Debt/Betrayal`, `NearbyRumorSeverity`, `HasRecentTheftRumor`）**全部不存在**。即使上游都通了，AI 也没有渠道消费社交数据。

**第一层 Utility AI 基线是健康的**：27 个 Action 与 ExecutorRegistry 零漂移、85+ InputResolver 全覆盖、三层缓存设计合理、性格 10 轴生成正确、4 状态 FSM 清晰。

报告末尾给出了从步骤 0（一行补 DispatchConsequences）到步骤 10（NpcKnowledge）的具体优先级排序，以及"何时该接入持久化"的判定标准。