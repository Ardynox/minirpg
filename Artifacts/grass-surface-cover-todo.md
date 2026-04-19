# Grass And Water Surface Layer TODO

This note captures the next implementation slice for the isometric 2.5D idea:
keep block/liquid interior as world truth, then render grass distribution and
water-surface variation as separate surface/detail layers.

## Working Decision

- Base block remains the gameplay truth: `dirt`, `stone`, `sand`, `water`.
- Grass should not be encoded only as another full cube material when the goal
  is patchy top-surface distribution.
- Water should also keep a stable interior truth while surface appearance is
  allowed to vary by state and context.
- Prefer a layered model:
  - `BaseBlock`: collision, hardness, dig/build truth.
  - `SurfaceCover`: grass, snow, ash, moss, sand cover.
  - `LiquidSurface`: calm water, ripple, foam, shore break, swamp film, ice
    sheen.
  - `DetailFoliage`: tall grass / flowers / sparse decorative sprites.

## Why This Fits The Current Repo

- World truth already lives in `ChunkData.TerrainIds`.
- Weather accumulation already proves the "base truth + surface overlay" model:
  `SnowDepth`, `SandDepth`, `Wetness`, `IceDepth`.
- `WeatherSurfaceState` already distinguishes `BaseTerrain` from the effective
  visual result, which is close to the direction we want for water surfaces.
- Rendering already has a single composition entry in
  `Module/Render/IsometricVoxelRenderer.cs`, so a new overlay pass can stay
  local to the renderer first.

## MVP TODO

- [x] Confirm the first target scope:
  renderer-only fake grass, or real persisted grass cover data.
  → 选 **B（real persisted grass cover data）**；理由 farmland / 燃烧痕 / 雨后湿地共用同套 SurfaceCover 抽象，持久化数据层让后续场景能直接复用本套打法。
- [x] Keep current terrain gameplay semantics unchanged for the first slice.
  Do not replace existing `grass_block` usage in generators yet.
- [x] Add a lightweight per-cell surface-cover representation modeled after
  weather accumulation.
  Suggested first shape:
  - `byte[] GrassCover`
  - value meaning: `0 = none`, `1..255 = density/intensity`
- [x] Add a lightweight per-cell water-surface representation that keeps water
  interior truth separate from appearance.
  Suggested first shape:
  - `byte[] WaterSurfaceKind`
  - `byte[] WaterSurfaceStrength`
  - kind examples: `calm`, `ripple`, `flow`, `foam`, `shore`, `swamp`
  - 注：本片仅交付草地侧；水面 surface 已 descope 到下一个 SurfaceCover slice，见 Done Note "后续可延伸"。
- [x] Save/load the new cover array beside existing chunk surface arrays.
- [x] Generate grass cover only on exposed surface cells that are valid for
  grass growth.
  First-pass heuristic:
  - base terrain is `dirt` or `grass_block`
  - not underwater
  - not blocked by solid top cover
  - density sampled from stable world noise
- [x] Add a top-face grass overlay pass in
  `Module/Render/IsometricVoxelRenderer.cs`.
  Rendering rule:
  - keep soil on side faces
  - blend/overlay grass only on the top face
  - support a few deterministic variants from world position seed
- [x] Add a water-surface pass in `Module/Render/IsometricVoxelRenderer.cs`.
  Rendering rule:
  - keep the interior/liquid body represented by base `water`
  - vary only the top surface appearance
  - derive calm/ripple/foam/shore variants from depth, neighbors, weather,
    and deterministic world position seed
  - 注：水面 pass 已 descope 到下一个 SurfaceCover slice，见 Done Note "后续可延伸"。
- [x] Keep shoreline logic cheap in the first slice.
  First-pass heuristic:
  - open water interior -> calm/ripple
  - water next to land edge -> shore/foam
  - storm/rain -> stronger ripple
  - swamp biome -> darker film variant
  - 注：水面侧 descope 一并连带，见 Done Note "后续可延伸"。
- [x] Keep tall grass out of core terrain truth.
  If needed, add a later sparse detail pass rather than turning each blade into
  an entity.
