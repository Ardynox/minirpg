# Module Boundary Matrix (2026-04-12)

说明：

- 口径同 `MODULE_BOUNDARY_AUDIT_2026-04-12.md`
- 记法：`行数 / 函数数`
- 结论只分三类：`边界内`、`边界内，但偏胖`、`职责边界外`

## MiniRPG.Shared/Module

- `MiniRPG.Shared/Module/ActorDerivedStateUpdater.cs`（41 / 5）：边界内。只负责派生状态同步入口。
- `MiniRPG.Shared/Module/GameSessionModule.cs`（1366 / 75）：职责边界外。把开局、世界目录、读档、continue、楼层切换都放在一个模块里。
- `MiniRPG.Shared/Module/ServerActionGateway.cs`（779 / 42）：职责边界外。把命令分发、保留策略、容器持久化、奖励生成混在一起。
- `MiniRPG.Shared/Module/TimelineTurnGateway.cs`（13 / 2）：边界内。只是回合写入口薄封装。

## MiniRPG.Shared/Module/Network

- `MiniRPG.Shared/Module/Network/ENetGameClient.cs`（228 / 9）：边界内。只做客户端传输与消息收发。
- `MiniRPG.Shared/Module/Network/ENetGameServer.cs`（247 / 10）：边界内。只做服务端传输与 peer/session 管理。
- `MiniRPG.Shared/Module/Network/ENetNativeLifetime.cs`（39 / 2）：边界内。只管理 ENet 生命周期。
- `MiniRPG.Shared/Module/Network/LobbyHttpClient.cs`（139 / 8）：边界内。只做 lobby HTTP 访问适配。

## MiniRPG.Shared/Module/Panel

- `MiniRPG.Shared/Module/Panel/ItemFormatHelper.cs`（227 / 14）：边界内。共享物品文本格式化职责清楚。

## MiniRPG.Shared/Module/Render

- `MiniRPG.Shared/Module/Render/FogOfWarTracker.cs`（211 / 8）：边界内。视野状态计算与记忆缓存职责单一。
- `MiniRPG.Shared/Module/Render/ViewModes.cs`（90 / 3）：边界内。只提供视图模式的显示映射策略。

## Module

- `Module/AutoTestCli.cs`（197 / 9）：边界内。只负责 AutoTest CLI 解析和运行时配置覆盖。
- `Module/AutoTestLogWriter.cs`（174 / 7）：边界内。只负责测试报告写出。
- `Module/AutoTestMetricBuilder.cs`（43 / 2）：边界内。只负责指标聚合。
- `Module/AutoTestModels.cs`（340 / 6）：边界内。主要是 AutoTest 数据模型。
- `Module/AutoTestModule.cs`（1607 / 43）：职责边界外。runner、场景实现、报告记录、截图探针和 save 文件读写耦合在一起。
- `Module/AutoTestVisionProbeHelper.cs`（47 / 1）：边界内。只做视野探针辅助。
- `Module/CharacterCreationModule.cs`（386 / 25）：边界内，但偏胖。角色创建流程、选项维护、预览与确认都塞在一处。
- `Module/CombatUIModule.cs`（18 / 1）：边界内。只做战斗 UI 胶水。
- `Module/ConfirmDialogModule.cs`（98 / 3）：边界内。只做确认弹窗。
- `Module/DialogUIModule.cs`（233 / 7）：边界内。只做对话流程编排。
- `Module/HealthAlertsModule.cs`（210 / 6）：边界内。只做生命/健康提醒。
- `Module/IGameUI.cs`（24 / 0）：边界内。纯接口。
- `Module/IModalInputLayer.cs`（11 / 0）：边界内。纯接口。
- `Module/IncidentAlertModule.cs`（176 / 6）：边界内。只做事件告警展示。
- `Module/InputBindingService.cs`（494 / 26）：边界内，但偏胖。解析、默认值、冲突处理、磁盘持久化都在同一个服务。
- `Module/InputModule.cs`（257 / 15）：边界内。输入焦点状态机职责清楚。
- `Module/KeyBindingsController.cs`（247 / 15）：边界内。绑定编辑状态和捕获流程清楚。
- `Module/KeyBindingsUIModule.cs`（196 / 12）：边界内。只做绑定页 UI。
- `Module/LoadRecoveryDialogModule.cs`（254 / 19）：边界内。恢复对话框的选择和确认流程仍在单一职责域。
- `Module/LogModule.cs`（452 / 33）：边界内，但偏胖。虽然格式化分支多，但始终围绕 `GameEvent -> log text`。
- `Module/LookModule.cs`（396 / 18）：边界内，但偏胖。它是只读观察聚合器，但依赖面已经很宽。
- `Module/MenuModule.cs`（95 / 5）：边界内。只做菜单切屏。
- `Module/ModalInputLogic.cs`（47 / 2）：边界内。纯 modal 输入辅助。
- `Module/MultiplayerHubModule.cs`（383 / 15）：边界内。单一的大厅页 UI。
- `Module/MultiplayerRoomPanelModule.cs`（340 / 13）：边界内。单一的房间管理 UI。
- `Module/NeedsHudModule.cs`（126 / 7）：边界内。只做 needs HUD。
- `Module/PartyHudModule.cs`（201 / 7）：边界内。只做队伍 HUD。
- `Module/PlayerTargetingModule.cs`（189 / 10）：边界内。只做玩家选目标流程。
- `Module/SaveBrowserModule.cs`（128 / 5）：边界内。只做存档列表浏览。
- `Module/SaveNameDialogModule.cs`（54 / 3）：边界内。只做命名弹窗。
- `Module/TargetSummaryHudModule.cs`（198 / 8）：边界内。只做目标摘要 HUD。
- `Module/ThreatHudModule.cs`（308 / 9）：边界内。只做仇恨 HUD。
- `Module/TradeUIModule.cs`（138 / 5）：边界内。只做交易 UI 编排。
- `Module/WorldManagerModule.cs`（820 / 39）：边界内，但偏胖。仍然是“世界选择/启动页”，但列表重建、导航、样式和状态文案都挤在一处。
- `Module/WorldSettingsDialogModule.cs`（254 / 15）：边界内。只做世界设置表单与校验。

