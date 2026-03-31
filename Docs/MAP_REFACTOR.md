# 地图重构：四层固定结构 → 格子栈模型

> 本文档描述地图底层数据结构的重构方案。
> 实现前必须先读完本文档 + `ARCHITECTURE.md` + 下方列出的所有受影响文件。

---

## 一、必读文件

| 文件 | 为什么要读 |
|------|-----------|
| `ARCHITECTURE.md` | 核心约束：状态/副作用分离、纯函数、事件驱动 |
| `Core/GameState.cs` | **要改**：替换四层数组为新结构 |
| `Core/MapModule.cs` | **要改**：所有读写方法重写 |
| `Core/ActorModule.cs` | **要改**：去掉 Objects 层同步，Actor 位置只存 Actor.X/Y |
| `Core/MapGenModule.cs` | **要改**：生成逻辑改用新 API |
| `Core/NestModule.cs` | **要改**：刷怪逻辑改用新 API |
| `Core/SaveModule.cs` | **要改**：序列化/反序列化适配新结构 |
| `Core/AI/PerceptionBuilder.cs` | **要改**：感知构造改用新 API |
| `Core/ActionModule.cs` | 检查是否需要适配 |
| `Module/RenderModule.cs` | **要改**：渲染逻辑改用新 API |
| `Main.cs` | **要改**：所有直接操作地图层的代码 |

---

## 二、现状问题

```csharp
// 现在 GameState 里的四层：
List<List<string>> Terrain;   // "#" / "."
List<List<string>> Fixtures;  // "N" / ">" / "<" / "I" / "H" / "D" / ""
List<List<string>> Objects;   // "P" / "M" / ""（Actor Glyph 的冗余副本）
List<List<Dictionary<string, string>?>> Meta;
```

问题：
1. **每层只能放一个东西** — 楼梯上不能有掉落物，掉落物上不能站人
2. **Objects 层是冗余数据** — Actor 的位置已经存在 `Actor.X/Y` 上，Objects 层只是 Glyph 的副本，需要手动 `RefreshObjectsCell()` 同步
3. **语义丢失** — 格子里存的是显示字符 `"N"`，不是实体引用，无法知道这个 N 巢穴的具体数据
4. **扩展困难** — 想加陷阱、尸体、血迹、烟雾，就得加新层

---

## 三、新模型：格子栈（Cell Stack）

### 3.1 核心思想

每个格子是一个**有序栈**，可以堆叠任意数量的实体。渲染时显示栈顶（优先级最高的）。

### 3.2 实体类型

```csharp
/// <summary>
/// 格子上的实体类型，按渲染优先级从低到高排列。
/// 数值越大，渲染优先级越高（显示在最上面）。
/// </summary>
public enum CellEntityType
{
    Terrain  = 0,   // 地形：墙、地面、水、岩浆
    Fixture  = 10,  // 设施：楼梯、巢穴、门、房屋
    Item     = 20,  // 掉落物：地上的物品
    Hazard   = 30,  // 危险物：陷阱、毒雾、火焰
    Corpse   = 40,  // 尸体/残骸
    Actor    = 50,  // 生物：玩家、怪物、NPC
    Effect   = 60,  // 视觉效果：爆炸、魔法光环（临时）
}
```

### 3.3 格子实体

```csharp
/// <summary>
/// 格子上的一个实体。轻量数据，可序列化。
/// 有些实体是"内联"的（地形、简单设施），有些是"引用"的（Actor 引用 ID）。
/// </summary>
public class CellEntity
{
    public CellEntityType Type { get; set; }

    /// <summary>显示用的字符/Emoji。</summary>
    public string Glyph { get; set; } = "";

    /// <summary>
    /// 实体标识。
    /// - Terrain: "wall" / "floor"
    /// - Fixture: "stair_down" / "stair_up" / "nest" / "door" / "house"
    /// - Item: 物品实例 ID（引用 GameState 中的掉落物表）
    /// - Actor: Actor ID（引用 GameState.Actors）
    /// - Hazard/Effect: 效果 ID
    /// </summary>
    public string EntityId { get; set; } = "";

    /// <summary>可选的附加数据。</summary>
    public Dictionary<string, string>? Meta { get; set; }
}
```

