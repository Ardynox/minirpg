# `Main.cs`

## 职责一句话

Godot 驱动层：负责 UI/菜单/设置、输入→命令分发、调用逻辑模块改 `GameState`、消费 `GameEvent` 写日志，并触发地图渲染。

## 关键成员

- `_state: GameState`：全局唯一状态容器（逻辑模块都直接操作它）
- `_inputModule: InputModule`：将键盘/文本输入转为 command 字符串
- `_renderModule: RenderModule`：把二维地图渲染成 BBCode/Emoji 文本
- `_mapPanel/_logPanel`：地图与日志显示面板
- `QuickSavePath/ManualSavePath`：存档路径（`OS.GetUserDataDir()/save/*.json`）

## 关键入口/流程

- `_Ready()`：
  - 绑定 UI 节点、按钮回调
  - 初始化 `RenderModule`、`InputModule`（订阅 `CommandReceived += OnCommand`）
  - 显示主菜单
- `_UnhandledInput(InputEvent)`：
  - 把按键交给 `InputModule.HandleKeyInput`
- `OnCommand(string cmd)`：
  - 处理快捷命令：`:settings` `:quicksave` `:quickload`
  - 处理动作命令：`w/a/s/d`（移动）`look`（观察）`enter`（上下楼）`save/load`
  - `newmap` 直接重置并生成新地图
- `DoMove(dx,dy)`：
  - `MapModule.TryMovePlayer` → 事件列表
  - `state.Turn++`
  - `NestModule.Tick` → 追加事件
  - `Dispatch(events)` 消费事件写日志；`FlushMap()`
- `DoEnterStairs()`：
  - 检查玩家当前格与四邻格 `Fixtures`，如果是 `<`/`>` 则 `GoUp/GoDown`
  - 楼层切换通过 `MapModule.GoUpFloor/GoDownFloor`（内部会触发 `SaveModule` 的楼层快照逻辑）
- `EnsurePlayerActor()`：
  - 读档后或新建地图后，保证 `state.Actors` 中存在玩家实例，并把玩家 glyph 写入 `Objects` 层

## 事件消费（`Dispatch(List<GameEvent>)`）

当前只消费少量事件类型：

- `hit_wall`：日志“撞墙”
- `attack_hit`：日志“击杀目标”（注意：`MapModule.TryMovePlayer` 里会直接 `ActorModule.Remove`）
- `monster_spawned`：日志提示刷怪坐标
- `actor_moved`：目前不写日志

## 与逻辑层的边界

- `Main.cs` 直接调用的逻辑模块：
  - `MapModule`、`MapGenModule`、`ActorModule`、`NestModule`、`SaveModule`
- 副作用仅发生在：
  - Godot 节点操作（UI Visible、按钮回调）
  - 文件 IO（通过 `SaveModule`）
  - 渲染输出（`RenderModule.RenderMap` → RichTextLabel）

## 评估关注点

- **状态同步**：`EnsurePlayerActor` 与 `ActorModule.MoveActor` 都会写 `Objects` 层；需要确认不会出现玩家坐标/Objects/Actors 不一致的路径
- **事件语义**：`attack_hit` 语义同时代表“攻击并击杀”（Remove 已发生），后续若引入伤害/未击杀会需要事件扩展

