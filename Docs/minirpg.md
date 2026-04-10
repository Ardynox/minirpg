# MiniRPG 项目约定与开发原则

> 本文档定义项目约定、默认决策规则和协作边界，不负责描述当前每个类已经怎样实现。

---

## 文档定位

- 这份文档用于说明项目约定、默认决策规则和协作边界。
- 做结构设计或评审时，用这份文档判断方案是否偏离团队默认做法。
- 如果要看当前代码事实、职责边界和调用链，请转到 [`ARCHITECTURE.md`](./ARCHITECTURE.md)。
- 如果要快速定位目录、入口文件和主路径，请转到 [`CODEBASE_MAP.md`](./CODEBASE_MAP.md)。
- 如果要看遗留问题和技术债，请转到 [`TODO.md`](./TODO.md)。
- For in-game AutoTest handoff and log inspection workflow, read [`AUTOTEST_AI_GUIDE.md`](./AUTOTEST_AI_GUIDE.md).
- Runtime diagnostics should prefer the in-game `AutoTest` button and `user://test_results.log` over ad-hoc manual probing when the issue depends on real session/UI state.

## 当前项目目标

- 在现有 Godot/C# 客户端框架上持续增量开发，而不是重新发明一套通用框架。
- 保持玩法逻辑、面板 UI、TileMap 渲染、世界/存档四条主线清晰分层。
- 在设计、实现、评审时优先遵守已有结构，而不是默认新开层。

## 默认决策规则

### 1. 增量开发优先

- 如无必要，无增实体。
- 新功能先找现有入口：`Main` 胶水层、现有 `Module/*`、现有 `Core/*`，确认不够后再扩展。
- 新增模块、系统、抽象层之前，必须先回答“为什么现有结构不够”。

### 2. 既有框架优先

- UI 面板遵守 `IPanel` + `PanelManager` + `PanelDragService`。
- 输入遵守 `InputBindingService` + `InputModule`。
- 渲染与玩家视觉遵守 `TileMapRenderModule` + `FogOfWarTracker`；如果改动影响信息暴露，要同步检查 `LookModule`。
- AI 感知遵守 `AIDispatcher` + `PerceptionBuilder` + `AIVisionBatch`，先走 CPU 批处理，不默认引入 GPU 路线。
- 会话与存档遵守 `GameSessionModule` + `SaveModule`。
- 世界与运行时状态遵守 `GameState` + `WorldMap` + `Actor` 字典。
- 事件反馈遵守 `GameEvent` + `Main.Dispatch` + `LogModule` / 流程 UI。

### 3. 状态与所有权

- `GameState` 是唯一运行时事实源。
- 世界格子数据在 `WorldMap` / `ChunkData`；`Actor` 不直接存进格子栈。
- UI 面板可以缓存显示状态，但不应持有新的玩法真相。
- 共享数据、缓存、布局状态都要明确 owner、失效时机和恢复路径。

### 4. 默认不要走的方向

- 不引入 ECS、通用状态机框架、额外的数据驱动运行时层。
- 不为了“整洁”绕开现有面板、输入、渲染、会话框架。
- 不把历史 MVP 假设当成当前事实，例如“文本渲染主路径”或“不使用 TileMap”。

### 5. 文档同步规则

- 改约定、非目标、默认策略：更新本文件。
- 改当前职责边界、入口、模块关系：更新 [`ARCHITECTURE.md`](./ARCHITECTURE.md)。
- 改目录、入口文件、快速导航信息：更新 [`CODEBASE_MAP.md`](./CODEBASE_MAP.md)。
- 如果一次改动同时改了约定和事实边界，这三份文档一起改。

### 6. 文本编码与本地化门禁

- 仓库内受控文本文件统一使用 UTF-8；脚本读写必须显式声明编码，禁止依赖系统默认编码或 ANSI 保存。
- Python 读写文本一律显式传 `encoding="utf-8"`；PowerShell 写文件必须显式使用 `-Encoding utf8`。
- 任何中文文本、`Data/I18n/*.json`、本地化脚本或场景文本改动后，提交前必须执行：
  - `python Tools\validate_i18n.py`
  - `dotnet test Tests\MiniRPG.Tests\MiniRPG.Tests.csproj --filter LocalizationCatalogTests`
- 如果上述任一命令失败，不应继续生成或覆盖 catalog 文件，先修复编码或 key parity 问题再继续。

## 当前仍然有效的约束

- 启动入口仍是 `App/Main.tscn`，但项目不是“单节点极简 UI”。
- 当前项目已经使用 TileMap/TileSet 渲染和多面板 UI。
- 当前仍然不默认引入 ECS、通用状态机或新的外部框架。
- 当前仍然遵守“先实现再抽象、增量收敛、避免无证明的新层”。

## 局域网导出与打包（Client + Dedicated Server）

- 标准脚本：`Tools/export_and_package_lan.ps1`
- 目标：一次命令完成客户端导出和服务端发布，并将产物包裹到统一目录。

### 默认输出结构

- 输出根目录：`Build/Packages/`
- 单次产物目录：`LAN-Package-时间戳/`（传 `-NoTimestamp` 时为 `LAN-Package-latest/`）
- 目录内容：
  - `Client/`：客户端导出产物（Debug/Release）
  - `Server/`：`MiniRPG.Server` 的 `dotnet publish` 产物
  - `README.txt`：本次产物说明与服务器启动示例

### 推荐用法

```powershell
powershell -ExecutionPolicy Bypass -File ".\Tools\export_and_package_lan.ps1"
```

可选参数示例：

```powershell
# 指定 Godot bin 目录（默认 D:\Godot\godot\bin）
powershell -ExecutionPolicy Bypass -File ".\Tools\export_and_package_lan.ps1" -GodotBinDir "D:\Godot\godot\bin"

# 指定 Godot 可执行文件
powershell -ExecutionPolicy Bypass -File ".\Tools\export_and_package_lan.ps1" -GodotExe "D:\Godot\godot\bin\godot.windows.editor.x86_64.mono.exe"

# 只打包服务端
powershell -ExecutionPolicy Bypass -File ".\Tools\export_and_package_lan.ps1" -SkipClient
```

### CI/本地使用约束

- 导出前必须保证项目可构建，脚本遇到任一步失败会立即停止。
- 客户端导出依赖 Godot 导出预设，默认使用 `Windows Desktop`。
- 本脚本用于 LAN 打包，不包含加密、签名、安装器构建等发布流程。

## 非目标

- 本文档不再记录早期 GDScript MVP 步骤、ASCII 伪代码或 Cursor 启动脚本。
- 需要历史上下文时，请查 git 历史，不要把历史示例当成当前实现事实。