## Module/Editor

- `Module/Editor/MapEditorBarModule.cs`（129 / 5）：边界内。只做地图编辑工具条 UI。
- `Module/Editor/MapEditorSession.cs`（206 / 15）：边界内。只做地图编辑会话状态。
- `Module/Editor/ResourceCatalogStore.cs`（275 / 11）：边界内。只做资源目录读写模型。
- `Module/Editor/ResourcePreviewControl.cs`（608 / 27）：边界内，但偏胖。预览、拖拽、命中测试、网格和 region 编辑全在一控件内。

## Module/Network

- `Module/Network/LocalProcessServerLauncher.cs`（239 / 11）：边界内。只做本地 server 进程拉起与健康检查。
- `Module/Network/NoopLocalServerLauncher.cs`（19 / 2）：边界内。纯 stub。

## Module/Panel

- `Module/Panel/ActorInspectPanelModule.cs`（196 / 11）：边界内。只做角色检查面板。
- `Module/Panel/ActorStatusTextBuilder.cs`（423 / 22）：边界内，但偏胖。文本构建职责单一，但 tab 文本与细节格式化函数过多。
- `Module/Panel/ChestPanelModule.cs`（207 / 16）：边界内。只做宝箱面板。
- `Module/Panel/DebugPanelModule.cs`（539 / 35）：边界内，但偏胖。仍是 debug panel，但 UI 与调试命令组装耦合偏重。
- `Module/Panel/DialogPanelModule.cs`（178 / 11）：边界内。只做对话面板。
- `Module/Panel/GroundPanelModule.cs`（285 / 15）：边界内。只做地面物品面板。
- `Module/Panel/InventoryPanelModule.cs`（565 / 30）：边界内，但偏胖。筛选、排序、上下文菜单和动作逻辑都在一个面板内。
- `Module/Panel/IPanel.cs`（52 / 0）：边界内。纯接口。
- `Module/Panel/LayoutEditBarModule.cs`（34 / 2）：边界内。只做布局编辑条。
- `Module/Panel/LimbTargetPanelModule.cs`（252 / 12）：边界内。只做肢体目标面板。
- `Module/Panel/ListPanelBase.cs`（156 / 18）：边界内。只做列表面板公共基类。
- `Module/Panel/PanelBorderHelper.cs`（28 / 1）：边界内。纯边框辅助。
- `Module/Panel/PanelButtonScaleService.cs`（189 / 15）：边界内。只做按钮缩放管理。
- `Module/Panel/PanelDragService.cs`（583 / 39）：边界内，但偏胖。拖拽、布局保存、edit session 和直接拖动都在一个类。
- `Module/Panel/PanelHoverChromeService.cs`（562 / 25）：边界内，但偏胖。hover chrome、设置 popup 和拖拽联动耦合偏重。
- `Module/Panel/PanelLayoutService.cs`（123 / 14）：边界内。只做面板布局/外观应用。
- `Module/Panel/PanelLayoutStore.cs`（224 / 17）：边界内。只做布局持久化。
- `Module/Panel/PanelManager.cs`（422 / 32）：边界内，但偏胖。注册、焦点栈、键盘路由和节点命中都堆在一个管理器。
- `Module/Panel/PanelTransition.cs`（82 / 5）：边界内。只做面板过渡。
- `Module/Panel/PauseMenuPanelModule.cs`（129 / 8）：边界内。只做暂停菜单。
- `Module/Panel/QuestPanelModule.cs`（157 / 13）：边界内。只做任务面板。
- `Module/Panel/RowStyleHelper.cs`（56 / 4）：边界内。纯样式辅助。
- `Module/Panel/SettingsFlowCoordinator.cs`（285 / 13）：边界内。只做设置流程协调。
- `Module/Panel/SettingsPanelModule.cs`（894 / 39）：边界内，但偏胖。标签页、选择模型、语言切换和 key bindings 模式全部集中。
- `Module/Panel/SettingsPanelSelectionModel.cs`（228 / 16）：边界内。只做设置面板选择状态。
- `Module/Panel/SkillBarModule.cs`（551 / 36）：边界内，但偏胖。tab、网格、详情与激活逻辑都堆在一个文件。
- `Module/Panel/SkillManagerModule.cs`（324 / 17）：边界内。仍然是技能管理面板单一职责。
- `Module/Panel/StatusModule.cs`（191 / 9）：边界内。只做状态面板。
- `Module/Panel/TabHelper.cs`（64 / 0）：边界内。纯 helper。
- `Module/Panel/TradePanelModule.cs`（257 / 17）：边界内。只做交易列表面板。
- `Module/Panel/TurnPanelModule.cs`（285 / 11）：边界内。只做回合信息面板。
- `Module/Panel/UIColors.cs`（49 / 0）：边界内。纯常量。
- `Module/Panel/WeatherLabPanelModule.cs`（523 / 30）：边界内，但偏胖。天气控制和视觉调参都在一个实验面板里。

