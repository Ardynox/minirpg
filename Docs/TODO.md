# MiniRPG 代码审查 TODO

> 基于代码审查生成，所有标注来源于 `// REVIEW:` 注释。
> 分为三个优先级：**立即修复**（当前就有 bug 或数据丢失风险）、**近期优化**（不影响正确性但影响可维护性和健壮性）、**远期规划**（功能不成熟/模块不完整/架构演进方向）。

---

## ~~一、立即修复~~ ✅ 已全部完成

### ~~1.1 SaveModule.CopyActor 未拷贝 BrainId~~ ✅

在 `CopyActor` 中补上 `BrainId = a.BrainId`。

### ~~1.2 GameState.Reset() 未重置设置字段~~ ✅

在 `Reset()` 中补上 `BumpAttack = true; WatchMode = false;`。

### ~~1.3 SaveModule.SaveGame 未保存设置字段~~ ✅

在 `FullSaveData` 中增加 `BumpAttack` / `WatchMode` 字段，`SaveGame` / `LoadGame` 中读写。

### ~~1.4 NestModule.FindSpawnSlot 使用 GetAt 可能漏判~~ ✅

改为 `ActorModule.GetAllAt(state, nx, ny).Count == 0`。

### ~~1.5 SaveModule.SnapshotToSaveData 的 Nests 未深拷贝~~ ✅

统一改为 `Nests = CopyNests(s.Nests)`。

---

## 二、近期优化（不影响正确性，但影响可维护性和健壮性）

### 2.1 Glyph 与 EntityId 混用做逻辑判断

| 项目 | 内容 |
|------|------|
| 文件 | `MapModule.IsWall()` / `Main.DoEnterStairs()` / `Main.FixtureLabel()` / `Main.CellLabel()` |
| 问题 | 格子栈模型下语义判断应基于 `EntityId`（如 `"wall"` / `"stair_down"`），但多处仍用 Glyph 字符 `"#"` / `">"` / `"<"` 做判断。如果 Emoji 模式修改了 Glyph，逻辑会失效。 |
| 建议 | `IsWall` 改为 `GetFirst(s, x, y, Terrain)?.EntityId == "wall"`；`DoEnterStairs` 改用 `HasFixture`；`FixtureLabel` 改为接受 EntityId 而非 Glyph。 |

### 2.2 魔术字符串缺乏编译期约束

| 项目 | 内容 |
|------|------|
| 文件 | 多处 |
| 问题 | `"hostile"` / `"wall"` / `"floor"` / `"stair_down"` / `"nest"` 等字符串散布在各模块中，拼写错误不会在编译期被捕获。 |
| 建议 | 引入常量类（如 `static class Factions { public const string Hostile = "hostile"; }`）或枚举。分两步：先定义常量，再逐步替换引用。 |

### 2.3 _mapDirty 是死代码

| 项目 | 内容 |
|------|------|
| 文件 | `Main.cs` → `_mapDirty` / `_Process()` |
| 问题 | `_mapDirty` 从未被赋值为 `true`，`_Process` 中的脏标记渲染分支永远不触发。所有渲染都通过直接调用 `FlushMap()` 完成。 |
| 建议 | 如果需要帧率节流渲染，应在状态变化处设 `_mapDirty = true` 并去掉直接 `FlushMap()` 调用；否则删除 `_mapDirty` 和 `_renderTimer` 相关代码。 |

### 2.4 ActionModule.CardinalDirs 未使用 + 方向数组重复定义

| 项目 | 内容 |
|------|------|
| 文件 | `Core/ActionModule.cs` |
| 问题 | 类级 `CardinalDirs` 从未使用。`GetInteractTargets` 内部重新定义了包含 `(0,0)` 的局部数组。`ActorModule.FindAdjacentHostile` 也有局部方向数组。 |
| 建议 | 抽取共享常量：`CardinalDirs`（四方向）和 `SurroundDirs`（四方向+脚下），各处统一引用。 |

### 2.5 PlayerStatus / GetPlayerStatus 疑似废弃

| 项目 | 内容 |
|------|------|
| 文件 | `Core/ActorModule.cs` |
| 问题 | `GetPlayerStatus()` 在整个代码库中从未被调用，`PlayerStatus` 类无引用方。 |
| 建议 | 确认是否为计划中的 API。如已废弃则删除；如需保留则在调用方接入。 |

