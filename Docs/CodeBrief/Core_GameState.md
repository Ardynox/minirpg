# `Core/GameState.cs`

## 职责一句话

**唯一数据源**：承载全局游戏状态（可 JSON 序列化），并提供楼层快照结构用于楼层切换缓存/存档。

## 关键结构

### `GameState`

- **回合/随机**：`Turn`、`RngSeed`
- **楼层**：`CurrentFloor`
- **地图（四层分离）**
  - `Terrain`：`"#"`墙、 `"."`地面
  - `Fixtures`：静态设施（`">"` `<` `"N"` `"I"` 等）
  - `Objects`：动态实体 glyph（玩家/怪物等）
  - `Meta`：每格可选的 `Dictionary<string,string>`（触发器/标记扩展位）
- **巢穴**：`Nests: List<NestData>`
- **实体**：`Actors: Dictionary<string, Actor>`；`PlayerId`
- **玩家快捷坐标**：`PlayerX`/`PlayerY`（需与 `Actors[player]` 同步）
- **楼层缓存**：`Floors: Dictionary<int, FloorData>`（非当前楼层的快照都放这里）

### `FloorData`

一层楼的完整快照：宽高、四层地图、Meta、Nests、Actors、玩家坐标。

### `NestData`

巢穴数据：坐标、刷新间隔、距上次刷新回合数、最大附近怪物数量限制（`MaxSpawned`）。

## 关键方法

- `Reset()`：清空所有状态，回到初始值（新游戏/重开地图会用）

## 评估关注点

- **一致性约束**：`PlayerX/Y` 与 `Actors[PlayerId].X/Y` 与 `Objects` 层应该始终一致；目前靠调用点约束而非集中校验
- **序列化边界**：`Meta` 用字典；`Actors` 内部含多态 tag 来源（见 `TagSystem` 的多态 JSON 标注）