## Module/Render

- `Module/Render/CombatFxPlayer.cs`（299 / 18）：边界内，但偏胖。播放、资源解析和缓存仍属同域，但已经过重，且轻微依赖 editor 目录模型。
- `Module/Render/CombatFxRegistry.cs`（426 / 13）：边界内。只做战斗效果命令解析/映射。
- `Module/Render/DirectionalSpriteHelper.cs`（32 / 1）：边界内。纯 helper。
- `Module/Render/FantasyCharacterAnimatable.cs`（252 / 14）：边界内。只做角色动画适配。
- `Module/Render/IAnimatable.cs`（27 / 0）：边界内。纯接口。
- `Module/Render/IsoCoordUtil.cs`（96 / 3）：边界内。只做等轴坐标换算。
- `Module/Render/IsometricVoxelRenderer.cs`（1548 / 63）：边界内，但偏胖。仍属于 2.5D 渲染域，但纹理生成、绘制排序、实体 sprite 解析和 hover 高亮已明显过重。
- `Module/Render/ItemWorldRenderRegistry.cs`（173 / 7）：边界内。只做地面物品渲染映射。
- `Module/Render/ResAccess.cs`（273 / 12）：边界内。只做资源/animatable 访问缓存。
- `Module/Render/SpineAnimatable.cs`（71 / 5）：边界内。只做 Spine 适配。
- `Module/Render/TileAnimatable.cs`（34 / 4）：边界内。只做 tile 动画适配。
- `Module/Render/TileMapRenderModule.cs`（2102 / 109）：职责边界外。2D TileMap、2.5D 模式切换、天气 FX、sprite pool、资源缓存和性能统计全部耦合。
- `Module/Render/WeatherFxVisualResolver.cs`（204 / 5）：边界内。只做天气粒子参数解析。
- `Module/Render/WeatherScreenFxResolver.cs`（267 / 9）：边界内。只做天气屏幕特效参数与插值。
- `Module/Render/WeatherScreenFxTuning.cs`（244 / 15）：边界内。只做天气屏幕特效调参模型。
