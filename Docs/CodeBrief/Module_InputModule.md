# `Module/InputModule.cs`

## 职责一句话

输入模块：在“动作模式（按键直出命令）”与“打字模式（LineEdit 输入）”之间切换，并把所有输入统一转成 `CommandReceived(string)` 事件。

## 关键概念

- **动作模式**：`WASD/L/R/Space/Esc/F5/F9...` 直接映射成命令字符串
- **打字模式**：`LineEdit` 接收文本，回车提交后变成命令字符串（自动 trim + lower）

## 主要 API

- `CommandReceived: event Action<string>`
- `EnterActionMode()`：
  - `_typingMode = false`
  - `LineEdit.ReleaseFocus()`
- `EnterTypingMode()`：
  - `_typingMode = true`
  - `LineEdit.GrabFocus()`
- `HandleKeyInput(InputEventKey key) -> bool`
  - 打字模式：
    - `Esc` 退出打字模式并消费事件
    - 其他按键不消费（交给 `LineEdit`）
  - 动作模式：
    - 通过 `Keycode` 映射到命令：
      - `W/A/S/D` → `"w/a/s/d"`
      - `L` → `"look"`
      - `R` → `":render"`
      - `Space` → `"enter"`
      - `Esc` → `":settings"`
      - `Enter/T` → `":typing"`（进入打字模式）
      - `F5` → `":quicksave"`
      - `F9` → `":quickload"`
    - 除 `":typing"` 外会直接触发 `CommandReceived(cmd)`

## 与 `Main.cs` 的连接方式

- `Main._UnhandledInput` 调用 `HandleKeyInput`
- `Main._Ready` 订阅 `CommandReceived += OnCommand`

## 评估关注点

- **输入一致性**：命令协议是纯字符串（如 `":settings"`），易扩展但缺少类型安全
- **焦点状态**：`FocusExited` 会把 `_typingMode` 设为 false，避免 UI 失焦后仍认为在打字模式