### 2.6 MapModule.Remove 按 EntityId 匹配但不限制 Type

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapModule.cs` → `Remove()` |
| 问题 | 如果不同 Type 的实体碰巧有相同 `EntityId`（如不同层级都用 `"floor"`），可能误删非预期实体。 |
| 建议 | 增加 `CellEntityType type` 参数，或确保 `EntityId` 在同一格内唯一。 |

### 2.7 HasFixture 内部创建临时 List

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapModule.cs` → `HasFixture()` |
| 问题 | 调用 `GetByType` 创建 `List` 再 `.Any()`，每次查询产生不必要的堆分配。 |
| 建议 | 改为 `GetStack(s, x, y).Any(e => e.Type == type && e.EntityId == fixtureId)`。 |

### 2.8 OnCommand 命令路由存在重复路径

| 项目 | 内容 |
|------|------|
| 文件 | `Main.cs` → `OnCommand()` |
| 问题 | `":interact"` 和 `"interact"` 分别在两处 switch 中处理，`":render"` 和 `"render"` 同理。快捷键和文本命令走不同路径但效果相同，增加维护成本。 |
| 建议 | 统一命令格式或合并处理路径。 |

### 2.9 LoadFromStrings 假设每行等长

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapModule.cs` → `LoadFromStrings()` |
| 问题 | `MapWidth = rows[0].Length`，但不校验后续行是否等长。行长不一致时会导致越界崩溃。 |
| 建议 | 加校验（取所有行最大长度 + 短行补墙），或抛出异常。 |

### 2.10 EnsurePlayerActor 会丢失玩家状态

| 项目 | 内容 |
|------|------|
| 文件 | `Main.cs` → `EnsurePlayerActor()` |
| 问题 | 当玩家 Actor 缺失时，用 `ActorTemplates.Spawn` 创建全新实例，丢失了之前的肢体损伤、背包、Buff 等状态。 |
| 建议 | 楼层切换应确保 player Actor 在切换时从旧层 Actors 移出并保留引用，新层恢复后放入。`EnsurePlayerActor` 仅作为异常情况的防御性 fallback，加日志警告。 |

### 2.11 static 计数器不随新游戏归零

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapGenModule.cs` (`_monsterCounter`, `_npcCounter`) / `Core/NestModule.cs` (`_nestSpawnCounter`) |
| 问题 | 进程生命周期内递增不归零，多次新建游戏后 Id 持续增长（如 `"mon_47"`），不利于调试。 |
| 建议 | 在 `MapGenModule.Generate` 入口处重置为 0，或改用 `Guid.NewGuid()` / `state.Turn` 组合生成 Id。 |

---

## 三、远期规划（功能不成熟 / 模块不完整 / 架构演进方向）

### 3.1 文档与代码不一致：CellEntityType 缺少 Actor=50

| 项目 | 内容 |
|------|------|
| 文件 | `Core/CellEntity.cs` vs `Docs/MAP_REFACTOR.md` |
| 问题 | MAP_REFACTOR.md 第 64 行列出了 `Actor = 50`，但代码中枚举不包含。按当前设计 Actor 不存在栈中所以不需要，但文档应同步修正。 |
| 归类 | 文档维护。 |

### 3.2 CellEntity 考虑改为 record

| 项目 | 内容 |
|------|------|
| 文件 | `Core/CellEntity.cs` |
| 问题 | 当前是 `class`，深拷贝需手动逐字段复制。如果改为 `record` 可获得值语义的相等比较和更简洁的 `with` 拷贝。 |
| 归类 | 架构优化——需评估 JSON 序列化兼容性。 |

### 3.3 FloorData 与 GameState 的重复字段

| 项目 | 内容 |
|------|------|
| 文件 | `Core/GameState.cs` + `Core/SaveModule.cs` |
| 问题 | `FloorData` 的字段与 `GameState` 的当前楼层字段一一对应，每次新增字段要改三处（GameState、FloorData、SaveModule 拷贝）。 |
| 归类 | 架构优化——考虑让 `GameState` 持有 `FloorData CurrentFloorData` 引用以消除重复。 |

### 3.4 深拷贝策略优化

| 项目 | 内容 |
|------|------|
| 文件 | `Core/SaveModule.cs` |
| 问题 | 全手动深拷贝，新增字段时容易遗漏。可选方案：1) JSON 序列化/反序列化通用深拷贝，2) 每个数据类实现 `ICloneable`，3) 使用 `record` 的 `with`。 |
| 归类 | 架构优化——性能 vs 安全的权衡。 |

### 3.5 MapModule 楼层切换逻辑职责越界

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapModule.cs` → `GoDownFloor()` / `GoUpFloor()` |
| 问题 | MapModule 调用 SaveModule 形成同级依赖。按架构图两者是同级基础服务，楼层切换的编排逻辑应由上层（Main 或新建 FloorModule）协调。 |
| 归类 | 架构优化——需要重构调用链。 |

### 3.6 MapModule.IsHostile 职责越界

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapModule.cs` → `IsHostile()` |
| 问题 | Actor 查询放在 MapModule 中，违反了 Actor 操作归 ActorModule 的职责边界。 |
| 归类 | 架构优化——调用方可直接使用 `ActorModule.GetHostileAt`。 |

