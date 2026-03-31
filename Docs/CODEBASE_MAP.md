# MiniRPG 代码全景图

> 基于当前代码实际状态生成，反映重构后的格子栈模型。

---

## 一、目录结构

```
mini-rpg/
├── Core/                    纯逻辑层（无 Godot 依赖）
│   ├── AI/                  AI 子系统
│   │   ├── IBrainModule.cs    接口 + 数据类（Perception / Decision / SimDetail）
│   │   ├── AIDispatcher.cs    调度器：分级模拟 + 决策执行
│   │   ├── SimpleBrain.cs     MVP 内核：chase → attack → wander
│   │   └── PerceptionBuilder.cs 从 GameState 构造感知快照
│   │
│   ├── GameState.cs         唯一数据源（格子栈 + Actor 表 + 楼层缓存）
│   ├── CellEntity.cs        格子实体类型定义
│   ├── Actor.cs             生物实体（Tag/Capacity/Limb/Buff）
│   ├── TagSystem.cs         ITagSource 接口 + Limb/Race/Profession/Buff/Experience + ActionDef
│   ├── Item.cs              物品 + 商店槽位
│   ├── CapacityDef.cs       能力定义（要害、乘数关系）
│   ├── GameEvent.cs         事件数据类
│   ├── InteractionDef.cs    交互定义数据类
│   │
│   ├── MapModule.cs         地图读写（格子栈 API）
│   ├── ActorModule.cs       Actor 增删查改
│   ├── ActionModule.cs      通用行动执行（移动/攻击/交互）
│   ├── CombatModule.cs      伤害计算 + 肢体破坏 + 死亡判定
│   ├── InteractionModule.cs 交互匹配 + 效果执行
│   ├── InventoryModule.cs   背包增删 + 装备 + 使用
│   ├── TradeModule.cs       买卖逻辑
│   ├── NestModule.cs        巢穴刷怪
│   ├── TurnModule.cs        回合推进（串联 Nest + AI）
│   ├── MapGenModule.cs      程序化地图生成
│   ├── SaveModule.cs        存档/读档 + 楼层缓存
│   ├── PresetDB.cs          JSON 预设数据加载
│   ├── ActorTemplate.cs     Actor 模板生成
│   ├── ActionDefs.cs        动作定义集合（委托 PresetDB）
│   └── InteractionDefs.cs   交互定义集合（委托 PresetDB）
│
├── Module/                  UI/副作用层（依赖 Godot）
│   ├── IGameUI.cs           UI 契约接口
│   ├── InputModule.cs       键盘输入 + 模式管理
│   ├── RenderModule.cs      地图渲染（ASCII / Emoji）
│   ├── StatusModule.cs      状态面板构建
│   ├── CombatUIModule.cs    战斗流程 UI
│   ├── TradeUIModule.cs     交易流程 UI
│   └── InventoryUIModule.cs 背包流程 UI
│
├── Data/                    JSON 预设
│   ├── actors.json          生物模板
│   ├── races.json           种族定义
│   ├── professions.json     职业定义
│   ├── limbs.json           肢体模板
│   ├── items.json           物品定义
│   ├── actions.json         动作定义
│   ├── capacities.json      能力定义
│   └── interactions.json    交互定义
│
├── Main.cs                  入口：输入路由 + 事件分发 + 菜单 + 渲染
├── Main.tscn                Godot 场景
└── project.godot            项目配置
```

---

## 二、模块依赖图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          Main.cs (入口)                                 │
│  实现 IGameUI │ 输入路由 │ 事件分发 │ 菜单系统 │ 渲染调度 │ 看海模式    │
└──────┬────────┬─────────┬──────────┬───────────┬───────────────────────┘
       │        │         │          │           │
       ▼        ▼         ▼          ▼           ▼
  ┌────────┐┌────────┐┌────────┐┌────────┐┌──────────┐
  │InputMod││Render  ││Combat  ││Trade   ││Inventory │  ← Module/ 层
  │ule     ││Module  ││UIModule││UIModule││UIModule  │
  └────────┘└────────┘└───┬────┘└───┬────┘└────┬─────┘
                          │         │          │
          ┌───────────────┴─────────┴──────────┘
          │ 所有 UI 模块通过 IGameUI 接口
          │ 访问 State / AddLog / EnterSelection / Dispatch
          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         Core/ 层（纯逻辑）                               │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────┐           │
