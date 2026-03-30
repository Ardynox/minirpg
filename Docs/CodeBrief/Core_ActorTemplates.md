# `Core/ActorTemplate.cs`（文件名）/ `ActorTemplates`（类型）

## 职责一句话

Actor 模板注册表：用"模板 id → 工厂函数"方式创建玩家、怪物、NPC 实例；新增类型只需注册新模板。

## 关键结构

- `Registry: Dictionary<string, Func<string, Actor>>`
  - key：模板 id（如 `"player"`, `"goblin"`, `"merchant"`）
  - value：工厂函数，输入实例 id，返回 `Actor`

## 初始化内容（当前内置模板）

### 玩家 `player`

- glyph `"P"`，显示名"你"，阵营 `"friendly"`
- 金币初始 50
- `Race(human)`：力量 5/防御 2
- `Limbs`：躯干(要害+防御)/右臂(近战+力量)/左臂(近战+格挡)/双腿(移动+速度)/双眼(视觉)

### 怪物

- `goblin`（哥布林）：glyph `"G"`，种族 goblin（力量 2/速度 3），肢体含夜眼
- `slime`（史莱姆）：glyph `"S"`，种族 slime（防御 3/毒性 2），弹性体+核心
- `skeleton`（骷髅）：glyph `"K"`，种族 undead（力量 4/防御 1/亡灵），骨臂骨腿

### NPC

- `merchant`（流浪商人）：glyph `"T"`，显示名"流浪商人"，阵营 `"friendly"`，金币 200
  - `Race(human)`：生命 15/力量 2/防御 1
  - `Profession(merchant)`：交易 3/视觉 2
  - `ShopSlots`：生命药水/力量药水/铁盾/钢剑/火把

- `elder`（村长）：glyph `"E"`，显示名"村长"，阵营 `"friendly"`
  - `Race(human)`：力量 1/防御 1
  - `Profession(elder)`：智慧 3/视觉 1

- `villager`（村民）：glyph `"V"`，显示名"村民"，阵营 `"friendly"`
  - `Race(human)`：力量 2

## 主要 API

- `Register(templateId, factory)`
- `Spawn(templateId, instanceId) -> Actor`
  - 未注册会抛 `ArgumentException`
- `MonsterIds`：
  - 当前为 `["goblin","slime","skeleton"]`
  - 用于 `MapGenModule` 与 `NestModule` 的随机刷怪池

## 评估关注点

- **数据驱动程度**：模板与 tag 目前写死在代码里；后续可以迁移到 JSON/资源表
- **tag 系统耦合**：模板直接构造 `Race/Limb` 并填 tag 字典，依赖 `TagSystem` 的类型定义
- **NPC 设计**：商人是唯一有 ShopSlots 的模板，其他 NPC 暂无特殊功能