# `Core/InventoryModule.cs`

## 职责一句话

背包系统：纯函数操作 Actor 的物品列表，支持添加、移除、装备/卸下、使用消耗品、丢弃等操作。

## `InventoryResult` 结构

```csharp
public class InventoryResult
{
    public bool Ok { get; set; }           // 操作是否成功
    public string Message { get; set; }    // 结果描述（如 "装备了 铁剑"）
}
```

## 主要 API

### `Add(Actor, Item)`

将物品直接添加到 Actor 的背包列表末尾。

### `RemoveAt(Actor, index) -> Item?`

根据下标移除物品：
- 无效下标返回 null
- 有效下标返回被移除的 Item

### `ToggleEquip(Actor, index) -> InventoryResult`

切换物品的装备状态：
- 检查下标有效性
- 反转 `item.Equipped` 布尔值
- 返回操作结果和描述

### `Use(Actor, index) -> InventoryResult`

使用消耗品：
- 检查下标有效性
- 检查物品是否含"治疗"tag（只有治疗物品可使用）
- 创建 Buff（剩余 3 回合）并添加到 Actor
- Buff 的 Tags 继承物品的 Tags（如治疗 5）
- 从背包移除物品
- 返回结果

### `Drop(Actor, index) -> InventoryResult`

丢弃物品：
- 检查下标有效性
- 从背包移除
- 返回结果

### `List(Actor) -> List<(int Index, Item Item)>`

列出背包所有物品，返回下标-物品对列表。

## 物品使用流程

```
ShowInventory() → 玩家选择物品 → ShowItemActions()
                                        ↓
                                   选择 [2] 使用
                                        ↓
                              检查 "治疗" tag
                                        ↓
                          创建 Buff → 添加到 Actor
                                        ↓
                           从背包移除物品
                                        ↓
                              返回结果日志
```

## 装备物品效果

装备物品实现 `ITagSource` 接口：
- 装备后物品的 `GetTags()` 会参与 Actor 的 tag 聚合
- 卸下后不再贡献 tag
- 物品的 Tags 影响 Actor 的属性（如防御、力量、视觉等）

## 依赖关系

- `Actor`：直接操作 `Actor.Inventory` 列表
- `Buff`：使用物品时创建 Buff

## 评估关注点

- **纯函数设计**：所有方法都无副作用，只操作传入的 Actor 参数
- **使用限制**：目前只支持"治疗"类消耗品，其他消耗类型需扩展
- **Buff 机制**：使用物品转化的 Buff 使用 `RemainingTurns = 3`，临时代替物品效果
- **装备状态**：装备物品参与 tag 计算，需要在存档中正确保存 Equipped 状态