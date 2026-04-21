# style_lock 登记册

> 配合 `Artifacts/素材身份卡表_2026-04-20.md` §0.2：每批发包前把「风格咨询清单」的结论记入 `Artifacts/style_locks_<batch>.md`，并在此更新状态。

| 批次 | style_lock 文件 | 状态 | 备注 |
| --- | --- | --- | --- |
| B0 | `Artifacts/style_locks_B0.md` | **已锁定** | 全局锚点，摘自 `Assets/Art/README.md` + 需求清单 §0 |
| B1 | `Artifacts/style_locks_B1.md` | 待填 | 特效 / 天气 / 品牌；天气 id 见下方「与身份卡差异」 |
| B2 | `Artifacts/style_locks_B2.md` | 待填 | NPC 俯视 + 立绘 |
| B3 | `Artifacts/style_locks_B3.md` | 待填 | 物品图标 130（可拆子批，可共用一个 B3 lock） |
| B4 | `Artifacts/style_locks_B4.md` | 待填 | 人脸部件 62 张 |
| B5 | `Artifacts/style_locks_B5.md` | 待填 | need / condition / thought |
| B6 | `Artifacts/style_locks_B6.md` | 待填 | profession / race / interaction / incident |
| B7 | `Artifacts/style_locks_B7.md` | 待填 | surgery / room / capacity / limb |
| B8 | `Artifacts/style_locks_B8.md` | 待填 | 作物 + 材料世界堆 |
| B9 | `Artifacts/style_locks_B9.md` | 待填 | 依赖 B4 + 男女素体 |
| B10 | `Artifacts/style_locks_B10.md` | 待填 | Generated 重刷，**最晚** |
| B11 | `Artifacts/style_locks_B11.md` | 待填 | facility 蓝图 |
| B12 | `Artifacts/style_locks_B12.md` | 待填 | P2 生活细节 + 联机色带 |

## 磁盘占位进度快照（2026-04-21 清点）

以下仅统计 `Assets/Art/Placeholders/**/*.png` 数量，用于对照身份卡「目标路径」是否齐套。

| 子目录 | PNG 数 | 身份卡目标 |
| --- | ---: | --- |
| `effects/` | 8 | B1.1 七张单帧 + 根目录 `fx_explosion` 等扩展 |
| `weather/` | 9 | 身份卡 B1.2 列 6 张；**运行时**另含 `snow` / `snow_cover` / `ice_gloss`（见 `WeatherFxController.WeatherAssetIds`） |
| `branding/` | 2 | B1.3 |
| `npc_map/` | 10 | B2.1 |
| `portraits/` | 10 | B2.2 |
| `item_icons/` | 130 | B3 全集 |
| `ui_icons/` | 169 | B5+B6+B7 等合并目录（含 `limb_*` 十张等） |
| `crops/` | 21 | B8.1 |
| `material_world/` | 24 | B8.2 |

**下一优先缺口（相对身份卡依赖图）**：B4 人脸 PNG（`Assets/Faces/**` 仍多靠程序占位）、B9 装备 overlay、B10/B11/B12 按路线图。

## 与身份卡原文的差异（避免发包对不上号）

1. **天气**：身份卡 B1.2 未列 `snow_cover` / `ice_gloss` / `snow`，仓库为玩法已接入，出图以 `WeatherAssetIds` 为准。  
2. **B7 肢体文件名**：身份卡 B7.4 示例为 `limb_arm_left.png` 等；**实际运行时使用** `limb_left_arm.png`、`limb_heart_lungs.png`、`limb_eyes.png`、`limb_hands.png` 等十类。详见 `Artifacts/素材身份卡_运行时映射补充_2026-04-21.md`。
