# MiniRPG 代码全量扫描审计
> 文档版本：v1.0.0
> 扫描日期：2026-04-06
> 扫描范围：`App/`、`Core/`、`Module/`、`Scene/`、`Data/`、`Tests/`、`Tools/`
> 验证结果：`dotnet build MiniRPG.csproj` 通过；`dotnet test Tests\MiniRPG.Tests\MiniRPG.Tests.csproj` 15/15 通过
> 备注：测试命令仍出现 Godot Source Generator 警告，见本文 P2-2

## 1. 结论摘要

当前代码库已经具备可工作的分层基础，但运行时热点、入口层职责扩散、保存链路阻塞和构建链细节问题已经开始累积。  
本轮扫描后，最值得优先处理的不是继续堆功能，而是先把以下四个边界收紧：

- 随机数与确定性边界
- 渲染 / AI 的空间查询边界
- `App/Main.cs` 的入口编排边界
- 保存 / 读档 / 存档列表的 I/O 边界

如果这四块不先收口，后续战斗、AI、UI 和地图规模一旦继续增大，维护成本会比功能收益增长得更快。

## 2. 高优先级优化项

### P1-1. 随机数种子分散且混入 `string.GetHashCode()`，确定性不足

证据：

- `Core/AI/AIDispatcher.cs:63`
- `Core/AI/AIDispatcher.cs:82`
- `Core/AI/AIDispatcher.cs:102`
- `Core/Combat/ActionModule.cs:143`
- `Core/Combat/CombatModule.cs:58`
- `Core/Combat/CombatModule.cs:215`
- `Core/Combat/TimelineTurnManager.cs:570`
- `Module/CombatUIModule.cs:61`
- `Module/DialogUIModule.cs:41`

问题：

- 当前多个模块各自 `new Random(...)`，并把 `actor.Id.GetHashCode()` 混入种子。
- 在现代 .NET 里，`string.GetHashCode()` 不适合作为跨进程稳定哈希，导致 AI、战斗、UI 随机选择的结果不够可重放，也不利于回归测试、录像回放和问题复现。
- 随机逻辑分散在 AI、战斗、时间轴和 UI 层，后续很难统一控制“同一回合的随机来源”。

建议：

- 引入统一的 `IRngService` 或 `DeterministicRng`。
- 用稳定哈希替代 `GetHashCode()`，例如自定义 FNV-1a / xxHash32。
- 明确区分“玩法随机”和“纯表现随机”，不要共用同一套种子规则。

### P1-2. 渲染和 AI 已有 chunk / actor 索引基础，但热点路径仍在全表扫描

证据：

- `Module/Render/TileMapRenderModule.cs:200`
- `Module/Render/TileMapRenderModule.cs:465`
- `Module/Render/TileMapRenderModule.cs:665`
- `Module/Render/TileMapRenderModule.cs:671`
- `Module/Render/TileMapRenderModule.cs:691`
- `Module/Render/TileMapRenderModule.cs:693`
- `Core/AI/AIVisionBatch.cs:50`
- `Core/AI/AIVisionBatch.cs:55`
- `Core/AI/AIVisionBatch.cs:139`
- `Core/AI/AIVisionBatch.cs:155`
- `Core/World/WorldMap.cs:106`
- `Core/World/WorldMap.cs:109`
- `Core/World/WorldMap.cs:331`
- `Core/World/WorldMap.cs:339`
- `Core/World/WorldMap.cs:352`

问题：

- `TileMapRenderModule.Flush()` 每次刷新都会重扫视口；在 `EntityLoc()` / `PeripheralEntityLoc()` 里又对 `_state.Actors.Values` 逐格线性扫描。
- `RenderGroundItems()` 每格都会调用 `GetEntitiesByType()`，而 `WorldMap.GetEntitiesByType()` 本身会再生成一份新列表。
- `AIVisionBatch.CollectCandidates()` 对每个观察者再次遍历 `state.Actors.Values`；`BuildLocalWalkable()` 和 `BuildLocalFixtures()` 还会为每个观察者新建字典。
- 代码库其实已经有 `WorldMap.RegisterActor()` / `UpdateActorChunk()` / `UnregisterActor()` 这套 chunk 级 actor 登记，但热路径没有真正复用。

影响：

- 当前视口只有 27x15 时还能扛住，但 actor 数量和观察者数量一上去，渲染与 AI 会一起退化。
- 你已经付出了维护 chunk 索引的复杂度，却还没有把它兑现成真实性能收益。

建议：

- 补一个统一的空间查询层，例如 `ActorSpatialIndex` / `WorldQueryService`。
- 让渲染、AI、邻格交互都走同一套“按 chunk / 按位置”查询。
- 把 `GetEntitiesByType()` 这类会在热点路径重复分配列表的 API 改成可复用缓冲区、枚举器或专用查询方法。

### P1-3. `App/Main.cs` 已经演化成 God Object，入口编排和业务流程高度耦合

证据：

- `App/Main.cs:415`
- `App/Main.cs:1200`
- `App/Main.cs:1248`
- `App/Main.cs:1274`
- `App/Main.cs:2261`
- `App/Main.cs:2336`
- `App/Main.cs:2407`
- `App/Main.cs:2559`

问题：

- `App/Main.cs` 当前约 2443 行，集中了启动装配、面板生命周期、事件分发、地图刷新、楼层切换、读档、角色创建、输入路由等职责。
- 多个流程使用 `async void`，包括菜单继续、角色创建确认、地图编辑器进入、读档、上下楼等入口；异常处理和调用关系都不够透明。
- 功能一旦同时影响 Core、UI、渲染，最终基本都会回流到 `Main` 上扩张。

