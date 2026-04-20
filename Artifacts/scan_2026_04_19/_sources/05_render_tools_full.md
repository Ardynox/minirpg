I'll perform a comprehensive audit of the specified areas. Let me start by reading the required context documents and listing all files in scope.

The user wants a comprehensive audit of the rendering, tools, and related code in their Godot/C# mini-RPG project. This is a massive task that requires reading many files. Let me start by understanding the scope and then systematically read through all the relevant files.

Let me begin by reading the key documents mentioned in AGENTS.md, then systematically go through all the files in the scan scope.

I'll need to:
1. Read Docs/README.md first (as per AGENTS.md)
2. Then read all files in Module/Render/
3. Read all files in MiniRPG.Shared/Module/Render/


4. Read all files in Tools/
5. Read Module/AutoTestModule.cs
6. Read Scene/WaterPainterlyBlockPreview.*
7. Read Assets/Shaders/*
8. Read App/ResourceCatalogEditor.cs
9. Do targeted grep searches for resource management patterns

Given the massive scope, I should use file listings and initial reads to understand the structure, then work through everything systematically using parallel operations where possible to move efficiently.

---

Let me start reading the large files in parallel. I'll batch the reads across the main rendering files.

---

Now let me read the remaining Module/Render files and start on Tools/shared code.

Good, I've read the main renderer files. Now let me continue with the remaining files. I need to read:
- Module/Render remaining files (VoxelTilePathResolver, VoxelSpritePool, TerrainAtlas, ResAccess, etc.)
- MiniRPG.Shared/Module/Render/* 
- Tools/*.cs
- Module/AutoTestModule.cs
- Scene/WaterPainterlyBlockPreview.*
- Assets/Shaders/*
- App/ResourceCatalogEditor.cs

Also need to do grep searches for resource management patterns.

Let me batch more reads.

---

Good, I've read most of the Module/Render files. Let me now continue reading the remaining files and do the grep searches. Let me batch the reads for the remaining render files, the shared render files, the water shader files, and start on the tools.

---

Now let me run the grep searches and read the water shader files and Tools in parallel.

---

Let me now read the large tool files and remaining critical files.

Now let me continue reading the remaining tools and the AutoTestModule. Let me also do more targeted greps for specific patterns.

---

Let me do a few final targeted checks before writing the report.

---

Now I have enough evidence to compile the full report. Let me write it.

# Module/Render + Tools 审计报告

## 0. 摘要

审计范围：`Module/Render/`（30 个 .cs）、`MiniRPG.Shared/Module/Render/`（4 个 .cs）、`Tools/`（6 个 .cs + 脚本）、`Module/AutoTestModule.cs`、`Scene/WaterPainterlyBlockPreview.*`、`Assets/Shaders/*`、`App/ResourceCatalogEditor.cs`。
共扫描约 15,000 行 C# 和 ~600 行 GLSL。

发现 **3 条严重缺陷**、**7 条主要缺陷**、**9 条次要缺陷**，以及若干设计/流程层观察。整体代码质量中上——sprite 池化、atlas 合批、chunk 表面缓存都已落地；主要风险集中在 GPU 资源生命周期管理和试验文件残留。

---

## 1. 严重缺陷（必修：崩溃 / GPU leak / 渲染错位 / 工具误删用户数据）

### S-1 `_entityMarkerCache` 中的 ImageTexture 从不释放

- **文件**: `Module/Render/IsometricVoxelRenderer.cs:1218, 1870-1901` 以及 `IsometricVoxelRenderer.HoverHighlight.cs:382-462`
- **现象**: `GetEntityMarkerTexture()` 每遇到新 label 或 hover 纹理 key 就调用 `ImageTexture.CreateFromImage(image)` 并存入 `_entityMarkerCache`。这些 `ImageTexture` 是 Godot 原生 GPU 资源，缓存字典在渲染器整个生命周期内只增不减，且无 `Dispose()` / `Free()` 清理路径。

```1218:1219:Module/Render/IsometricVoxelRenderer.cs
private readonly Dictionary<string, ImageTexture> _entityMarkerCache = new();
private readonly Dictionary<string, FacilityFrameBounds> _facilityFrameBoundsCache = new();
```

- **风险**: 在长时间游戏 session 中，每种新实体标签都会累积一张 64x64 RGBA 纹理到 GPU。若大量不同 glyph 出现（mod、批量 fixture），VRAM 缓慢泄漏。切换楼层/存档重载也不会清空。
- **建议**: 在 `Init()` / 楼层切换时遍历 `_entityMarkerCache.Values` 调用 `Dispose()`，然后 `Clear()`。或把 hover 纹理提前在 Init 阶段生成完一次性集合，不在运行时动态增长。

### S-2 `TerrainAtlas._imageLoadCache` 缓存 `Image` 对象永不释放

- **文件**: `Module/Render/TerrainAtlas.cs:265-282`
- **现象**: `LoadImageSource()` 使用 `Image.LoadFromFile()` 加载磁盘图片后存入 `_imageLoadCache`，Build 完成后这些中间 Image 不再需要但永不释放。`TerrainAtlas.Build()` 调用 `_atlasTexture?.Dispose()` 清理旧图集，但对 `_imageLoadCache` 不做任何清理。

```265:283:Module/Render/TerrainAtlas.cs
private readonly Dictionary<string, Image?> _imageLoadCache = new(StringComparer.OrdinalIgnoreCase);
// ...
_imageLoadCache[normalizedPath] = loaded;
```

- **风险**: 30+ 个地形类型 × 每个 2-3 张 Image（top+side fallback）= ~100 张 `Image` 常驻内存，Build 后从未使用。
- **建议**: 在 `Build()` 末尾清空 `_imageLoadCache`（Image 无需手动 Dispose——Godot GC 追踪——但 `_imageLoadCache.Clear()` 让 GC 可回收）。

### S-3 `WaterPainterlyBlockPreview` Droplet ShaderMaterial 在非正常退出路径下泄漏

- **文件**: `Scene/WaterPainterlyBlockPreview.cs:228-260, 292-294`
- **现象**: 每个 droplet 创建 `new ShaderMaterial`（L234）并存入 `_droplets` 列表。正常路径通过 `StepDroplets()` 在命中水面或超时时调用 `d.Mat.Dispose()`（L293）。但如果场景被 `QueueFree()` 或用户直接退出场景，`_ExitTree` 没有清理 `_droplets` 列表中残余的 ShaderMaterial。
- **风险**: 对于试验场景影响有限，但如果多次进出预览会累积。
- **建议**: 重写 `_ExitTree()` 遍历 `_droplets` 并 Dispose 所有 Material，或改为从对象池复用 Material。

---

## 2. 主要缺陷

### M-1 `FogOfWarTracker.Update()` 每次调用重建两个 HashSet

- **文件**: `MiniRPG.Shared/Module/Render/FogOfWarTracker.cs:83-84`
- **现象**: `_fullVisible = [];` 和 `_directionalVisible = [];` 每帧创建新 HashSet 实例。在大视野（BaseVisionRadius=12, 3 层深度）下，每个 HashSet 含数百元素，导致频繁 GC 压力。

```83:84:MiniRPG.Shared/Module/Render/FogOfWarTracker.cs
_fullVisible = [];
_directionalVisible = [];
```

- **建议**: 改为 `_fullVisible.Clear()` + `_directionalVisible.Clear()`，复用已分配的 HashSet 容量。

### M-2 `LightMap.CellKey` 在大坐标下会碰撞

- **文件**: `MiniRPG.Shared/Module/Render/LightMap.cs:237-238`
- **现象**: `CellKey` 将 x、y、z 各加 32768 后打包进 64 位。y 占 bit 16-31，z 占 bit 0-15。但 y+32768 超过 65535（即 y > 32767）时会溢出到 x 的位段，z 同理。当前世界不太可能到达这个范围，但与 chunk 坐标系无上限的设计矛盾。

```237:238:MiniRPG.Shared/Module/Render/LightMap.cs
private static long CellKey(int x, int y, int z) =>
    ((long)(x + 32768) << 32) | ((long)(y + 32768) << 16) | (long)(z + 32768);
```

- **建议**: 使用 `(long)(ushort)(x + 32768)` 等带截断的打包，或改为 `(x, y, z)` 元组 key（性能差异在几百个 cell 的光照计算中可忽略）。

### M-3 `WeatherFxController` 的 `_weatherFxShader` 和 `_weatherFxQuadTexture` 初始化为 null 但从不赋值

- **文件**: `Module/Render/WeatherFxController.cs:51-52, 113-114, 298-299`
- **现象**: `Init()` 中 `_weatherFxQuadTexture = null;` 和 `_weatherFxShader = null;`，`ResolveWeatherQuadTexture()`（L413-418）和 `ResolveWeatherFxShader()`（L407-408）定义了创建逻辑但从未被调用。导致 `TryRenderWeatherShaderFx()` 总是 return false（L298-299），所有天气 FX 回退到 legacy texture sprite 路径。

```113:114:Module/Render/WeatherFxController.cs
_weatherFxQuadTexture = null;
_weatherFxShader = null;
```

- **风险**: 这不是 crash，但 `weather_realtime_fx.gdshader` 实际从未被使用——是死代码路径。
- **建议**: 如果 legacy 路径已经够用，删除 `TryRenderWeatherShaderFx` 和相关代码。如果需要 shader FX，在 `Init()` 中调用 `_weatherFxShader = ResolveWeatherFxShader(); _weatherFxQuadTexture = ResolveWeatherQuadTexture();`。

### M-4 `CombatFxPlayer.LoadTexture()` 每次调用 `new AtlasTexture` 不释放

- **文件**: `Module/Render/CombatFxPlayer.cs:251-255`
- **现象**: 当 `region != null` 时创建 `new AtlasTexture`，但这些 AtlasTexture 作为 Sprite 的 Texture 被设置后，替换时旧的 AtlasTexture 不被 Dispose。Godot 的 RefCounted 机制*可能*回收，但如果 Sprite 节点被直接 QueueFree 而非先清空 Texture 引用，资源生命周期不确定。
- **建议**: 使用缓存避免重复创建，或确认 Godot C# binding 的引用计数语义覆盖此路径。

### M-5 `EditorPerspectiveResolver.Resolve()` 每次调用分配新 `HashSet<WorldCoord>`

- **文件**: `Module/Render/EditorPerspectiveResolver.cs:28, 43, 61`
- **现象**: 在编辑器视图中，每帧调用 `Resolve()` 创建新 `HashSet<WorldCoord>`。室内场景下可能包含数十到上百个元素。
- **建议**: 将 HashSet 作为可复用缓冲区传入或缓存在调用方。

### M-6 水体 shader 文件过多且存在 .bak 残留（5 个 shader + 1 个 .bak）

- **文件**: `Assets/Shaders/` 目录
- **现象**:
  - `painterly_water_block.gdshader` — 当前活跃，被 `WaterPainterlyBlockPreview` 引用
  - `painterly_water_block.gdshader.bak` — **备份残留，应删除**
  - `gerstner_water_block.gdshader` — 被 `Scene/WaterGerstnerPreview.cs` 引用
  - `river_flow_water_block.gdshader` — 被 `Scene/WaterRiverFlowPreview.cs` 引用
  - `seascape_top_surface.gdshader` — **未找到任何 .cs 引用，疑似废弃**
  - `water/water_surface.gdshader` — **未找到任何 .cs 引用**（canvas_item 着色器，需要 texture uniform 驱动，但无代码加载它）
  - `weather_realtime_fx.gdshader` — 如 M-3 所述从未被加载
  - `Scene/WaterPainterlyBlockPreview.cs.bak` 和 `.tscn.bak` — **备份残留，应删除**
- **建议**: 见第 4 节统一清理建议。

### M-7 `ResAccess` 静态缓存 `_cache` 从不释放 GPU 资源

- **文件**: `Module/Render/ResAccess.cs:30, 109-112, 214-225`
- **现象**: `Get<T>()` 加载资源后存入静态 `_cache`，`Release()` 只做 `_cache.Remove(path)` 但不调用 `Dispose()`。`Reset()` 调用 `_cache.Clear()` 同样不 Dispose。Texture2D / Shader 等 GPU 资源的 Godot 引用计数可能不会立刻降为零（被场景树其他节点引用），但如果 `Reset()` 被调用时有"孤儿"资源，它们泄漏。
- **建议**: `Release()` 内部在 Remove 后检查资源的 `ReferenceCount`，必要时 `Dispose()`。或明确文档化 `ResAccess` 是 application-lifetime cache，不负责释放。

---

## 3. 次要缺陷（一行一条）

1. `VoxelTerrainShading` 常量 (`LeftDarken=0.65f` 等) 与 `IsometricVoxelRenderer` 中的同名 `private const` 重复定义——后者是遗留，未删除（L21-42 vs VoxelTerrainShading.cs:21-24）。
2. `TerrainAtlas.TerrainColors` fallback 颜色字典仍在使用（L128-147, L180, L241），路线图曾提到删除但实际仍是 tile 缺失时的兜底——确认保留但应添加注释说明其兜底角色。
3. `IsoCoordUtil.SortKey()` 使用 `(uint)depthOrder` 强转——当 `depthOrder < 0`（即 `wz > int.MaxValue`）时会回绕，但当前世界 z 范围远小于此，风险极低。
4. `WeatherFxController.ComputeWeatherVisualHash()` 使用 `params int[]`（L544），每次调用会分配数组——天气渲染时每个可见 cell 调用一次，可改为固定参数重载。
5. `VoxelTilePreviewTool` 的 `_textureCache` 和 `_imageCache`（L66-67）在工具关闭时不清理——作为 [Tool] 场景影响有限。
6. `ActorMotionTracker.PruneState()` 使用 `motion.Segments.RemoveAt(0)` 逐个移除 List 头部——List 的 O(n) 左移，但 MaxContinuousSegmentsPerActor=3 所以实际无影响。
7. `AutoTestModule.ResourceScenePaths`（L26-35）列出的场景路径如果被重命名/删除，测试会 fail 但不会给出有意义的错误——建议加 `ResourceLoader.Exists()` 预检。
8. `water/` 子目录下四张 .png 纹理（flowmap, depth_mask, ripple_height, riverbed）是 git 未跟踪新文件且仅被 `water_surface.gdshader` 的 uniform 声明引用——如果 shader 废弃则这些纹理也应一起删除。
9. `PlaceholderPaintTool` 和 `VoxelTilePreviewTool` 都包含独立的 `FileDialog` 管理、workspace I/O、undo/redo 逻辑——无公共基类但代码结构高度相似（均为 `Control` 子类 + `BuildUi()` + `LoadData()` + 左中右三栏布局）。

---

## 4. 设计/流程层观察

### 4.1 IsometricVoxelRenderer 当前最值得先拆的 1-2 个区域

文件当前 2112 行。以下是按"拆出后独立性最高、对剩余代码干扰最小"排序的两个候选：

**拆分点 1 — Entity Rendering（约 500 行）**

方法签名范围：

```csharp
// 拆出为 IsometricVoxelRenderer.EntityRendering.cs (partial class)
private void CollectEntityCommands(int cx, int cy, int cz, int halfW, int halfH, int zMin, int zMax)  // L1221
private bool TryDrawActorSprite(Vector2 pos, Actor actor, Color tint)  // L1362
private bool TryDrawFacilitySprite(Vector2 pos, FacilityInstance facility, Color tint)  // L1386
private bool TryResolveActorSpriteVisual(Actor actor, out ActorSpriteVisual visual)  // L1408
private bool TryResolveFacilitySpriteVisual(FacilityInstance facility, out FacilitySpriteVisual visual)  // L1444
private bool TryBuildFacilityDrawCommand(...)  // L1465
// + 所有 Resolve*Scale/Offset/Region 辅助方法 (L1558-1845)
// + EntityDrawCommand, ActorSpriteVisual, FacilitySpriteVisual 等记录类型
```

这块与主渲染管线仅通过 `_entityCommands` 列表交互，独立性最好。

**拆分点 2 — Camera & Coordinate Picking（约 250 行）**

```csharp
// 拆出为 IsometricVoxelRenderer.CameraPicking.cs (partial class)
private bool TryGetMapLocalFromGlobalPosition(Vector2 globalPos, out Vector2 mapLocal)  // L540
public bool TryGetWorldCellFromGlobalPosition(...)  // L513
public bool TryGetEditorWorldCellFromGlobalPosition(...)  // L531
private bool TryPickIsometricCell(...)  // L587
private bool TryPickEditorCell(...)  // L625
private bool TryPickInspectCell(...)  // L636
private bool HasInspectableCellContent(...)  // L685
// + Zoom 方法 (L740-778)
// + Camera smoothing (L465-504, L1923-1979)
```

### 4.2 Water shader 文件清理建议

| 文件 | 状态 | 建议 |
|------|------|------|
| `painterly_water_block.gdshader` | 活跃 | 保留 |
| `painterly_water_block.gdshader.bak` | 备份 | **删除** |
| `painterly_droplet.gdshader` | 被 WaterPainterlyBlockPreview 引用 | 保留 |
| `gerstner_water_block.gdshader` | 被 WaterGerstnerPreview 引用 | 保留（试验件） |
| `river_flow_water_block.gdshader` | 被 WaterRiverFlowPreview 引用 | 保留（试验件） |
| `seascape_top_surface.gdshader` | **无代码引用** | **删除或归档** |
| `water/water_surface.gdshader` | **无代码引用** | **删除或归档**（连同 `water/*.png`） |
| `weather_realtime_fx.gdshader` | Init 中赋值为 null，从未加载 | **删除或修复 M-3** |
| `Scene/WaterPainterlyBlockPreview.cs.bak` | 备份 | **删除** |
| `Scene/WaterPainterlyBlockPreview.tscn.bak` | 备份 | **删除** |

所有 `*.bak` 文件均无代码引用（grep 确认），安全删除。

### 4.3 Tools 公共抽象建议

`VoxelTilePreviewTool`（2060 行）和 `PlaceholderPaintTool`（1700 行）共享如下模式：
- `Control` 子类 + 左/中/右三栏 `BuildUi()` 骨架
- `FileDialog` / `ConfirmationDialog` / `AcceptDialog` 管理
- `_suppressUiEvents` 防递归标志
- 页码翻页 + 过滤器组合
- workspace JSON 加载/保存 + "有未保存更改"守卫

**建议**: 抽取一个 `EditorToolBase : Control` 提供：
1. `BuildThreeColumnLayout()` 返回 `(VBoxContainer left, VBoxContainer center, VBoxContainer right)`
2. `_suppressUiEvents` + `WithUiSuppressed(Action)` 模式
3. `EnsureUnsavedGuard(Action onConfirm)` 通用逻辑
4. Dialog 创建工厂方法

`ResourceCatalogEditor`（1745 行）也可受益于此基类，但优先级低于前两者。

### 4.4 渲染 pass 顺序

当前 `Render()` 方法（L912-1009）的阶段是隐式的——通过 `Stopwatch.GetTimestamp()` 测量块划分。建议引入一个轻量枚举仅用于性能跟踪和日志：

```csharp
private enum RenderStage { Terrain, LightMap, Entity, Hover, Scene }
```

不需要变成 command pattern，但可让 `RenderTraceSample` 的字段名和代码块对应关系更清晰。

### 4.5 FOW / Day-Night / Weather 更新顺序与缓存依赖

当前 `Flush()` 中的顺序是：
1. `_fogTracker.Update(_state)` — 依赖 `_state.World`, actor 位置
2. `_weatherFxController.RefreshWeatherScreenFxTarget()` — 依赖 `_state.PlayerZ`
3. `Render()` → 内部 `DayNightCycle.Compute(_state.Turn)` → `_lightingCalc.Configure()` → `_lightMap.Rebuild()`
4. `_weatherFxController.UpdateWeatherScreenFxOverlay()`

这个顺序是正确的（FOW 先于渲染，day-night 在渲染内部、light map 在 terrain draw 之后）。`LightMap.Rebuild()` 有版本号缓存（`_lastWorldVersion`），只在世界数据变化时重算。**无已知顺序 bug**。

### 4.6 缩放/模式切换残留状态

`ToggleRenderMode()` 当前只返回一个 localization 字符串（L780-781），不做实际切换——"2D vs 2.5D" 模式切换已经被移除，仅保留 iso 模式。确认无残留状态问题。

### 4.7 AutoTestModule 与运行时隔离

`AutoTestModule` 通过 `IAutoTestHost` 接口与运行时交互，不直接持有渲染器或 UI 引用。`ResourceScenePaths`（L26-35）是硬编码的场景路径列表——如果路径变更需要手动同步。建议改为从某个 manifest 或 const 统一引用。

---

## 5. 已确认良好的实践

1. **VoxelSpritePool** — 经典 pooling 模式，每帧 `BeginFrame()`/`EndFrame()` 隐藏多余精灵，不创建也不销毁 Node。WeatherFxController 的 5 个 sprite pool 也遵循相同模式。
2. **TerrainAtlas 单张图集合批** — 所有地形面打包进一张 ImageTexture，`VoxelFaceBatchCanvas._Draw()` 使用 `DrawTextureRectRegion` 一次 draw call 完成全部地形面绘制。
3. **ChunkTerrainSurfaceCacheStore** — 按 `TerrainGeometryRevision` 缓存，世界数据不变时跳过重建，且每帧 Prune 已卸载的 chunk。
4. **IsoCoordUtil 的 CompareSortOrder** — 四级稳定排序（对角线 → 深度 → 屏幕 X → 世界 X/Y），消除了同边缘面片的绘制抖动。
5. **VoxelTerrainShading 统一常量** — wall/solid/non-solid 的 edge/darken 参数在一个类中定义，TerrainAtlas 和 VoxelTilePreviewTool 共用，避免了参数漂移。
6. **DayNightCycle** — 纯函数，无状态，Season 过渡有 SmoothStep 混合防止硬跳变。
7. **ActorMotionTracker** — 连续段限制（MaxContinuousSegmentsPerActor=3）防止无限队列，blocking gate 机制让 UI 在动画完成前不发新命令。
8. **MonsterMapAssetGenerator** — `using` 语句正确释放 System.Drawing.Bitmap（L1006-1041），finally 块清理所有生成的 Bitmap。

---

报告已完成。所有条目均基于实际代码读取和 grep 搜索，附有文件路径和行号证据。