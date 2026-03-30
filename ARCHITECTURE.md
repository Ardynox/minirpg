# MiniRPG 架构约束

## 核心原则
- GameState 是唯一数据源，所有游戏数据存在这里
- logic/ 目录下的模块是纯函数，输入状态，输出新状态+事件列表
- logic/ 里禁止出现任何 $ 节点引用、emit_signal、get_node
- 副作用（渲染、日志、音效）只在 view/ 和 Main.gd 里

## 状态 vs 副作用

| 类别 | 判断标准 |
|------|----------|
| 状态 | 能序列化成 JSON |
| 副作用 | 不能（或不需要）序列化 |

| 数据 | 类型 | 理由 |
|------|------|------|
| 地图数据 | 状态 | 二维数组，可序列化 |
| 玩家坐标、HP、属性 | 状态 | 纯数据 |
| 怪物列表、AI意图 | 状态 | 纯数据 |
| 背包、装备 | 状态 | 纯数据 |
| 技能冷却、Buff列表 | 状态 | 纯数据 |
| 任务进度、对话进度 | 状态 | 纯数据 |
| 随机种子 | 状态 | 决定论要求必须存 |
| 当前回合数 | 状态 | 纯数据 |
| 渲染地图 | 副作用 | 操作节点 |
| 播放音效 | 副作用 | 操作硬件 |
| 显示日志 | 副作用 | 操作 UI |
| 网络同步 | 副作用 | IO |
| 存档读写 | 副作用 | IO |

## 状态结构

```gdscript
var state = {
    "turn": 0,
    "rng_seed": 12345,

    "map": {
        "width": 8,
        "height": 5,
        "cells": [...],
        "rooms": [],
    },

    "actors": {
        "player": { ... },
        "goblin_1": { ... },
    },

    "turn_queue": ["player", "goblin_1"],

    "events": [],
    "quests": {},
    "global_flags": {},
}
```

## Actor 数据结构

```gdscript
var actor = {
    "id": "player",
    "pos": {"x": 1, "y": 1},
    "components": {
        "stats":     {"hp": 10, "max_hp": 10, "atk": 3, "def": 1},
        "inventory": {"slots": [], "max_slots": 10},
        "ai":        null,
        "faction":   "player"
    }
}
```

## 事件格式

```gdscript
{"type": "actor_moved",    "id": "player", "from": [1,1], "to": [1,2]}
{"type": "damage_dealt",   "attacker": "player", "target": "goblin_1", "amount": 3}
{"type": "actor_died",     "id": "goblin_1", "killer": "player"}
{"type": "item_picked",    "actor": "player", "item": "potion"}
{"type": "hit_wall",       "id": "player", "dir": [0,-1]}
{"type": "turn_end",       "turn": 5}
{"type": "map_revealed",   "cells": [[2,3],[2,4]]}
```

## 模块组织

### 必要模块（MVP）

| 模块 | 职责 |
|------|------|
| GameState | 游戏状态容器，唯一数据源 |
| CommandProcessor | 接收命令，驱动状态变化，产出事件 |
| MapModule | 地图数据读写、碰撞查询 |
| ActorModule | 玩家和怪物的公共行为（移动、受击） |
| EventBus | 事件列表，连接逻辑和副作用 |

### 常见模块（第二阶段）

| 模块 | 职责 |
|------|------|
| CombatModule | 伤害计算、命中判定、死亡处理 |
| InventoryModule | 背包增删、装备槽、物品效果 |
| StatsModule | 属性系统（力量、敏捷）及派生值计算 |
| AIModule | 怪物行为决策（巡逻、追击、逃跑） |
| TurnManager | 回合推进、行动顺序排队 |

### 可能的模块（后期扩展）

| 模块 | 职责 |
|------|------|
| QuestModule | 任务触发、进度追踪、完成判定 |
| DialogueModule | 对话树状态机 |
| FogOfWar | 视野计算、地图揭露 |
| SkillModule | 技能定义、冷却、效果施加 |
| BuffModule | Buff/Debuff 堆叠、持续、到期 |
| MapGenModule | 程序化地图生成 |
| SaveModule | 序列化/反序列化 GameState |
| NetworkModule | 状态同步、命令广播 |

## 代码结构

```
Main.gd              驱动层：接收输入，调用逻辑，分发事件，触发渲染

logic/
  GameState.gd       状态容器 + 深拷贝工具
  CommandProcessor.gd  命令入口，调度各模块
  MapModule.gd       地图查询、碰撞、修改
  ActorModule.gd     Actor 增删改查、通用操作
  CombatModule.gd    伤害计算
  AIModule.gd        怪物决策
  TurnManager.gd     回合推进

view/
  MapRenderer.gd     消费事件，渲染地图
  LogView.gd         消费事件，输出日志
  UIController.gd    消费事件，更新 HP 条等
```

## 新增功能的标准流程

1. 在 GameState 里加数据字段
2. 在对应 logic/ 模块里写纯函数处理逻辑，产出事件
3. 在 _dispatch() 里加事件处理，调用 view/ 层
4. 不允许跳过任何一步直接在 Main.gd 里写逻辑

## 模块边界

- MapModule 只管 state.map
- ActorModule 只管 state.actors
- CombatModule 调用 ActorModule，不直接改 state.map
- AIModule 只读状态，输出"意图命令"，不直接改任何状态

## 用组合，不用继承

Actor 是一个数据包，能力靠挂载组件决定。

```gdscript
# Actor 本身只是数据容器
var actor = {
    "id": "player",
    "pos": {"x": 1, "y": 1},
    "components": {
        "stats":     {"hp": 10, "max_hp": 10, "atk": 3, "def": 1},
        "inventory": {"slots": [], "max_slots": 10},
        "ai":        null,
        "faction":   "player"
    }
}

var monster = {
    "id": "goblin_1",
    "pos": {"x": 3, "y": 2},
    "components": {
        "stats":   {"hp": 5, "max_hp": 5, "atk": 2, "def": 0},
        "ai":      {"behavior": "chase", "target_id": "player"},
        "faction": "enemy",
        "loot":    {"gold": 3, "items": ["potion"]}
    }
}
```

## 事件分发

```gdscript
func _dispatch(events):
    for e in events:
        match e["type"]:
            "actor_moved":   _on_actor_moved(e)
            "damage_dealt": _on_damage_dealt(e)
            "actor_died":   _on_actor_died(e)
            "hit_wall":     add_log("撞墙了 🚧")
```

## 一句话总结

状态是名词，命令是动词，事件是过去式，副作用是观察者。

```
move(player, north) → state 变化 + [actor_moved, ...] → 渲染/日志/网络各自响应
```

这个分界一旦守住：
- 多线程 → 把逻辑扔进线程
- 联机 → 命令走网络、状态走同步
- 存档 → 把 state 序列化
- 回放 → 重放命令列表

每件事都是独立的、可测试的、改动不互相污染的。
