# `Module/RenderModule.cs`

## 职责一句话

渲染模块：把“二维字符串地图”渲染成可显示文本；支持 ASCII（BBCode 着色 + 等宽字体）与 Emoji 两种模式。

## 关键结构

- `RenderMode`：`Ascii` / `Emoji`
- `RenderModule.Mode`：当前渲染模式（默认 Emoji）
- `UsesBBCode`：Ascii 时为 true（上层需要开启 `RichTextLabel.BbcodeEnabled`）

## 主要 API

- `ToggleMode() -> RenderMode`
- `SetMode(RenderMode)`
- `RenderMap(List<List<string>> map) -> string`
  - 逐行拼接 `CellText(c)`，每行末尾加 `\n`
- `ApplyFont(RichTextLabel panel)`
  - Ascii：设置 `SystemFont`（优先 Consolas/Courier New）和字号 22
  - Emoji：移除主题字体覆盖，交给系统默认

## 映射规则

- Ascii：用 BBCode `[color=...]` + 2 字符宽（例如墙是 `██`，地面 `· `）
- Emoji：把 `"#" "." "P" "G" "S" "K" "N" "D" ">" "<"` 映射为对应 emoji

## 评估关注点

- **等宽问题**：Emoji 的宽度在不同平台/字体上可能不一致；Ascii 模式更稳定
- **职责边界**：模块只负责字符映射与字符串构建，不依赖 `GameState`

