# Combat

这个目录负责战斗动作、伤害结算和时间线推进。
它既包含玩家/AI 共享动作入口，也包含回合时间线和技能落地，但不负责 UI 呈现。

## 从哪开始读

- 先看 `TimelineTurnManager.cs`：玩家时间线、自动推进、输入锁和行动消费都在这里
- 共享动作入口：`ActionModule.cs`
- 伤害、护甲、部位受伤和死亡判定：`CombatModule.cs`
- 技能释放与目标落地：`SkillCasting.cs`
- 枪械相关：`FirearmModule.cs`
- 移动/攀爬事件组装：`MovementEventFactory.cs`

## 常见改动去哪里

- 改玩家移动、相邻攻击、共享动作入口：`ActionModule.cs`
- 改时间线充能、玩家行动消费、自动推进：`TimelineTurnManager.cs`
- 改伤害公式、护甲、部位伤害结果：`CombatModule.cs`
- 改技能施放或目标解析落地：`SkillCasting.cs`
- 改移动或攀爬事件 payload：`MovementEventFactory.cs`

## 不要在这里解决什么

- 不把战斗动画、镜头、FX、日志路由塞进 Core combat，表现层看 `App/RuntimeUi/*` 和 `Module/Render/*`
- 不把会话切换、多人房间流程或 UI 面板逻辑塞进这里
- 不把所有战斗规则重新堆回 `TimelineTurnManager.cs`；能抽局部 helper 就先抽局部 helper

## 什么时候更新这份 README

- 只有当 combat 稳定入口、时间线落点、共享动作入口或常见改动入口发生变化时才更新
