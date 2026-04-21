# Tools/banana — Nano Banana 2 出图 CLI

调用 **Gemini 3.1 Flash Image**（俗称 Nano Banana 2，2026-02 发布）出图。
解决 Cursor 内置 `GenerateImage` 工具的 4 个死结：

| Cursor 内置工具 | 本 CLI |
|---|---|
| 固定吐 1376×768 | 512px-4K 任选，1K/2K/4K 分级 |
| 不能出 1:8 / 8:1 sheet | 支持 16 种宽高比（含 1:8 / 8:1，可直接出 8 方向 vertical sheet） |
| 同 prompt 出 N 张风格漂 | thinking mode + 多角色一致性，5 角色 / 14 物体可保持一致 |
| 不能指定模型 | 默认 `gemini-3.1-flash-image-preview`，可 `--model` 覆盖 |

---

## ⚠ 重要：免费档不可用

**2026 年起 Google 把所有 image generation 模型移出免费档**（Imagen、Nano Banana 1、Nano Banana 2 全部）。
免费 key 调用 `gemini-2.5-flash-image` / `gemini-3.1-flash-image-preview` 等都会返回 `429 RESOURCE_EXHAUSTED, limit:0`。

要让本工具真出图，必须：

1. 去 https://aistudio.google.com/apikey
2. 旁边点 **"Set up Billing"**，绑一张信用卡
3. 同一个 key 自动从 Free tier 升到 Tier 1，**新用户绑卡首次有 $300 / 90 天试用额度**（约 4400 张 1K 图）

文本模型（`gemini-2.5-flash` 等）依然免费，本工具的 warmup 也用文本模型，所以不绑卡也能 dry-run + warmup 验证流程。

## 0. 一次性配置（约 5 分钟）

### 0.1 装依赖

```powershell
pip install -r Tools/banana/requirements.txt
```

### 0.2 申请 API key

去 https://aistudio.google.com/apikey 一键拿一个免费 key（5000 prompt/月免费，超出按量）。

### 0.3 配 key（二选一）

**方式 A：项目本地（推荐，不污染系统环境）**

```powershell
Copy-Item Tools/banana/.env.example Tools/banana/.env
notepad Tools/banana/.env   # 把 GEMINI_API_KEY=... 填好
```

`.env` 已在 `.gitignore`，绝对不会进 git。

**方式 B：系统环境变量**

```powershell
$env:GEMINI_API_KEY = "你的key"     # 仅当前 PowerShell 会话
# 或永久：
[System.Environment]::SetEnvironmentVariable("GEMINI_API_KEY", "你的key", "User")
```

### 0.3.5 中国大陆必读：配代理

Gemini API 的 endpoint `generativelanguage.googleapis.com` 在中国大陆直连不通（TCP 443 被墙）。
浏览器代理插件（SwitchyOmega、Chrome 内置代理等）**只对浏览器生效**，Python 进程不会走它。

打开你常用的代理客户端（Clash / v2rayN / Shadowsocks 等），看「端口设置」/「Listen Port」，
然后在 `Tools/banana/.env` 里追加（端口替换成你实际的）：

```
HTTPS_PROXY=http://127.0.0.1:7890
HTTP_PROXY=http://127.0.0.1:7890
```

常见客户端默认端口：

| 客户端 | 默认 HTTP 端口 |
|---|---|
| Clash for Windows | 7890 |
| Clash Verge | 7897 |
| v2rayN | 10809 |
| Shadowsocks (SOCKS5) | 1080（用 `socks5h://127.0.0.1:1080`） |

诊断命令（不用代理则超时，用了代理则 OPEN）：

```powershell
$tcp = New-Object System.Net.Sockets.TcpClient
if ($tcp.ConnectAsync("generativelanguage.googleapis.com", 443).Wait(5000)) {
    "TCP 443 OPEN"
} else {
    "TCP 443 BLOCKED — 需要代理"
}
$tcp.Close()
```

### 0.4 验证

```powershell
python Tools/banana/banana_gen.py `
    --style style_base --subject "single small wooden bucket" --negatives standard `
    --aspect 1:1 --size 1K `
    --out Artifacts/banana_smoke_test.png --dry-run
```

`--dry-run` 不调用 API，只打印拼好的 prompt 和预估成本，确认 prompt 看起来对就去掉 `--dry-run` 真出。

---

## 1. AI 直接调用（给后续 Cursor agent 看）

后续 Cursor agent 可以直接用 Shell 工具跑这个脚本，**不再用内置 `GenerateImage`**：

```powershell
python Tools/banana/banana_gen.py `
    --style style_base --subject "<主体一句话>" --negatives standard `
    --aspect 1:1 --size 1K `
    --out Artifacts/preview/<name>.png
```

要点：
- `--out` **必须在 `Artifacts/` 或 `Assets/` 下**，脚本会拒绝其他位置
- `--count N` 一次出 N 张候选（最多 4 张），文件名自动加 `_01` / `_02`
- 出图入仓前必须过 `Assets/Art/README.md` §5「读感验收」

