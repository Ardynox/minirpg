# MiniRPG 项目约定与开发原则

> 本文档负责描述项目目标、技术约束、设计原则和默认开发方式。

---

## 文档定位

- 先读这份文档，再决定要不要新增实体、模块、抽象层或框架。
- 这份文档定义“应该遵守什么”，不负责描述当前每个类已经怎样实现。
- 如果你要看当前代码事实、职责边界和调用链，请转到 [`ARCHITECTURE.md`](./ARCHITECTURE.md)。
- 如果你要快速定位目录、模块依赖和数据流，请转到 [`Docs/CODEBASE_MAP.md`](./Docs/CODEBASE_MAP.md)。
- 如果你要看遗留问题和技术债，请转到 [`Docs/TODO.md`](./Docs/TODO.md)。

## 阅读提醒

- 本文档后半部分保留了项目早期 MVP 启动记录。
- 涉及 `Cursor`、单脚本拆分、MVP 验收、启动步骤的内容，属于历史上下文，不是当前实现事实。
- 当前代码现状以 [`ARCHITECTURE.md`](./ARCHITECTURE.md) 和 [`Docs/CODEBASE_MAP.md`](./Docs/CODEBASE_MAP.md) 为准。

---

## 历史：原始 MVP 方案修正总览

| 问题 | 严重程度 | 说明 |
|------|----------|------|
| 字符串不可索引赋值 | 🔴 必须修 | GDScript 字符串不可变，`row[x] = "."` 会直接报错 |
| `log()` 命名冲突 | 🔴 必须修 | `log()` 是 GDScript 内置数学函数，重名会产生难以排查的 bug |
| `attack()` 无目标检测 | 🟡 建议修 | 站在空地也能「命中」，需加四周 M 检测 |
| `look` 指令无实现描述 | 🟡 建议修 | Cursor 会自由发挥或跳过，需补充明确实现 |
| Input 清空与焦点 | 🟢 后期注意 | 输入后应清空 LineEdit 并重新抢焦点 |
| Output 无限增长 | 🟢 后期注意 | `text +=` 长期运行会无限堆积 |

---

## 一、项目目标

实现一个**极简、本地运行的 MUD 风格 RPG 游戏**，具备以下特点：

- 使用**字符画地图（ASCII）**
- 使用**Emoji 提供表现反馈**
- 使用**文本输入驱动交互**
- 单场景、低复杂度、快速迭代
- 核心目标：**最小可运行循环（Move → Event → Feedback）**

---

## 二、技术约束（必须遵守）

### 架构约束

- 单场景（Main.tscn）
- 不使用 TileMap
- 不使用复杂节点结构
- 不引入状态机框架 / ECS / 数据驱动系统
- 逻辑尽量集中（前期允许单脚本）

### 节点结构

```
Main (Node)
├── UI (Control)
│   ├── Output (RichTextLabel)
│   └── Input (LineEdit)
```

---

## 三、核心系统设计

### 1. 地图系统

#### 数据结构

> 🔴 **修正 #1 — 必须使用二维数组，不能用字符串数组**
>
> GDScript 中字符串是不可变类型，`row[x] = "."` 会在运行时报错。
> 必须改用 Array of Array，这样 `map[y][x] = "."` 才合法。

```gdscript
# ❌ 错误写法（字符串数组，下标赋值会报错）
var map = [
    "########",
    "#P.....#",
    "#..M...#",
    "#......#",
    "########"
]

# ✅ 正确写法（二维数组，支持 map[y][x] = "." 赋值）
var map = [
    ["#","#","#","#","#","#","#","#"],
    ["#","P",".",".",".",".",".",  "#"],
    ["#",".",".","M",".",".",".", "#"],
    ["#",".",".",".",".",".",".","#"],
    ["#","#","#","#","#","#","#","#"]
]
```

#### 符号定义

