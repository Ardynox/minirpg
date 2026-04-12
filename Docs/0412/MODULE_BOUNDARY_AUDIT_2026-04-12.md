# Module Boundary Audit (2026-04-12)

## 1. 范围与方法

- 范围：`Module/**/*.cs` 与 `MiniRPG.Shared/Module/**/*.cs`
- 审计规模：100 个 C# 文件，1457 个函数，29138 行代码
- 结论口径：以“一个文件中的 module/class 所承载的函数集合”作为职责单元判断
- 判断标准：
  - `边界内`：绝大多数函数都服务于同一个清晰职责
  - `边界内，但偏胖`：仍属于同一职责域，但函数数量、状态量或子流程已经偏多
  - `职责边界外`：同一个模块同时承担了两个以上一等职责，函数簇已经跨层或跨子系统

这次不是只看公开 API，而是把私有辅助函数一起纳入判断。小文件主要按命名、依赖、函数簇和构造方式核对；大文件逐个读码。

## 2. 总结论

- `边界内`：79 个文件
- `边界内，但偏胖`：17 个文件
- `职责边界外`：4 个文件

风险最高的 4 个模块：

1. `MiniRPG.Shared/Module/GameSessionModule.cs`
2. `MiniRPG.Shared/Module/ServerActionGateway.cs`
3. `Module/AutoTestModule.cs`
4. `Module/Render/TileMapRenderModule.cs`

整体上，这个仓库的 module 分层是成立的：大多数文件都在做“胶水 / UI / 渲染 / shared 适配层”里的单一工作。真正的问题集中在少数超大模块，它们把流程编排、资源加载、持久化、模式切换、UI 状态、运行时副作用堆在一起，导致函数边界已经溢出。

## 3. 分目录判断

| 目录 | 文件数 | 函数数 | 结论 |
| --- | ---: | ---: | --- |
| `Module/` | 35 | 397 | 以胶水层和流程层为主，整体健康，`AutoTestModule` 明显越界 |
| `Module/Panel/` | 33 | 533 | 大多数面板边界清楚，但若干“管理器型面板”开始偏胖 |
| `Module/Render/` | 15 | 278 | 小型 helper 很干净，风险集中在渲染总控文件 |
| `Module/Editor/` | 4 | 58 | 边界基本清晰，`ResourcePreviewControl` 偏胖 |
| `Module/Network/` | 2 | 13 | 边界清楚 |
| `MiniRPG.Shared/Module/` | 4 | 124 | shared 根目录问题最大，`GameSessionModule` 和 `ServerActionGateway` 都过胖 |
| `MiniRPG.Shared/Module/Render/` | 2 | 11 | 边界清楚 |
| `MiniRPG.Shared/Module/Network/` | 4 | 29 | 边界清楚 |
| `MiniRPG.Shared/Module/Panel/` | 1 | 14 | 边界清楚 |

## 4. 重点越界模块

### 4.1 `GameSessionModule`

文件规模：1366 行 / 75 函数。

它现在同时承担了至少五组职责：

- 新开局与世界初始化：`NewGame`、`InitializeWorld`
- 世界目录管理：`CreateWorld`、`DeleteWorldSaveData`、`DeleteWorldAssets`、`ListWorlds`、`ListWorldCharacters`
- 读档/预读/恢复：`PrepareLoadGame`、`PrepareLoadWorldCharacter`、`CommitPreparedLoad`
- Continue 状态管理：`TryContinue`、`ResolveContinueTarget`、`SaveContinueStateForWorldCharacter`
- 楼层切换与会话上下文：`TryUseStairs`、`ChangeFloor`、`ResetSessionContext`

这已经不是“会话模块”单一职责，而是“世界目录 + 存档编排 + continue 策略 + 楼层切换 + 初始化入口”的复合体。

建议拆成：

- `SessionBootstrapService`
- `WorldCatalogService`
- `SaveLoadCoordinator`
- `ContinueStateService`
- `FloorTransitionService`

### 4.2 `ServerActionGateway`

文件规模：779 行 / 42 函数。

它的问题不是“命令多”，而是“命令分发之外还亲自做太多事情”。当前函数簇同时覆盖：

- 命令总分发：`Execute`
- 行动家族处理：`ExecuteInteraction`、`ExecuteTradeBuy`、`ExecuteCombatAttack`、`ExecuteUseSkill`
- 容器与交易保留：`TryReserveIfNeeded`、`BuildTradeReservationKey`、`BuildContainerReservationKey`
- 容器/库存持久化：`PersistContainer`
- 奖励与掉落：`GenerateLoot`、`ResolveRewardActor`

作为 gateway，保留统一入口是合理的；但保留策略、容器持久化和奖励生成继续塞在这里，就已经越界。

建议拆成：

- `ServerActionDispatcher`
- `InteractionCommandHandler`
- `InventoryAndContainerCommandHandler`
- `TradeCommandHandler`
- `CombatCommandHandler`
- `ReservationService`

