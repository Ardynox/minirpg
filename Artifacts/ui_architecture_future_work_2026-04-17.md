# UI 架构未来工作（2026-04-17）

本次 UI 优化的 P3 梯队里有两项适合单独规划、不在本次动代码的方向。下次改时照这份文档走。

## 一、P3-19：`Module/Panel/` 目录重组

### 现状

35 个 `.cs` + `.uid` 文件全部平铺在 `Module/Panel/` 下，包含：

| 类别 | 代表文件 |
|---|---|
| 框架 / 核心 | `PanelManager.cs`、`IPanel.cs`、`PanelLayoutService.cs`、`PanelLayoutStore.cs`、`PanelDragService.cs` |
| 视觉 / 动画 | `PanelBorderHelper.cs`、`PanelHoverChromeService.cs`、`PanelButtonScaleService.cs`、`PanelTransition.cs`、`ButtonHoverEffect.cs`、`ButtonPressFlash.cs`、`PanelFocusCapture.cs` |
| 可访问性 / 主题 | `UIColors.cs`、`UIScaleService.cs`、`UIAccessibilityService.cs` |
| 辅助 | `RowStyleHelper.cs`、`ListPanelBase.cs`、`TabHelper.cs`、`SettingsPanelTextResolver.cs`、`UIDevProbe.cs` |
| 具体面板 | `SettingsPanelModule.cs`、`PauseMenuPanelModule.cs`、`InventoryPanelModule.cs`、`ChestPanelModule.cs`、`GroundPanelModule.cs`、`TradePanelModule.cs`、`DialogPanelModule.cs`、`QuestPanelModule.cs`、`SkillBarModule.cs`、`SkillManagerModule.cs`、`DebugPanelModule.cs`、`TurnPanelModule.cs`、`TurnControllerPanelModule.cs`、`LimbTargetPanelModule.cs`、`LayoutEditBarModule.cs`、`StatusModule.cs`、`ActorStatusTextBuilder.cs`、`ActorInspectPanelModule.cs`、`SettingsPanelSelectionModel.cs` |

### 触发条件（什么时候做）

按 `AGENTS.md` 的"触发式维护"原则，**只有当搜索经常命中不相关目录导致效率下降时才重组**。目前没有明显症状。

### 建议目录（真做时）

```
Module/Panel/
  Core/        PanelManager / IPanel / PanelLayout* / PanelDragService
  Chrome/      PanelBorderHelper / PanelHoverChromeService / PanelButtonScaleService
               PanelTransition / ButtonHoverEffect / ButtonPressFlash / PanelFocusCapture
  Accessibility/  UIColors / UIScaleService / UIAccessibilityService / UIDevProbe
  Helpers/     RowStyleHelper / ListPanelBase / TabHelper
               SettingsPanelTextResolver / SettingsPanelSelectionModel
  Screens/     *PanelModule.cs / *ControllerModule.cs / StatusModule.cs / ActorStatusTextBuilder
```

单次 `git mv` + `dotnet build` 验证命名空间不要改（保持 `MiniRPG.Module.Panel`）。

### 风险

- 合并大量重命名会和并行进程的 `git restore` 冲突（本次 UI 改动就碰到过）。
- 测试里有 `File.ReadAllText(Path.Combine(ResolveRepoRoot(), "Module", "Panel", "PanelManager.cs"))` 这类硬编码路径，搬迁后要同步更新。

---

## 二、P3-20：`IPanel` 状态机显式化

### 现状

`IPanel` 的状态分布在多个布尔值：

```csharp
bool Visible { get; set; }
bool CanFocus { get; }
bool ConsumeUnhandledKeys { get; }
bool AllowGlobalClose { get; }
bool Dirty { get; set; }
```

真实生命周期包含但没被显式表达的状态：

- `Closed` → `Opening`（FadeIn 中） → `Open` → `Closing`（FadeOut 中） → `Closed`
- 额外分支：`Capturing`（如 KeyBindings 录入）、`Modal`（阻断全局快捷键）

目前：
- `Opening/Closing` 借助 `PanelTransition` 的 tween seq 隐式表达；
- `Capturing` 分散在 `SettingsPanelModule.IsCapturingKeyBindings` 这样的 per-panel 属性；
- `Modal` 由 `IModalInputLayer` 另外一套接口承接，和 IPanel 平行；
- 面板关闭时的清理逻辑（`OnPanelClosed`）靠 `PanelManager` 侧 `_focusStack` 兜底。

### 期望

一个 `PanelState` 枚举 + `IPanel.State` 属性，以及状态迁移规则明确在 `PanelManager` 里。好处：

- FadeOut 中用户再点 Open，不会出现"两套 tween 都在跑最后 Visible 乱了"。
- `IModalInputLayer` 和 `IPanel` 能合流到统一状态机。
- 面板自己不再需要 `IsCapturingKeyBindings` 这种特化属性，`PanelState.Capturing` 就够用。

### 迁移路径

1. 加 `enum PanelState`（Closed/Opening/Open/Closing/Capturing），默认 `Closed`。
2. `IPanel.State` 属性由 `PanelManager` 读写（只给 PanelManager 可写，面板自己只读）。
3. `PanelTransition.FadeIn/FadeOut` 把 state 迁移嵌入 tween 开始/结束回调。
4. `PanelManager.SwitchFocus` 检查 state：`Closing` 的 panel 拒绝 push 焦点。
5. 对现有每个 `IPanel` 实现做兼容 audit：是否有手动 set `Visible` 的地方，迁到 state API。
6. `IModalInputLayer` 改为 `IPanel` 的一个 state（`Modal`），去掉重复接口。

### 工作量

- 新枚举 + 接口扩展：半天
- 迁移 10+ 个 Panel 实现：1-2 天
- 验证 `PanelManager` 所有分支 + 回归所有 Panel 相关测试：1 天

合计 3 天，建议和 `SettingsPanelModule` 拆分（见 `settings_panel_refactor_plan_2026-04-17.md`）在**不同**批次做——两者都属于核心面板系统改造，同批做回归成本高。

### 验收

- `IPanel.State` 能在一帧内唯一描述面板运行时状态。
- 所有 FadeIn / FadeOut 的交叠 race 被状态机约束（tween seq + state check）。
- `PanelManager` 的 focus 栈操作从"靠约定"变成"根据 state 合法性判断"。