| 字符 | 含义 |
|------|------|
| `#`  | 墙   |
| `.`  | 地面 |
| `P`  | 玩家 |
| `M`  | 怪物 |

---

### 2. 渲染系统

```gdscript
func visual_char(c):
    match c:
        "#": return "⬛"
        ".": return "·"
        "P": return "🙂"
        "M": return "👾"
        _: return c

func render_map():
    var output = ""
    for row in map:
        for c in row:
            output += visual_char(c)
        output += "\n"
    $UI/Output.text = output
```

---

### 3. 玩家系统

```gdscript
var player_x = 1
var player_y = 1
```

---

### 4. 输入系统

#### 信号绑定

```
LineEdit → text_submitted(String)
```

#### 处理函数

> 🟢 **修正 #5 — 加入清空与焦点逻辑**
> 不加这两行的话，每次输入后需要手动点击输入框才能继续，体验很差。

```gdscript
func _on_Input_text_submitted(text):
    handle_command(text.strip_edges().to_lower())
    $UI/Input.clear()       # ✅ 清空输入框
    $UI/Input.grab_focus()  # ✅ 重新获得焦点
```

---

### 5. 指令系统

#### 支持指令

| 指令   | 行为 |
|--------|------|
| `w`    | 上移 |
| `s`    | 下移 |
| `a`    | 左移 |
| `d`    | 右移 |
| `atk`  | 攻击（检测周围是否有怪物） |
| `look` | 查看当前位置及周围格子 |

#### 指令解析

```gdscript
func handle_command(cmd):
    match cmd:
        "w": move(0, -1)
        "s": move(0, 1)
        "a": move(-1, 0)
        "d": move(1, 0)
        "atk": attack()
        "look": look()
        _: add_log("未知指令 ❓")
```

---

### 6. 移动系统

```gdscript
func move(dx, dy):
    var nx = player_x + dx
    var ny = player_y + dy
    var target = map[ny][nx]   # ✅ 二维数组直接访问

    if target == "#":
        add_log("撞墙了 🚧")
        return

    if target == "M":
        add_log("遇到怪物 👾，请使用 atk 攻击")
        return

    update_player(nx, ny)
```

#### 更新玩家位置

```gdscript
func update_player(nx, ny):
    map[player_y][player_x] = "."   # ✅ 二维数组赋值，不会报错
    player_x = nx
    player_y = ny
    map[player_y][player_x] = "P"
    render_map()
```

---

### 7. 战斗系统

> 🟡 **修正 #3 — attack() 加目标检测**
> 原版无条件输出「命中」，站在空地也能「攻击成功」，体验奇怪。
> 修正后检测四个方向是否有 M，无则提示「附近没有敌人」。

```gdscript
func attack():
    var dirs = [[0,-1],[0,1],[-1,0],[1,0]]
    for d in dirs:
        if map[player_y + d[1]][player_x + d[0]] == "M":
            add_log("你发动攻击 ⚔️")
            add_log("命中目标 💥")
            map[player_y + d[1]][player_x + d[0]] = "."   # 怪物消失
            render_map()
            return
    add_log("附近没有敌人 🤷")
```

---

### 8. look 系统

> 🟡 **修正 #4 — look 补全实现**
> 原文档只列出了 look 指令，没有给出任何实现，Cursor 会自由发挥或跳过。
> 修正版输出玩家坐标 + 四个方向的地块类型。

```gdscript
func look():
    var desc = "📍 你在 (%d, %d)\n" % [player_x, player_y]
    var dirs = {"上": [0,-1], "下": [0,1], "左": [-1,0], "右": [1,0]}
    for dir_name in dirs:
        var d = dirs[dir_name]
        var cell = map[player_y + d[1]][player_x + d[0]]
        var label = ""
        match cell:
            "#": label = "墙 🚧"
            ".": label = "空地"
            "M": label = "怪物 👾"
        desc += "%s: %s\n" % [dir_name, label]
    add_log(desc)
```

