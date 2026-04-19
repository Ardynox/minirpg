# Grass Surface Cover 任务并行交接契约

> **ARCHIVED 2026-04-19**：草地任务全部 4 路（Wave 1 路 A/B/C/D）+ Wave 2.1/2.2/2.3 + Wave 3.1/3.2/3.3 已全部完成。
> 实际落地结果（含 9 个 commit hash 链 + 5 条 Acceptance Check 验证 + 后续 SurfaceCover 延伸方向）见 `Artifacts/grass-surface-cover-todo.md` 末尾 **Done Note 2026-04-19** 段。
> 本文件保留作为"如何做并行任务交接契约 + Wave-based 调度"的范例参考，下次做 farmland / 燃烧痕 / 雨后湿地 / 踩踏 / 雪压 / 水面状态等 SurfaceCover 类抽象时可直接照搬本文档结构。

本文档冻结 4 个接口签名 + 4 路文件分配，让多个 AI 进程可独立并行做。
**所有路按本契约对齐；签名变动必须先回到本文档讨论再改。**

源决策文档：`Artifacts/grass-surface-cover-todo.md`、`Artifacts/world-layering-table.md`。
不要重读全仓；只读本文档 + 自己路指明的 1–2 个文件。

## 决策标尺

"这个改动离最终可玩更近吗？" — **Y**。
草地是 SurfaceCover 抽象的第一个落地实例；同一模式后续会复用到 farmland、燃烧痕、雨后湿地、踩踏等场景。本片只动顶面渲染 + 一个 byte 数组，不动现有 grass_block 游戏语义，零回归风险。

## 冻结接口（任何路都按这 4 条对齐）

### 1. ChunkData 新增字段

```csharp
// 位置：MiniRPG.Shared/Core/World/ChunkData.cs，紧挨 SnowDepth 等字段后
public byte[] GrassCover { get; set; } = new byte[Area];
```

- 含义：`0 = 无草`，`1..255 = 草地密度/强度`。
- 索引方式与 `SnowDepth` 完全一致：`GrassCover[ly * Size + lx]`。
- 不需要新 setter / 不需要 `Bump...Revision`（属于表面层，几何不变）。

### 2. GrassCoverSampler 静态方法

```csharp
// 位置：MiniRPG.Shared/Core/World/Surface/GrassCoverSampler.cs（新文件）
namespace MiniRPG.Core.World.Surface;

public static class GrassCoverSampler
{
    /// <summary>
    /// 在生成阶段为单个格子采样草地密度。
    /// 必须是确定性的：同一 (worldSeed, wx, wy) 永远返回同样结果。
    /// </summary>
    /// <param name="worldSeed">世界种子（来自 WorldConfig 等）。</param>
    /// <param name="wx">世界 X。</param>
    /// <param name="wy">世界 Y。</param>
    /// <param name="baseTerrainId">该格的 base terrain（来自 TerrainIds）。</param>
    /// <param name="exposedToSky">该格上方是否畅通（不被实心格挡住）。</param>
    /// <param name="underwater">该格是否在水下。</param>
    /// <returns>0 = 无草；1..255 = 密度。</returns>
    public static byte Sample(
        int worldSeed,
        int wx,
        int wy,
        ushort baseTerrainId,
        bool exposedToSky,
        bool underwater);
}
```

- 仅在 `baseTerrainId == TerrainRegistry` 中查到的 `dirt` / `grass_block` 上返回非零。
- `exposedToSky == false` 或 `underwater == true` 时返回 0。
- 噪声采样方式自定（推荐 Perlin / Simplex / 多频叠加），但同一坐标 + 同一种子结果必须稳定。

### 3. GrassOverlayPass 渲染入口

```csharp
// 位置：Module/Render/Surface/GrassOverlayPass.cs（新文件）
namespace MiniRPG.Module.Render.Surface;

public static class GrassOverlayPass
{
    /// <summary>
    /// 在已绘制的土块顶面之上叠加草地。仅画顶面，不动侧面。
    /// </summary>
    /// <param name="ctx">渲染上下文（具体类型在接入时填，先用 object 占位也可）。</param>
    /// <param name="cover">本格 GrassCover byte（0..255）。</param>
    /// <param name="wx">世界 X，用于哈希选变体。</param>
    /// <param name="wy">世界 Y，用于哈希选变体。</param>
    public static void DrawTopFace(object ctx, byte cover, int wx, int wy);
}
```

- `cover == 0` 必须直接 return，零开销。
- 变体选择：`hash(wx, wy)` 决定贴图索引；同坐标永远同变体。
- 路 D 可以先用 `object ctx` 占位独立开发；Wave 2.2 接入时再换成 `IsometricVoxelRenderer` 内部的真实上下文类型。

### 4. SaveSnapshot / SaveModule 字段

```csharp
// 位置：MiniRPG.Shared/Core/Map/SaveSnapshot.cs，紧挨 SnowDepth/SandDepth/Wetness/IceDepth 后
public byte[]? GrassCover { get; set; }
```