### 3.4 GameState 新结构

```csharp
public class GameState
{
    // ── 地图 ──
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }

    /// <summary>
    /// 地图格子：每格一个栈（列表），按 CellEntityType 排序。
    /// Cells[y][x] = List<CellEntity>，从底到顶排列。
    /// </summary>
    public List<List<List<CellEntity>>> Cells { get; set; } = [];

    // ── 删除以下字段 ──
    // List<List<string>> Terrain;    ← 删
    // List<List<string>> Fixtures;   ← 删
    // List<List<string>> Objects;    ← 删
    // List<List<Dictionary<string, string>?>> Meta;  ← 删

    // ── 其他字段不变 ──
    public Dictionary<string, Actor> Actors { get; set; } = new();
    // ...
}
```

### 3.5 FloorData 同步修改

```csharp
public class FloorData
{
    public int Width { get; set; }
    public int Height { get; set; }
    public List<List<List<CellEntity>>> Cells { get; set; } = [];
    // 删除 Terrain/Fixtures/Objects/Meta
    // 其余不变
}
```

---

## 四、MapModule 新 API

### 4.1 基础读写

```csharp
public static class MapModule
{
    // ── 读取 ──
    /// <summary>获取格子的完整栈。</summary>
    static List<CellEntity> GetStack(GameState s, int x, int y);

    /// <summary>获取格子中指定类型的所有实体。</summary>
    static List<CellEntity> GetByType(GameState s, int x, int y, CellEntityType type);

    /// <summary>获取格子中指定类型的第一个实体（最常见用法）。</summary>
    static CellEntity? GetFirst(GameState s, int x, int y, CellEntityType type);

    /// <summary>获取渲染用字符：栈顶（优先级最高的）实体的 Glyph。</summary>
    static string GetDisplayCell(GameState s, int x, int y);

    // ── 写入 ──
    /// <summary>往格子上压入一个实体（自动按 Type 排序插入）。</summary>
    static void Push(GameState s, int x, int y, CellEntity entity);

    /// <summary>从格子上移除指定 EntityId 的实体。</summary>
    static bool Remove(GameState s, int x, int y, string entityId);

    /// <summary>从格子上移除指定类型的所有实体。</summary>
    static int RemoveByType(GameState s, int x, int y, CellEntityType type);

    // ── 查询（兼容旧逻辑的便捷方法） ──
    static bool IsWall(GameState s, int x, int y);
    // → GetFirst(s, x, y, Terrain)?.EntityId == "wall"

    static bool IsWalkable(GameState s, int x, int y);
    // → !IsWall(s, x, y)

    static bool HasFixture(GameState s, int x, int y, string fixtureId);
    // → GetByType(s, x, y, Fixture).Any(e => e.EntityId == fixtureId)

    static bool IsDownStair(GameState s, int x, int y);
    // → HasFixture(s, x, y, "stair_down")

    static bool IsUpStair(GameState s, int x, int y);
    // → HasFixture(s, x, y, "stair_up")
}
```

### 4.2 Actor 不再同步到地图层

```
旧流程：
  ActorModule.MoveActor() → 改 Actor.X/Y → RefreshObjectsCell() → 改 Objects[y][x]
  渲染时：读 Objects[y][x]

新流程：
  ActorModule.MoveActor() → 改 Actor.X/Y（完毕）
  渲染时：GetDisplayCell() 自动查 Actors 表找该位置的 Actor
```

**关键变化**：`GetDisplayCell()` 不再只读栈，还要查 `state.Actors` 里坐标匹配的 Actor。
或者，Actor 移动时在格子栈上压入/移除 Actor 类型的 CellEntity（选哪种都行，但要统一）。

