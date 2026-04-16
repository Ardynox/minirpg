# 活素材实地对照 — 2026-04-17

只覆盖 **被运行时 JSON 实际引用** 的 54 个 PZ_Tiles PNG。
方法：逐张用 Read 工具读图，AI 视觉识别实际内容，对比 `Data/tile_mapping.json` 的映射 id。

仓库共 7032 个 PZ_Tiles PNG，活素材只有 54 个（**0.77% 利用率**）。

## 对得上（43 项）

### terrain
| id | 素材 | 实际内容 |
| --- | --- | --- |
| `sand` | floors/blends_natural_01_0 | 米黄色沙地 ✓ |
| `grass` | floors/blends_natural_01_21 | 鲜绿草地 ✓ |
| `grass_block` | floors/blends_natural_01_22 | 深绿草地 ✓ |
| `marsh` | floors/blends_natural_01_53 | 棕绿湿地感 ✓ |
| `dirt` | floors/blends_natural_01_64 | 棕色泥土 ✓ |
| `wall_soil` | floors/blends_natural_01_69 | 深棕土墙面 ✓ |
| `ice` | floors/blends_natural_02_0 | 灰蓝冰面 ✓ |
| `water` | floors/blends_natural_02_6 | 斑驳蓝色水面 ✓ |
| `floor` | floors/blends_street_01_0 | 浅灰水泥地 ✓ |
| `gravel` | floors/blends_street_01_64 | 中灰碎石 ✓ |
| `stone` / `wall_stone` | floors/blends_street_01_70 | 深灰石面 ✓（共用合理） |
| `wall_granite` | floors/blends_street_01_96 | 灰绿花岗岩 ✓ |
| `snow` | floors/blends_street_01_101 | 近白雪地 ✓ |
| `rubble` | floors_burnt_01/floors_burnt_01_24 | 烧焦瓦砾 ✓ |
| `ore_coal` | floors_rugs_01/floors_rugs_01_109 | 黑色带反光，煤 ✓ |
| `ore_copper` | floors_rugs_01/floors_rugs_01_100 | 深棕红铜色 ✓ |
| `wall_iron` / `ore_iron` | floors/blends_natural_02_5 | 金属灰 ✓（共用合理） |
| `wall_obsidian` | floors_rugs_01/floors_rugs_01_104 | 黑色块状 ✓ |

### fixture
| id | 素材 | 实际内容 |
| --- | --- | --- |
| `bed` | furniture_bedding_01/furniture_bedding_01_0 | 木架床 + 白色被褥 ✓ |
| `dormitory_bed` | furniture_bedding_01/furniture_bedding_01_1 | 金属床架 + 白被褥 ✓ |
| `shelf` | furniture_shelving_01/furniture_shelving_01_10 | 木质三层书架 ✓ |
| `stair_down` / `stair_up` | fixtures_stairs_01/_0 and _12 | 楼梯（上下） ✓ |
| `market_stall` | fixtures_counters_01/fixtures_counters_01_10 | 简单木柜台 ✓ |
| `door` | fixtures_doors_01/fixtures_doors_01_10 | 木门 ✓ |
| `stove` | fixtures_fireplaces_01/fixtures_fireplaces_01_0 | 黑色炉子 ✓ |
| `house` | roofs_01/roofs_01_10 | 灰色坡顶 ✓ |
| `fire` | Fire_02/Fire_02_0 | 中等黄色火焰 ✓ |
| `nest` | constructedobjects_01/constructedobjects_01_10 | 金属框笼/鸟窝 ✓ |

### item
| id | 素材 | 实际内容 |
| --- | --- | --- |
| `drop` / `container` | stashes_01/stashes_01_17 | 白色布包/堆叠 ✓ |

## 勉强可接受（5 项）

| id | 素材 | 问题 |
| --- | --- | --- |
| `butcher_table` | furniture_tables_high_01/furniture_tables_high_01_0 | 就是普通木桌，没屠夫特征 |
| `herbal_bench` | furniture_tables_high_01/furniture_tables_high_01_1 | 和上面一样，就是普通木桌 |
| `smithy` | fixtures_fireplaces_01/fixtures_fireplaces_01_1 | 黑色炉子，作熔炉勉强 |
| `campfire` | Fire_01/Fire_01_0 | 小火焰，作篝火偏小（适合蜡烛） |
| `fire_brazier` | constructedobjects_01/constructedobjects_01_1 | 单团小火焰/蜡烛，作火篮勉强 |
| `swamp` | floors_rugs_01/floors_rugs_01_93 | 暗橄榄色地块（沼泽缺少水感） |

## 明显不对（6 项 — **需要重新映射或换素材**）

| id | 当前素材 | 实际图像 | 问题描述 |
| --- | --- | --- | --- |
| `tree` | floors/blends_natural_01_48 | 干黄绿草地 | **平面地块冒充树** |
| `fungus` | floors_rugs_01/floors_rugs_01_82 | 深灰色空白菱形 | 无真菌/苔藓特征 |
| `ore_crystal` | floors_rugs_01/floors_rugs_01_85 | 深紫灰菱形 | 水晶该更亮/结晶感 |
| `lava` | floors_rugs_01/floors_rugs_01_96 | 暗红/紫红菱形 | 熔岩该明亮橙红 |
| `ore_gold` | floors_rugs_01/floors_rugs_01_101 | 深红棕菱形 | 黄金该是金黄色 |
| `loom` | carpentry_01/carpentry_01_24 | 棕色木桌 | 不是织布机 |
| `ladder` | constructedobjects_01/constructedobjects_01_12 | 木板栅栏/坡道 | 不是梯子 |
| `mountain` | floors/blends_street_01_70 | 与 stone 同灰石 | 山需要立体/峰型 |
| `crystal_vein` | floors_rugs_01/floors_rugs_01_84 | 深紫灰菱形 | 和水晶矿脉无直接关联 |

## 其他观察

- `Fire_01` / `Fire_02` / `Fire_03` 各 4 帧循环，但 tile_mapping 只用了 `_0` 帧作为单帧 fixture；其他帧由动画系统消费
- `Fire_03`（大火）整组 4 帧**没有任何 terrain/fixture 映射使用**—— 只可能在 voxel_tile_mapping 里被用
- `rugs_01/_82..109` 共 9 张都被当作 terrain tile 使用 —— 但它们在 PZ_Tiles 原本是 rug（地毯）分类
- `floors_rugs_01` 和 `floors` 目录里的这批平面色块被**大量借用为"矿物地面"**，因为原 PZ_Tiles 素材里确实没有真正的矿物/树/熔岩立体素材

## 根本问题

PZ_Tiles 是 Project Zomboid 的**末日废土**素材库，不含：
- 奇幻 RPG 需要的矿物（crystal / gold / copper 矿脉）—— 用平面色块凑
- 树、植物的立体素材 —— 用地面色块凑
- 熔岩、真菌类奇幻地形 —— 同上
- 织布机、梯子等手工业工具 —— 用最接近的东西代

## 建议

1. **短期（改映射）**：把 9 个"明显不对"的 id 要么换素材、要么接受"占位"状态（在注释里说明）
2. **中期（补素材）**：为树、矿物、熔岩、织布机、梯子准备**专用生成素材**（如 `Assets/Art/Generated/` 下那 89 个已有的生成素材里找）
3. **长期（整理命名）**：按实际用途（不是 PZ_Tiles 原目录结构）重命名并扁平化活素材，减少"假映射"
