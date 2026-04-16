# Panel

这个目录负责本地 UI 面板框架和具体面板实现。
它处理焦点、布局、拖拽、设置流和各类运行时面板，但不负责主流程编排。

## 从哪开始读

- 先看 `PanelManager.cs`：焦点、关闭、命中测试和面板栈都在这里
- 布局与外观持久化：`PanelLayoutService.cs`、`PanelLayoutStore.cs`
- 设置与暂停菜单流程：`SettingsFlowCoordinator.cs`、`SettingsPanelModule.cs`
- 列表型面板公共基类：`ListPanelBase.cs`
- 具体玩法面板：`InventoryPanelModule.cs`、`GroundPanelModule.cs`、`SkillManagerModule.cs`、`TradePanelModule.cs`、`QuestPanelModule.cs`

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