**推荐方案**：Actor 的 CellEntity 条目**不存在栈里**，而是渲染时动态查询。理由：
- Actor 移动频繁，每次改栈太重
- Actor 已经有 X/Y 坐标，再存栈里是冗余
- `state.Actors` 已经是 Actor 的权威数据源

所以 `GetDisplayCell()` 的逻辑是：
```
1. 查 state.Actors 里是否有 Actor 在 (x,y) → 有则返回其 Glyph（优先级最高）
2. 否则返回 Cells[y][x] 栈顶的 Glyph
```

---

## 五、受影响模块的迁移清单

### 5.1 GameState.cs
- 删除 `Terrain`, `Fixtures`, `Objects`, `Meta` 四个字段
- 新增 `Cells` 字段
- `Reset()` 方法适配
- `FloorData` 同步修改

### 5.2 MapModule.cs — 全部重写
- 删除所有 `GetTerrain/GetFixture/GetObject/GetMeta/SetTerrain/SetFixture/SetObject/SetMeta`
- 新增 `GetStack/GetFirst/GetByType/Push/Remove/RemoveByType`
- 保留便捷方法 `IsWall/IsWalkable/IsDownStair/IsUpStair`，内部实现改为查栈
- `GetDisplayCell()` 改为：查 Actor → 查栈顶
- `LoadFromStrings()` 改为：解析字符 → 创建 CellEntity → Push 到栈

### 5.3 ActorModule.cs
- **删除** `RefreshObjectsCell()` — 不再需要同步 Objects 层
- `MoveActor()` 简化为只改 Actor.X/Y
- `Add/Remove` 不再操作 Objects 层

### 5.4 MapGenModule.cs
- 所有 `SetTerrain/SetFixture/SetObject` 调用改为 `MapModule.Push()`
- 生成墙：`Push(s, x, y, new CellEntity { Type = Terrain, Glyph = "#", EntityId = "wall" })`
- 生成楼梯：`Push(s, x, y, new CellEntity { Type = Fixture, Glyph = ">", EntityId = "stair_down" })`

### 5.5 NestModule.cs
- `GetFixture(s, x, y) == "N"` → `MapModule.HasFixture(s, x, y, "nest")`
- 刷怪后不再操作 Objects 层

### 5.6 SaveModule.cs
- 序列化/反序列化适配新的 Cells 结构
- FloorData 缓存适配

### 5.7 PerceptionBuilder.cs
- `GetTerrain/GetFixture` → `MapModule.GetFirst()` 或 `MapModule.HasFixture()`

### 5.8 RenderModule.cs / Main.cs 渲染部分
- `GetDisplayCell()` 签名不变，内部已适配，这层**不需要改**

---

## 六、迁移策略

**建议分两步，每步可独立编译运行：**

### 第一步：替换数据结构（保持行为不变）
1. 在 GameState 中新增 `Cells` 字段
2. 重写 MapModule 的所有方法，底层改为操作 `Cells`
3. 保留旧的便捷方法签名（IsWall、IsDownStair 等），只改内部实现
4. 修改 ActorModule 删除 `RefreshObjectsCell()`
5. 修改 MapGenModule / NestModule / SaveModule
6. **确保游戏行为和重构前完全一致**

### 第二步：利用新结构加新功能
1. 物品掉落到地上（CellEntityType.Item）
2. 尸体残留（CellEntityType.Corpse）
3. 陷阱/危险区域（CellEntityType.Hazard）
4. AI 可以感知格子上的所有实体（不只是最顶层的）

---

## 七、验收标准

第一步完成后：
1. 游戏启动正常，地图正常渲染
2. 玩家移动、战斗、交互、上下楼梯全部正常
3. 怪物 AI 正常追击、攻击、游荡
4. 巢穴正常刷怪
5. 存档/读档正常
6. 一个格子上可以同时存在地形 + 设施 + 掉落物（数据层面，不需要新 UI）
