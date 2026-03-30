# `Core/ActorTemplate.cs`（文件名）/ `ActorTemplates`（类型）

## 职责一句话

Actor 模板注册表：用“模板 id → 工厂函数”方式创建玩家与怪物实例；新增类型只需注册新模板。

## 关键结构

- `Registry: Dictionary<string, Func<string, Actor>>`
  - key：模板 id（如 `"player"`, `"goblin"`）
  - value：工厂函数，输入实例 id，返回 `Actor`

## 初始化内容（当前内置模板）

在静态构造函数中注册：

- 玩家 `player`
  - glyph `"P"`，显示名“你”，阵营 `"friendly"`
  - `Race(human)`：生命/力量/防御
  - `Limbs`：右臂/左臂/腿/眼（贡献近战、格挡、移动、视觉等 tag）
- 怪物
  - `goblin` glyph `"G"`
  - `slime`  glyph `"S"`
  - `skeleton` glyph `"K"`

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

