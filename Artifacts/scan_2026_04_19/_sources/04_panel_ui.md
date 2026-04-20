I've now read all the code in scope. Here is the complete audit report.

# Module/Panel + UI 审计报告

## 0. 摘要

共扫描 42 个 `.cs` 文件（Module/Panel）、6 个 Module/Audio 文件、Module/WorldManagerModule.cs、Data/UI/design_tokens.json 及 i18n 消费机制。发现 **5 条严重缺陷**、**7 条主要缺陷**、**8 条次要缺陷** 及若干设计/流程层观察。整体框架质量中等偏上——ListPanelBase 池化、RowStyleHelper 的 ThemeTypeVariation 路线、PanelTransition 的 sequence 防覆盖、UIScaleService 的 baseline 快照机制、UIAccessibilityService 的模式分离均为良好实践。核心问题集中在：StyleBoxFlat 热路径未缓存、RichTooltipLayer 信号泄漏、BBCode 硬编码颜色绕过 design_tokens、以及 SettingsPanelModule 体量过大。

---

## 1. 严重缺陷（必修：崩溃 / 资源泄漏 / 焦点失锁 / 设置丢失）

### S-1 RichTooltipLayer 信号连接不可断开，控件生命周期外泄漏

- **文件**: `Module/Panel/RichTooltipLayer.cs` 行 92-94
- **现象**: `Attach()` 对每个 control 用匿名 lambda 挂 `MouseEntered`/`MouseExited`/`TreeExiting`。lambda 引用未存储，`Detach()` 仅从 `_sources` 字典移除 key，不断开信号连接。
- **证据**:

```92:94:Module/Panel/RichTooltipLayer.cs
		control.MouseEntered += () => OnHoverStart(control);
		control.MouseExited += () => OnHoverEnd(control);
		control.TreeExiting += () => Detach(control);
```

`Detach` 只做 `_sources.Remove(control)`，不调用任何 `-=`。
- **风险**: 如果同一个 control 反复 Attach/Detach（如 ListPanelBase 行池化重用），信号堆积导致每次 hover 触发多次回调。长时间运行 GC pressure 增大。
- **建议**: 存储委托引用到字典，Detach 时 `-=` 断开；或改用 Callable + `IsConnected` 检查。

### S-2 TurnPanelModule 每帧 Refresh 时 new StyleBoxFlat 未缓存

- **文件**: `Module/Panel/TurnPanelModule.cs` 行 212-229
- **现象**: `ApplyChipStyle()` 每次调用创建 `new StyleBoxFlat`，由 `ConfigureChip` → `RenderQueue` → `Refresh()` → `FlushIfDirty()` 调用，在自动推进/观看模式下可达每帧一次。
- **证据**:

```212:229:Module/Panel/TurnPanelModule.cs
		var style = new StyleBoxFlat
		{
			BgColor = isCurrent
				? new Color(0.18f, 0.15f, 0.08f, 0.9f)
				: new Color(0.1f, 0.1f, 0.16f, 0.7f),
			// ...
		};

		chip.AddThemeStyleboxOverride("panel", style);
```

同时 `CreateQueueChip()` 行 268-285 也对 ProgressBar 的 `background`/`fill` 各 new 一个 StyleBoxFlat——但这一次是创建时一次性的，问题在 `ApplyChipStyle`。
- **风险**: 观看模式/自动推进中每回合 new 4-8 个 StyleBoxFlat + AddThemeStyleboxOverride，造成 GC 压力和不必要重绘。
- **建议**: 只创建 2 个缓存实例（current / non-current），复用。参考 `SkillBarModule.StyleCache` 已有的模式。

### S-3 WorldManagerModule 每次 RefreshView 创建 5×N 个未缓存 StyleBoxFlat

- **文件**: `Module/WorldManagerModule.cs` 行 787-791, 799-818
- **现象**: `ApplyRowButtonTheme` 对每个按钮调用 `CreateRowStyle` 5 次（normal/hover/pressed/focus/disabled），每次 `new StyleBoxFlat`。`RefreshView` 在 tab 切换、选择变更时调用 `RebuildWorldList`+`RefreshSelectedWorldDetails`+`RebuildScenarioList`+`RebuildLegacyList`，全部触发。
- **证据**:

