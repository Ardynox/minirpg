# `Core/ActionDefs.cs`

## 职责一句话

内置动作定义表：以数据方式列出所有动作的前置 tag 需求与效果描述；用于根据 `Actor` tag 自动推导"当前可用动作"。

## 核心内容

- `ActionDefs.All: List<ActionDef>`

当前内置动作：

- `"melee_attack"`（近战攻击）：需要 `近战:1` + `力量:1`，伤害公式
- `"heavy_strike"`（重击）：需要 `近战:3` + `力量:4`，高伤害
- `"block"`（格挡）：需要 `格挡:1`，防御+5（1回合）
- `"poison_spit"`（毒液喷射）：需要 `毒性:2`，无视防御的伤害
- `"undead_drain"`（亡灵吸取）：需要 `亡灵:1` + `近战:2`，伤害+自愈
- `"move"`（移动）：需要 `移动:1`
- `"look"`（观察）：需要 `视觉:1`

每个动作包含：

- `Required`：例如近战/力量阈值
- `EffectType`：例如 `"melee_attack"` `"block"` `"move"`
- `Power`：倍率（由执行系统解释）

## 当前使用点

- `ActorModule.GetPlayerStatus`：
  - `ActionQuery.GetAvailable(player, ActionDefs.All)` 得到 `AvailableActions`
- `CombatModule.GetAttackActions`：
  - 过滤出 `melee_attack`、`poison_attack`、`drain_attack` 类型
- `Main.DoLook()` 会把可用动作名称拼到日志里展示

## 评估关注点

- **数据驱动方向**：注释中明确后续可从 JSON 加载；现在是硬编码
- **动作类型**：
  - 战斗类：melee_attack, heavy_strike, poison_spit, undead_drain, block
  - 移动类：move
  - 感知类：look