# `Core/TradeModule.cs`

## 职责一句话

交易系统：纯函数处理玩家与商人之间的买/卖操作，直接操作 Actor 的 Gold 和 Inventory/ShopSlots。

## `TradeResult` 结构

```csharp
public class TradeResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public Item? Item { get; set; }
    public int Price { get; set; }
}
```

## 主要 API

### `Buy(Actor buyer, Actor merchant, int slotIndex) -> TradeResult`

玩家从商人购买：
1. 检查槽位有效性
2. 检查库存（`Stock > 0`）
3. 检查金币是否足够
4. 扣除买家金币，增加商人金币
5. 减少商人库存
6. 创建物品副本并加入买家背包
7. 返回成功结果

### `Sell(Actor seller, Actor merchant, int inventoryIndex) -> TradeResult`

玩家向商人出售：
1. 检查背包下标有效性
2. 检查物品是否未装备
3. 计算售价（原价的 50%，向下取整）
4. 检查商人金币是否足够
5. 增加卖家金币，减少商人金币
6. 从卖家背包移除物品
7. 如果商人已有同款物品，增加其库存；否则新增货架槽
8. 返回成功结果

### `ListGoods(Actor merchant) -> List<(int Index, ShopSlot Slot)>`

列出商人所有可购买的货物（库存 > 0），返回下标-货架对列表。

## 交易规则

### 购买

- 玩家必须支付物品标价
- 商人库存减少
- 物品被复制到玩家背包

### 出售

- 售价 = 原价 ÷ 2（向下取整）
- 物品必须未装备
- 如果商人金币不足，无法出售
- 商人可能已有限定款，增加库存；否则新建货架

## 使用流程

```
Main.ShowTradeGoods() → 玩家选择 [N] 购买
                            ↓
                    TradeModule.Buy()
                            ↓
                    复制物品到背包
                            ↓
                    再次显示交易界面

或：玩家选择 [N] 出售
                            ↓
                    Main.ShowSellMenu()
                            ↓
                    玩家选择物品
                            ↓
                    TradeModule.Sell()
                            ↓
                    更新双方金币
                            ↓
                    再次显示出售界面
```

## 依赖关系

- `InventoryModule`：购买后调用 `InventoryModule.Add`
- `Actor`：直接操作 `buyer.Gold`、`merchant.Gold`、背包和货架

## 评估关注点

- **金币验证**：买和卖都检查金币是否足够
- **库存管理**：商人货架有库存限制，卖出会影响商人库存
- **物品复制**：购买时创建新 Item 实例，避免共享引用问题
- **售价公式**：固定 50% 折扣，可扩展为按物品类型差异化定价