# `Core/NestModule.cs`

## 职责一句话

巢穴刷怪逻辑：地图设施层 `"N"` 代表巢穴；每回合 `Tick` 增加计时，到达间隔后在相邻可走格刷怪并产出事件。

## 关键规则

- 巢穴来源：扫描 `Fixtures` 层 `"N"`
- 刷怪位置：巢穴四邻格（上下左右），要求 `MapModule.IsWalkable == true`
- 刷怪上限：统计"附近半径内（轴对齐方框）敌对怪物数"，达到 `MaxSpawned` 则不刷
- RNG：`new Random(state.RngSeed + state.Turn)`（同 seed + 回合可复现）

## 主要 API

- `RegisterNests(GameState state, int spawnInterval = 5, int maxSpawned = 3)`
  - 清空 `state.Nests`，扫描地图登记 `NestData`
- `Tick(GameState state) -> List<GameEvent>`
  - 对每个巢穴 `TurnsSinceSpawn++`
  - 达到间隔才尝试刷怪
  - 若可刷：`ActorTemplates.Spawn(...)` + `ActorModule.Add` 并返回 `monster_spawned(TargetX,TargetY)`

## 依赖关系

- `MapModule.GetFixture/IsWalkable`
- `ActorTemplates.MonsterIds`
- `ActorModule.Add`
- `GameEvent`

## 评估关注点

- **距离计算**：附近怪物统计是"方形半径"（`|dx|<=r && |dy|<=r`），不是欧式/曼哈顿
- **计数口径**：用 `actor.Faction == "hostile"` 判定怪物；友方/中立不计入
- **模板支持**：支持固定模板刷怪（`NestData.TemplateId`）或随机怪物