---

## 2. 常用调用模板

### 2.1 单 prop（256×256 顶视）

```powershell
python Tools/banana/banana_gen.py `
    --style style_base --negatives standard `
    --subject "single small campfire: ring of weathered grey stones surrounding charred logs, warm orange flame, thin smoke wisp" `
    --aspect 1:1 --size 1K --count 4 `
    --out Artifacts/preview_props_2026-XX-XX/campfire.png
```

### 2.2 8 方向角色 sheet（1:8 vertical）

```powershell
python Tools/banana/banana_gen.py `
    --prompt-file Tools/banana/prompts/example_char_8dir.txt `
    --aspect 1:8 --size 2K `
    --out Artifacts/preview/char_base_human_8dir.png
```

> 提示：8 方向 sheet 最容易产生方向不一致或姿态飘的问题。先出 4 张候选，挑最稳的，必要时人工切片重组。

### 2.3 地表 tile（1:1 无缝预期）

```powershell
python Tools/banana/banana_gen.py `
    --style style_base --negatives standard `
    --subject "seamless top-down stone path tile, rough irregular flagstones, muted grey palette, designed for repetition" `
    --aspect 1:1 --size 1K --count 4 `
    --out Artifacts/preview/terrain_stone_path.png
```

> 注意：所谓"无缝"AI 不能硬保证，最终入仓前要 ImageMagick / GIMP 验证 4 角拼接。

### 2.4 完整自由 prompt（不拼接 style / negatives）

```powershell
python Tools/banana/banana_gen.py `
    --prompt "你自己整段写好的 prompt" `
    --aspect 1:1 --size 1K `
    --out Artifacts/preview/foo.png
```

### 2.5 本地兜底（无 API：`B2` / `B4` / `B7`）

计费不可用或只想先把路径填满时，可用 PIL 在 `Assets/` 下落占位 PNG（风格与 `b2_item_world_fill_local` 一致：土色块 + 线框）。

| 脚本 | 落点 |
|------|------|
| `python Tools/banana/b2_item_world_fill_local.py` | `Assets/Art/Placeholders/item_world/category_*.png` |
| `python Tools/banana/b7_ui_icons_fill_local.py` | `Assets/Art/Placeholders/ui_icons/surgery_*、room_*、capacity_*、limb_*` |
| `python Tools/banana/b4_faces_fill_local.py` | `Assets/Faces/...`（与 `Data/FaceParts/*.json` 中非空 `imagePath` 一一对应，当前 **62** 张） |
| `python Tools/banana/b11_facility_blueprint_fill_local.py` | `Assets/Art/Generated/facilities_blueprint/facility_<id>_blueprint.png`（与 `entity_render.json` 每条 `facility_*` 对应，当前 **10** 张；蓝图阶段渲染优先走该图，见 `IsometricVoxelRenderer`） |

替换 Banana 真图后无需删兜底脚本；合并前跑 `python Tools/validate_art_res_paths.py --catalog --combat-audit`。

---

## 3. 与 Banana 总表的关系

`Artifacts/banana资源生成提示词总表_2026-04-18.md` 是 prompt 内容资产（每条资源该写什么）。
本 CLI 是调用通道（怎么发出去）。两者解耦：

- 改 prompt 内容 → 编辑总表
- 加调用方式 / 改默认模型 / 改成本估算 → 编辑本目录

总表里 §F01 / §H01 那种带「res 路径 + prompt」的条目可以直接做成本 CLI 的批跑驱动文件，但**那是后续工作**，不在本批范围。

---

## 4. 成本与配额

- **1K image**：$0.067
- **2K image**：$0.134
- **4K image**：$0.268（估算，官方价目以 https://ai.google.dev/pricing 为准）
- 免费档：~5000 prompt/月

每次调用都会写一行到 `Tools/banana/.cache/usage.jsonl`：

```jsonl
{"ts":"2026-04-19T12:00:00+00:00","model":"gemini-3.1-flash-image-preview","size":"1K","count":4,"ok":true,"est_cost_usd":0.268}
```

盘账：

```powershell
Get-Content Tools/banana/.cache/usage.jsonl |
    ForEach-Object { ConvertFrom-Json $_ } |
    Measure-Object -Property est_cost_usd -Sum
```

---

## 5. 安全护栏

脚本硬编码了几条护栏，避免把账号烧光 / 把图写到奇怪的地方：

| 护栏 | 说明 |
|---|---|
| `--count` ≤ 4 | 单次最多 4 张，防误触发 100 张 |
| `--out` 必须在 `Artifacts/` 或 `Assets/` 下 | 不会写到 `Tests/` / `Module/` 等地方 |
| API key 不打印 | stdout / log 里都看不到 key |
| 失败也记账 | `usage.jsonl` 里 `ok:false` 行便于排查 |
| 默认 dry-run 友好 | prompt 拼接错可以先 `--dry-run` 看 |

要批量跑（比如总表里十几条一起），自己写个 PowerShell 循环调用本 CLI 即可，本脚本**不内置批量驱动**（避免一个错把所有钱烧光）。