│  │                  TurnModule（回合驱动器）                   │           │
│  │  Tick() ──┬──→ NestModule.Tick()    巢穴刷怪              │           │
│  │           └──→ AIDispatcher.TickAll() AI 决策             │           │
│  └──────────────────────────────────────────────────────────┘           │
│                              │                                          │
│            ┌─────────────────┼──────────────────┐                       │
│            ▼                 ▼                  ▼                       │
│  ┌──────────────┐  ┌─────────────────┐  ┌──────────────┐              │
│  │  NestModule  │  │  AI/            │  │ ActionModule │              │
│  │  巢穴刷怪     │  │  AIDispatcher   │  │ 通用行动执行  │              │
│  │              │  │  SimpleBrain    │  │ TryMove      │              │
│  │              │  │  Perception     │  │ TryAttack    │              │
│  │              │  │  Builder        │  │ TryInteract  │              │
│  └──────┬───────┘  └────────┬────────┘  └──────┬───────┘              │
│         │                   │                   │                       │
│         ▼                   ▼                   ▼                       │
│  ┌─────────────────────────────────────────────────────────┐           │
│  │                    基础服务层                              │           │
│  │                                                          │           │
│  │  MapModule        地图格子栈 CRUD + 查询                   │           │
│  │  ActorModule      Actor 增删查改                          │           │
│  │  CombatModule     伤害计算 + 肢体破坏 + 死亡              │           │
│  │  InteractionModule 交互匹配 + 效果执行                    │           │
│  │  InventoryModule  背包增删 + 装备 + 使用                  │           │
│  │  TradeModule      买卖逻辑                                │           │
│  │  SaveModule       序列化 + 楼层缓存                       │           │
│  │  MapGenModule     程序化生成                              │           │
│  └─────────────────────────┬───────────────────────────────┘           │
│                            │                                            │
│                            ▼                                            │
│  ┌─────────────────────────────────────────────────────────┐           │
│  │                    数据层                                 │           │
│  │                                                          │           │
│  │  GameState    唯一数据源（Cells + Actors + Floors）        │           │
│  │  Actor        生物实体                                    │           │
│  │  CellEntity   格子实体                                    │           │
│  │  GameEvent    事件数据                                    │           │
│  │  TagSystem    ITagSource + Limb/Race/Profession/Buff     │           │
│  │  Item         物品                                       │           │
│  │  PresetDB     JSON → 内存预设                             │           │
│  └─────────────────────────────────────────────────────────┘           │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 三、接口与继承关系

```
ITagSource                          ← 核心标签接口
├── Limb      : ITagSource          肢体（贡献 tag + 能力权重）
├── Race      : ITagSource          种族（固定 tag）
├── Profession: ITagSource          职业（可转职）
├── Buff      : ITagSource          增益/减益（有持续回合）
├── Experience: ITagSource          经历/成就（永久 tag）
└── Item      : ITagSource          物品（装备后贡献 tag）

ITagSourcePolymorphic : ITagSource  ← JSON 多态序列化标记
├── [JsonDerivedType] Limb
├── [JsonDerivedType] Race
├── [JsonDerivedType] Profession
├── [JsonDerivedType] Buff
└── [JsonDerivedType] Experience

IBrainModule                        ← AI 大脑接口
└── SimpleBrain : IBrainModule      MVP 内核（chase/attack/wander）
    （未来：BehaviorTreeBrain、GOAPBrain、ExternalBrain...）

IGameUI                             ← UI 契约接口
└── Main : Node, IGameUI            Godot 入口节点
```

