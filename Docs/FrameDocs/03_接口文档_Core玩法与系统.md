# 03 接口文档: Core 玩法与系统

## 范围与统计

- 范围：`Core/Combat`、`Core/AI`、`Core/Health`、`Core/Needs`、`Core/Dialog`、`Core/Trade`、`Core/Debug`
- 当前统计：35 个运行时 `.cs` 文件，75 个 `public` 类型
- 目标：梳理回合推进、动作执行、AI、健康、需求、对话、交易与调试系统的公开协作面

## 行为型系统总表

统一观察模板：

- 入口方法
- 校验 / 决策
- 状态变更
- 事件产出

| 类型 | 入口方法 | 校验 / 决策 | 状态变更 | 事件产出 | 主要调用者 / 协作者 |
| --- | --- | --- | --- | --- | --- |
| `TimelineTurnManager` | `Reset`、`SubmitPlayerAction`、`AdvanceAuto`、`CreateDebugSnapshot` | 校验当前 actor 是否可控、是否死亡、是否轮到玩家、是否需要自动步进 | 写 `GameState.Timeline`、推进 `TurnModule.AdvanceWorld`、切换当前 actor | `TimelineStepResult.Events`、调试快照 | `Main.SubmitPlayerAction`、`Main.AdvanceTimelineAutoStep`、`AIDispatcher` |
| `ActionModule` | `TryMove`、`TryAttack`、`TryDig`、`TryInteract`、`TryCastSkill`、`CanCastSkill` | 校验地形可走、目标合法、技能目标类型、范围、资源与装备条件 | 角色朝向、位移、目标血量 / 肢体 / 背包 / 地形 | 返回 `ActionExecutionResult` / `GameEvent` | `TimelineTurnManager`、`AIDispatcher` |
| `CombatModule` | `Attack`、`ApplyEnvironmentalDamage`、`CalcDamage`、`PickPreferredTargetLimb` | 判断 `block`、伤害类型、护甲、要害肢体、死亡条件 | 目标肢体耐久、buff、掉落、死亡相关健康状态 | `combat_attack`、`combat_block`、`limb_destroyed`、`actor_killed` 等 | `ActionModule`、`FireSystem`、`AIDispatcher` |
| `TurnModule` | `AdvanceWorld`、`Tick`、`TickWatchMode` | 判断是否旁观模式、是否需要玩家 AI | 写 `GameState.Turn`，推进巢穴、天气、火焰 | 世界推进事件列表 | `TimelineTurnManager` |
| `AIDispatcher` | `TickAll`、`DecideAndExecuteOneResult`、`DecideAndExecuteAnyResult` | 选择模拟粒度、构造感知、短路健康/火焰/温度/需求行为、最后调用脑模块决策 | AI 角色状态、朝向、动作结果 | `ActionExecutionResult.Events` | `TimelineTurnManager`、`TurnModule`、`IBrainModule` |
| `IBrainModule` | `Decide` | 根据 `Perception` 与随机源做决策 | 无直接写入 | 输出 `Decision` | `AIDispatcher` |
| `SimpleBrain` | `Decide` | 默认攻击脑，基于完整感知做战斗导向决策 | 无直接写入 | `Decision` | `AIDispatcher` |
| `HealthSystem` | `EnsureInitialized`、`Sync`、`AddOrUpdateInjury`、`ApplyTreatment`、`GetFatalCause` | 校验健康配置、环境暴露、可治疗条件 | 角色健康状态、感染/受伤/治疗进度、容量乘数 | 健康事件、致死判定 | `SaveModule`、`CombatModule`、`HealthActionModule`、`HealthBehaviorModule` |
| `HealthActionModule` | `TryTendSelf`、`TryTendOther` | 校验是否存在可治疗目标与物资 | 治疗进度、物资消耗 | `ActionExecutionResult` | `ActionModule`、AI 行为 |
| `FireSystem` | `Advance`、`TryIgniteActor`、`TryIgniteCell`、`TryExtinguish`、`CanExtinguishAt` | 判断点燃 / 熄灭合法性、环境危险格 | 火焰强度、实体燃烧状态、地形火焰 | 火焰相关 `GameEvent` | `TurnModule`、`ActionModule`、AI / 玩家技能 |
| `SurgeryModule` | `TrySpawnCorpseOnDeath`、`TryOperateOnCorpse`、`TryOperateOnActor` | 判断尸体/活体可操作肢体、可安装部件 | 尸体物品、肢体缺失、移植结果 | 手术执行结果、掉落事件 | `ActionModule`、`CombatModule` |
| `NeedSystem` | `EnsureInitialized`、`Sync`、`GetNeedValue`、`ApplyThought`、`ApplyTemporaryThought` | 按当前回合、思想来源与环境更新需求 | 饥饿、休息、心情、思想状态 | 需求与思想事件 | `SaveModule`、`NeedActionModule`、`NeedBehaviorModule`、`DialogUIModule` |
| `NeedActionModule` | `TryConsumeFood`、`TryRest`、`HasBedroll` | 校验食物 / 床具 / 休息上下文 | 需求值、背包、世界地面食物 | `NeedActionResult` | `ActionModule`、AI |
| `DialogRuleEngine` | `SelectEntry`、`FilterOptions`、`Matches` | 根据 `DialogContext` 做条件匹配与优先级选择 | 无直接写入 | 对话入口与选项列表 | `DialogUIModule` |
| `TradeModule` | `ListGoods`、`Buy`、`Sell`、`SellPrice` | 校验金币、库存、装备状态 | 玩家 / 商人金币、双方背包与 shop slot | `TradeResult` | `TradeUIModule` |
| `DebugModule` | `HandleCommand` | 解析 `/chest /spawn /god /down /weather ...` | 视命令写世界、角色、天气、楼层 | `Result.Logs`、`NeedsFlush` | `Main.OnCommand` |

