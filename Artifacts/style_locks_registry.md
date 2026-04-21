# style_lock 登记册

> 配合 `Artifacts/素材身份卡表_2026-04-20.md` §0.2：每批发包前把「风格咨询清单」的结论记入 `Artifacts/style_locks_<batch>.md`，并在此更新状态。

| 批次 | style_lock 文件 | 状态 | 备注 |
| --- | --- | --- | --- |
| B0 | `Artifacts/style_locks_B0.md` | **已锁定** | 全局锚点 |
| B1 | `Artifacts/style_locks_B1.md` | **已锁定（初版）** | 磁盘齐；Gemini 细化可追加「批复」段 |
| B2 | `Artifacts/style_locks_B2.md` | **已锁定（初版）** | 同上 |
| B3 | `Artifacts/style_locks_B3.md` | **已锁定（初版）** | 同上 |
| B4 | `Artifacts/style_locks_B4.md` | **已锁定（初版）** | 62×256 人脸；可 Banana 重刷 |
| B5 | `Artifacts/style_locks_B5.md` | **已锁定（初版）** | 同上 |
| B6 | `Artifacts/style_locks_B6.md` | **已锁定（初版）** | 同上 |
| B7 | `Artifacts/style_locks_B7.md` | **已锁定（初版）** | limb 文件名见映射补充 |
| B8 | `Artifacts/style_locks_B8.md` | **已锁定（初版）** | 同上 |
| B9 | `Artifacts/style_locks_B9.md` | **已锁定（初版）** | 88 PNG + `EquipmentOverlayPaths` + 投影后叠图 |
| B10 | `Artifacts/style_locks_B10.md` | **登记待批** | 整批重刷最晚；开批前补 Gemini 批复 |
| B11 | `Artifacts/style_locks_B11.md` | **已锁定（初版）** | 蓝图占位 10 张已齐 |
| B12 | `Artifacts/style_locks_B12.md` | **P2** | 细节/mp 非 P0 |

## 磁盘占位进度快照（`Placeholders`）

| 子目录 | PNG 数 | 身份卡目标 |
| --- | ---: | --- |
| `effects/` | 8 | B1.1 + 扩展 |
| `weather/` | 9 | B1.2 + `snow` / `snow_cover` / `ice_gloss` |
| `branding/` | 2 | B1.3 |
| `npc_map/` | 10 | B2.1 |
| `portraits/` | 10 | B2.2 |
| `item_icons/` | 130 | B3 |
| `ui_icons/` | 169 | B5+B6+B7 等 |
| `crops/` | 21 | B8.1 |
| `material_world/` | 24 | B8.2 |

### `Assets/Faces/**/*.png`（B4）

| 指标 | 值 |
| --- | ---: |
| 张数 | **62** |
| 画布 | **256×256** |

### `Assets/Art/Generated/equipment_overlays/*.png`（B9）

| 指标 | 值 |
| --- | ---: |
| 张数 | **88**（5+3+3 种 ×8 向） |
| 画布 | **256×256**（与 `MapSpriteRuntimeFactory.ExportSize` 一致） |
| 生成 | `Tools/banana/b9_equipment_overlay_fill_local.py` |

**下一优先（产品级美术）**：B10 Generated 重刷；B12 P2；各批「Gemini 批复」段可按需补写。

## 与身份卡原文的差异

1. **天气**：见 `WeatherAssetIds`。  
2. **B7 limb 文件名**：见 `Artifacts/素材身份卡_运行时映射补充_2026-04-21.md`。  
3. **B9 画布**：身份卡写 128×128；运行时为与 8 向 sheet 一致采用 **256**，小图自动最近邻放大叠画。
