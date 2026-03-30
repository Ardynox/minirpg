# `Core/GameEvent.cs`

## 职责一句话

逻辑层产出的"发生了什么"的数据包；由驱动层（`Main.cs`）消费以触发日志/渲染等副作用。

## 结构

### `GameEvent`

- `Type: string`：事件类型
- `TargetX/TargetY: int`：常用坐标槽
- `TargetActorName?: string`：用于日志显示的目标名
- `InitiatorId?: string`：发起者 ID
- `TargetId?: string`：目标 ID

### 战斗事件

- `Damage: int`：伤害数值
- `LimbName?: string`：目标肢体名
- `ActionName?: string`：动作名称

### 交互事件

- `InteractionDefId?: string`：交互定义 ID
- `InteractionName?: string`：交互名称
- `EffectType?: string`：效果类型

## 当前事件类型

### 移动相关

- `hit_wall`：撞墙
- `actor_moved`：玩家移动成功

### 战斗相关

- `combat_bump`：撞击敌人（触发战斗菜单）
- `combat_attack`：攻击造成伤害
- `combat_block`：格挡成功
- `limb_destroyed`：肢体被摧毁
- `actor_killed`：生物死亡（含金币掉落）
- `player_limb_hit`：玩家肢体被攻击
- `player_died`：玩家死亡

### 刷怪相关

- `monster_spawned`：巢穴刷出怪物

### 交互相关

- `interaction`：交互事件（talk/trade/combat/tame）

## 当前使用点

- 产生方：
  - `MapModule.TryMovePlayer`：`hit_wall` / `actor_moved` / `combat_bump`
  - `NestModule.Tick`：`monster_spawned`
  - `CombatModule.Attack`：`combat_attack` / `combat_block` / `limb_destroyed` / `actor_killed`
  - `InteractionModule.Execute`：`interaction`
- 消费方：
  - `Main.Dispatch`：写日志/触发菜单

## 评估关注点

- **扩展性**：事件字段覆盖了战斗和交互，但仍可能需要 payload 字典或子类型来处理更复杂的场景