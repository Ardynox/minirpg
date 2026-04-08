# MiniRPG 架构扫描与优化建议

> 扫描日期：2026-04-05  
> 扫描范围：`App/`、`Core/`、`Module/`、`Scene/`、`Data/`、`Tools/`、`Docs/`  
> 目标：基于当前代码事实，梳理项目主架构、识别主要优化点，并给出可执行的演进顺序。

## 1. 结论摘要

当前项目已经具备比较清晰的基础分层：

- `App/` 负责 Godot 入口与场景装配
- `Module/` 负责 UI、渲染、会话、输入等胶水层
- `Core/` 负责世界、战斗、AI、存档、数据与配置
- `Data/` 和 `Assets/` 负责运行时内容与资源
- `Tools/` 和 `Module/Editor/` 开始承接编辑器工具链

问题不在于“完全没有架构”，而在于以下几点已经开始明显堆积：

- 入口编排层过重，`App/Main.cs` 正在吸收越来越多职责
- 新旧地图访问方式并存，适配层和核心层边界开始模糊
- 世界分块和 Actor 分块注册已经存在，但查询路径仍大量走全表扫描
- 运行时资源管线和编辑器资源目录是两套体系，尚未打通
- 存档、事件分发、UI 脏标记仍偏手工维护，后续扩展成本会持续上升

如果继续在当前结构上直接叠加功能，短期还能推进，但会越来越依赖“记忆代码位置”和“人工避免踩线”，而不是依赖清晰边界。

## 2. 当前架构的正向基础

以下部分值得保留，不建议推倒重来：

- `Core/*` 里大部分玩法逻辑仍保持为纯 C# 模块，未直接绑定 Godot 节点，这一点是整个项目最重要的可维护性基础。
- `WorldMap -> ChunkManager -> ChunkData` 这条世界访问链路已经明确，比直接把地图状态散落在 `GameState` 里要健康得多。
- `Module/Panel` 已经形成了面板焦点、拖拽、布局、边框反馈的统一机制，说明 UI 并不是完全堆在场景节点里。
- `GameConfig` 把高频调参项放进 `Data/Config/`，对玩法调参与压测是友好的。
- `AutoTestModule` 虽然不是单元测试，但至少已经建立了“运行期回归验证”的意识。

换句话说，项目不是从零开始补架构，而是要把已经存在的分层进一步收紧，避免边界重新塌回入口层。

## 3. 重点优化项

### P1. `App/Main.cs` 已经成为事实上的总控类

相关位置：

- `App/Main.cs:_Ready()` 负责启动期几乎全部装配
- `App/Main.cs:Dispatch()` 负责事件路由
- `App/Main.cs:FlushMap()` / `ProcessDirtyPanels()` 负责渲染与 UI 刷新节奏
- `App/Main.cs` 文件总长度约 2042 行

观察：

- 入口类同时承担了启动装配、输入命令路由、事件分发、面板生命周期、地图刷新、死亡处理、观察模式、交互流程、存读档入口等职责。
- 很多新功能如果需要“既影响核心逻辑、又影响 UI、又影响渲染”，最终都会回到 `Main` 上打洞。

风险：

- 任何新功能都更容易变成“改 `Main` + 改一堆模块”的模式。
- 回归风险集中在单个大文件，评审和定位成本会持续增加。
- Godot 生命周期逻辑和业务流程逻辑高度耦合，不利于测试。

建议：

- 把 `Main` 限缩为 Godot 桥接层，只保留节点查找、信号绑定、主循环钩子。
- 抽出至少 4 个协作者：
  - `MainBootstrap`：启动装配与依赖初始化
  - `MainCommandRouter`：命令路由与输入到行为的映射
  - `MainEventDispatcher`：`GameEvent` 到 UI/表现层副作用的分发
  - `MainUiCoordinator`：面板开关、脏标记、统一刷新
- 新功能默认不要直接往 `Main` 增长方法，而是先判断应该归属到哪个 coordinator。

### P1. `MapModule` 仍是强兼容层，新旧世界 API 并存

相关位置：

- `Core/Map/MapModule.cs`
- `Core/World/WorldMap.cs`
- `Module/GameSessionModule.cs:ChangeFloor()`

观察：

- `WorldMap` 已经是更合理的 3D 世界访问入口。
- `MapModule` 仍保留了大量以 `PlayerZ` 为默认值的 2D 包装方法，并混入了 glyph 到实体 ID 的转换、楼层跳转、测试地图载入等兼容逻辑。
- 新旧调用方式现在都能工作，但边界不够清晰。

风险：

- 调用方不容易判断该走 `MapModule` 还是 `WorldMap`。
- 语义容易重复：同一能力在兼容层和核心层各有一个入口。
- 越往后迁移，越容易出现“新代码继续堆在兼容层里”的情况。

建议：

- 明确把 `MapModule` 标记为“旧接口适配层”，停止向里面新增领域规则。
- 新代码默认直接依赖 `WorldMap` 或显式 3D 签名。
- 给 `MapModule` 建一份迁移清单，优先迁出：
  - 楼层切换相关流程
  - Fixture/Glyph 互转
  - 仅服务测试的字符串地图加载

