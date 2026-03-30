# `Core/Actor.cs`

## 职责一句话

统一的生物实体数据结构：**没有子类**；能力由“tag 来源”组合而成，并通过实时计算得到当前 tag 表。

## 核心字段

- 身份/表现：
  - `Id`
  - `X/Y`
  - `Glyph`（写入地图 `Objects` 层用）
  - `DisplayName`（日志/UI 用）
- 阵营：
  - `Faction`：字符串（默认 `"hostile"`；玩家模板会设为 `"friendly"`）
- tag 来源（组合）：
  - `Limbs: List<Limb>`
  - `Race?: Race`
  - `Profession?: Profession`
  - `Buffs: List<Buff>`
  - `Experiences: List<Experience>`

## tag 计算策略

- `Tags`（属性）：
  - 标记了 `[JsonIgnore]`，避免存档重复/过期值
  - 调用 `ComputeTags()` 实时汇总
- `ComputeTags()`：
  - 将所有来源的 `GetTags()` 逐项累加（同名 tag 值相加）
- `GetTag(key)`：
  - 即时计算后取值；不存在返回 0

## Buff 生命周期

- `TickBuffs()`：
  - 从后往前遍历
  - `RemainingTurns` 若为负则视为“无限/不计时”
  - 否则 -1；降到 0 则移除

## 依赖关系

- tag 来源类型定义在 `Core/TagSystem.cs`（`Limb/Race/Profession/Buff/Experience` 也在那里）

## 评估关注点

- **性能**：不缓存 tag（每次都现算），简单但在频繁查询时会产生重复计算；是否需要缓存/脏标记取决于后续规模
- **序列化**：`Actor` 内部包含多种 tag 来源对象；存档正确性依赖 JSON 多态配置（见 `TagSystem`）

