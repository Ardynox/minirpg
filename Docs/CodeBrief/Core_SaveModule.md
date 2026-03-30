# `Core/SaveModule.cs`

## 职责一句话

存档系统：支持**全局存档（所有楼层 → JSON 文件）**，以及**楼层切换时的内存快照（当前楼层 ↔ `state.Floors` 缓存）**。

## 两条路径

### 1) 全局存档（JSON 文件）

- `SaveGame(GameState state, string filePath)`
  - 生成 `FullSaveData`
  - 当前楼层：从 `GameState` 直接快照成 `MapSaveData`
  - 非当前楼层：从 `state.Floors` 的 `FloorData` 转成 `MapSaveData`
  - `System.Text.Json` 序列化写入文件（自动建目录）
- `LoadGame(GameState state, string filePath) -> bool`
  - 读文件 → `FullSaveData`
  - `state.Floors` 填满所有楼层（`floor -> FloorData`）
  - 若存在当前楼层：把它恢复到 `GameState` 的“直属字段”，并从 `Floors` 移除该楼层缓存

### 2) 楼层切换内存快照（不落盘）

- `SaveFloorToDict(GameState state)`
  - 把当前楼层快照保存到 `state.Floors[state.CurrentFloor]`
- `LoadFloorFromDict(GameState state, int floor) -> bool`
  - 若缓存存在：恢复到 `GameState` 当前直属字段，并从 `Floors` 移除

## 数据结构

- `FullSaveData`
  - `CurrentFloor/Turn/RngSeed`
  - `Floors: Dictionary<string, MapSaveData>`（key 是 floor 的字符串）
- `MapSaveData`（单层平铺存储）
  - `Width/Height`
  - `Terrain/Fixtures/Objects`：平铺 `List<string>`
  - `Meta`：平铺 `List<Dictionary<string,string>?>`
  - `PlayerX/PlayerY`
  - `Nests`
  - `Actors`（字典）

## 实现要点

- 深拷贝：
  - 地图层：逐行复制
  - Meta：逐格复制字典
  - Nests：逐项复制
  - Actors：逐 actor 复制；并复制 tag 来源（Limbs/Race/Profession/Buffs/Experiences）
- 平铺/还原：
  - `FlattenLayer` / `UnflattenLayer`
  - `FlattenMeta` / `UnflattenMeta`

## 与其他模块关系

- `MapModule.GoUpFloor/GoDownFloor` 依赖这里的 `SaveFloorToDict/LoadFloorFromDict`
- `Actor` 内部的 tag 来源需要正确序列化/反序列化（见 `Core/TagSystem.cs` 的多态 JSON 标注）

## 评估关注点

- **一致性**：Load 后 `Main.EnsurePlayerActor` 会补玩家；但如果 JSON 里 Actors/Objects 与 PlayerX/Y 不一致，恢复路径是否能自愈
- **扩展成本**：新增 `GameState` 字段需要同步更新快照/序列化结构（否则会丢数据）

