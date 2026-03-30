# `Module/InputModule.cs`

## 职责一句话

输入模块：在"动作模式（按键直出命令）"、"打字模式（LineEdit 输入）"与"选择模式（数字键选择）"之间切换，并把所有输入统一转成 `CommandReceived(string)` 事件。

## 关键概念

- **动作模式**：`WASD/L/R/Space/Esc/F5/F9/F/I...` 直接映射成命令字符串
- **打字模式**：`LineEdit` 接收文本，回车提交后变成命令字符串（自动 trim + lower）
- **选择模式**：数字键直接输入选择编号（如选择菜单项）

## 主要 API

- `CommandReceived: event Action<string>`
- `EnterActionMode()`：
  - `_typingMode = false`，`_selectionMode = false`
  - `LineEdit.ReleaseFocus()`
- `EnterTypingMode()`：
  - `_typingMode = true`
  - `LineEdit.GrabFocus()`
- `EnterSelectionMode()`：
  - `_selectionMode = true`
  - `LineEdit.ReleaseFocus()`
- `HandleKeyInput(InputEventKey key) -> bool`

### 打字模式处理

- `Esc`：退出打字模式并消费事件
- 其他按键不消费（交给 `LineEdit`）

### 选择模式处理

- `0-9`：发送 `:select_N` 命令
- `Esc`：发送 `:select_cancel`，退出选择模式

### 动作模式处理

通过 `Keycode` 映射到命令：

- `W/A/S/D` → `"w/a/s/d"`（移动）
- `L` → `"look"`（观察）
- `R` → `":render"`（切换渲染模式）
- `F` → `":interact"`（交互）
- `I` → `":inventory"`（背包）
- `Space` → `"enter"`（上下楼）
- `Esc` → `":settings"`（设置）
- `Enter/T` → `":typing"`（进入打字模式）
- `F5` → `":quicksave"`
- `F9` → `":quickload"`

## 与 `Main.cs` 的连接方式

- `Main._UnhandledInput` 调用 `HandleKeyInput`
- `Main._Ready` 订阅 `CommandReceived += OnCommand`

## 评估关注点

- **输入一致性**：命令协议是纯字符串（如 `":settings"`），易扩展但缺少类型安全
- **焦点状态**：`FocusExited` 会把 `_typingMode` 设为 false，避免 UI 失焦后仍认为在打字模式
- **选择模式设计**：用于菜单选择，避免与普通文本输入冲突