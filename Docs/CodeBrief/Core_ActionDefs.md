# `Core/ActionDefs.cs`

## 职责一句话

内置动作定义表：以数据方式列出所有动作的前置 tag 需求与效果描述；用于根据 `Actor` tag 自动推导“当前可用动作”。

## 核心内容

- `ActionDefs.All: List<ActionDef>`
  - `"melee_attack"`（近战攻击）
  - `"heavy_strike"`（重击）
  - `"block"`（格挡）
  - `"poison_spit"`（毒液喷射）
  - `"undead_drain"`（亡灵吸取）
  - `"move"`（移动）
  - `"look"`（观察）

每个动作包含：

- `Required`：例如近战/力量阈值
- `EffectType`：例如 `"melee_attack"` `"block"` `"move"`
- `Power`：倍率（由执行系统解释）

## 当前使用点

- `ActorModule.GetPlayerStatus`：
  - `ActionQuery.GetAvailable(player, ActionDefs.All)` 得到 `AvailableActions`
  - `Main.DoLook()` 会把可用动作名称拼到日志里展示

## 评估关注点

- **数据驱动方向**：注释中明确后续可从 JSON 加载；现在是硬编码
- **闭环缺失**：有“动作定义/可用动作”，但暂未实现“执行动作”的通用模块（除移动/观察的局部逻辑）

