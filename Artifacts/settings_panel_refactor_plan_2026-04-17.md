# SettingsPanelModule / MainAppFlowCoordinator 拆分设计（2026-04-17）

## 为什么现在不直接做

本次 UI 优化批次里，SettingsPanelModule（~750 行）和 MainAppFlowCoordinator（~1050 行）被列入 P1 拆分目标。但具体实施需要 Tests project 编译通过后做充分回归，而当前并行渲染重构让 Tests project 处于中间态（`IsometricRenderTests.cs` 引用的 `IsometricVoxelRenderer.BuildChunkTerrainSurfaceEntries` 已被移走但测试未同步）。为了不把拆分落在无测试保护的窗口里，先把设计落到这里。

这份文档只覆盖"下次做这件事的时候，人/AI 该怎么下手"，不是抽象架构讨论。

---

## 一、SettingsPanelModule 拆分

### 目标

- 单文件不超过 300 行。
- "每个 Section 只关心自己那几个 Row 的节点引用 + 事件 + 文本刷新"，不需要看 orchestrator。
- `SettingsPanelModule` 变成协调器：面板生命周期、Tab 切换、Section 之间的公共状态（focus selection、key capture）。

### 目标结构

```
Module/Panel/
  SettingsPanelModule.cs         （约 220 行）
  SettingsPanelTextResolver.cs   （已存在，不动）
  SettingsPanelSelectionModel.cs （已存在，不动）
  SettingsSections/
    ISettingsSection.cs          （section 的最小契约）
    DisplaySection.cs            （Language / Render / MapZoom / UiFontScale，约 210 行）
    GameplaySection.cs           （WatchMode / FastTurnMode，约 90 行）
    ControlOptionsSection.cs     （KeyboardTargeting / AutoNav / DebugPanel，约 130 行）
    BindingsSection.cs           （KeyBindings，约 90 行）
    SessionSection.cs            （Save / Load / MapEditor / LayoutEdit，约 120 行）
```

### `ISettingsSection` 契约

每个 Section 负责自己那块的节点 + 事件，不关心 tab 切换或 selection 模型：

```csharp
internal interface ISettingsSection
{
    IReadOnlyDictionary<SettingsPanelRowId, PanelContainer> RowPanels { get; }

    void UpdateStaticTexts();
    void UpdateDynamicState(SettingsUiState state, bool suppressToggleSignals);
    void ApplyVisibility(SettingsUiState state);  // 例如 WatchModeRow 只在 InGamePause 显示

    event Action<SettingsPanelRowId>? RowSelectionRequested;
    void ActivateRow(SettingsPanelRowId rowId);   // Enter 键激活选中项时调用
}
```

每个 Section 构造时接收对应 Row 路径 + `SettingsFlowEvents` 回调对象，把原本 `SettingsPanelModule` 里的 `WatchModeToggleRequested?.Invoke()` 之类直接冒泡到外层。

### `SettingsPanelModule` 保留职责

- `_panel` / `_titleLabel` / `_subtitleLabel` / `_footerHintLabel` / `_tabButtons`
- `PanelId` / `PanelNode` / `Visible` / `AudioSettingsHost`
- `Open` / `Close` / `ApplyState` / `RefreshTexts`（协调各 section 的对应方法）
- `HandleKeyInput` / `HandleMouseInput`（selection model + key bindings）
- `_selectionModel` + `SelectRow` / `ActivateSelectedRow`（delegate 到对应 section）
- `_rowPanels` 合并自各 section 的 `RowPanels`

### 迁移顺序（每步独立可回滚）

1. 抽 `GameplaySection`（最小，2 row）
2. 抽 `BindingsSection`（1 row + KeyBindingsView，边界清晰）
3. 抽 `SessionSection`（4 row，纯按钮）
4. 抽 `ControlOptionsSection`（3 row，含 checkbutton + 循环按钮）
5. 抽 `DisplaySection`（5 row，含 language option button + 4 个 `Adjust*` 按钮对）
6. 清理 `SettingsPanelModule` 剩余字段/方法

每一步做完都跑：

```powershell
dotnet build mini-rpg.sln --nologo
dotnet test Tests\MiniRPG.Tests\MiniRPG.Tests.csproj --filter "FullyQualifiedName~Settings"
python Tools\validate_i18n.py
```