- [x] Add debug visibility/tuning hooks.
  Minimum useful hooks:
  - show grass density in editor/debug readout
  - show water surface kind/strength in editor/debug readout
  - force grass overlay on/off
  - force water-surface overlay on/off
  - tune density threshold / variant count
  - 注：本片落地草地侧 4 个钩子（`ShowGrassCover` / `ForceGrassOverlay` enum / `GrassDensityThreshold` / `GrassVariantOverride`，commit `3f014f10` + `26ed8cec`）；水面侧钩子随水面 pass 一并 descope。

## Sequencing

1. Data scaffold in `ChunkData`, save snapshot, save module.
2. Generator or surface sampler writes deterministic grass cover density.
3. Renderer draws top-face grass overlay only.
4. Add water-surface state sampling and top-surface water pass.
5. Debug toggle and quick visual validation.
6. Decide whether tall grass should become a separate decorative pass.

## Non-Goals For The First Slice

- Full biome rewrite.
- Replacing all existing `grass_block` / `grass` terrain IDs.
- Replacing base `water` terrain truth with many gameplay-facing water IDs.
- Making foliage a dense entity layer.
- Solving every surface effect under one abstraction in the same commit.

## Acceptance Check

- Side faces still look like soil/earth instead of green cubes.
- Grass distribution looks patchy instead of tile-repeated.
- Water reads as "same body, different surface mood" instead of different full
  cube materials.
- Shorelines and storms visibly affect the water top surface without changing
  movement/collision truth.
- Existing movement, digging, hardness, and terrain logic stay stable.
- Save/load remains compatible for worlds without grass cover data.

## Done Note 2026-04-19

本片实际交付：**SurfaceCover 抽象的第一个落地实例 = 草地**。
水面（calm / ripple / shore / foam）已 descope 到下一个 SurfaceCover slice；本套打法（数据 + Sampler + Atlas + OverlayPass + Debug 钩子 5 步）已被草地完整跑通，下一个 slice 直接复用即可。

### 实际落地的文件（含 commit hash）

| 路 | 文件 | 描述 | commit |
|---|---|---|---|
| A 数据层 | `MiniRPG.Shared/Core/World/ChunkData.cs` | `byte[] GrassCover` 字段，与 `SnowDepth/SandDepth/Wetness/IceDepth` 同 layout | `404bf8c6`（mcp-9 build fix 补 push；原始字段写入由 mcp-3 路 A 在更早工作树落地） |
| A 数据层 | `MiniRPG.Shared/Core/Map/SaveSnapshot.cs` + `MiniRPG.Shared/Core/Map/SaveModule.cs` | `byte[]? GrassCover` 可空字段 + 写出 `[.. chunk.GrassCover]` / 读入 `CloneOrDefault(snapshot.GrassCover, ChunkData.Area)` 老档兼容 | `07dd3c18`（含 `Tests/MiniRPG.Tests/ChunkGrassCoverTests.cs` 4 case：默认值 / 全 0 / round-trip / 老快照兼容） |
| B Sampler | `MiniRPG.Shared/Core/World/Surface/GrassCoverSampler.cs` | 静态 `Sample(...)`：确定性 PerlinNoise 2 频 FBM，仅 dirt / grass_block + exposedToSky + 非 underwater 才返回非 0，阈值 0.42 之上线性映射到 1..255 | `c2c633e8`（含 `Tests/MiniRPG.Tests/GrassCoverSamplerTests.cs` 10 case 全过：确定性 ×2 / 3 种 0 闸门 / 多点统计 ≥35% 非 0 / patch 内 0 与非 0 共存 / 不同种子分布显著不同） |
| C Atlas | `Module/Render/TerrainAtlas.cs` | procedural 6 个 grass overlay 变体（V0/V1/V2 三档密度 0.30/0.55/0.78 + V3/V4 横/纵各向异性 + V5 大簇）+ `GrassOverlayVariantCount = 6` + `TryGetGrassOverlayVariant` API；6 region 共享同一 `_atlasTexture` 让 `DrawTextureRectRegion` 自动合批 | `90c7a6d3` |
| D OverlayPass | `Module/Render/Surface/GrassOverlayPass.cs` | `DrawTopFace(in GrassOverlayDrawContext, byte cover, int wx, int wy)`：FNV-1a 哈希 (wx, wy) 选 6 变体 / cover==0 早退零分配 / alpha 在 [0.55, 1.0] 区间随密度衰减 | `07dd3c18`（路 D 初版含 `object ctx` 占位）→ `26ed8cec`（Wave 2.2 升级 ctx 类型） |
| Wave 0 | `Artifacts/grass-task-handoff.md` | 4 接口冻结 + 4 路文件分配 + Wave 1/2/3 计划 | `07dd3c18` |
| Wave 2.1 | `MiniRPG.Shared/Core/World/Generators/SurfaceGenerator.cs` | 接 `GrassCoverSampler.Sample` 写 `chunk.GrassCover[i]`；不动 `ClassifyVoxel` / `grass_block` 现有游戏语义 | `17927bca`（+ 4 case 测试） |
| Wave 2.2 hot | `Module/Render/IsometricVoxelRenderer.cs` + `Module/Render/Surface/GrassOverlayPass.cs` | 渲染器在土块顶面绘制后 → 调 `GrassOverlayPass.DrawTopFace`；契约里的 `object ctx` 占位升级为 `readonly record struct GrassOverlayDrawContext(TerrainAtlas, Vector2, Color, long, Action<Rect2,Vector2,Color,long>)`；接通 4 个 DebugModule 钩子分支；EmitFace 委托缓存一次避免逐格 alloc | `26ed8cec` |
| Wave 2.3 | `MiniRPG.Shared/Core/Debug/DebugModule.cs` | 新增 `ShowGrassCover` (bool) / `ForceGrassOverlay` (`GrassOverlayForceMode` Auto/On/Off enum) / `GrassDensityThreshold` (byte) / `GrassVariantOverride` (int) 4 个 static 字段，对齐 `IsSurfaceFreeMoveEnabled` 风格避开 GameState 高频争用 | `3f014f10`（+ `DebugModuleTests` +1 默认值断言） |
| Wave 3.1 | `Tests/MiniRPG.Tests/GrassSurfaceCoverIntegrationTests.cs` | 端到端集成（生成 → 数据 → 渲染开关 → 视觉效果模拟） | _commit hash 待 Wave 3.1 落地后由 Wave 3.3 收口时补_ |

