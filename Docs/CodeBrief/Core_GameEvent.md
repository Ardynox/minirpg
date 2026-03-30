# `Core/GameEvent.cs`

## 职责一句话

逻辑层产出的“发生了什么”的数据包；由驱动层（`Main.cs`）消费以触发日志/渲染等副作用。

## 结构

### `GameEvent`

- `Type: string`：事件类型（例如：`"hit_wall"`, `"actor_moved"`, `"attack_hit"`, `"monster_spawned"`）
- `TargetX/TargetY: int`：常用坐标槽（不同事件复用）
- `TargetActorName?: string`：用于日志显示的目标名（目前主要用于 `"attack_hit"`）

## 当前使用点

- 产生方：
  - `MapModule.TryMovePlayer`：`hit_wall` / `actor_moved` / `attack_hit`
  - `NestModule.Tick`：`monster_spawned`
- 消费方：
  - `Main.Dispatch`：写日志（部分事件只影响渲染，不写日志）

## 评估关注点

- **表达力**：事件字段偏少（没有来源 actorId、伤害数值、物品 id 等），后续扩展可能需要：
  - 把 `GameEvent` 改成更通用 payload（字典/结构化子类型），或引入多个事件类

