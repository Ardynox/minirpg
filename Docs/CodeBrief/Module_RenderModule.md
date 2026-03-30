# `Module/RenderModule.cs`

## 职责一句话

渲染模块：把"二维字符串地图"渲染成可显示文本；支持 ASCII（BBCode 着色 + 等宽字体）与 Emoji 两种模式。

## 关键结构

- `RenderMode`：`Ascii` / `Emoji`
- `RenderModule.Mode`：当前渲染模式（默认 Emoji）
- `UsesBBCode`：Ascii 时为 true（上层需要开启 `RichTextLabel.BbcodeEnabled`）

## 主要 API

- `ToggleMode() -> RenderMode`：切换渲染模式
- `SetMode(RenderMode)`
- `RenderMap(List<List<string>> map) -> string`
  - 逐行拼接 `CellText(c)`，每行末尾加 `\n`
- `ApplyFont(RichTextLabel panel)`
  - Ascii：设置 `SystemFont`（优先 Consolas/Courier New）和字号 22
  - Emoji：移除主题字体覆盖，交给系统默认

## 映射规则

### Ascii（BBCode 着色）

| 字符 | 颜色 | 含义 |
|------|------|------|
| `#` | #555555 | 墙 |
| `.` | #333333 | 地面 |
| `P` | #44ee44 | 玩家 |
| `M` | #ee4444 | 怪物 |
| `G` | #44cc44 | 哥布林 |
| `S` | #44ddaa | 史莱姆 |
| `K` | #cccccc | 骷髅 |
| `T` | #ffcc44 | 商人 |
| `E` | #44aaff | 村长 |
| `V` | #88cc88 | 村民 |
| `N` | #aa44ff | 巢穴 |
| `H` | #aa8844 | 房屋 |
| `D` | #ffaa00 | 门 |
| `>` | #00ccff | 下行楼梯 |
| `<` | #00ccff | 上行楼梯 |

### Emoji

| 字符 | Emoji | 含义 |
|------|-------|------|
| `#` | ⬛ | 墙 |
| `.` | ⬜ | 地面 |
| `P` | 🙂 | 玩家 |
| `M` | 👾 | 怪物 |
| `G` | 👺 | 哥布林 |
| `S` | 🟢 | 史莱姆 |
| `K` | 💀 | 骷髅 |
| `T` | 🧑 | 商人 |
| `E` | 👴 | 村长 |
| `V` | 😐 | 村民 |
| `N` | 🕳️ | 巢穴 |
| `H` | 🏠 | 房屋 |
| `D` | 🚪 | 门 |
| `>` | ⬇️ | 下行楼梯 |
| `<` | ⬆️ | 上行楼梯 |

## 评估关注点

- **等宽问题**：Emoji 的宽度在不同平台/字体上可能不一致；Ascii 模式更稳定
- **职责边界**：模块只负责字符映射与字符串构建，不依赖 `GameState`
- **扩展性**：添加新字符只需在 `AsciiCell`/`EmojiCell` 中添加映射