### P1. Actor 查询仍大量是 O(n) 扫描，分块索引收益没有真正落地

相关位置：

- `Core/Data/ActorModule.cs:GetAllAt()` / `GetHostileAt()`
- `Module/Render/TileMapRenderModule.cs:EntityLoc()` / `PeripheralEntityLoc()`
- `Core/World/WorldMap.cs:RegisterActor()` / `UpdateActorChunk()`
- `Core/World/ChunkManager.cs:TickSimulation()`

观察：

- `ActorModule.GetAllAt()` 和 `GetHostileAt()` 仍然直接遍历 `state.Actors.Values`。
- `TileMapRenderModule` 在每个可见格上也会再扫一遍 Actor 集合。
- `WorldMap`/`ChunkData` 已经维护了 `ActorIds`，但目前几乎只用于注册，没有成为统一查询入口。
- `ChunkManager.TickSimulation()` 已存在，但当前仓库里没有调用点。

风险：

- 当前地图窗口不大时问题不明显，但一旦 Actor 数量提升，渲染、AI、交互查询会同时退化。
- 已投入的分块注册复杂度还没有转化成真实性能收益。
- 未使用的模拟入口会持续制造“架构上已经支持”的错觉。

建议：

- 建一个统一的 `ActorSpatialIndex` 或基于 `ChunkData.ActorIds` 的查询服务。
- 让以下调用都走同一套索引能力：
  - 渲染层实体绘制
  - 战斗/交互邻格查询
  - AI 观察者和候选目标筛选
- 对 `TickSimulation()` 做二选一：
  - 要么正式接入 `TurnModule`
  - 要么删除/注释为预留能力，避免死接口继续扩散

### P1. 存档链路高度依赖手工深拷贝，字段演进风险大

相关位置：

- `Core/Map/SaveModule.cs:SaveGame()`
- `Core/Map/SaveModule.cs:LoadGame()`
- `Core/Map/SaveModule.cs:CopyActor()`
- `Core/Map/SaveModule.cs:CopyItem()`

观察：

- 当前存档方案是清晰的：全局状态 + dirty chunk。
- 但序列化前的数据复制完全依赖手写 `Copy*` 方法。
- `Actor`、`Item`、`Quest`、`Limb`、`Buff` 等结构一旦增加字段，必须人工同步到存档复制链。

风险：

- 最危险的问题不是“马上崩”，而是“字段新增后悄悄丢档”。
- 这种问题通常只有读档或旧档兼容时才暴露，定位成本高。

建议：

- 引入最小成本保护：为 `SaveModule` 增加快照级测试。
- 至少覆盖以下回归点：
  - `Actor` 字段 round-trip
  - `Item` 嵌套容器 round-trip
  - dirty chunk 恢复
  - quest / limb / buff / dialog 状态恢复
- 中期再评估是否切到更稳定的 snapshot DTO / source-generated serializer 路线。

### P1. 资源管线现在分成“运行时映射”和“编辑器目录”两套系统

相关位置：

- `Module/Render/ResAccess.cs` 使用 `Data/entity_render.json`
- `Module/Render/TileMapRenderModule.cs` 使用 `Data/tile_mapping.json`
- `Module/Render/TileMapRenderModule.cs` 还依赖 `Tools/tile_name_to_id.json`
- `App/ResourceCatalogEditor.cs`
- `Module/Editor/ResourceCatalogStore.cs` 写入 `Data/resource_catalog.json`

观察：

- 运行时渲染使用的是 `entity_render.json` 与 `tile_mapping.json`。
- 编辑器工具维护的是 `resource_catalog.json`。
- 当前没有看到运行时直接消费 `resource_catalog.json` 的链路。
- 这说明“资源目录编辑器”和“运行时渲染配置”在结构上还是分离的。

风险：

- 编辑器产物无法成为真正的单一事实源。
- 相同资源信息可能在多个 JSON 中重复维护。
- `TileMapRenderModule` 直接依赖 `Tools/` 下的运行时数据，不利于工具链和游戏运行边界分离。

建议：

- 统一成“编辑器目录 -> 构建/导出 -> 运行时映射”的单向流程。
- `resource_catalog.json` 应该成为原始资产元数据。
- `entity_render.json`、`tile_mapping.json`、甚至 tile id 映射应优先由导出步骤生成，而不是多处手工维护。
- `Tools/` 下的运行时依赖数据应迁回 `Data/Generated/` 之类的正式目录。

### P2. 启动期同步加载过重，初始化顺序隐藏在 `_Ready()` 中

相关位置：

- `App/Main.cs:_Ready()`
- `Core/Config/GameConfig.cs:Load()`
- `Module/Render/ResAccess.cs:Load()`

观察：

- `_Ready()` 在单次启动路径中连续完成：
  - config / terrain / preset / localization / dialog / render registry 加载
  - TileSet / Theme / 各类 Panel / SaveBrowser / MapEditor / Menu 初始化
  - 大量信号绑定和 UI 文本刷新

风险：

