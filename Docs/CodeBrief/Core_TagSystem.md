# `Core/TagSystem.cs`

## 职责一句话

定义 tag 体系与动作体系：tag 来源接口 + 具体来源类型 + 动作定义结构 + 动作可用性筛选；并提供 JSON 多态标注以支持存档反序列化。

## tag 系统

### `ITagSource`

- `GetTags() -> Dictionary<string,int>`：返回贡献的 tag 集合

### 具体 tag 来源（均实现 `ITagSource`）

- `Limb`：肢体（可挂载/移除）
- `Race`：种族（固定）
- `Profession`：职业（可变）
- `Buff`：Buff/Debuff（可计时到期）
- `Experience`：经历/成就（永久）

每个类型都有：

- `Id`
- `Name`
- `Tags: Dictionary<string,int>`
- `GetTags()` 返回 `Tags`

## 动作系统

### `ActionDef`

- `Id/Name`
- `Required: Dictionary<string,int>`：前置 tag 要求
- `EffectType: string`：效果类型（由外部系统/事件解释）
- `Power: int`：强度倍率

### `ActionQuery.GetAvailable(Actor, allActions)`

- 取 `actor.ComputeTags()`
- 过滤 `allActions`：要求所有 `Required` 的 tag 值都达到阈值

## JSON 多态支持（用于存档）

- `ITagSourcePolymorphic : ITagSource`
  - 用 `[JsonDerivedType]` 标注所有已知实现（`Limb/Race/Profession/Buff/Experience`）
  - 目的是让 `System.Text.Json` 知道接口字段/集合里真实类型，能正确反序列化

## 评估关注点

- **接口使用一致性**：当前 `Actor` 直接持有具体类型列表（`List<Limb>` 等），未直接用 `ITagSourcePolymorphic`；多态标注是否真正覆盖到存档路径取决于 `SaveModule` 的序列化对象图
- **动作解释缺位**：`EffectType/Power` 暂时只是数据；没有“执行动作”的模块（当前玩法只实现了移动与秒杀攻击）