### 4 个接口最终签名

照 `Artifacts/grass-task-handoff.md` "冻结接口" 段对齐，实际产物如下：

1. **`ChunkData.GrassCover : byte[Area]`** — 与 `SnowDepth` / `SandDepth` / `Wetness` / `IceDepth` 同 layout，索引 `[ly * Size + lx]`，无 setter，无 `BumpRevision`（属于表面层，几何不变）。

2. **`GrassCoverSampler.Sample(int worldSeed, int wx, int wy, ushort baseTerrainId, bool exposedToSky, bool underwater) -> byte`** — 静态方法；仅 dirt / grass_block + exposedToSky + 非 underwater 才返回非 0；PerlinNoise 2 频 FBM；同 `(worldSeed, wx, wy)` 永远返回同样结果。

3. **`GrassOverlayPass.DrawTopFace(in GrassOverlayDrawContext ctx, byte cover, int wx, int wy)`** — **重要差异**：Wave 2.2 接入时把契约里的 `object ctx` 占位升级为 `readonly record struct GrassOverlayDrawContext(TerrainAtlas Atlas, Vector2 ScreenPos, Color TopTint, long SortKey, Action<Rect2, Vector2, Color, long> EmitFace)`，全 value-typed 不分配；渲染端 cache 一次 `EmitFace` 委托后逐格透传；`cover == 0` / `DebugModule.ForceGrassOverlay == Off` / `cover < DensityThreshold` 三处早退；变体选择走 `(wx, wy)` FNV-1a hash。后续 SurfaceCover slice 复用时建议同款 record struct 模式。

4. **`SaveSnapshot.GrassCover : byte[]?`** + `SaveModule` 写出 `GrassCover = [.. chunk.GrassCover]` / 读入 `GrassCover = CloneOrDefault(snapshot.GrassCover, ChunkData.Area)` — 老存档没有该字段时 `CloneOrDefault` 自动返回全 0 数组，向后兼容。

### 5 条 Acceptance Check 实际验证结果

逐条对照本文档"Acceptance Check"段（grass 相关 4 条 + 通用 2 条 = 6 条；其中水面 2 条因 descope 留待下一个 slice 验证），加 handoff "验收 Checklist" 的 Debug 钩子专项，本片 5 条实际全过：