- 启动延迟会越来越不透明。
- 某个模块初始化顺序变化时，容易出现隐蔽副作用。
- 很难为“菜单先出来，重资源稍后加载”这类体验优化留空间。

建议：

- 把启动拆成几个明确阶段：
  - `LoadConfigAndData`
  - `InitWorldServices`
  - `InitUiServices`
  - `BindSignals`
  - `EnterMenu`
- 为每个阶段加简单计时日志。
- 非首屏必要的模块（如部分面板、编辑器工具）延迟初始化。

### P2. 事件分发与 UI 刷新仍偏手工，容易产生隐式耦合

相关位置：

- `App/Main.cs:Dispatch()`
- `App/Main.cs:FlushMap()`
- `App/Main.cs:ProcessDirtyPanels()`
- `Core/Data/GameEvent.cs`

观察：

- `Dispatch()` 通过字符串事件类型切换到动画、战斗 UI、对话 UI、地面面板失效等副作用。
- `FlushMap()` 之后再调用 `MarkUIDirty()`，然后由 `_Process()` 驱动一部分面板刷新。
- 这套机制可用，但它依赖开发者记住“某个行为之后到底要 dispatch、flush、invalidate 哪些 UI”。

风险：

- 漏刷或重复刷新的 bug 不容易在代码层直接看出来。
- 字符串事件类型扩展时，编译器无法提醒遗漏的 case。

建议：

- 中期把 `GameEvent.Type` 升级为 enum 或分层常量。
- 给 UI 刷新建立更明确的订阅边界，例如：
  - inventory changed
  - ground items changed
  - actor stats changed
  - layout changed
- 让 `Dispatch` 更像“分发业务事件”，而不是“同时兼任 UI 刷新脚本”。

### P2. 编辑器工具体量已经足够大，但仍与运行时放在同一应用层

相关位置：

- `App/ResourceCatalogEditor.cs`
- `Module/Editor/ResourceCatalogStore.cs`
- `Module/Editor/ResourcePreviewControl.cs`

观察：

- `ResourceCatalogEditor.cs` 已经约 2001 行。
- 它拥有完整的导入、筛选、批量编辑、切片、预览、校验和保存流程，已经不是一个轻量面板。
- `CloseEditor()` 直接 `GetTree().Quit()`，说明当前更像“工具场景单独运行”，但代码组织上仍和主游戏放在一起。

风险：

- 编辑器逻辑会持续拉高主项目编译、审查和维护成本。
- 运行时和工具链职责更难进一步拆分。

建议：

- 继续保留 `Module/Editor/` 作为可复用逻辑层。
- 但从项目结构上，把资源编辑器明确为独立工具入口：
  - 单独 scene / tool app
  - 或单独 assembly / project
- 至少先做到目录与命名空间隔离，再考虑工程级拆分。

### P2. 自动化验证方向是对的，但缺少低成本、可重复的纯逻辑测试

相关位置：

- `Module/AutoTestModule.cs`

观察：

- 现有自动测试更偏运行期 smoke/regression。
- 它能发现集成问题，但执行成本高，且不适合覆盖所有数据结构细节。

风险：

- 像 `SaveModule`、`GameConfig`、`MapModule.LoadFromStrings()`、资源目录规范化这类纯逻辑问题，没有最低成本的防线。

建议：

- 新增独立测试项目，优先补最值得的纯逻辑测试：
  - 存档 round-trip
  - 配置加载与默认值
  - 资源目录 normalization / validation
  - 地图字符串加载
  - Actor 空间查询
- 保留 `AutoTestModule` 做集成回归，不要让它承担全部测试责任。

## 4. 推荐演进顺序

建议按“低风险收边界 -> 再做结构性迁移”的顺序推进，而不是一次性大重构。

### 第一阶段：先补护栏

- 为 `SaveModule`、`ResourceCatalogStore`、`GameConfig` 增加纯逻辑测试
- 给关键字符串常量收口：事件类型、实体 ID、面板 ID、命令字
- 给启动和关键路径加基础耗时日志
- 给未启用的接口（如 `TickSimulation()`）明确状态：接入、注释或删除

### 第二阶段：拆总控入口

- 从 `App/Main.cs` 拆出 bootstrap / command router / event dispatcher / UI coordinator
- 约束新需求不再直接把流程代码堆回 `Main`
- 把观察模式、交互流程、面板切换逐步移到协作者类

### 第三阶段：收敛地图与 Actor 查询边界

- 明确 `MapModule` 只做兼容适配
- 给 `WorldMap`/`ChunkData` 建立统一 Actor 查询入口
- 让渲染、AI、交互都复用同一套空间查询能力

### 第四阶段：打通资源工具链

- 让 `resource_catalog.json` 成为源数据
- 通过导出步骤生成运行时映射
- 把 `Tools/` 中的运行时依赖数据迁出，减少工具目录和主游戏耦合

## 5. 一句话判断

这个项目当前最该做的不是继续扩玩法，而是先把“入口编排”“地图兼容层”“Actor 查询”“资源管线”这四个边界收紧。只要这四块不继续发散，后面的战斗、AI、编辑器和内容扩展都还能在现有基础上稳定推进。