```787:791:Module/WorldManagerModule.cs
		button.AddThemeStyleboxOverride("normal", CreateRowStyle(normalBackground, borderColor, 2));
		button.AddThemeStyleboxOverride("hover", CreateRowStyle(hoverBackground, borderColor, 2));
		button.AddThemeStyleboxOverride("pressed", CreateRowStyle(hoverBackground, focusBorderColor, 3));
		button.AddThemeStyleboxOverride("focus", CreateRowStyle(hoverBackground, focusBorderColor, 3));
		button.AddThemeStyleboxOverride("disabled", CreateRowStyle(RowDisabledBackground, RowDisabledBorderColor, 2));
```

- **风险**: 选择/切换时为所有行（worlds+characters+scenarios+legacy 四个列表）创建大量 StyleBoxFlat。主菜单交互密集时触发频繁。
- **建议**: 使用 `RowStyleHelper` 的 ThemeTypeVariation 模式，或至少用 `Dictionary<(Color,Color,int), StyleBoxFlat>` 做静态缓存。WorldManagerModule 的行样式状态有限（normal/selected/disabled × hover/non-hover），最多约 10 种组合。

### S-4 RichTooltipLayer 的 _pendingTimer 在 HoverEnd 时未取消

- **文件**: `Module/Panel/RichTooltipLayer.cs` 行 112, 121-128
- **现象**: `OnHoverStart` 创建 `SceneTreeTimer`，存入 `_pendingTimer`。`OnHoverEnd` 设置 `_currentHovered = null` 并 `HideTooltip()`，但 **不取消** `_pendingTimer`。虽然 Timeout 回调内有 `if (_currentHovered != pending) return;` 防止显示，但 timer 本身仍活着。快速连续 hover 不同控件时，多个 timer 同时活跃。
- **证据**:

```121:128:Module/Panel/RichTooltipLayer.cs
	private void OnHoverEnd(Control control)
	{
		if (_currentHovered == control)
		{
			_currentHovered = null;
			HideTooltip();
		}
	}
```

没有对 `_pendingTimer` 做任何处理。
- **风险**: 不会造成错误显示（guard 有效），但 SceneTreeTimer 不可主动取消（Godot 限制）。真正的问题是如果用户快速扫过 N 个控件，会产生 N 个 timer 同时活跃，轻微 GC/调度压力。严重程度中等但在低端设备上值得关注。
- **建议**: 在 `OnHoverEnd` 中将 `_pendingTimer = null`（已经做了引用覆盖但不够——在 `OnHoverStart` 开头加 `_pendingTimer = null` 之前设一个 generation counter 即可，类似 PanelTransition 的 sequence 模式）。

### S-5 SkillManagerModule 硬编码颜色与 UIColors 常量不一致

- **文件**: `Module/Panel/SkillManagerModule.cs` 行 251-257
- **现象**: 技能详情中 category 颜色使用硬编码 hex：`"#ff6666"`/`"#66ccff"`/`"#66ff88"`/`"#cccccc"`，而 UIColors 定义的对应值为 `HexCombat="#ff7366"`, `HexUtility="#73bfff"`, `HexSocial="#73e699"`, `HexNormal="#d1ccc0"`。**四个颜色全部不匹配。**
- **证据**:

```251:257:Module/Panel/SkillManagerModule.cs
		var color = skill.Category switch
		{
			"combat" => "#ff6666",
			"utility" => "#66ccff",
			"social" => "#66ff88",
			_ => "#cccccc",
		};
```

vs `UIColors.cs` 行 48-49: `HexCombat = "#ff7366"`, `HexUtility = "#73bfff"` 等。
- **风险**: 玩家在技能管理器和技能栏看到同一分类技能用不同颜色，视觉不一致。高对比度/色盲模式下更明显。
- **建议**: 统一替换为 `UIColors.HexCombat` / `UIColors.HexUtility` / `UIColors.HexSocial` / `UIColors.HexNormal`。

---

## 2. 主要缺陷

### M-1 design_tokens.json 完全未被代码消费

- **文件**: `Data/UI/design_tokens.json` 行 2
- **现象**: JSON 自身注释 `"尚未代码接入"`。全仓 Grep `design_tokens` 无代码引用。UIColors.cs 手工复制了颜色值。
- **风险**: 设计师改 tokens 后代码不会同步，双口径。目前只有 `UIThemeColorConsistencyTests.cs` 守护 UIColors ↔ Theme 一致性，但 tokens JSON 本身无任何守护。
- **建议**: 短期：把 UIThemeColorConsistencyTests 扩展为三方守护（tokens ↔ UIColors ↔ Theme）。中期：写 codegen 脚本从 tokens 生成 UIColors.cs。

