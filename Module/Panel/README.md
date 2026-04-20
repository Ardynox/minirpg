# Panel

这个目录负责本地 UI 面板框架和具体面板实现。
它处理焦点、布局、拖拽、设置流和各类运行时面板，但不负责主流程编排。

> **改面板的展示内容 / 操作 / 触发方式 / 默认快捷键之前**，先看 [`../../Docs/界面与面板.md`](../../Docs/界面与面板.md)。
> 那份文档记录每个面板对玩家显示的字段与边界；改完同步更新它，不要让代码和文档对不上。

## 从哪开始读

- 先看 `PanelManager.cs`：焦点、关闭、命中测试和面板栈都在这里
- 布局与外观持久化：`PanelLayoutService.cs`、`PanelLayoutStore.cs`
- 设置与暂停菜单流程：`SettingsFlowCoordinator.cs`、`SettingsPanelModule.cs`
- 列表型面板公共基类：`ListPanelBase.cs`
- 具体玩法面板：`InventoryGridPanelModule.cs`、`GroundPanelModule.cs`、`SkillManagerModule.cs`、`TradePanelModule.cs`、`QuestPanelModule.cs`

## Godot 4 C# 面板实现常识（写新面板前务必读）

这一节不是 API 手册，是"过去踩过、还会再踩"的陷阱。新面板落地前核对一遍：

### Drag-Drop

- **源 Control 必须自己 override `_GetDragData`**。Godot 4 的 drag 启动只问"鼠标按下那一刻最深命中的那个 Control"，**不会向父节点冒泡**。只让父容器有 `_GetDragData` 而子节点是普通 `Control`，drag 永远启动不了（这是 Godot 3 的 `set_drag_forwarding` 心智模型，在 4 里不成立）。
- **不要用 `SetDragForwarding` + `Callable.From`**。Godot 4 C# 里 `Callable.From` 对 `Variant` 返回值的 marshal 有微妙坑，会让 `_GetDragData` 静默返回 null、drag 起不来。统一走 `override _GetDragData` / `_CanDropData` / `_DropData`。`InventoryGridPanelModule.cs` 末尾的 `DragDropControl` 封装了这一模式，**直接复用**它或照抄。
- **`MouseFilter` 选择**：源 Control `Pass` 或 `Stop` 都能启动 drag；选 `Pass` 是因为还要让 `GuiInput`（选中 / 右键菜单）在**同一个 Control**上生效。`Ignore` 完全不接收事件，就启不了 drag。
- **放 target Control 必须自己 override `_CanDropData` / `_DropData`**。同样不冒泡。如果希望"鼠标落在任一子 Control 上都按父容器的规则判断可否放"，子 Control 也得实现，并把 `atPosition` 转成父坐标再委托。`InventoryGridPanelModule::BuildPlacementVisual` 就是这个套路的范例。

### Refresh 节奏契约（和 drag-drop 强相关）

- **鼠标 Pressed 不要 `Refresh()`**。`Refresh()` 通常 `QueueFree` 一整片子树然后重建。press 的那一瞬间 drag threshold 还没到，源 Control 已经被 QueueFree → drag 永远启动不了。**这是网格背包"拖不动"bug 的元因，别再踩。**
- **Refresh 推迟到鼠标 Released**。单纯点击（没 drag）时 release 的 gui_input 照常触发、Refresh 能跑；发生了 drag 时 Godot 用 `_DropData` 替代 release 的 gui_input，由 drop 处理完后自行 Refresh。
- **右键按下立即弹 context menu**，不调 `Refresh()`。菜单本身是独立 `PopupMenu`，不依赖 Refresh。
- **任何"改世界"的命令提交（`TrySubmitClientCommand` / `ServerActionGateway.Execute`）后**：`Refresh()` + `_host.FlushMap()`，让面板与地图同步到权威结果。

### 面板的"反馈可观察性"

- 面板里每一种玩家输入，都必须对应**一种玩家能立刻看到的反馈**（高亮 / toast / 日志 / hint）。不能"提交命令后静默"。
- 失败路径都走 i18n key 而不是硬编码文案。`log.<panel>.<action>` 是约定前缀，`log.inventory.grid_error.<ErrorCode>` 是典型的"错误码→本地化文案"桥（见 `ServerActionGateway::ResolveGridErrorCode`）。
- 文档侧在 `Docs/界面与面板.md` 的面板条目里必须补齐"操作 & 反馈"5 栏表（输入 / 反馈 / 命令 / 失败 i18n / 跨面板联动）。范例见 4.2 InventoryPanel。

## 常见改动去哪里

- 改焦点切换、全局关闭、键盘命令分发：`PanelManager.cs`
- 改面板尺寸、按钮缩放、布局持久化：`PanelLayoutService.cs`、`PanelLayoutStore.cs`
- 改暂停菜单和设置面板的交互流：`SettingsFlowCoordinator.cs`、`SettingsPanelModule.cs`
- 改列表面板通用行池、光标、滚动可见：`ListPanelBase.cs`
- 改某个具体面板的展示和命令：对应 `*PanelModule.cs`

## 不要在这里解决什么

- 不把主菜单、会话切换、启动流程决策塞进面板模块，这些由 `App/RuntimeUi/*` 协调
- 不把面板显示缓存当成玩法真相，状态真相仍在 `GameState` / Core / Module 层
- 不把通用布局持久化逻辑复制进单个面板

## 什么时候更新这份 README

- 只有当面板框架入口、设置流入口、布局持久化落点或常见改动入口发生变化时才更新