---

## 四、数据流：一个完整回合

```
玩家按 W
  │
  ▼
InputModule.HandleKeyInput()
  │ 发出命令 "w"
  ▼
Main.OnCommand("w")
  │
  ▼
Main.DoMove(0, -1)
  │
  ├──→ ActionModule.TryMove(state, player, 0, -1)
  │      │
  │      ├─ 撞墙？ → GameEvent("hit_wall")
  │      ├─ 撞敌人 + BumpAttack？ → CombatModule.Attack() → 战斗事件列表
  │      └─ 空地？ → ActorModule.MoveActor() → GameEvent("actor_moved")
  │
  ├──→ TurnModule.Tick(state)
  │      │
  │      ├─ state.Turn++
  │      │
  │      ├─ NestModule.Tick()
  │      │    └─ 遍历巢穴 → 到期刷怪 → GameEvent("monster_spawned")
  │      │
  │      └─ AIDispatcher.TickAll(state, playerX, playerY, viewRange)
  │           │
  │           └─ 对每个有 BrainId 的 Actor：
  │                ├─ Classify → Full / Simplified / Summary
  │                ├─ PerceptionBuilder.Build() → Perception
  │                ├─ SimpleBrain.Decide(perception, rng) → Decision
  │                └─ ExecuteDecision()
  │                     ├─ Attack → ActionModule.TryAttack()
  │                     ├─ MoveTo/Wander/Flee → ActionModule.TryMove()
  │                     └─ Idle → 无操作
  │
  ▼
Main.Dispatch(events)
  │
  ├─ "hit_wall"            → AddLog("撞墙了")
  ├─ "actor_moved"         → （静默）
  ├─ "monster_spawned"     → AddLog("怪物出现了")
  ├─ "combat_bump"         → CombatUIModule.HandleCombatBump()
  ├─ "combat_attack"       → AddLog(伤害详情)
  ├─ "limb_destroyed"      → AddLog(肢体破坏)
  ├─ "actor_killed"        → 掉金 + 移除 Actor
  ├─ "actor_incapacitated" → AddLog(失去行动能力)
  └─ "interaction"         → 分派到对话/交易/战斗/驯服
  │
  ▼
Main.FlushMap() + RefreshStatus()
  │
  ├─ BuildDisplayMap() → 21×11 视窗
  │    └─ MapModule.GetDisplayCell() → 查 Actors 表 → 查栈顶 Glyph
  │
  ├─ RenderModule.RenderMap() → ASCII/Emoji 文本
  │
  └─ StatusModule.Build*() → 6 个状态面板
```

---

## 五、地图格子栈模型

```
旧模型（已废弃）：4 个固定层，每层每格只能放 1 个
  Terrain[y][x] = "#"
  Fixtures[y][x] = ">"
  Objects[y][x] = "M"    ← Actor 的冗余副本
  Meta[y][x] = {...}

新模型（当前）：每格一个栈，任意堆叠
  Cells[y][x] = List<CellEntity> [
    { Type: Terrain,  Glyph: ".", EntityId: "floor" },
    { Type: Fixture,  Glyph: ">", EntityId: "stair_down" },
    { Type: Item,     Glyph: "!", EntityId: "potion_01" },
    // 可继续堆叠 Hazard、Corpse、Effect...
  ]

  Actor 不存在栈里 → 渲染时由 GetDisplayCell() 动态查询 state.Actors

CellEntityType 枚举（渲染优先级从低到高）：
  Terrain  = 0    地形：墙、地面
  Fixture  = 10   设施：楼梯、巢穴、门、房屋
  Item     = 20   掉落物
  Hazard   = 30   陷阱、毒雾
  Corpse   = 40   尸体/残骸
  Effect   = 60   视觉特效

GetDisplayCell(x, y) 逻辑：
  1. state.Actors 里有 Actor 在 (x,y)？ → 返回 Actor.Glyph
  2. 否则 → 返回 Cells[y][x] 栈顶 Glyph
```