## AI 与时间线模型索引

| 类型 | 归类 | 语义 / 关键字段 | 主要消费方 |
| --- | --- | --- | --- |
| `DecisionType` | enum | AI 决策类型 | `IBrainModule`、`AIDispatcher` |
| `Decision` | DTO | 脑模块决策结果 | `AIDispatcher.ExecuteDecision` |
| `SimDetail` | enum | AI 模拟粒度：完整 / 简化 / 摘要 | `AIDispatcher`、`PerceptionBuilder` |
| `Perception` | DTO | AI 感知上下文 | `IBrainModule`、行为模块 |
| `FactionRelation` | static / helper | 阵营关系判定 | `ActionModule`、AI |
| `AIVisionRequest` | DTO | 批量视野请求 | `AIVisionBatch` |
| `AIVisionMetrics` | DTO | AI 视野统计结果 | `AIVisionBatch` |
| `AIVisionBatch` | service | 批量构造 AI 视野 | `PerceptionBuilder`、`AIDispatcher` |
| `TimelinePlayerActionType` | enum | 玩家动作类型：移动 / 挖掘 / 攻击 / 施法 / 吃 / 休息 | `TimelineTurnManager` |
| `TimelinePlayerAction` | DTO | 玩家动作请求 | `Main`、`TimelineTurnManager` |
| `TimelineActorState` | DTO | 单角色蓄力状态 | `TimelineState` |
| `TimelineState` | DTO | 时间线运行时状态 | `GameState` |
| `TimelineStepResult` | DTO | 单步推进结果：事件、当前 actor、是否继续自动推进 | `Main.ApplyTimelineStep` |
| `TimelineDebugPhase` | enum | 时间线调试阶段 | `TurnPanelModule` |
| `TimelineInputLockReason` | enum | 输入锁定原因 | `TurnPanelModule`、`Main` |
| `TimelineDebugEntry` | DTO | 调试面板中的角色条目 | `TurnPanelModule` |
| `TimelineDebugSnapshot` | DTO | 调试快照根对象 | `TurnPanelModule`、`Main` |
| `SkillTargetType` | enum | 技能目标类型：自身 / 角色 / 格子 / 物品 | `ActionModule` |
| `SkillCastFailureReason` | enum | 技能失败原因 | `ActionModule`、UI |
| `ActionExecutionResult` | DTO | 动作执行结果：是否消耗回合与事件列表 | `ActionModule`、`AIDispatcher` |