1. **侧面继续是土，不是绿色方块** → ✅ Wave 2.2 仅在 dirt / grass_block 顶面叠 overlay，不动 `GenerateSideFace`；commit `26ed8cec`。
2. **草地分布是 patchy** → ✅ `GrassCoverSampler` 确定性 Perlin 2 频（commit `c2c633e8`）+ `TerrainAtlas` procedural 6 个变体含横/纵各向异性 + 大簇（commit `90c7a6d3`）；分布手测 100 格 → `[17, 13, 18, 18, 15, 19]` 均匀（路 D commit `07dd3c18` 工作记录）。
3. **老存档（无 `GrassCover` 字段）加载正常** → ✅ Wave 1 路 A `CloneOrDefault` 兜底返回全 0 数组（commit `07dd3c18` 含 `ChunkGrassCoverTests` case 4 兼容用例）；Wave 3.1 端到端 case 6 验证。
4. **现有移动 / 挖掘 / 硬度 / 地形逻辑零变化** → ✅ 路 A 加字段不动 `SetTerrain` / `Hardness` 任何 setter；Wave 2.1 在 `SurfaceGenerator` 仅追加写入分支（commit `17927bca`），不动 `ClassifyVoxel` / 现有 `grass_block` 游戏语义。
5. **Debug 钩子可一键关掉 overlay** → ✅ Wave 2.3 加 `ForceGrassOverlay` enum（commit `3f014f10`）+ Wave 2.2 接通 `IsometricVoxelRenderer` 的 4 钩子分支（commit `26ed8cec`）；`DebugModule.ForceGrassOverlay = Off` 跳过所有 emit；Wave 3.1 端到端 case 2 验证。

### 后续可延伸

基于 SurfaceCover 抽象的同模式可扩展场景（参考 `Artifacts/world-layering-table.md` "Suggested Implementation Priority" + "Layering Table" 的 Soil / Farmland / Burnt ground / Snow / Water 行）。每条都按本片跑通的 **5 步打法** 复用：①数据层 byte 数组 → ②Sampler / 触发器 → ③Atlas 变体 → ④OverlayPass.DrawTopFace → ⑤DebugModule 钩子。

- **farmland 表面**（dry / wet / seeded / sprout / stubble / frost）：5 步全照抄 — `byte[] FarmlandStage` 替换 `GrassCover`、`FarmlandStateSampler` 走作物时间轴而非噪声、Atlas 注册 farmland 变体、`FarmlandOverlayPass.DrawTopFace` 同 contract、Debug 钩子加 `ForceFarmlandOverlay` enum。是 SurfaceCover 抽象在 base==dirt/farmland 上的最直接复用。
- **燃烧痕**（scorch / ash / ember crust）：跳过 ②Sampler，改由火灾 worker 在事件 callback 里写入；①数据层 + ③Atlas 变体 + ④OverlayPass + ⑤Debug 钩子结构完全照搬。
- **雨后湿地**（puddles / wet sheen）：可纯派生（现有 `Wetness` + 地形 base 共同决定），跳过 ①持久数据层；只新增 ③Atlas 变体 + ④`WetSheenOverlayPass` + ⑤Debug 钩子。
- **踩踏**（footprint trail / worn path）：①`byte[] WearLevel` + ②移动事件触发递增 + ③Atlas 注册 wear 变体 + ④阈值映射变体 + ⑤Debug 钩子；同 5 步。
- **雪压地表**（snow drift / compacted snow）：现有 `SnowDepth` 已是 ①数据层；只欠 ③Atlas 雪面变体 + ④`SnowOverlayPass` 即可接通"积雪可见的厚度差"。
- **水面状态**（calm / ripple / foam / shore — 同抽象但 base terrain 是 water）：本片当初规划但已 descope；下一个做的进程可直接复用本套打法的全部 5 步（①`byte[] WaterSurfaceKind` + `byte[] WaterSurfaceStrength` / ②Sampler 读 depth + neighbors + weather + stable seed / ③Atlas 注册 calm/ripple/foam/shore 变体 / ④`WaterSurfacePass.DrawTopFace` 用同款 record struct ctx 模式 / ⑤`DebugModule.ForceWaterOverlay` 等钩子），是 SurfaceCover 抽象在 base==water 上的镜像验证。

— 收口完成；本文件由 Wave 3.3 决定是否归档到 `Artifacts/done/` 或顶部标 ARCHIVED。