---

### 9. 日志系统

> 🔴 **修正 #2 — 函数名改为 `add_log()`**
> `log()` 是 GDScript 内置数学函数（自然对数），重名会产生难以排查的 bug。
> 全文统一使用 `add_log()`。
>
> 🟢 **修正 #6 — 加入行数上限**
> `text +=` 长期运行内容会无限堆积，加 MAX_LOG_LINES 限制。

```gdscript
const MAX_LOG_LINES = 30

func add_log(msg):
    var lines = $UI/Output.text.split("\n")
    lines.append(msg)
    if lines.size() > MAX_LOG_LINES:
        lines = lines.slice(lines.size() - MAX_LOG_LINES)
    $UI/Output.text = "\n".join(lines)
```

---

## 四、游戏循环

```
输入 → 指令解析 → 状态变化 → 渲染 → 输出日志
```

---

## 历史：MVP 启动步骤（Cursor 执行顺序）

### STEP 1：项目初始化

- 创建 Godot 项目：MiniRPG
- 创建 Main.tscn
- 搭建 UI 节点结构

### STEP 2：基础地图显示

- 定义 map（**使用二维数组**，见第三节修正 #1）
- 实现 render_map()

✅ 目标：屏幕显示字符地图

### STEP 3：玩家系统

- 定义 player_x / player_y
- 实现 update_player()（直接对二维数组赋值）

✅ 目标：能修改地图数据

### STEP 4：移动逻辑

- 实现 move()
- 增加墙体检测和怪物检测

✅ 目标：玩家可移动，不会穿墙

### STEP 5：输入系统

- 连接 LineEdit.text_submitted 信号
- 实现 handle_command()
- 加入 clear() 和 grab_focus()

✅ 目标：通过输入控制移动

### STEP 6：Emoji 渲染

- 实现 visual_char()
- 替换 render_map() 为 Emoji 版本

✅ 目标：地图视觉升级

### STEP 7：事件系统

- 加入怪物（M）
- 移动时触发事件提示

### STEP 8：战斗与 look 系统

- 实现 attack()（含方向检测）
- 实现 look()（含坐标和周围描述）
- 实现 add_log()（含行数限制）

---

## 历史：早期代码组织建议

前期：

```
Main.gd（全部逻辑）
```

后期（可选拆分）：

```
Map.gd
Player.gd
Command.gd
```

---

## 历史：早期扩展方向

仅在基础完成后考虑：

1. HP / 数值系统
2. 多地图（房间切换）
3. 随机生成地图
4. 状态效果（🔥 ❄️ ☠️）
5. 简单 AI（怪物移动）

---

## 八、设计原则（必须遵守）

> 如无必要，无增实体

### 禁止行为

- ❌ 引入复杂架构
- ❌ 过早优化
- ❌ UI 复杂化
- ❌ 事件系统泛化

### 推荐行为

- ✅ 小步快跑
- ✅ 每步可运行
- ✅ 先实现再抽象
- ✅ 保持代码直接

---

## 历史：MVP 验收标准

满足以下全部条件即完成第一阶段：

- 可显示地图（Emoji）
- 可输入 w/a/s/d 移动
- 有碰撞检测（墙体 + 怪物）
- 有简单日志输出（add_log）
- 可触发战斗文本（attack 含目标检测）
- look 指令输出位置与周围信息

---

## 历史：MVP 交付说明（给 Cursor）

实现要求：

- 严格按步骤执行，每步确保可运行
- **地图必须用二维数组（Array of Array），不能用字符串数组**
- **日志函数统一使用 `add_log()`，禁止使用 `log()`**
- 不自行扩展功能，不引入额外系统
- 保持代码简洁、可读

---

## 历史：MVP 后续步骤

完成 MVP 后：

👉 提交代码 → 进行第二轮优化（结构收敛 + 可扩展性设计）
