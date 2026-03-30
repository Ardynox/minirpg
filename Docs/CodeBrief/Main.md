# `Main.cs`

## 职责一句话

Godot 驱动层：负责 UI/菜单/设置、输入→命令分发、调用逻辑模块改 `GameState`、消费 `GameEvent` 写日志，并触发地图渲染。完整实现交互、交易、背包、战斗等系统。

## 关键成员

- `_state: GameState`：全局唯一状态容器（逻辑模块都直接操作它）
- `_inputModule: InputModule`：将键盘/文本输入转为 command 字符串
- `_renderModule: RenderModule`：把二维地图渲染成 BBCode/Emoji 文本
- `_mapPanel/_logPanel`：地图与日志显示面板
- `_selectionCallback: Action<int>?`：选择模式的回调函数
- `_inMenu/_gameStarted/_playerDead`：游戏状态标志
- `QuickSavePath/ManualSavePath`：存档路径（`OS.GetUserDataDir()/save/*.json`）

## 关键入口/流程

### 初始化

- `_Ready()`：
  - 绑定 UI 节点、按钮回调
  - 初始化 `RenderModule`、`InputModule`（订阅 `CommandReceived += OnCommand`）
  - 显示主菜单
- `_UnhandledInput(InputEvent)`：
  - 把按键交给 `InputModule.HandleKeyInput`

### 主菜单

- `ShowMainMenu()`：显示主菜单面板，检测存档是否存在
- `MenuContinue()`：尝试加载快速存档或手动存档，失败则新游戏
- `MenuNewGame()`：新游戏
- `MenuLoadGame()`：加载手动存档或快速存档
- `MenuSettings()`：打开设置面板
- `BackToMenu()`：返回主菜单（游戏中会先快速存档）

### 命令处理（`OnCommand(string cmd)`）

- 快捷命令：`:settings` `:quicksave` `:quickload` `:interact` `:inventory`
- 移动命令：`w/a/s/d`
- 功能命令：`look`（观察）`enter`（上下楼）`save/load` `newmap`
- 选择模式命令：`:select_cancel` `:select_N`

### 移动与回合

- `DoMove(dx,dy)`：
  - `MapModule.TryMovePlayer` → 事件列表
  - `state.Turn++`
  - `NestModule.Tick` → 追加事件
  - `Dispatch(events)` 消费事件写日志；`FlushMap()`
  - 玩家死亡后方向键返回主菜单

### 上下楼

- `DoEnterStairs()`：检查玩家当前格与四邻格 `Fixtures`，如果是 `<`/`>` 则 `GoUp/GoDown`
- `GoDown()`：进入下一层（缓存已有或生成新地图）
- `GoUp()`：返回上一层（顶层保护）

## 交互系统

### 交互入口

- `DoInteract()`：
  - `InteractionModule.GetAvailableTargets()` 扫描玩家周围可交互目标
  - 单一目标直接显示交互选项；多目标让玩家选择

### 显示交互选项

- `ShowInteractionsFor(Actor player, Actor target)`：
  - `InteractionModule.GetInteractions()` 获取可用交互列表
  - 显示交互选项供玩家选择
  - 选择后执行 `InteractionModule.Execute()` 并 `Dispatch(events)`

### 交易菜单

- `OpenTradeMenu(GameEvent e)`：打开与商人的交易界面
- `ShowTradeGoods(Actor player, Actor merchant)`：
  - 显示商人货物列表（库存 > 0）
  - 选项：购买/出售/离开
- `ShowSellMenu(Actor player, Actor merchant)`：
  - 显示玩家背包中可出售物品
  - 装备中的物品不可出售

## 背包系统

### 入口

- `DoInventory()`：打开背包

### 显示背包

- `ShowInventory(Actor player)`：
  - 列出所有物品（编号、价格、标签、装备状态）
  - 选项：操作物品/关闭

### 物品操作

- `ShowItemActions(Actor player, int invIndex, Item item)`：
  - `[1] 装备/卸下`
  - `[2] 使用`（仅消耗品显示）
  - `[3] 丢弃`
  - `[0] 返回`