### 4.3 `AutoTestModule`

文件规模：1607 行 / 43 函数。

它把以下职责都放在了一起：

- 测试主流程：`RunAllSafeAsync`、`RunAllCoreAsync`
- 场景目录与调度：`BuildScenarioSequence`、`RunScenarioAsync`
- 多个测试场景实现：`RunResourceSmokeAsync`、`RunCombatArenaScenarioAsync`、`RunBenchmarkSuiteAsync`
- 报告与 case 记录：`RecordCase`、`CreateRunReport`
- 截图/探针/保存文件修改：`CaptureSnapshotSafely`、`TryReadSaveVersion`、`TryRewriteSaveVersion`

这已经不是单纯“测试模块”，而是“runner + scenario pack + IO helper + report builder”的组合包。

建议拆成：

- `AutoTestRunner`
- `AutoTestScenarioCatalog`
- `AutoTestSaveProbeService`
- `AutoTestCaptureService`
- `AutoTestReportRecorder`

### 4.4 `TileMapRenderModule`

文件规模：2102 行 / 109 函数，是当前最明显的渲染总控胖模块。

它至少同时承担了六组职责：

- 2D TileMap 图层初始化与可见性：`Init`、`ClearLayers`、`SetTileMapLayersVisible`
- 地形/实体/地面物品绘制：`RenderTerrainCell`、`RenderEntityCell`、`RenderGroundItems`
- 天气表现与屏幕特效：`RenderWeatherFx`、`TryRenderWeatherShaderFx`、`EnsureWeatherScreenFxOverlay`
- 2D/2.5D 模式切换：`ToggleRenderMode`、`CycleIsometricLightingProfile`
- Sprite 池和资源缓存：`AcquireEntitySprite`、`AcquireGroundItemSprite`、`ResolveWeatherTexture`
- 性能统计：`CommitPerfFrame`

这已经超出“2D TileMap 渲染器”边界，实质上是渲染子系统总线。

建议拆成：

- `TileLayerRenderer`
- `EntityAndGroundSpritePass`
- `WeatherFxController`
- `RenderModeRouter`
- `RenderPerfRecorder`

## 5. 偏胖但暂未越界的模块

这些模块目前还可以保留，但已经是下一批重构候选：

- `Module/Render/IsometricVoxelRenderer.cs`
  - 仍属于 2.5D 渲染域，但同时做了纹理合成、绘制排序、实体可视映射、hover 高亮与相机更新，过胖明显。
- `Module/Panel/SettingsPanelModule.cs`
  - 仍属于设置面板，但标签页切换、选择模型、绑定编辑模式、语言切换和动态状态渲染全挤在一个文件里。
- `Module/Panel/PanelManager.cs`
  - 仍属于面板焦点管理，但“注册、焦点栈、节点命中、键盘路由、被动面板适配”已经形成多块子逻辑。
- `Module/Panel/PanelDragService.cs`
  - 仍属于面板拖拽/布局域，但直接拖拽、编辑态、持久化、floating/docked 切换全在一个类。
- `Module/Panel/PanelHoverChromeService.cs`
  - 仍属于 hover chrome，但“悬浮条 UI 构建 + popup 布局 + 拖拽联动 + 面板外观设置”耦合偏重。
- `Module/InputBindingService.cs`
  - 绑定解析、默认值构建、占位冲突检测、磁盘持久化都在一个服务中，职责仍同域但过重。
- `Module/LookModule.cs`
  - 作为只读信息聚合器是合理的，但当前对天气、地面、同格生物、肢体、可见性等读取面过广。

## 6. 当前边界最健康的区域

这些区域的函数职责最整齐：

- `MiniRPG.Shared/Module/Network/*`
  - client/server/lifetime/http adapter 的分工很清楚。
- `MiniRPG.Shared/Module/Render/*`
  - `FogOfWarTracker` 与 `ViewModes` 边界干净。
- `Module/Network/*`
  - 本地 server launcher 和 noop launcher 的替换点清晰。
- 多数具体面板
  - 例如 `ChestPanelModule`、`DialogPanelModule`、`QuestPanelModule`、`TradePanelModule`、`StatusModule` 都在做单一 UI 责任。

## 7. 推荐重构顺序

1. 先拆 `GameSessionModule`
   - 这是跨“世界目录 / 存档 / continue / 开局 / 楼层切换”的总耦合源。
2. 再拆 `TileMapRenderModule`
   - 这是渲染侧最大的复杂度中心，也最容易继续膨胀。
3. 再拆 `ServerActionGateway`
   - 让命令处理按家族落到独立 handler。
4. 最后拆 `AutoTestModule`
   - 这会改善可读性和后续自动测试扩展速度。

## 8. 配套矩阵

逐文件结果已经单独整理在：

- [`MODULE_BOUNDARY_MATRIX_2026-04-12.md`](./MODULE_BOUNDARY_MATRIX_2026-04-12.md)

那份矩阵覆盖全部 100 个 module 文件，方便后续做分批重构排期。
