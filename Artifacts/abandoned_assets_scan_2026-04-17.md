# 废弃 / 低质量素材扫描 — 2026-04-17

扫描维度：
1. **引用状态**：是否被 `.cs / .json / .tscn / .tres / .gd / .ps1` 里的路径字符串引用（排除 `pz_tile_catalog.json`、`pz_world_visual_registry.json`、`rename_plan.csv` 等素材目录文件）
2. **内容质量**：用 Read 抽样查看实际图像

## 整体数字（Assets/Art）

| 目录 | 总 PNG | 被运行时引用 | 利用率 |
| --- | ---: | ---: | ---: |
| `PZ_Tiles/` | 7038 | 46 直接（54 含 catalog） | 0.65% |
| `Tilesets/FantasyKingdom/` | 3287 | **0** | **0%** |
| `Placeholders/` | 277 | 168 | 61% |
| `Generated/` | 89 | 30 | 34% |
| **总计** | **10691** | **245 / 253** | **2.3%** |

## 高置信度「废弃 + 低质量」（强烈建议删除）

### 完全空白 PNG（画布被创建但没有绘制内容）

| 路径 | 大小 | 内容 |
| --- | ---: | --- |
| `Assets/Art/Placeholders/monster_map/monster_bear.png` | 867B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_goblin.png` | 1101B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_orc_warrior.png` | 867B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_rat.png` | 867B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_scorpion.png` | 867B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_skeleton.png` | 880B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_slime.png` | 867B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_spider.png` | 878B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_treant.png` | 867B | 空白 |
| `Assets/Art/Placeholders/monster_map/monster_wolf.png` | 867B | 空白 |
| `Assets/Art/Placeholders/weather/snow.png` | 95B | 空白 |
| `Assets/Art/Placeholders/weather/snow_cover.png` | 199B | 空白 |
| `Assets/Art/Placeholders/weather/ice_gloss.png` | 258B | 空白 |

**13 张完全空白 PNG**，总大小 ~10KB（不影响体积，但占用仓库心智空间）。

### 纯色调试占位

| 路径 | 内容 |
| --- | --- |
| `Assets/Art/PZ_Tiles/erosion/e_debug_1_1..e_debug_1_13.png` | 13 张纯色菱形（红、黄、绿等各一）|

PZ_Tiles/README.md 明确标记这些是 "调试占位组"，不打算真用。**13 个纯色菱形 + 13 个 .import = 26 个文件**。

## 中高置信度「废弃但质量高」（`Tilesets/FantasyKingdom/`）

**3287 个 PNG 整套 第三方幻想王国素材包**，零引用，磁盘占用规模可观。

抽样看到：
- 角色动画 sprite sheet（例如 Enemy 1 攻击动画拆成 ~100 帧）
- 环境素材（Environment/ 1876 张）
- 动画库（Animations/ 1161 张）

**艺术质量很高，但从未被集成到游戏里。** 可能是早期买来的储备。要不要删取决于是否还有意向用它们。

### 推荐处理路径（FantasyKingdom）

- **选 A**：移到 `Assets/Art/_Archive/FantasyKingdom/`（保留但标记为归档）
- **选 B**：删除（仓库瘦身明显，未来如需再引进）
- **选 C**：写一份"未来使用计划"的 TODO 文档，决定用哪部分，再按需保留

## 中置信度「占位质量一般但被使用中」

以下素材**被运行时引用**，但质量是"粗糙像素占位"而非最终美术：

| 目录 | 数量 | 特征 |
| --- | ---: | --- |
| `Placeholders/portraits/` | 10 | 低分辨率角色肖像（像素画，动作僵硬） |
| `Placeholders/npc_map/` | 10 | 低分辨率 NPC 立绘 |
| `Placeholders/item_icons/` | 130 | 简单物品图标（大多 300-1000B，极小） |
| `Placeholders/effects/` | 106 | 特效占位（部分几乎透明） |
| `Placeholders/branding/` | 2 | 品牌占位 |

**这些被使用中**，不能随意删，但可以列为"等待美术替换"的渐进目标。

## 低置信度「未被运行时直接引用但存活于 catalog」

- PZ_Tiles 里 **6992 个**（`7038 - 46`）PNG 未被运行时直接路径引用
- 它们进 `pz_tile_catalog.json` 作为"候选池"，编辑器 / 工具里可能被引用
- **不建议删**，保留为"素材储备" 符合 PZ_Tiles 的定位（上游素材库）

## 建议行动（从安全到激进）

### 第 1 步（立即可做，零风险）

删除 13 张完全空白 PNG + 13 张调试菱形 tile + 它们对应的 `.png.import`：

- `Placeholders/monster_map/*.png`（10 张）
- `Placeholders/weather/snow.png` / `snow_cover.png` / `ice_gloss.png`（3 张）
- `PZ_Tiles/erosion/e_debug_1_*.png`（13 张）

**共 26 张 PNG + 26 张 .import = 52 个文件**

确认没有测试会因"monster_map/xxx"这种 id 编造而失败（实际上这些空白图没被用到）。

### 第 2 步（需要决策）

`Tilesets/FantasyKingdom/` 3287 张如何处理？

### 第 3 步（长期）

把"占位 → 真美术素材"作为长期 TODO，逐步替换 `Placeholders/portraits/npc_map/item_icons/effects` 下的内容。