## 健康与需求模型索引

| 类型 | 归类 | 语义 / 关键字段 | 主要消费方 |
| --- | --- | --- | --- |
| `HealthConditionIds` | const/static ids | 健康状态 ID 常量 | `HealthSystem`、战斗、治疗 |
| `HealthThoughtSources` | const/static ids | 健康思想来源 | `NeedSystem`、`HealthSystem` |
| `HealthConditionKinds` | const/static ids | 健康状态种类标识 | `HealthSystem` |
| `HealthConditionState` | DTO | 单个健康状态运行时值 | `HealthSystem`、存档 |
| `HealthProfileDef` | 配置 DTO | 健康档案定义 | `HealthCatalog` |
| `HealthConditionDef` | 配置 DTO | 伤病 / 感染 / 缺肢定义 | `HealthCatalog`、`HealthSystem` |
| `HealthThoughtDef` | 配置 DTO | 健康思想定义 | `HealthCatalog` |
| `EnvironmentExposureSnapshot` | DTO | 环境暴露快照 | `HealthSystem.Sync` |
| `RoomContextSnapshot` | DTO | 房间环境上下文 | `RoomContextAnalyzer`、`HealthSystem` |
| `IEnvironmentExposureProvider` | 接口 | 环境暴露来源抽象 | `HealthSystem` |
| `DefaultEnvironmentExposureProvider` | service | 默认环境暴露提供者 | `SaveModule`、`HealthSystem` |
| `NeedIds` | const/static ids | 需求 ID 常量 | `NeedSystem` |
| `NeedThoughtSources` | const/static ids | 思想来源常量 | `NeedSystem`、对话、战斗 |
| `NeedState` | DTO | 单需求运行时值 | `NeedSystem`、存档 |
| `ThoughtState` | DTO | 思想运行时值 | `NeedSystem`、HUD |
| `NeedDef` | 配置 DTO | 需求定义 | `NeedCatalog` |
| `NeedStageDef` | 配置 DTO | 需求阶段定义 | `NeedCatalog` |
| `NeedProfileDef` | 配置 DTO | 需求档案定义 | `NeedCatalog` |
| `ThoughtDef` | 配置 DTO | 思想定义 | `NeedCatalog` |
| `RestContext` | DTO | 休息上下文 | `NeedActionModule`、AI |
| `NeedActionResult` | DTO | 吃 / 休息动作结果 | `NeedActionModule`、`ActionModule` |

## 对话、交易与调试数据索引

| 类型 | 归类 | 语义 / 关键字段 | 主要消费方 |
| --- | --- | --- | --- |
| `TradeResult` | DTO | 买卖结果：是否成功、物品、价格、消息 | `TradeUIModule` |
| `TradeGood` | DTO | 统一的可交易商品视图 | `TradeModule.ListGoods`、交易面板 |
| `Source` | enum | 商品来源：`Shop` / `Inventory` | `TradeGood` |
| `Result` (`DebugModule`) | struct | 调试命令执行结果：日志与是否要求刷新 | `Main.OnCommand` |

## 玩法系统观察

- `TimelineTurnManager` 是真正的回合推进中心。`Main` 只负责提交动作与消费结果，不负责判定谁行动。
- `ActionModule` 是玩家与 AI 的统一执行器。无论输入来自键盘、面板还是脑模块，最终都下沉到这里。
- `AIDispatcher` 先跑一串“短路型行为模块”再问脑模块，说明 AI 行为链已经形成优先级流水线。
- `HealthSystem` 与 `NeedSystem` 都是跨回合同步型系统，既被世界推进调用，也被保存流程调用，以保证快照稳定。
- `DebugModule` 目前直接拿 `GameSessionModule` 做楼层切换和预设导出，是明显的跨层倒挂。