### M-2 BBCode 硬编码颜色散布于 7+ 个面板文件

- **文件**: `QuestPanelModule.cs`(#aaaaaa, #66ff88, #888888), `SkillManagerModule.cs`(#888888, #ffdd88, #ffcc00, #aaaaaa, #cc8866, #666666), `SkillBarModule.cs`(#888888, #aaaaaa), `InventoryPanelModule.cs`(#66ff88, #666666, #cccccc, #ff4444), `ChestPanelModule.cs`(#666666), `ActorInspectPanelModule.cs`(#99ffaa), `DialogPanelModule.cs`(红/橙/黄/灰/绿/青色)
- **现象**: 均为 BBCode 内嵌 `[color=#xxxxxx]`，绕过 UIColors hex 常量。
- **风险**: 高对比度/色盲模式（UIAccessibilityService）只 override Theme 层颜色，BBCode 硬编码不受影响。视觉一致性和无障碍功能被削弱。
- **建议**: 将所有常用 hex 提取到 UIColors 常量，BBCode 中引用 `UIColors.HexXxx`。

### M-3 DialogPanelModule.FlushIfDirty 空实现

- **文件**: `Module/Panel/DialogPanelModule.cs` 行 48
- **现象**: `public void FlushIfDirty() { }` — Dirty 属性存在但永远不会被 flush。
- **证据**:

```47:48:Module/Panel/DialogPanelModule.cs
	public bool Dirty { get; set; }
	public void FlushIfDirty() { }
```

- **风险**: 如果外部设置 `Dirty = true`，下一帧 PanelManager 调 FlushIfDirty 什么也不做。目前 Dialog 的数据由 `Show()` 直接推送所以不触发，但违反 IPanel 契约。
- **建议**: 要么实现 `FlushIfDirty`（重新调用 `Show` 缓存最近参数），要么在 IPanel 接口中标明 Dialog 不需要脏刷新。

### M-4 AudioSettingsModule 硬编码颜色

- **文件**: `Module/Audio/AudioSettingsModule.cs` 行 33
- **现象**: `new Color(0.85f, 0.78f, 0.55f)` 用于 section title，不经过 UIColors。
- **证据**:

```33:33:Module/Audio/AudioSettingsModule.cs
		_sectionTitleLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.78f, 0.55f));
```

- **风险**: 与 UIColors.TextHeader `(0.92, 0.85, 0.70)` 不同。UIAccessibilityService 的高对比度/色盲调整不影响此处。
- **建议**: 改用 `UIColors.TextHeader` 或 Theme variation `"HeaderLabel"`。

### M-5 SettingsPanelModule 行样式颜色硬编码绕过 UIColors

- **文件**: `Module/Panel/SettingsPanelModule.cs` 行 261-268
- **现象**: `_rowNormalStyle` 和 `_rowSelectedStyle` 用 `new Color(0.1f, 0.1f, 0.16f, 0.85f)` 和 `new Color(0.18f, 0.15f, 0.08f, 0.95f)` — 与 UIColors.RowBg / UIColors.SelectedBg RGB 匹配但 alpha 不同（UIColors 版本无 alpha）。
- **证据**:

```261:268:Module/Panel/SettingsPanelModule.cs
		_rowNormalStyle = CreateRowStyle(
			new Color(0.1f, 0.1f, 0.16f, 0.85f),
			UIColors.IdleBorder,
			leftBorderWidth: 1);
		_rowSelectedStyle = CreateRowStyle(
			new Color(0.18f, 0.15f, 0.08f, 0.95f),
			UIColors.FocusBorder,
			leftBorderWidth: 4);
```

- **建议**: 使用 `new Color(UIColors.RowBg, 0.85f)` 构造器复用 UIColors 基色。

### M-6 SkillBarModule category 基色硬编码

- **文件**: `Module/Panel/SkillBarModule.cs` 行 547-550
- **现象**: `GetPalette` 返回硬编码的 category 基色，不在 UIColors 中。
- **证据**:

```545:550:Module/Panel/SkillBarModule.cs
	private static (Color BaseColor, Color AccentColor) GetPalette(string category) => category switch
	{
		"combat" => (new Color(0.2f, 0.1f, 0.12f), UIColors.TextCombat),
		"utility" => (new Color(0.1f, 0.15f, 0.22f), UIColors.TextUtility),
		"social" => (new Color(0.1f, 0.18f, 0.14f), UIColors.TextSocial),
		_ => (new Color(0.1f, 0.1f, 0.16f), UIColors.IdleBorder),
	};
```

AccentColor 正确引用 UIColors，但 BaseColor 是硬编码。
- **建议**: 将 category 基色加入 UIColors（如 `CombatBg`, `UtilityBg`, `SocialBg`），或至少加注释标明这些值的来源。

### M-7 StatusModule / ActorInspectPanelModule 光标高亮颜色硬编码且不一致

- **文件**: `Module/Panel/StatusModule.cs` 行 245; `Module/Panel/ActorInspectPanelModule.cs` 行 176
- **现象**: StatusModule 用 `UIColors.HexSelected` (`#ffe073`)，ActorInspectPanelModule 用硬编码 `#99ffaa`（绿色）。两个面板功能相同（状态查看 + 光标行高亮），颜色不同。
- **建议**: 统一使用 `UIColors.HexSelected` 或新增一个 `HexInspectHighlight` 常量。

---

## 3. 次要缺陷

### N-1 TurnPanelModule chip 颜色硬编码

- **文件**: `Module/Panel/TurnPanelModule.cs` 行 215-216, 270, 279
- **现象**: Chip 背景 `new Color(0.18f, 0.15f, 0.08f, 0.9f)` / `new Color(0.1f, 0.1f, 0.16f, 0.7f)` 及 ProgressBar 填充 `new Color(0.4f, 0.4f, 0.5f)` 均为硬编码。与 UIColors.SelectedBg / RowBg RGB 吻合但 alpha 不同。
- **建议**: 同 M-5，使用 `new Color(UIColors.SelectedBg, 0.9f)` 模式。

### N-2 PanelBorderHelper shadow pulse 颜色硬编码

- **文件**: `Module/Panel/PanelBorderHelper.cs` 行 48
- **现象**: `new Color(0.95f, 0.82f, 0.35f, 0.55f)` 与 design_tokens `focus_pulse.color: [0.95, 0.82, 0.35, 0.55]` 完全一致但硬编码。
- **建议**: 如果引入 tokens codegen，此处应由生成常量替换。当前低优先级。

### N-3 InventoryPanelModule 焦点指示器硬编码颜色

- **文件**: `Module/Panel/InventoryPanelModule.cs` 行 457
- **现象**: `" [color=#66ff88]●[/color]"` / `" [color=#666666]○[/color]"` — 焦点 dot 颜色硬编码。
- **建议**: 使用 `UIColors.HexSuccess` / `UIColors.HexDim`。

### N-4 QuestPanelModule detail 颜色硬编码

- **文件**: `Module/Panel/QuestPanelModule.cs` 行 135, 140-142, 150
- **现象**: `#66ff88`/`#88ccff`/`#ff6666`/`#aaaaaa`/`#888888` 全部硬编码。
- **建议**: 映射到 UIColors hex 常量。

### N-5 TradePanelModule tab 标签未本地化

- **文件**: `Module/Panel/TradePanelModule.cs` 行 34
- **现象**: `TabLabels = ["Buy", "Sell"]` — 英文硬编码，尽管 `Refresh()` 行 118-119 会用 `LocalizationService.T` 覆写，构造期间初始文本仍为英文。Tab 构造后到首次 Refresh 之间可能闪现英文。
- **证据**:

```34:34:Module/Panel/TradePanelModule.cs
	private static readonly string[] TabLabels = ["Buy", "Sell"];
```

- **建议**: 在构造后立即调 Refresh 或把 TabLabels 改为 i18n key。同样问题存在于 QuestPanelModule 行 25 `["Active", "Completed", "Failed"]` 和 SkillManagerModule/SkillBarModule 的 `["All", "Combat", "Utility", "Social"]`。

### N-6 ButtonHoverEffect.Attach null guard 后仍调用 AttachPressHook(button!)

- **文件**: `Module/Panel/ButtonHoverEffect.cs` 行 28
- **现象**: 当 `button == null` 时，分支直接调 `AttachPressHook(button!)` — 用 `!` 强制非空。虽然 `AttachPressHook` 开头有 `button == null` 检查，但 null-forgiving 运算符掩盖了逻辑意图。
- **证据**:

```26:29:Module/Panel/ButtonHoverEffect.cs
		if (button == null || button.HasMeta(HookedMeta))
		{
			AttachPressHook(button!);
			return;
		}
```

- **建议**: 分拆 `button == null` 和 `HasMeta` 两种情况。null 时直接 return。

### N-7 WorldManagerModule 自己管理行样式，不复用 RowStyleHelper

- **文件**: `Module/WorldManagerModule.cs`
- **现象**: 使用 `ApplyRowButtonTheme` + `CreateRowStyle` 自建一套行样式系统，与 `RowStyleHelper` 的 ThemeTypeVariation 模式完全不同。
- **建议**: 如果 WorldManager 的行需要 multi-line text 等特殊布局，至少将 CreateRowStyle 结果缓存到静态字典。

### N-8 LimbTargetPanelModule.CreateControl 创建的 cancelButton text 未本地化

- **文件**: `Module/Panel/LimbTargetPanelModule.cs` 行 122
- **现象**: `Text = "ui.common.cancel"` — 看似 i18n key 但作为 raw text 赋值。需要在外部调用 `LocalizationService.LocalizeTree` 才会生效。
- **证据**:

```117:123:Module/Panel/LimbTargetPanelModule.cs
		var cancelButton = new Button
		{
			Name = "CancelBtn",
			ThemeTypeVariation = "ActionButton",
			Text = "ui.common.cancel",
		};
```

- **风险**: 如果 `LocalizationService.LocalizeTree` 未被调用（或调用时机晚），按钮显示为 raw key。
- **建议**: 确认调用链中 LocalizeTree 覆盖了此节点。

---

## 4. 设计/流程层观察

### 4.1 SettingsPanelModule 917 行拆分建议

当前结构：
- **87 个字段声明**（行 35-112）：全部是 scene 节点引用
- **20 个 event 声明**（行 129-148）
- **构造器 305 行**（行 150-455）：GetNode + 信号连接 + 初始化
- **RefreshTexts 40 行**（行 482-523）：遍历设置全部 label
- **UpdateDynamicStateTexts 70 行**（行 662-732）：状态 → 文本映射
- **HandleCommand / HandleKeyInput / HandleMouseInput**：命令路由
- **行选择管理**：SelectRow, RefreshSelectionUi, EnsureSelectionVisible

**建议拆分方案**（不改公共 API，只内部重组）：

| 提取目标 | 行数 | 职责 |
|---|---|---|
| `SettingsPanelNodeBinder`（或构造器 helper） | ~120 | 从 scene 拿 87 个节点引用 + 信号连接 |
| `SettingsPanelTextResolver`（已存在） | 已有 | 纯函数 key 映射——现有设计良好 |
| `SettingsPanelSelectionModel`（已存在） | 已有 | tab/row 选择状态——现有设计良好 |
| 保留在 SettingsPanelModule | ~400 | Open/Close/ApplyState/HandleCommand + 用 binder 和 model |

核心痛点是构造器太长。将 GetNode 调用和信号 wiring 提取到一个 record/helper 中即可显著减重。**已抽出的 `SettingsPanelTextResolver` 和 `SettingsPanelSelectionModel` 是正确方向。**

### 4.2 design_tokens.json 与代码消费差距

- **tokens → UIColors**: 所有 `colors` 节点的 RGB 值在 `UIColors.cs` 中有手工镜像，两者一致（由 `UIThemeColorConsistencyTests` 守护 UIColors ↔ Theme，但 tokens 不在守护范围内）。
- **tokens → motion/spacing/radii/font_sizes**: 代码中 `PanelTransition.DefaultDuration=0.22f` 与 tokens `panel_fade_in_sec=0.22` 一致；`ButtonPressFlash.AttackSeconds=0.035f` 与 tokens `button_flash_attack_sec=0.035` 一致；`ButtonHoverEffect.HoverScale=1.03f` 与 tokens `button_hover_scale=1.03` 一致；`UIScaleService.BaselineFontSize=15` 与 tokens `baseline_font_size=15` 一致。**但全部是人工同步，无机器守护。**
- **tokens → ui_scale**: `min=0.75`, `max=1.5`, `step=0.05` 与 `UIScaleService.MinScale/MaxScale` 一致。
- **差距**: tokens 中的 `shadows` 节点（panel 阴影大小/颜色/偏移）在代码中找不到对应消费者——阴影由 Theme `.tres` 文件直接定义，tokens 只是参考。

### 4.3 Panel 生命周期不一致

| 面板 | Open | Close | OnFocus | OnBlur | FlushIfDirty |
|---|---|---|---|---|---|
| SettingsPanelModule | ✓ | ✓ (Visible=false) | 无（PanelManager 外部调） | 无 | 无（直接 ApplyState） |
| InventoryPanelModule | 无（Visible setter 触发 Refresh） | 由 host 控制 | 无 | `{ }` | ✓ (基类) |
| QuestPanelModule | ✓ (设 state+Refresh) | ✓ (Visible=false, state=null) | 无 | `{ }` | 无 |
| DialogPanelModule | Show() | Close() | 无 | `{ }` | `{ }` 空实现 |
| SkillBarModule | ✓ | ✓ | `{ }` | ✓ (清 hover) | ✓ |
| TradePanelModule | ✓ | ✓ (QueueFree rows) | 无 | `{ }` | 无 |

**核心问题**: 没有统一的 `OnShow`/`OnHide` hook。有的面板在 `Close()` 中清状态（QuestPanelModule 设 `_state=null`），有的不清（InventoryPanelModule）。`IPanel` 接口提供了 `OnFocus`/`OnBlur` 但它们的语义是焦点而非可见性。

### 4.4 TradePanelModule.Close() 额外 QueueFree 行

- `TradePanelModule.Close()` 行 102 做 `foreach (var row in _itemRows) row.QueueFree(); _itemRows.Clear();`——这是唯一在 Close 时销毁行的面板。其他 ListPanelBase 子类不这样做。
- 这意味着 Trade 面板 re-Open 时需要从零重建，而其他面板复用池化行。
- 不是 bug，但与 ListPanelBase 设计意图不一致。

### 4.5 SettingsFlowCoordinator 事件转发链路

- `SettingsFlowCoordinator` 行 183-201 有 19 行一对一事件转发：`_settings.XxxRequested += () => XxxRequested?.Invoke()`。
- 这是为了让上层只依赖 Coordinator 而非直接依赖 SettingsPanel，设计上可以接受。
- 但如果继续增加设置项，每增一项需要改 `ISettingsOverlay` + `SettingsPanelModule` + `SettingsFlowCoordinator` + `GameplayCommandCoordinator` 四处。可考虑改为 `event Action<SettingsCommand>` 单一通道 + enum 派发。

---

## 5. 已确认良好的实践

1. **ListPanelBase 行池化** — `RebuildRows` 正确 QueueFree 多余行、复用现有行、cursor clamp，7 个子类均正确继承。
2. **RowStyleHelper 使用 ThemeTypeVariation** — 完全避免运行时 `AddThemeColorOverride`，性能最优路线。
3. **PanelTransition 的 sequence 防覆盖** — FadeIn/FadeOut 竞争时用递增 sequence 防止旧回调覆盖新状态。
4. **UIScaleService 的 baseline 快照** — 首次 Apply 时 `CaptureBaseline`，后续从 baseline 计算，防止连续缩放 drift。
5. **UIAccessibilityService 与 UIScaleService 独立** — 互不依赖，各自 Apply Theme，baseline 缓存互不干扰。
6. **SkillBarModule 的 StyleCache** — 用 `Dictionary<string, StyleBoxFlat>` 缓存所有组合，正确做法。TurnPanelModule 和 WorldManagerModule 应参考此模式。
7. **PanelManager 的 focus stack + PruneInvalidPanels** — 焦点恢复链路健壮，处理了面板被外部关闭/失效的边界。
8. **PanelFocusCapture** — 简洁、递归查找首个可交互控件、正确检查 `IsQueuedForDeletion`。
9. **ButtonPressFlash / ButtonHoverEffect** — 动画自动 QueueFree，幂等 hook 检查（HasMeta），不会重复挂载。
10. **ToastOverlay** — MaxVisible 限制 + pending 队列 + 退场后自动 QueueFree + DispatchPending 补位。ValidityCheck 在 RetireToast 中正确处理。
11. **PanelLayoutStore 的 JSON 序列化** — try-catch + fallback to empty，不会因 parse 失败崩溃。Save/Load 对称。
12. **SettingsPanelTextResolver / SettingsPanelSelectionModel** — 纯函数/纯状态分离，可独立测试，是 SettingsPanelModule 瘦身的正确方向。

Now I have all the information I need to write the comprehensive audit report. Let me compile my findings.

Key findings from my analysis:

**Bugs/Code defects:**

1. **RichTooltipLayer signal leak** - `Attach()` at line 92-94 hooks `MouseEntered`, `MouseExited`, `TreeExiting` via lambda closures every time but `Detach()` only removes from `_sources` dict - it does NOT disconnect the signals. When `Attach` is called again on the same control (wasAttached=true path), it only replaces the factory but the old lambdas still fire. And if the control is later re-attached after being detached, old signals pile up.


2. **RichTooltipLayer _pendingTimer not cancelled** - When `OnHoverEnd` fires, the pending timer from `OnHoverStart` isn't cancelled, so if the user hovers in and out quickly, the timeout callback still executes even though it checks `_currentHovered`.
3. **TurnPanelModule creates new StyleBoxFlat every call** - `ApplyChipStyle()` creates a fresh `StyleBoxFlat` instance each time it's invoked, which happens repeatedly from `ConfigureChip`.
4. **WorldManagerModule allocates StyleBoxFlat objects excessively** - `ApplyRowButtonTheme` spawns 5 new `StyleBoxFlat` objects per button during view refreshes, with no caching mechanism in place.
5. **SettingsPanelModule hardcodes color values** - Color definitions are hardcoded directly in the code rather than referencing the design tokens they're meant to correspond to.
6. **Multiple modules scatter hardcoded colors throughout** - SkillBarModule, TurnPanelModule, and PanelBorderHelper all define colors inline instead of pulling from a centralized color system, and BBCode has scattered hex values embedded in various files.

7. **AudioSettingsModule and SettingsPanelModule have additional issues** - AudioSettingsModule hardcodes a section title color that doesn't reference UIColors, and SettingsPanelModule connects signals in the constructor but never disconnects them, which could cause memory leaks.

8. **RichTooltipLayer uses anonymous lambdas for signal connections** - The MouseEntered, MouseExited, and TreeExiting signals are connected with lambdas that can't be individually disconnected, creating signal connection leaks on the Godot side even though the safety check prevents actual harm.

9. **DialogPanelModule's FlushIfDirty method is empty** - The Dirty flag exists but the FlushIfDirty method does nothing, suggesting incomplete implementation.

10. **InventoryPanelModule hardcodes weight color values** - The overweight indicator uses hardcoded hex colors instead of referencing the UIColors constants.

11. **PanelBorderHelper duplicates StyleBoxFlat without explicit cleanup** - A duplicated StyleBoxFlat is created for the focus pulse effect and removed after timeout, but the duplicate itself isn't explicitly freed.

12. **SkillManagerModule hardcodes skill category colors** - Several skill categories use hardcoded hex values that partially match UIColors constants but don't align consistently across the codebase.

The design_tokens.json file explicitly states it hasn't been wired into code yet, so UIColors.cs manually duplicates all color values instead. This means changes to the design tokens won't automatically propagate to the UI. Additionally, DialogPanelModule has a dirty flag that's set but never actually flushed, so data changes while the dialog is visible won't trigger updates. SettingsPanelModule is also extremely large at 917 lines, containing 87 field declarations, roughly 30 event handlers, and over 160 lines just for constructor wiring.

There's also significant boilerplate in SettingsFlowCoordinator where 19 lines are dedicated to pure pass-through event forwarding, and WorldManagerModule creates new StyleBoxFlat objects per button without caching them, which is inefficient compared to how RowStyleHelper uses ThemeTypeVariation. Finally, the panel lifecycle is inconsistent across modules—some use Open/Close methods, others just toggle Visible, and the cleanup logic varies significantly between implementations like QuestPanelModule and DialogPanelModule.

Most ListPanelBase subclasses properly reuse the base implementation, but SkillBarModule breaks the pattern with its own grid-based selection model, and both StatusPanelModule and ActorInspectPanelModule duplicate cursor management logic instead of inheriting it. There's also a flow defect where the settings flow doesn't properly flush other panels when values like language or UI scale change, leaving cached text stale. WorldManagerModule's button creation captures the index variable correctly since buttons are only added at the end, so the lambda references remain valid.