影响：

- 回归风险集中在单文件。
- 很多行为无法独立测试，只能靠人工走流程或集成级验证。
- 后续维护会越来越依赖“知道 Main 里哪段代码会被顺带触发”。

建议：

- 把 `Main` 收缩成 Godot 桥接层。
- 先拆出 `Bootstrap`、`EventDispatcher`、`UiCoordinator`、`SaveLoadCoordinator` 这类协作者。
- 把 `async void` 尽量收敛到真正的 UI 事件边界，内部逻辑统一改成 `Task`。

## 3. 中优先级优化项

### P2-1. 存档链路和存档列表仍是同步全文件 I/O，UI 只做了“忙碌遮罩”但没有真正异步化

证据：

- `Core/Map/SaveModule.cs:28`
- `Core/Map/SaveModule.cs:35`
- `Core/Map/SaveModule.cs:48`
- `Core/Map/SaveModule.cs:56`
- `Core/Map/SaveModule.cs:543`
- `Module/GameSessionModule.cs:169`
- `Module/GameSessionModule.cs:182`
- `Module/GameSessionModule.cs:261`
- `App/Main.cs:2407`

问题：

- `SaveModule.SaveGame()` / `LoadGame()` 仍然直接 `File.WriteAllText` / `File.ReadAllText`。
- `TryReadSaveHeader()` 为了展示存档列表摘要，会再次读完整个文件并解析 JSON。
- `Main.DoLoad()` 虽然有加载进度 UI，但真正的读档和反序列化依旧发生在主线程。

影响：

- 存档规模一旦增大，卡顿会直接体现在游戏输入和 UI 响应上。
- 存档列表越多，打开浏览器的等待时间越明显。

建议：

- 至少把“存档列表摘要”改成缓存头信息，避免每次枚举都做完整文件读取。
- 保存和读档链路改成真正的后台 I/O + 主线程应用结果。
- 后续如需进一步提速，可考虑保存头和正文分离，或者在保存时顺手生成轻量 summary。

### P2-2. 测试全绿，但测试构建链仍有 Godot Source Generator 警告

证据：

- `MiniRPG.csproj:12`
- `Tests/MiniRPG.Tests/MiniRPG.Tests.csproj:7`
- 本轮执行 `dotnet test Tests\MiniRPG.Tests\MiniRPG.Tests.csproj` 时出现：
  `ScriptPathAttributeGenerator ... Property 'GodotProjectDir' is null or empty`

问题：

- 主工程已经声明了 `CompilerVisibleProperty Include="GodotProjectDir"`，测试工程只有 `GodotProjectDir` 属性，没有同等可见性声明。
- 结果是测试能过，但构建日志不干净，后续 CI 一旦把 warning 提升为 gate，就会埋雷。

建议：

- 先把测试工程的 generator 输入补齐，保证 `dotnet test` 日志干净。
- 同时把“主工程 build 无 warning”和“测试工程 test 无 warning”分开作为两个最小验收标准。

### P2-3. 编辑器 / 工具模块体量已经接近独立应用，但仍与主游戏工程紧耦合

证据：

- `App/ResourceCatalogEditor.cs:11`
- `App/ResourceCatalogEditor.cs:104`
- `Tools/PlaceholderPaintTool.cs:9`
- `Tools/PlaceholderPaintTool.cs:93`

问题：

- `App/ResourceCatalogEditor.cs` 约 1745 行，`Tools/PlaceholderPaintTool.cs` 约 1701 行。
- 两者都已经不是“轻量辅助面板”，而是完整的工作流工具。
- 继续和运行时游戏逻辑放在同一工程里，编译、审阅、启动链和职责边界都会越来越重。

建议：

- 先做目录级和命名空间级隔离。
- 中期再评估是否拆到独立工具入口或单独工程。

## 4. 验证与测试覆盖观察

- `dotnet build MiniRPG.csproj`：通过，无 warning。
- `dotnet test Tests\MiniRPG.Tests\MiniRPG.Tests.csproj`：15/15 通过，但测试构建链有 generator warning。
- 当前测试更偏功能正确性，缺少对“性能退化”和“确定性行为”的守护。

建议补的低成本测试：

- 固定 seed 下的 AI / 战斗随机结果一致性测试
- actor 数量增长时的 AI 观察者性能基线测试
- 存档列表读取的摘要缓存测试
- 空间索引查询与当前位置查询的一致性测试

## 5. 推荐落地顺序

### 第一阶段：先收口热点和构建噪音

- 统一随机数入口，去掉 `GetHashCode()` 种子。
- 给渲染和 AI 接入统一空间查询层。
- 修掉测试工程的 generator warning。

### 第二阶段：削薄入口层

- 从 `App/Main.cs` 拆出启动、事件分发、读档协调和 UI 协调。
- 把内部 `async void` 改成 `Task` 流程。

### 第三阶段：再处理 I/O 和工具链

- 让存档列表不再重复全文件读取。
- 让保存 / 读档真正异步化。
- 拆分编辑器 / 工具的工程边界。

## 6. 一句话判断

这次扫描最明确的信号是：项目现在不是“没有架构”，而是“已有架构没有完全落到热点路径上”。  
先把确定性、空间索引、入口职责和存档 I/O 这四个边界收紧，后续继续扩战斗、AI 和编辑器功能才不会被基础设施反噬。
