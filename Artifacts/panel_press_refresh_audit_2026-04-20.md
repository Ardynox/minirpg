# 面板 press→Refresh 同款 bug 巡检 — 2026-04-20

## 起因

今天修了 `InventoryGridPanelModule` 的"左键拖不动"bug（commit `576cbfdf`），元因是
**`mb.Pressed` 分支立刻调 `Refresh()` → `QueueFree` 当前源 Control → Godot drag
threshold 还没到、源就被销毁、drag 永远启动不了**。

担心其他交互面板有同款隐患——本报告做一次预防性 grep 巡检。

## 扫描方法

1. `rg "_GetDragData|_CanDropData|_DropData|SetDragForwarding"` 找全仓所有
   Godot 4 drag-drop 实现点。
2. `rg "GuiInput\s*\+="` 找所有 GuiInput 注册（drag 必须经过 `_gui_input` →
   drag threshold 才能启动）。
3. `rg "mb\.Pressed"` 找所有"鼠标按下时立刻处理"的代码点。
4. 交叉对比，定位"既注册 GuiInput 又自己实现 drag-drop API"的双重命中文件。

## 关键发现

**整个仓库里只有一个文件用 Godot 4 drag-drop API**：
`Module/Panel/InventoryGridPanelModule.cs`。

其他文件提到 `_GetDragData / SetDragForwarding` 都是文档引用（`Docs/界面与面板.md`、
`Module/Panel/README.md`、`Artifacts/wip*.md`）或本次修复的内联注释，**不是真正的代码实现**。

## 各面板 GuiInput 模式审计

| 文件 | 模式 | 是否有 drag-drop | press→Refresh 风险 |
| --- | --- | --- | --- |
| `Module/Panel/InventoryGridPanelModule.cs` | DragDropControl override + GuiInput 选中/右键 | ✅ 真有 | ✅ **已修**（press 不再 Refresh，commit `576cbfdf`）|
| `Module/Panel/ListPanelBase.cs` | `Button.Pressed` 事件 + `HandleRowGuiInput` 钩子 | ❌ 无 | ❌ 安全：`Button.Pressed` 是 release 触发 |
| `Module/Panel/TradePanelModule.cs` | 继承 ListPanelBase，`HandleRowGuiInput` 仅响应右键 | ❌ 无 | ❌ 安全：左键不进路径 |
| `Module/Panel/ChestPanelModule.cs` | 同上 | ❌ 无 | ❌ 安全 |
| `Module/Panel/GroundPanelModule.cs` | 同上 | ❌ 无 | ❌ 安全 |
| `Module/Panel/SettingsPanelModule.cs` | `WireRowSelection` press → `SelectRow`（rebuild 部分行样式）| ❌ 无 | ❌ 安全：不支持 drag |
| `Module/Panel/PanelHoverChromeService.cs` | 面板自身拖动（拖动整个 Panel 位置） | 自定义路径 | ❌ 安全：走 `PanelDragService`，与"面板内物品拖拽"是两套机制 |
| `Module/Panel/PanelDragService.cs` | 同上 | 自定义路径 | ❌ 安全：同上 |
| `Module/PartyHudModule.cs` | `OnGuiInput` press → `SetActive` | ❌ 无 | ❌ 安全：`SetActive` 不重建 PartyHud 本身 |

## 结论

- **当前仓库无同款 bug**。
- **未来风险**：如果新增任何面板要用 Godot 4 drag-drop API（比如把 ChestPanel 升级
  成网格、TradePanel 加拖拽换序、技能格拖到快捷栏），**必须遵守** `Module/Panel/README.md`
  「Godot 4 C# 面板实现常识」段中的 3 条契约：
  1. 源 Control 必须自己 override `_GetDragData`（`DragDropControl` 是现成封装）；
  2. 鼠标 Pressed 不要 `Refresh()` / `QueueFree` 子树，推迟到 Released；
  3. 复用 `DragDropControl`，不用 `SetDragForwarding + Callable.From`。

## 建议（不强制）

未来加 drag-drop 面板时，在 `Tests/MiniRPG.Tests/` 里加一个**反射守护测试**：
扫所有 `*PanelModule.cs`，如果发现某文件同时引用 `DragDropControl` 又在 `mb.Pressed`
分支调 `Refresh()`，xunit 直接红——把今天这条隐式契约变成机器可核查的硬约束。

本次未做，避免反射守护本身成为下次重构的负担；等真正第二个 drag-drop 面板出现时再加更划算。

## 决策标尺 0

N（本次只产出一份只读报告，不直接推进可玩闭环；但确认了"今天的 bug 没有兄弟在
其他面板"的事实，让用户对今天的修复范围心里有底，是协作基建）。