---

## 六、Actor 能力计算管线

```
Actor
  ├── Limbs[]        ──┐
  ├── Race?           ──┤
  ├── Profession?     ──┼──→ ComputeTags()
  ├── Buffs[]         ──┤     合并所有 ITagSource 的 GetTags()
  ├── Experiences[]   ──┤     → Dictionary<string, int>
  └── Inventory[]     ──┘     （装备中的 Item 也贡献 tag）
       (Equipped)

ComputeCapacities()
  第一轮（基础值）：
    每个 Limb 的每个 Capacity 权重 × (Durability / MaxDurability)
    → baseValues["manipulation"] = 0.8

  第二轮（乘数）：
    查 CapacityDef.Multipliers
    → final["manipulation"] = base × Π(依赖能力的基础值)

ActionQuery.GetAvailable(actor, allActions)
  → 过滤 ActionDef.Required（tag 门槛）
  → 过滤 ActionDef.CapacityRequired（能力门槛）
  → 返回该 Actor 当前可用的动作列表

CombatModule.CalcDamage(attacker, action, target)
  → baseDmg = action.Power × 2 × (0.5 + manipulation)
  → finalDmg = max(1, baseDmg - target.防御tag)
```

---

## 七、AI 子系统

```
┌─────────────────────────────────────────────┐
│ IBrainModule (接口)                          │
│   Decision Decide(Perception, Random)       │
├─────────────────────────────────────────────┤
│ SimpleBrain (当前唯一实现)                    │
│   1. 相邻有敌人 → Attack                     │
│   2. 感知范围内有敌人 → MoveTo                │
│   3. 否则 → Wander                          │
├─────────────────────────────────────────────┤
│ AIDispatcher (调度器)                        │
│   Register("simple", new SimpleBrain())     │
│                                             │
│   TickAll() 流程：                           │
│     遍历所有 BrainId != null 的 Actor        │
│     ├─ Classify(actor, viewCenter, range)   │
│     │   ├─ 曼哈顿距离 ≤ range → Full        │
│     │   ├─ 超出 range → Simplified          │
│     │   └─ 其他楼层 → Summary（跳过）        │
│     ├─ Simplified 每 3 回合才决策             │
│     ├─ PerceptionBuilder.Build()            │
│     ├─ brain.Decide(perception, rng)        │
│     └─ ExecuteDecision() → 调用 ActionModule │
├─────────────────────────────────────────────┤
│ Perception (感知快照)                        │
│   Self           当前 Actor                  │
│   NearbyActors   感知范围内的其他 Actor       │
│   NearbyWalkable 周围地形可行走性             │
│   NearbyFixtures 周围设施类型                │
│   Turn / Floor   时间/空间上下文              │
├─────────────────────────────────────────────┤
│ Decision (决策输出)                           │
│   Type: Idle / Wander / MoveTo / Attack     │
│         / Flee / Interact / UseItem         │
│   TargetPos / TargetActorId                 │
│   ActionDefId / TargetLimbId                │
├─────────────────────────────────────────────┤
│ FactionRelation                             │
│   hostile ↔ player   敌对                    │
│   hostile ↔ friendly 敌对                    │
│   同阵营不攻击                                │
└─────────────────────────────────────────────┘
```

---

## 八、事件系统

