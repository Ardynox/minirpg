# `Core/Item.cs`

## 职责一句话

物品和商店货架数据结构：物品可交易、可持有、可装备，实现 ITagSource 接口参与 Actor 的 tag 聚合。

## `Item` 结构

```csharp
public class Item : ITagSource
{
    public string Id { get; set; }              // 唯一标识符
    public string Name { get; set; }            // 显示名称
    public int Price { get; set; }              // 售价/价值
    public bool Equipped { get; set; }          // 是否已装备
    public Dictionary<string, int> Tags { get; set; }  // 属性标签
}
```

### Item 实现的 ITagSource

```csharp
public Dictionary<string, int> GetTags() => Tags;
```

## `ShopSlot` 结构

```csharp
public class ShopSlot
{
    public Item Item { get; set; }    // 商品
    public int Stock { get; set; }    // 库存数量
}
```

## 物品示例（来自 ActorTemplates）

### 商人出售的物品

| 物品 ID | 名称 | 价格 | Tags |
|---------|------|------|------|
| `potion_hp` | 生命药水 | 10G | 治疗+5 |
| `potion_str` | 力量药水 | 15G | 力量+3 |
| `shield_iron` | 铁盾 | 30G | 防御+4, 格挡+2 |
| `sword_steel` | 钢剑 | 40G | 近战+5, 力量+3 |
| `torch` | 火把 | 5G | 视觉+2 |

## 物品分类

### 消耗品

- 含"治疗"tag 的物品可在背包中使用
- 使用后创建 Buff 临时代替物品效果

### 装备品

- 可装备/卸下
- 装备后通过 `ITagSource` 接口贡献 tag 给 Actor
- 影响 Actor 的战斗属性

### 一般物品

- 既不可使用也不可装备
- 可交易/丢弃
- 可能用于任务系统等

## 在 Actor 中的位置

```csharp
public class Actor
{
    // 玩家持有
    public List<Item> Inventory { get; set; } = [];
    
    // 商人持有
    public List<ShopSlot> ShopSlots { get; set; } = [];
}
```

## 在存档中的处理

`SaveModule` 深拷贝 Item：
```csharp
private static Item CopyItem(Item i) => new()
{
    Id = i.Id,
    Name = i.Name,
    Price = i.Price,
    Equipped = i.Equipped,
    Tags = new Dictionary<string, int>(i.Tags),
};
```

## 评估关注点

- **ITagSource 设计**：装备物品通过接口参与 tag 聚合，扩展性好
- **双用途**：Items 既用于玩家背包，也用于商人货架
- **无实例数据**：Item 目前是纯数据，没有耐久度等实例属性（相对于 Limb）
- **Tag 驱动**：物品效果完全由 Tags 决定，支持灵活的物品设计