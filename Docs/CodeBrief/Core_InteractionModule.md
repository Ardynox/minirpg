# `Core/InteractionModule.cs`

## 职责一句话

交互系统：检测玩家周围的可交互目标，根据双方 tag 过滤可用交互，并执行交互产生事件。

## 主要 API

### `GetAvailableTargets(GameState) -> List<Actor>`

扫描玩家周围的可交互目标：
- 检查玩家相邻四格（上下左右）
- 加上玩家当前格子（脚下）
- 排除玩家自己
- 返回所有非玩家 Actor

### `GetInteractions(Actor initiator, Actor target, IReadOnlyList<InteractionDef> allDefs) -> List<InteractionDef>`

根据双方 tag 过滤可用交互：
1. 计算发起者的 tag 表
2. 计算目标的 tag 表
3. 遍历所有交互定义
4. 检查发起者是否满足 `Required` 条件
5. 检查目标是否满足 `TargetRequired` 条件（含特殊条件解析）
6. 返回同时满足条件的交互列表

### `Execute(GameState, initiator, target, def) -> List<GameEvent>`

执行交互并产出事件：
1. 创建 `interaction` 事件
2. 填充 `InteractionDefId`、`InteractionName`、`EffectType`
3. 根据 `EffectType` 处理副作用：
   - `tame`：将目标阵营改为 `friendly`，给发起者添加驯服经验
   - `combat`：空（战斗由 Main 处理）
4. 返回事件列表

## 条件检查（`CheckTags`）

处理三种条件类型：

### 普通 tag

```csharp
if (tags.GetValueOrDefault(key, 0) < val) return false;
```

### 阵营条件

```csharp
if (key.StartsWith("@faction:"))
{
    var requiredFaction = key["@faction:".Length..];
    if (faction != requiredFaction) return false;
}
```

### 上限条件

```csharp
if (key.StartsWith("@max:"))
{
    var parts = key["@max:".Length..].Split(':');
    if (parts.Length != 2 || !int.TryParse(parts[1], out var maxVal)) return false;
    if (tags.GetValueOrDefault(parts[0], 0) > maxVal) return false;
}
```

## 使用流程

```
DoInteract()  →  GetAvailableTargets()  →  ShowInteractionsFor()
                                              ↓
                                         GetInteractions()
                                              ↓
                                    用户选择交互类型
                                              ↓
                                    Execute() → 事件
                                              ↓
                                    Main.DispatchInteraction()
```

## 依赖关系

- `ActorModule`：获取玩家和目标
- `GameState`：获取玩家坐标
- `GameEvent`：产出交互事件

## 评估关注点

- **扫描范围**：当前只扫描 5 格（中心+四邻），适合近距离交互
- **条件解析**：特殊条件语法需要严格匹配，否则解析失败返回 false
- **副作用分离**：Execute 只产出事件，实际效果（如交易、驯服）由 Main 处理