### 测试影响

- `SettingsPanelModuleTests.ModuleBindingRoots_MatchCurrentSceneHierarchy`：检查 `const string ...RowPath` 字符串 + Scene 节点名。拆分后常量搬到各 Section，测试断言改为读取各 Section 源文件。
- `SettingsPanelModuleTests.SelectionModel_*`：只用 `SettingsPanelSelectionModel` + `SettingsPanelTextResolver`，不受影响。
- `SettingsFlowCoordinatorTests` / `SettingsFlowModalInputAdapterTests`：使用 `FakeSettingsOverlay`，不关心内部拆分。

---

## 二、MainAppFlowCoordinator 拆分

### 现状

1054 行，承担：

- 启动流程（`_onResourcesReady`、启动失败/重试）
- 主菜单 ↔ 游戏切换（`HandleContinue` / `HandleBackToMenu` / `HandleNewGame`）
- 世界管理器开关（`OpenWorldManager`）
- 设置流 sync（`_syncSettingsUiState`）
- 会话切换（`_sessionCoordinator.*`）
- 地图编辑器（`ToggleMapEditor`）
- 快速存读档（`HandleQuickLoadRequested`）
- 多人联机 return（`HandleMultiplayerReturnToMenu`）
- 世界管理器 status banner（已于 `fcf4f4b1` 抽到 `WorldManagerStatusBanner`）

### 目标结构

```
App/RuntimeUi/
  MainAppFlowCoordinator.cs          （约 300 行，保留 orchestrator）
  AppFlow/
    MainMenuFlowController.cs        （约 150 行，菜单 ↔ 游戏切换）
    WorldManagerFlowController.cs    （约 200 行，世界管理器相关）
    SessionSwitchFlowController.cs   （约 150 行，Continue/NewGame/QuickLoad）
    MapEditorFlowController.cs       （约 120 行，地图编辑器 open/close/toggle）
    SettingsStateSync.cs             （约 100 行，_syncSettingsUiState + SettingsFlowCoordinator 对接）
```

### 迁移原则

- 每个 FlowController 只读 orchestrator 提供的依赖（`Main`、`GameSessionModule`、`MenuModule` 等），不反向回调 `MainAppFlowCoordinator`。
- 事件仍由 `Main` 层注册（`Main.Wiring.cs`），orchestrator 只做参数整理后转发给 FlowController。
- 每个 FlowController 的构造参数全部走依赖注入（没有静态单例）。

### 迁移顺序

1. 抽 `SettingsStateSync`（最独立，没有跨流程依赖）
2. 抽 `MapEditorFlowController`（纯开关）
3. 抽 `WorldManagerFlowController`（和 `WorldManagerStatusBanner` 配套）
4. 抽 `SessionSwitchFlowController`（Continue/NewGame/QuickLoad，含确认弹窗）
5. 抽 `MainMenuFlowController`（最后，因为它是其它 FlowController 的回归点）

### 测试影响

- `MainMenuPanelVisibilityTests`、`MainStartupTests`：走 `Main` 层，不直接触 FlowController。
- 新增建议测试：每个 FlowController 加一个"给定 state + action → 预期 panel state"的小测试，覆盖分支逻辑（不走 Godot runtime）。

---

## 验收标准

| 检查 | 目标 |
|---|---|
| 单文件行数 | SettingsPanelModule ≤ 300 行 / MainAppFlowCoordinator ≤ 300 行 |
| 编译 | `dotnet build mini-rpg.sln` 0 警告 0 错误 |
| 测试 | `dotnet test` 全绿，新增 Section / FlowController 单测不少于 3 个/各 |
| i18n | `validate_i18n.py` 通过 |
| 文档 | `Docs/架构现状.md` 的"当前热点"段落把两个类从"胖"名单移除 |

## 不变量

- `ISettingsOverlay` 对外事件签名**不变**（`SettingsFlowCoordinator` 订阅不破）
- `MainAppFlowCoordinator` 的公共 `Open*` / `Handle*` 方法签名**不变**（`Main.Wiring` 订阅不破）
- `SettingsUiState` / `SettingsPanelRowId` 枚举**不变**