### 3.7 ActorModule.GetAllAt 的性能隐患

| 项目 | 内容 |
|------|------|
| 文件 | `Core/ActorModule.cs` → `GetAllAt()` |
| 问题 | 每次遍历整个 `Actors` 字典 O(n)，且创建新 `List`。Actor 数量多时有性能风险。 |
| 归类 | 性能优化——可维护坐标 → Actor 列表的空间索引。当前 Actor 数量少，暂不急。 |

### 3.8 NPC 驻守用 NestData 的 hack

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapGenModule.cs` → `PopulateSurface()` |
| 问题 | 用 `NestData`（怪物刷新点语义）管理 NPC 驻守。`SpawnInterval=1, MaxSpawned=1` 会导致 NPC 每回合都可能被 `NestModule.Tick` 重新刷出。 |
| 归类 | 新功能——需要为 NPC 设计独立的驻守/刷新机制。 |

### 3.9 PopulateDungeon 未利用 floor 参数

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapGenModule.cs` → `PopulateDungeon()` |
| 问题 | `floor` 参数传入但未使用，不会根据层数调整难度（怪物密度、巢穴数量、怪物模板）。 |
| 归类 | 新功能——实现深层递增难度。 |

### 3.10 RNG seed 无法完整重现地图

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapGenModule.cs` → `Generate()` |
| 问题 | `seed` 为 null 时，先用 `TickCount` 构造 `rng`，再用 `rng.Next()` 覆盖 `RngSeed`。这导致保存的 `RngSeed` 无法完整重现地图生成过程。 |
| 归类 | 未来如需 replay/确定性测试时修复。 |

### 3.11 房间连接算法可改进

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapGenModule.cs` → `ConnectRooms()` |
| 问题 | 链式连接 rooms[i-1]→rooms[i]，远距离房间之间走廊过长。 |
| 归类 | 新功能——可用最小生成树或 Delaunay 三角化获得更自然的布局。 |

### 3.12 SetFixture 强制单 Fixture

| 项目 | 内容 |
|------|------|
| 文件 | `Core/MapModule.cs` → `SetFixture()` |
| 问题 | 先 `RemoveByType` 再 `Push`，每格只保留一个 Fixture。但格子栈设计本允许多 Fixture 共存（如楼梯+陷阱）。 |
| 归类 | 新功能——未来需要多 Fixture 时重新设计此方法。 |

### 3.13 GameEvent.Type 使用字符串

| 项目 | 内容 |
|------|------|
| 文件 | `Main.cs` → `Dispatch()` |
| 问题 | 事件 Type 使用字符串匹配，无编译期完整性检查。新增事件类型时容易遗漏 case。 |
| 归类 | 架构优化——改为枚举或增加 default 分支日志警告。 |

### 3.14 Main.cs 类过大

| 项目 | 内容 |
|------|------|
| 文件 | `Main.cs` |
| 问题 | ~980 行，承担输入路由、事件分发、楼层切换、菜单管理、渲染等多个职责。 |
| 归类 | 架构优化——可将楼层切换、查看、交互等流程提取为独立的 Module 类。 |

### 3.15 WatchMode 的存档一致性

| 项目 | 内容 |
|------|------|
| 文件 | `Core/GameState.cs` + `Main.cs` |
| 问题 | `WatchMode` 是运行时 UI 状态，但放在 `GameState` 中会被序列化。加载存档后 `WatchMode` 可能为 true，但 UI 端的按钮文字和 `BrainId` 未同步。 |
| 归类 | 架构优化——考虑将 `WatchMode` 移出 `GameState`，放到 `Main` 的 UI 状态中。 |

### 3.16 FullSaveData.Floors 的 key 类型

| 项目 | 内容 |
|------|------|
| 文件 | `Core/SaveModule.cs` → `FullSaveData` |
| 问题 | JSON 对象 key 只能是 string，所以 `Floors` 用 `Dictionary<string, MapSaveData>`，解析时需要 `int.TryParse`。 |
| 归类 | 代码优雅性——可用 `List<MapSaveData>` + 索引，或自定义 `JsonConverter`。 |

---

## 分类统计

| 分类 | 数量 | 说明 |
|------|------|------|
| **立即修复** | 5 | 有 bug 或数据丢失风险，应尽快处理 |
| **近期优化** | 11 | 不影响正确性，但影响可维护性/健壮性 |
| **远期规划** | 16 | 功能不成熟/模块不完整/架构演进方向 |
