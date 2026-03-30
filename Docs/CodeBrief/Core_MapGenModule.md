# `Core/MapGenModule.cs`

## 职责一句话

随机地图生成：房间放置 + 走廊连接 + 放置玩家/楼梯/巢穴/怪物/NPC，并在生成后注册巢穴列表。

## 关键数据结构

- `Room`：房间矩形（`X,Y,W,H`），含中心点 `CenterX/CenterY`

## 主要入口

- `Generate(GameState state, int width, int height, int floor = 0, int? seed = null) -> List<Room>`
  - 初始化 RNG，并写入 `state.RngSeed`
  - 重置尺寸与回合（`state.Turn = 0`）
  - `ActorModule.ClearAll(state)` 清掉 Actor
  - `InitLayers`：把四层地图初始化为墙/空
  - `PlaceRooms`：多次尝试随机房间，避免重叠，成功就 `CarveRoom` 把地形挖成 `.`
  - `ConnectRooms`：按房间序列用 L 型走廊连接
  - `Populate`：
    - 第一个房间中心放玩家（`ActorTemplates.Spawn("player")` + `ActorModule.Add`）
    - `floor > 0` 时在第一房间中心放 `<`（上行楼梯）
    - 若有第二个房间，把最后一个房间中心放 `>`（下行楼梯）
    - 中间房间：60% 概率放 `"N"` 巢穴；40% 概率放 1 只怪
  - `NestModule.RegisterNests(state)`：扫描地图设施层，填充 `state.Nests`

### 地表（floor = 0）特殊处理

- `PopulateSurface`：
  - 最后一个房间放 `>`（下行楼梯）
  - 第一个之后的中间房间放置 NPC：
    - 商人小屋（merchant）
    - 村长小屋（elder）
    - 村民房（villager）
  - NPC 所在格设为 `H`（房屋标记）
  - 注册 NPC 巢穴（NestData），间隔 1，最大 1

### 地下城（floor > 0）特殊处理

- `PopulateDungeon`：
  - 最后一个房间放 `>`（下行楼梯）
  - 中间房间：60% 概率放 `"N"` 巢穴；40% 概率随机放置一只怪物

## 依赖关系

- 地形雕刻与设施写入：`MapModule.SetTerrain/SetFixture/IsWalkable`
- 玩家/怪物实例：`ActorTemplates` + `ActorModule`
- 巢穴注册：`NestModule`

## 评估关注点

- **seed 行为**：`seed == null` 时用 `Environment.TickCount` 初始化 RNG，并把 `state.RngSeed` 设为 `seed ?? rng.Next()`（可复现性依赖保存 seed）
- **状态复位**：生成会清空 Actor 与地图层，但不会显式清 `state.Floors`（由调用方决定是否 Reset）
- **NPC 设计**：地表有完整的 NPC 体系，地下城只有怪物