---

## 6. 已知限制

| 项 | 状态 | 备注 |
|---|---|---|
| 透明背景 | ~80% 成功率 | 失败时 prompt 已要求 fallback 中性灰底，便于 keying |
| 8 方向 sheet 一致性 | 比内置工具好很多但不 100% | 仍建议 `--count 4` 出多张挑 |
| 严格无缝 auto-tile | LLM-image 类模型都不能硬保证 | 出完用 ImageMagick `tile:2x2` 验拼接 |
| 9 宫格 UI | 不可用 | 用现有 `Assets/UI/Themes/UITheme.tres` 程序化 |
| 4-8 帧动画 | 帧间一致性差 | 多帧动画建议手 K 或别的工具 |

---

## 7. rembg 抠图（后处理，本地、不计 API 费）

灰底 / 非透明 JPG 出图后，用 `rembg_cutout.py` 抠成 PNG（RGBA）：

```powershell
pip install -r Tools/banana/requirements.txt

python Tools/banana/rembg_cutout.py `
  --in Artifacts/banana_smoke_test.jpg `
  --out Artifacts/preview/banana_smoke_test_nobg.png
```

- `--out` 必须在 `Artifacts/` 或 `Assets/` 下（与 `banana_gen.py` 一致）。
- `--model` 默认 `u2net`；边缘更稳可试 `isnet-general-use`（首次会下载 ONNX 权重）。
- `--alpha-matting`：更干净边缘，更慢，需 rembg 完整依赖。

**首次运行 / 离线**：默认模型从 GitHub 拉取 `u2net.onnx`（约 176MB）。若自动下载失败，用浏览器下载  
https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2net.onnx  
保存到 **`%USERPROFILE%\.u2net\u2net.onnx`**（rembg 默认目录），或放到任意目录后设置环境变量 **`U2NET_HOME`** 指向该目录（目录内需有 `u2net.onnx`，不要指向文件本身）。也可在 **`Tools/banana/.env`** 里写 `U2NET_HOME=...`（`rembg_cutout.py` 启动时会加载）。

---

## 8. xais 中转：aspect 烟测 + prop 批 1 重出

在 `Tools/banana/.env` 配置（**不要把 key 提交到 git**）：

- `GEMINI_PROTOCOL=openai`
- `GEMINI_BASE_URL=https://<你的中转根域名>`（OpenAI 兼容根 URL，脚本会拼 `/v1/chat/completions`）
- `GEMINI_API_KEY=<中转站给的 key>`
- 模型名与中转约定一致时用 `--model` 覆盖；不设则用 `banana_gen.py` 内 `DEFAULT_MODEL_OPENAI`

**① aspect 是否稳定（9:16 / 4:3，各约 1×1K ≈ $0.067，合计约 90s 量级）** — 用短 subject 只看比例，不追求画面质量：

```powershell
python Tools/banana/banana_gen.py `
  --protocol openai `
  --style style_base --negatives standard `
  --subject "single small red apple, centered, product photo, neutral lighting" `
  --aspect 9:16 --size 1K `
  --out Artifacts/preview/xais_aspect_916.png

python Tools/banana/banana_gen.py `
  --protocol openai `
  --style style_base --negatives standard `
  --subject "single small red apple, centered, product photo, neutral lighting" `
  --aspect 4:3 --size 1K `
  --out Artifacts/preview/xais_aspect_43.png
```

出图后用系统看图或 Godot 导入看像素宽高比是否接近 9:16 与 4:3（OpenAI 协议靠 prompt 内嵌 aspect，以中转实际行为为准）。

**② 与 `Artifacts/preview_props_2026-04-19` 同批 3 个 prop 对照**（prompt 已复制到 `Tools/banana/prompts/prop_batch1_*.txt`）：

```powershell
python Tools/banana/banana_gen.py --protocol openai --prompt-file Tools/banana/prompts/prop_batch1_campfire.txt `
  --aspect 1:1 --size 1K --count 4 --out Artifacts/preview/xais_prop_campfire.png

python Tools/banana/banana_gen.py --protocol openai --prompt-file Tools/banana/prompts/prop_batch1_firewood.txt `
  --aspect 1:1 --size 1K --count 4 --out Artifacts/preview/xais_prop_firewood.png

python Tools/banana/banana_gen.py --protocol openai --prompt-file Tools/banana/prompts/prop_batch1_chest.txt `
  --aspect 1:1 --size 1K --count 4 --out Artifacts/preview/xais_prop_chest.png
```

需要抠图时：`python Tools/banana/rembg_cutout.py --in ... --out Artifacts/preview/..._nobg.png`

---

## 9. 触发本文件失效的事件

- Google 把 `gemini-3.1-flash-image-preview` 模型名改了 / 下线了 → 改 `DEFAULT_MODEL`
- 价格变了 → 改 `COST_PER_IMAGE`
- 出现更强的新模型 → 评估是否替换 + 写迁移说明
- google-genai SDK 大版本升级且 break API → 改脚本 + bump `requirements.txt`