```csharp
// 位置：MiniRPG.Shared/Core/Map/SaveModule.cs
// 写出（约 481 行附近）：
GrassCover = [.. chunk.GrassCover],

// 读入（约 496 行附近）：
GrassCover = CloneOrDefault(snapshot.GrassCover, ChunkData.Area),
```

- 老存档没有 `GrassCover` 字段时 `CloneOrDefault` 自动返回全 0 数组，向后兼容。

## 4 路文件分配（Wave 1）

| 路 | 负责通道 | 动的文件 | hot file? | 状态 |
|---|---|---|---|---|
| **A 数据层** | qtwx-mcp-3 | `MiniRPG.Shared/Core/World/ChunkData.cs`、`MiniRPG.Shared/Core/Map/SaveSnapshot.cs`、`MiniRPG.Shared/Core/Map/SaveModule.cs` | 否 | ✅ 完成 2026-04-19（4 单测全过） |
| **B Sampler** | qtwx-mcp-4 | **新文件** `MiniRPG.Shared/Core/World/Surface/GrassCoverSampler.cs` + **新文件** `Tests/MiniRPG.Tests/GrassCoverSamplerTests.cs` | 否 | ✅ 完成 2026-04-19（10 单测全过） |
| **C 贴图 + Atlas** | 待领 | `Data/Assets/iso8/...`（新 PNG，4–6 张草地变体）、`Module/Render/TerrainAtlas.cs`（注册新条目 + 查询 API） | TerrainAtlas 半 hot | ⏳ 未开工，是 Wave 2.2 的最后障碍 |
| **D 渲染 pass 草稿** | qtwx-mcp-5 | **新文件** `Module/Render/Surface/GrassOverlayPass.cs` | 否 | ✅ 完成 2026-04-19（FNV-1a 选 6 变体、cover==0 零分配、占位 ctx） |

Wave 1 的 4 路完全独立，可同时开。**当前缺路 C，需补做。**

## Wave 2 接入（部分可并行）

实际依赖关系修正：Wave 2.1 / 2.3 / 路 C 三者互相独立，可同时开；Wave 2.2 必须等三者全部完成。

| 步 | 负责通道 | 动的文件 | hot file? | 依赖 | 状态 |
|---|---|---|---|---|---|
| **2.1 接 Sampler 到生成器** | 待领 | `MiniRPG.Shared/Core/World/Generators/SurfaceGenerator.cs` | 否 | 路 A、B | ⏳ 未开工 |
| **2.3 Debug 钩子** | 待领 | `MiniRPG.Shared/Core/Debug/DebugModule.cs`（+ 面板/cvar 视情况） | 否 | 无 | ⏳ 未开工 |
| **路 C 补做** | 待领 | `Data/Assets/iso8/`、`Module/Render/TerrainAtlas.cs` | 半 hot | 无 | ⏳ 未开工 |
| **2.2 接 OverlayPass 到主渲染器** | 待领 | `Module/Render/IsometricVoxelRenderer.cs`、`Module/Render/Surface/GrassOverlayPass.cs` | **HOT** | Wave 2.1 + 2.3 + 路 C | ⏳ 必须等前三者 |

Wave 2.2 是唯一真正的协调点。**碰 IsometricVoxelRenderer.cs 前必须在 `Artifacts/wip.md` 登记**，hot file 不过夜。

## Wave 3 收口（依赖 Wave 2.2 完成）

| 步 | 负责通道 | 动的文件 | 状态 |
|---|---|---|---|
| **3.1 端到端集成测试** | 待领 | **新文件** `Tests/MiniRPG.Tests/GrassSurfaceCoverIntegrationTests.cs` | ⏳ 等 Wave 2.2 |
| **3.2 文档收口** | 待领 | `Artifacts/grass-surface-cover-todo.md`（MVP TODO 打勾 + 写"Done" 段） | ⏳ 等 Wave 2.2 |
| **3.3 归档 handoff** | 待领 | 把本文件移到 `Artifacts/done/` 或在顶部标 ARCHIVED | ⏳ 等 3.1 + 3.2 |

注：路 A 已交付的 `ChunkGrassCoverTests.cs`、路 B 已交付的 `GrassCoverSamplerTests.cs` 是单元层；3.1 的目标是端到端（生成 → 数据 → 渲染开关 → 视觉效果模拟）。

## 验收 Checklist（来自 grass-surface-cover-todo.md）

- [ ] 侧面继续是土，不是绿色方块
- [ ] 草地分布是 patchy 的，不是 tile-repeat
- [ ] 老存档（无 `GrassCover` 字段）能正常加载并显示成无草
- [ ] 现有移动 / 挖掘 / 硬度 / 地形逻辑零变化
- [ ] Debug 钩子可以一键关掉草地 overlay 验证回归

## 协作纪律提醒

- 开工前先读 `Artifacts/wip.md`，确认没人在动你这条路。
- 动 hot file 前必须在 `Artifacts/wip.md` 登记。
- 不要扩散修改；本契约外的文件不要碰。
- 完成后在 wip.md 把自己挪到"已完成"段。
