# `Core/MapModule.cs`

## 职责一句话

地图读写与查询模块：对 `GameState` 的四层地图（Terrain/Fixtures/Objects/Meta）做统一访问，并实现玩家移动与楼层切换的核心规则。

## 关键约定

- **层序**：Terrain → Fixtures → Objects → Meta
- **渲染优先级**：`Objects > Fixtures > Terrain`（`GetDisplayCell`）
- **可通行**：非墙 + `Objects` 层为空；`Fixtures` 不阻挡移动（见 `IsWalkable`）

## 主要 API

### 初始化/导入

- `LoadFromStrings(GameState, string[] rows)`
  - `"#"` `"."` → Terrain
  - `"D"` `"N"` `"I"` `"H"` `">"` `"<"` → Fixtures
  - 其他字符 → Objects
  - 遇到 `"P"` 会设置 `state.PlayerX/Y`

### 读取/写入

- `GetTerrain/GetFixture/GetObject/GetMeta`
- `SetTerrain/SetFixture/SetObject/SetMeta`
- `GetDisplayCell`：按渲染优先级返回一个格子的显示字符

### 查询

- `InBounds`
- `IsWall`
- `IsWalkable`
- `IsHostile`（通过 `ActorModule.GetHostileAt`）
- `IsDoor/IsDownStair/IsUpStair`

### 楼层切换

- `GoDownFloor(GameState)`
  - 先 `SaveModule.SaveFloorToDict(state)` 保存当前层快照
  - `CurrentFloor++`
  - `SaveModule.LoadFloorFromDict(state, CurrentFloor)`：若命中缓存返回 true，否则 false（需要生成新地图）
- `GoUpFloor(GameState)`
  - 顶层保护（`CurrentFloor <= 0` 返回 false）
  - 保存快照、`CurrentFloor--`、从缓存恢复
- `PlacePlayerAtFixture(GameState, string fixtureType)`
  - 扫描 `Fixtures` 找目标标记，并用 `ActorModule.MoveActor` 移动玩家

### 玩家移动（核心规则）

- `TryMovePlayer(GameState, dx, dy) -> List<GameEvent>`
  - 目标是墙：`hit_wall`
  - 目标有敌对 Actor：创建 `combat_bump`（携带名字）
  - 目标不可走（例如 `Objects` 非空但非敌对）：`hit_wall`
  - 否则：`ActorModule.MoveActor` + `actor_moved`

## 与其他模块耦合点

- 通过 `ActorModule` 处理移动与敌对查询（保证 `Actors` 与 `Objects` 同步）
- 楼层切换直接调用 `SaveModule`（Map 与 Save 有耦合）

## 评估关注点

- **战斗语义**：`combat_bump` 事件触发后由 Main 处理战斗流程；不同于之前的直接击杀设计
- **地图与存档耦合**：`GoUp/GoDown` 直接依赖 `SaveModule`，会影响模块可测试性/可替换性
- **设施层设计**：Fixtures 层存储静态设施不阻挡移动，适合扩展门、陷阱等