# RuntimeUi

这个目录负责运行时编排。
它承接 `App/Main*.cs` 下沉出来的启动、输入、主流程、多人联机、运行时视图和事件到表现的协调逻辑。

## 从哪开始读

- 先看 `MainRuntimeComposition.cs`：这里定义运行时服务、UI 引用和钩子分组。
- 启动与资源预热：`MainStartupCoordinator.cs`
- 输入路由与模态层：`MainInputCoordinator.cs`、`InputHandlerSet.cs`、`ModalStateController.cs`
- 主菜单、会话切换、设置、世界管理、地图编辑入口：`MainAppFlowCoordinator.cs`
- 运行时刷新与脏面板处理：`RuntimeViewCoordinator.cs`
- `GameEvent -> 日志 / UI / FX`：`GameEventPresentationRouter.cs`
- 多人相关入口：`MultiplayerFlowCoordinator.cs`、`MultiplayerHubCoordinator.cs`、`MultiplayerRuntimeCoordinator.cs`

## 常见改动去哪里

- 改启动阶段、重资源加载、启动失败处理：`MainStartupCoordinator.cs`
- 改键盘、鼠标、面板拖拽、模态输入优先级：`MainInputCoordinator.cs`
- 改菜单进入游戏、返回主菜单、继续游戏、存档切换：`MainAppFlowCoordinator.cs`
- 改地图刷新、面板脏标记、运行时视图同步：`RuntimeViewCoordinator.cs`
- 改战斗事件、对话/交易打开、玩家死亡表现：`GameEventPresentationRouter.cs`

## 不要在这里解决什么

- 不把玩法真相或领域规则塞回协调器，状态真相仍在 `GameState` / `MiniRPG.Shared/Core/*`
- 不在这里实现存档目录、继续游戏持久化、backend 细节，相关逻辑看 `GameSessionModule`、`ContinueStateService`、`MiniRPG.Shared/Module/Session/*`
- 不在这里堆具体渲染细节，地图和天气表现看 `Module/Render/*`

## 什么时候更新这份 README

- 只有当稳定入口、主协调器分工、或“改某类问题先看哪个文件”发生变化时才更新
