# 列表面板继承 ListPanelBase 迁移审计（2026-04-17）

本次 UI 优化批次里 P1-10 的实际价值是"让统一键盘导航（PageUp/PageDown/Home/End）在更多面板里工作"。走查完 `Module/` 和 `Module/Panel/` 下所有与"列表/行/选择"相关的类型后，结论见下。

## 已经继承 ListPanelBase（7 个，不需要再动）

- `InventoryPanelModule`
- `GroundPanelModule`
- `ChestPanelModule`
- `TradePanelModule`
- `QuestPanelModule`
- `SkillManagerModule`
- `LimbTargetPanelModule`

这 7 个面板在 P1-9 的扩展后，子类只需要在各自 `HandleCommand` 里**优先调用 `HandleListCommand(cmd)`**，就能免费得到 `up/down/page_up/page_down/home/end/confirm` 行为。下一批可以逐个检查 `HandleCommand` 实现，把硬编码的 up/down 换掉。

## 候选未继承，经审视不建议迁移

- **`SaveBrowserModule` (112 行)**：不是"一列行可选"结构，是存档浏览对话框（可能含预览/缩略图 + 对话按钮），迁移到 ListPanelBase 会破坏现有对话流。标记为"只做 `IFocusableListPanel` 接口采用"候选，但工作量不值得。
- **`WorldManagerModule` (745 行)**：有多 Tab（Worlds/Characters/Saves）、每个 Tab 自己的列表，现在用 `IModalInputLayer` 承接键盘。自己实现了 `MoveSelection(int delta)`（private）。迁移路径：
  1. 把 MoveSelection 改为 public + 加 `RowCount`/`SelectedIndex`/`ActivateSelection`，实现 `IFocusableListPanel`。
  2. 但 WorldManager 的键盘仍由 `IModalInputLayer` 承接，和 `PanelManager.HandleKey` 是两套。要完全接入，需要先把它转为 `IPanel`，是一个大重构。
- **`MultiplayerHubModule`、`MultiplayerRoomPanelModule`、`CharacterCreationModule`、`DebugPanelController`**：要么是 dialog，要么是表单，没有"一列行可选"主结构。

## 建议的下一步（不是本次动）

1. 先让已有的 7 个 `ListPanelBase` 子类通过 `HandleListCommand` 拿到 PageUp/PageDown/Home/End 响应。预计工作量：每个 15 分钟，共 2 小时。
2. 如果 WorldManagerModule 真的要纳入统一导航，先完成 `Settings Panel` 拆分（见 `settings_panel_refactor_plan_2026-04-17.md`），再考虑 `WorldManagerModule` 的 IPanel 转换——两个 745 行的类同一周开工风险太大。
3. `SaveBrowserModule` 不做迁移，它的 UX 不属于"列表型面板"。

## 纳入 `Docs/` 与否

- 这是一次性审计，结论稳定后直接指向 `settings_panel_refactor_plan_2026-04-17.md` 的第 1 条工作即可，不进 `Docs/`。