## 附录: 穷举索引

| 源码文件 | public 类型 |
| --- | --- |
| `Core/AI/AIDispatcher.cs` | `AIDispatcher` |
| `Core/AI/AIVisionBatch.cs` | `AIVisionRequest`, `AIVisionMetrics`, `AIVisionBatch` |
| `Core/AI/AwarenessModule.cs` | `AwarenessModule` |
| `Core/AI/FireBehaviorModule.cs` | `FireBehaviorModule` |
| `Core/AI/IBrainModule.cs` | `DecisionType`, `Decision`, `SimDetail`, `Perception`, `IBrainModule`, `FactionRelation` |
| `Core/AI/PerceptionBuilder.cs` | `PerceptionBuilder` |
| `Core/AI/SimpleBrain.cs` | `SimpleBrain` |
| `Core/Combat/ActionModule.cs` | `ActionModule` |
| `Core/Combat/CombatModule.cs` | `CombatModule` |
| `Core/Combat/FirearmModule.cs` | `FirearmModule` |
| `Core/Combat/NestModule.cs` | `NestModule` |
| `Core/Combat/SkillCasting.cs` | `SkillTargetType`, `SkillCastFailureReason`, `ActionExecutionResult` |
| `Core/Combat/TimelineTurnManager.cs` | `TimelinePlayerActionType`, `TimelinePlayerAction`, `TimelineActorState`, `TimelineState`, `TimelineStepResult`, `TimelineDebugPhase`, `TimelineInputLockReason`, `TimelineDebugEntry`, `TimelineDebugSnapshot`, `TimelineTurnManager` |
| `Core/Combat/TurnModule.cs` | `TurnModule` |
| `Core/Debug/DebugModule.cs` | `DebugModule`, `Result` |
| `Core/Dialog/DialogPool.cs` | `DialogPool` |
| `Core/Dialog/DialogRuleEngine.cs` | `DialogRuleEngine` |
| `Core/Dialog/TemplateRenderer.cs` | `TemplateRenderer` |
| `Core/Health/DefaultEnvironmentExposureProvider.cs` | `DefaultEnvironmentExposureProvider` |
| `Core/Health/FireSystem.cs` | `FireSystem` |
| `Core/Health/HealthActionModule.cs` | `HealthActionModule` |
| `Core/Health/HealthBehaviorModule.cs` | `HealthBehaviorModule` |
| `Core/Health/HealthCatalog.cs` | `HealthCatalog` |
| `Core/Health/HealthModels.cs` | `HealthConditionIds`, `HealthThoughtSources`, `HealthConditionKinds`, `HealthConditionState`, `HealthProfileDef`, `HealthConditionDef`, `HealthThoughtDef`, `EnvironmentExposureSnapshot`, `RoomContextSnapshot`, `IEnvironmentExposureProvider` |
| `Core/Health/HealthSystem.cs` | `HealthSystem` |
| `Core/Health/HeatActionModule.cs` | `HeatActionModule` |
| `Core/Health/RoomContextAnalyzer.cs` | `RoomContextAnalyzer` |
| `Core/Health/SurgeryModule.cs` | `SurgeryModule` |
| `Core/Health/TemperatureBehaviorModule.cs` | `TemperatureBehaviorModule` |
| `Core/Needs/NeedActionModule.cs` | `NeedActionModule` |
| `Core/Needs/NeedBehaviorModule.cs` | `NeedBehaviorModule` |
| `Core/Needs/NeedCatalog.cs` | `NeedCatalog` |
| `Core/Needs/NeedModels.cs` | `NeedIds`, `NeedThoughtSources`, `NeedState`, `ThoughtState`, `NeedDef`, `NeedStageDef`, `NeedProfileDef`, `ThoughtDef`, `RestContext`, `NeedActionResult` |
| `Core/Needs/NeedSystem.cs` | `NeedSystem` |
| `Core/Trade/TradeModule.cs` | `TradeResult`, `TradeGood`, `Source`, `TradeModule` |
