# `Core/CombatModule.cs`

## 职责一句话

基于肢体耐久的伤害系统：实现攻击计算、伤害传递、肢体断裂判定、怪物死亡处理；并提供简单的怪物 AI 决策。

## 核心机制

### 伤害计算

- 普通攻击（melee_attack）：伤害 = 攻击者力量 + 动作威力×2 - 目标防御
- 毒液攻击（poison_attack）：伤害 = 攻击者毒性 + 动作威力×2，无视防御
- 亡灵吸取（drain_attack）：伤害同普通攻击，额外自愈攻击者肢体

### 肢体耐久

- 每个肢体有 `Durability`（当前）和 `MaxDurability`（最大）
- 伤害直接扣除目标肢体耐久
- 耐久归零时肢体断裂并从 Actor 移除

### 死亡判定

- 要么肢体含"要害"tag 的断裂
- 要么没有含"要害"tag 的肢体
- 死亡时掉落金币（`max(target.Gold, 5)`）

## 主要 API

### `Attack(GameState, attacker, target, action, targetLimb) -> List<GameEvent>`

核心攻击方法：
1. 格挡动作：给攻击者添加防御+5 的 Buff（1 回合）
2. 计算伤害并应用到目标肢体
3. 若是亡灵吸取，治愈攻击者随机肢体
4. 肢体耐久归零时触发 `limb_destroyed`
5. 满足死亡条件时触发 `actor_killed`，移除 Actor，掉落金币

### `CalcDamage(attacker, action, target) -> int`

- 取攻击者 `力量`/`毒性` tag + 动作威力
- 扣减目标 `防御` tag（毒液除外）
- 最小伤害为 1

### `IsDead(Actor) -> bool`

- 检查 Actor 是否还有含"要害"tag 的肢体

### `GetAttackActions(Actor) -> List<ActionDef>`

- 过滤可用动作，只保留 `melee_attack`、`poison_attack`、`drain_attack`

### `MonsterChooseAction(GameState, monster, player) -> (ActionDef, Limb)?`

简单怪物 AI：
- 随机选一个可用攻击动作
- 随机选玩家一个肢体
- 使用 `state.RngSeed + state.Turn + monster.Id.GetHashCode()` 作为 RNG 种子

## 战斗流程

1. 玩家选择一个动作（近战/重击/毒液/吸取）
2. 选择目标肢体的位置（头/手/腿等）
3. `CombatModule.Attack()` 执行攻击
4. 若未击杀，怪物反击（`MonsterCounterAttack`）
5. 双方更新 Buff（`TickBuffs`）

## 依赖关系

- `ActorModule`：用于移除死亡 Actor
- `ActionDefs`：内置动作定义
- `ActionQuery`：过滤可用动作
- `GameEvent`：产出战斗事件

## 评估关注点

- **伤害公式**：防御对毒液无效，这是史莱姆等毒系怪物的优势
- **自愈机制**：亡灵吸取让不死系怪物有持续作战能力
- **要害设计**：头/核心等含要害 tag 的肢体断裂直接导致死亡
- **怪物 AI**：当前是纯随机选择，可扩展为更智能的决策