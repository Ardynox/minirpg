# `Core/Actor.cs`

## 职责一句话

统一的生物实体数据结构：**没有子类**；能力由"tag 来源"组合而成，并通过实时计算得到当前 tag 表。支持阵营、背包、商店、Buff 等完整功能。

## 核心字段

### 身份/表现

- `Id`：唯一标识符
- `X/Y`：当前坐标
- `Glyph`：写入地图 `Objects` 层用的字符（如 "P"、"G"、"S"）
- `DisplayName`：日志/UI 显示用名称（如 "你"、"哥布林"）

### 阵营

- `Faction`：字符串（默认 `"hostile"`；玩家和 NPC 模板会设为 `"friendly"`）
  - `"hostile"`：敌对
  - `"friendly"`：友方

### 经济/背包

- `Gold`：持有金币数量
- `Inventory: List<Item>`：玩家持有的物品列表
- `ShopSlots: List<ShopSlot>`：商人货架（普通生物此列表为空）

### tag 来源（组合）

- `Limbs: List<Limb>`：肢体列表（单独存，方便增删改查）
- `Race?: Race`：种族
- `Profession?: Profession`：职业
- `Buffs: List<Buff>`：Buff/Debuff 列表
- `Experiences: List<Experience>`：经历/成就列表

## tag 计算策略

- `Tags`（属性，`[JsonIgnore]`）：
  - 标记了 `JsonIgnore`，避免存档重复/过期值
  - 调用 `ComputeTags()` 实时汇总
- `ComputeTags()`：
  - 将所有来源的 `GetTags()` 逐项累加（同名 tag 值相加）
  - 来源顺序：Limbs → Race → Profession → Buffs → Experiences → 装备物品
- `GetTag(key)`：
  - 即时计算后取值；不存在返回 0

## 肢体操作

- `AttachLimb(Limb limb)`：添加肢体（同时注册到 TagSources）
- `DetachLimb(Limb limb)`：移除指定肢体
- `DetachLimb(string limbId)`：按 ID 移除肢体

## Buff 生命周期

- `AddBuff(Buff buff)`：添加 Buff
- `RemoveBuff(string buffId)`：按 ID 移除 Buff
- `TickBuffs()`：
  - 从后往前遍历
  - `RemainingTurns` 若为负则视为"无限/不计时"
  - 否则 -1；降到 0 则移除

## 评估关注点

- **性能**：不缓存 tag（每次都现算），简单但在频繁查询时会产生重复计算；是否需要缓存/脏标记取决于后续规模
- **序列化**：`Actor` 内部包含多种 tag 来源对象；存档正确性依赖 JSON 多态配置（见 `TagSystem`）
- **商店功能**：非商人角色也有 `ShopSlots` 字段（空列表），设计一致但需注意检查