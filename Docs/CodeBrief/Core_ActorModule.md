# `Core/ActorModule.cs`

## 职责一句话

Actor 的增删查改：对 `GameState.Actors` 做统一操作，并同步地图 `Objects` 层与 `GameState.PlayerX/Y`。

## 主要 API

### 增删

- `Add(GameState, Actor)`：
  - 写入 `state.Actors[id]`
  - `MapModule.SetObject(x,y,glyph)` 同步 `Objects` 层
- `Remove(GameState, string id)`：
  - 清空 `Objects` 层对应格
  - 从 `state.Actors` 删除

### 查询

- `GetById(GameState, string id)`：按 ID 查找
- `GetAt(GameState, x, y)`：线性扫描 `Actors.Values`（按坐标找单个）
- `GetAllAt(GameState, x, y)`：返回指定坐标上的**所有** Actor
- `GetHostileAt(GameState, x, y)`：返回指定坐标上的敌对 Actor
- `GetPlayer(GameState)`：获取玩家 Actor
- `GetAllHostile(GameState)`：返回所有敌对列表（`Faction == "hostile"`）
- `FindAdjacentHostile(GameState)`：扫描四邻格找敌对

### 移动

- `MoveActor(GameState, id, nx, ny)`：
  - 清旧格 `Objects`，更新 actor 坐标，写新格 `Objects`
  - 若是玩家（`id == state.PlayerId`），同步 `state.PlayerX/Y`

### 工具方法

- `RefreshObjectsCell(GameState, x, y)`：
  - 重算一格的 Objects 层显示
  - 优先级：player > hostile > 其他
- `ClearAll(GameState)`：清空 `state.Actors`（注意：不清 `Objects` 层）

### 状态获取

- `GetPlayerStatus(GameState) -> PlayerStatus?`：
  - `player.ComputeTags()` 得到 tags
  - 抽取 `生命/力量/防御` 作为简版面板数值
  - `ActionQuery.GetAvailable(player, ActionDefs.All)` 得到可用动作

## 数据结构

### `PlayerStatus`

- `Tags`：完整 tag 表
- `Hp/Atk/Def`：从 tags 派生
- `AvailableActions: List<ActionDef>`

## 与其他模块关系

- 强依赖 `MapModule`（同步 `Objects` 层）
- 依赖 `ActionQuery` 与 `ActionDefs`（tag→可用动作）

## 评估关注点

- **一致性风险**：`ClearAll` 只清 `Actors`，不清 `Objects`；目前地图生成路径会重建地图层，避免残留，但调用方需注意
- **查询复杂度**：`GetAt/GetHostileAt` 线性扫描，Actor 多时需要空间索引或网格映射
- **多 Actor 同格**：支持多个 Actor 占据同一格（Objects 层只显示优先级最高的）