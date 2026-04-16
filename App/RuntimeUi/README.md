# App/RuntimeUi

这个目录承接从 `App/Main*.cs` 下沉的运行时编排职责。30+ 个文件按三条主线阅读，不要逐个打开。

## 阅读顺序

启动链先读，运行时流再读，多人联机最后读。遇到具体问题再进下面对应小节。

### 启动链

- `MainStartupCoordinator` — 入口。按阶段装配服务、UI 引用、输入、协调器。
- `MainRuntimeComposition` — 装配结果打包成三组：服务、UI 引用、钩子。
- `InputHandlerSet` — 注册所有 `IModalInputLayer` 到 `InputModule`。

其他启动期辅助：`WorldManagerContext`、`RuntimeUiResetReason`、`RuntimeUiModeSnapshot`、`RuntimeStatusPanelPlacementPolicy`。

### 运行时流

- `MainAppFlowCoordinator` — 菜单 / 新游戏 / 读档 / 世界管理 / 地图编辑 / 载入中 UI 的主入口。
- `GameplayCommandCoordinator` — 键鼠命令 → `ClientCommand` 路由。
- `MainInputCoordinator` — 和 `InputModule` 对接，分发到 gameplay / panel / debug 三条路线。
- `RuntimeViewCoordinator` + `RuntimeCameraController` + `RuntimeCameraRightDragHandler` — 视图、相机、拖拽。
- `GameEventPresentationRouter` — `GameEvent` → UI / FX / 日志的翻译层（本目录最重要的解耦点之一）。
- `AutoNavigationCoordinator` + `PlayerTargetingCoordinator` + `LimbTargetCoordinator` — 自动寻路 / 锁定 / 部位选择。

以及若干面板 / 模态 / overlay：`ModalStateController`、`SettingsFlowModalInputAdapter`、`DebugPanelController`、`RuntimeStatusPanelController`、`TurnControllerPanelController`、`ChestCoordinator`、`MapEditorCoordinator`、`RuntimeWorldToolSession`、`AltLabelOverlayController`。

`MainAppFlowCoordinator` 长时间是这块最胖的点。抽出的 `WorldManagerStatusBanner` 和 `WorldManagerDeletionRouter` 承担 World Manager 面板的状态与删除操作。

### 多人联机链

- `MultiplayerFlowCoordinator` — 大厅 → 加入房间 → 进入会话的阶段切换。
- `MultiplayerHubCoordinator` — 大厅面板的对接。
- `MultiplayerRuntimeCoordinator` — 进入会话后，负责接 `MultiplayerSessionBackend` 的事件、做客户端预测与回滚。
- `ClientCommandRouter` — `ClientCommand` 的最终发送点：单机直接过 `LocalSessionBackend`，联机走 `MultiplayerRuntimeCoordinator.TrySubmitClientCommand`。
- `ClientPredictionState` — 预测队列与回滚判定，配置来自 `PredictionConfig`。

入口全链路的权威 vs 预测边界见 [`../../Docs/多人联机契约.md`](../../Docs/多人联机契约.md)。

## 新增协调器的落点判断

- 是"Godot 节点获取、组合根装配"？留在 `App/Main*.cs`，不要进这里。
- 是"会话生命周期的编排"？下沉到 `MiniRPG.Shared/Module/GameSessionModule.cs` 或 `MiniRPG.Shared/Module/Session/*`。
- 是"运行时交互流 / 输入流 / 事件呈现"？进这个目录。

新增一个 `*Coordinator` 前，先问：现有哪个是否已经承担了该责任、能否往里并一小步。默认不引入新协调器。

## 相关文档

- 架构主线：[`../../Docs/架构现状.md`](../../Docs/架构现状.md)
- 会话切换清理：[`../../Docs/会话生命周期.md`](../../Docs/会话生命周期.md)
- 多人联机契约：[`../../Docs/多人联机契约.md`](../../Docs/多人联机契约.md)