```
GameEvent
  │
  ├── Type (string)          事件类型标识
  │
  ├── 通用字段
  │   ├── InitiatorId        发起者 Actor ID
  │   ├── TargetId           目标 Actor ID
  │   ├── TargetActorName    目标显示名
  │   ├── TargetX / TargetY  目标坐标
  │
  ├── 交互专用
  │   ├── InteractionDefId
  │   ├── InteractionName
  │   └── EffectType         "talk" / "trade" / "combat" / "tame"
  │
  └── 战斗专用
      ├── Damage             伤害值
      ├── LimbName           被击中的肢体
      └── ActionName         使用的动作名

已定义的事件类型：
  hit_wall             撞墙
  actor_moved          移动成功
  monster_spawned      巢穴刷出怪物
  combat_bump          撞击触发战斗（非 BumpAttack 模式）
  combat_attack        攻击（附带伤害详情）
  combat_block         格挡
  limb_destroyed       肢体被破坏
  actor_killed         击杀（附带掉落金币）
  actor_incapacitated  失去行动能力
  interaction          交互（附带效果类型）
```

---

## 九、UI 层架构

```
IGameUI (接口)
  │ AddLog(msg)          写日志
  │ EnterSelection(cb)   进入选择模式（数字键选择）
  │ CancelSelection()    取消选择
  │ FlushMap()           刷新地图渲染
  │ Dispatch(events)     分发事件
  │ State { get; }       访问 GameState
  │ PlayerDead { get; set; }
  │
  └── Main : Node, IGameUI
        │
        ├── InputModule         键盘 → 命令
        │   模式：Action / Typing / Selection
        │   快捷键：WASD 移动, F 交互, I 背包,
        │          L 查看, Space 楼梯, R 切换渲染,
        │          F5 快存, F9 快读, Esc 设置
        │
        ├── RenderModule        字符 → 显示文本
        │   模式：ASCII（BBCode 着色）/ Emoji
        │   视窗：21×11 格，以玩家为中心
        │
        ├── StatusModule        Actor → 状态面板文本
        │   面板：名字/楼层, 肢体, 能力, 标签, Buff, 装备
        │
        ├── CombatUIModule      战斗交互流程
        │   HandleCombatBump → ShowActionSelection
        │   → ShowLimbTargetSelection → CombatModule.Attack
        │   → MonsterCounterAttack → AIDispatcher.DecideAndExecuteOne
        │
        ├── TradeUIModule       交易交互流程
        │   OpenTradeMenu → ShowTradeGoods → Buy
        │                 → ShowSellMenu → Sell
        │
        └── InventoryUIModule   背包交互流程
            Open → ShowInventory → ShowItemActions
            → Equip / Use / Drop
```

---

## 十、设计理念总结

### 10.1 状态是名词，命令是动词，事件是过去式

```
TryMove(player, north) → state 变化 + [actor_moved] → 渲染/日志各自响应
```

所有可序列化的东西是**状态**，不可序列化的是**副作用**。

### 10.2 用组合，不用继承

Actor 只有一个类，能力由挂载的 `ITagSource` 组件决定：
- 换种族 → 换 Race 对象
- 断手 → 移除 Limb
- 中毒 → 加 Buff
- 装备武器 → Item.Equipped = true

### 10.3 逻辑/表现完全分离

- `Core/` 不知道 Godot 的存在，不引用任何节点
- `Module/` 只消费 GameEvent，不改 GameState
- 未来可以：多线程跑逻辑、网络同步状态、命令回放

### 10.4 AI 接口稳定，内核可替换

```
IBrainModule.Decide(Perception, Random) → Decision
```

接口只有一个方法。换行为树、GOAP、外部进程，只需新建实现类 + 注册到 AIDispatcher。

### 10.5 格子栈：统一的世界容器

一个格子可以同时有地面 + 楼梯 + 掉落物 + 陷阱。
Actor 不存在栈里（移动太频繁），渲染时动态查询。
显示 = 先查 Actor → 再取栈顶。

### 10.6 通用行动执行

`ActionModule` 是所有 Actor 的行动入口，玩家和 AI 走同一条路：

```
Main.cs      → ActionModule.TryMove()   ← 玩家输入
AIDispatcher → ActionModule.TryMove()   ← AI 决策
```

未来加新行动（捡物品、用楼梯、使用物品），只需扩展 ActionModule + Decision 类型。