## 战斗系统

### 碰撞战斗

- `HandleCombatBump(GameEvent e)`：
  - 获取玩家可用攻击动作
  - 随机选择动作和目标肢体
  - `CombatModule.Attack()` 执行攻击
  - 若未击杀则怪物反击
  - `TickAllBuffs()` 更新 Buff 状态

### 主动攻击

- `OpenCombatMenu(GameEvent e)`：对目标显示攻击菜单
- `ShowActionSelection(Actor player, Actor target)`：
  - 显示可用攻击动作（含预估伤害）
  - 显示格挡选项
  - `[0] 取消`

### 选择目标肢体

- `ShowLimbTargetSelection(Actor player, Actor target, ActionDef action)`：
  - 列出目标所有肢体（含要害标记和耐久度）
  - 选择肢体后执行攻击
  - 若未击杀则怪物反击

### 怪物反击

- `MonsterCounterAttack(Actor player, Actor monster)`：
  - `CombatModule.MonsterChooseAction()` 随机选择动作和目标
  - 执行攻击并处理事件
  - 玩家死亡时设置 `_playerDead` 标志

### Buff 更新

- `TickAllBuffs()`：玩家回合结束时调用 `Actor.TickBuffs()`

## 事件消费（`Dispatch(List<GameEvent>)`）

- `hit_wall`：日志"撞墙"
- `actor_moved`：目前不写日志
- `monster_spawned`：日志提示刷怪坐标
- `combat_bump`：触发 `HandleCombatBump`
- `combat_attack`：日志显示伤害值和肢体耐久
- `combat_block`：日志显示格挡效果
- `limb_destroyed`：日志显示肢体被摧毁
- `actor_killed`：触发 `HandleActorKilled`（掉落金币）
- `interaction`：触发 `DispatchInteraction`

### `DispatchInteraction(GameEvent e)`

- `talk`：显示 NPC 对话
- `trade`：打开交易菜单
- `combat`：打开攻击菜单
- `tame`：驯服成功，变为友方

## 观察系统

- `DoLook()`：显示玩家状态信息
  - 楼层/坐标/回合
  - ATK/DEF/金币
  - 肢体状态（含要害标记）
  - 脚下设施
  - 同格其他角色
  - 四邻格信息（墙/角色/设施）

## 存档与读档

- `DoSave(string path, string label)`：调用 `SaveModule.SaveGame`
- `DoLoad(string path, string label)`：调用 `SaveModule.LoadGame`，加载后 `EnsurePlayerActor`
- `GenerateNewMap()`：调用 `MapGenModule.Generate`

## 渲染

- `FlushMap()`：调用 `RenderModule.RenderMap` 并更新 RichTextLabel
- `BuildDisplayMap()`：以玩家为中心的视口裁剪
- `ToggleRender()`：切换 ASCII/Emoji 模式

## 与逻辑层的边界

- `Main.cs` 直接调用的逻辑模块：
  - `MapModule`、`MapGenModule`、`ActorModule`、`NestModule`、`SaveModule`
  - `CombatModule`、`InteractionModule`、`InteractionDefs`
  - `InventoryModule`、`TradeModule`
  - `ActionDefs`、`ActionQuery`
- 副作用仅发生在：
  - Godot 节点操作（UI Visible、按钮回调）
  - 文件 IO（通过 `SaveModule`）
  - 渲染输出（`RenderModule.RenderMap` → RichTextLabel）

## 评估关注点

- **状态同步**：`EnsurePlayerActor` 与 `ActorModule.MoveActor` 都会写 `Objects` 层；需要确认不会出现玩家坐标/Objects/Actors 不一致的路径
- **事件语义**：`attack_hit` 语义同时代表"攻击并击杀"（Remove 已发生），后续若引入伤害/未击杀会需要事件扩展
- **选择模式**：使用 `_selectionCallback` 回调实现菜单选择，需要确保正确取消
- **死亡处理**：`_playerDead` 标志允许方向键返回主菜单