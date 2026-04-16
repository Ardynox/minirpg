# PZ Tiles 命名整理

本目录先保留上游文件名，不直接改动 PNG 或目录名。

原因很简单：
- Godot 的 `.import` 和潜在资源引用会跟着原名走。
- 当前仓库已经有大量未提交的导入侧改动，不适合顺手批量重命名原文件。

更稳妥的做法是：
- 先给目录、前缀、文件建立“建议别名”。
- 后续如果确实要改物理文件名，再单独做引用扫描、批量 rename 和导入重建。

本次整理产物：
- `rename_plan.csv`：可直接拿去做 catalog、批量重命名脚本或人工筛选。
- 本 README：给出目录级、前缀级的建议名和用途说明。

## 命名规则

- 目录或资源族：`category_subject_variant_01`
- 循环动画帧：`<base>_frame_00`
- 地面垃圾：`ground_trash_<theme>_<size>_<nn>`
- 自然侵蚀：优先按“表面/对象/季节”拆，不延续 `d_`、`e_`、`f_` 这种黑盒前缀
- 场景专用地面：优先保留场景来源，如 `floor_shop_mall_01`，不要强行塞进通用材质名

## 已确认修正

- `security_01` 更像 X 光安检机和监视屏，不是摄像头、报警器或保险箱。
- `appliances_misc_01_0.png` 更适合命名为便携发电机，不建议写成泛泛的“电池装置”。
- `Fire_01`、`Fire_02`、`Fire_03` 可以稳定理解为小、中、大三档循环火焰。
- `trash_01` 里不只是垃圾堆，还混有死鸟、纸张、垃圾袋、瓦砾带状碎片。
- `erosion` 不是单一“侵蚀”目录，而是裂缝、杂草、灌木、藤蔓、树种、积雪、调试占位的合集。
- `floors` 根目录不是单一材质库，而是场景专用混合地表仓。

## 目录级建议名

| 原目录 | 建议英文键 | 建议中文名 | 说明 |
| --- | --- | --- | --- |
| `appliances_misc_01` | `portable_generator_01` | 便携发电机组 | 当前只有 1 张，实际是一台红色便携发电机。 |
| `Fire_01` | `fire_small_loop_01` | 小型循环火焰 | 4 帧小火焰循环。 |
| `Fire_02` | `fire_medium_loop_01` | 中型循环火焰 | 4 帧中火焰循环。 |
| `Fire_03` | `fire_large_loop_01` | 大型循环火焰 | 4 帧大火焰循环。 |
| `trash_01` | `ground_trash_mixed_01` | 地面垃圾混合组 | 从小碎片、纸张到大垃圾袋和垃圾堆都有。 |
| `carpentry_01` | `player_built_wood_01` | 玩家木工结构组 | 木墙、木箱、桌椅、木门等木工搭建件。 |
| `constructedobjects_01` | `constructed_objects_mixed_01` | 搭建物杂项组 | 蜡烛、框架、绳索、金属板、土堆、木箱等构筑件混合。 |
| `stashes_01` | `buried_stash_01` | 埋藏补给点组 | 土坑、覆盖布、卷包类“藏匿点”素材。 |
| `recreational_01` | `recreation_machines_tables_01` | 娱乐设备组 | 点唱机、街机、弹珠台、钢琴、台球桌。 |
| `security_01` | `xray_security_scanner_01` | X 光安检机组 | 安检机主体和监视屏控制台。 |
| `erosion` | `erosion_nature_overgrowth_mixed_01` | 自然侵蚀与季节覆盖组 | 杂草、裂缝、藤蔓、树木、积雪等大合集。 |
| `floors` | `floors_scene_specific_mixed_01` | 场景专用混合地表组 | 混有通用地表、品牌店地面、拖车地板、屋顶等。 |

## `erosion` 前缀建议名

| 原前缀 | 建议英文键 | 建议中文名 | 说明 |
| --- | --- | --- | --- |
| `d_floorleaves_1` | `ground_leaf_scatter_01` | 地面落叶散布 | 少量落叶和碎叶覆盖片。 |
| `d_generic_1` | `ground_nature_debris_mixed_01` | 地面自然杂物混合组 | 小草、枯枝、树桩、石块、零碎灌丛。 |
| `d_plants_1` | `ground_plants_low_mixed_01` | 低矮植物混合组 | 低矮绿丛和花草覆盖片。 |
| `d_streetcracks_1` | `street_cracks_with_weeds_01` | 街道路裂与杂草 | 路面裂缝和裂缝里长出的草。 |
| `d_wallcracks_1` | `wall_cracks_overlay_01` | 墙面裂缝覆盖 | 细线墙裂、边角裂纹。 |
| `e_americanholly_1` | `tree_american_holly_01` | 美国冬青树组 | 常绿树序列。 |
| `e_americanlinden_1` | `tree_american_linden_01` | 美国椴树组 | 落叶树序列。 |
| `e_canadianhemlock_1` | `tree_canadian_hemlock_01` | 加拿大铁杉组 | 常绿针叶树序列。 |
| `e_carolinasilverbell_1` | `tree_carolina_silverbell_01` | 卡罗来纳银铃树组 | 落叶树序列。 |
| `e_cockspurhawthorn_1` | `tree_cockspur_hawthorn_01` | 鸡距山楂树组 | 落叶树序列。 |
| `e_debug_1` | `debug_placeholder_erosion_01` | 侵蚀调试占位组 | 调试用占位块。 |
| `e_dogwood_1` | `tree_dogwood_01` | 山茱萸树组 | 落叶树序列。 |
| `e_easternredbud_1` | `tree_eastern_redbud_01` | 东部紫荆树组 | 落叶树序列。 |
| `e_exterior_1` | `ground_exterior_blend_01` | 室外地表过渡 | 室外基础覆盖片。 |
| `e_exterior_snow_1` | `ground_exterior_snow_blend_01` | 室外雪地过渡 | 室外基础覆盖片的雪地版。 |
| `e_newgrass_1` | `grass_tall_newgrowth_01` | 新长高草组 | 成簇高草和新生草丛。 |
| `e_newgrassbase_1` | `grass_base_blend_01` | 草地基底过渡 | 草地底层和草边过渡片。 |
| `e_newgrassbase_snow_1` | `grass_base_blend_snow_01` | 雪地草基底过渡 | 草地底层和边缘的积雪版。 |
| `e_redmaple_1` | `tree_red_maple_01` | 红枫树组 | 含绿叶、红叶和落叶阶段。 |
| `e_riverbirch_1` | `tree_river_birch_01` | 河桦树组 | 落叶树序列。 |
| `e_roof_snow_1` | `roof_snow_overlay_01` | 屋顶积雪覆盖 | 屋脊、边角、坡面雪层。 |
| `e_virginiapine_1` | `tree_virginia_pine_01` | 弗吉尼亚松组 | 常绿针叶树序列。 |
| `e_yellowwood_1` | `tree_yellowwood_01` | 黄木树组 | 落叶树序列。 |
| `f_bushes_1` | `bushes_mixed_01` | 灌木混合组 | 裸枝灌木、绿灌木、花灌木。 |
| `f_wallvines_1` | `wall_vines_mixed_01` | 墙面藤蔓组 | 枯藤和带叶藤蔓混合。 |

## `floors` 核心材质与混合组

| 原前缀 | 建议英文键 | 建议中文名 | 说明 |
| --- | --- | --- | --- |
| `floors_rugs_01` | `rugs_mixed_01` | 地毯混合组 | 长条地毯、拼色地垫、半圆门垫。 |
| `floors_interior_tilesandwood_01` | `floor_interior_tiles_wood_01` | 室内砖木地面组 | 室内瓷砖、木地板、过渡边线。 |
| `floors_interior_carpet_01` | `floor_interior_carpet_01` | 室内地毯地面组 | 纯地毯类室内地表。 |
| `floors_exterior_street_01` | `floor_exterior_street_01` | 室外街道路面组 | 沥青、混凝土、道路白线和坡口。 |
| `blends_street_01` | `floor_street_blend_01` | 街道路面过渡组 | 路面之间的过渡拼接片。 |
| `floors_exterior_natural_01` | `floor_exterior_natural_01` | 室外自然地表组 | 草地、泥地、沙地、碎石地。 |
| `blends_natural_01` | `floor_natural_blend_01` | 自然地表过渡组 | 草、土、碎石之间的过渡片。 |
| `blends_natural_02` | `floor_natural_blend_alt_01` | 自然地表过渡补充组 | 小批量自然过渡补图。 |
| `floors_exterior_tilesandstone_01` | `floor_exterior_tiles_stone_01` | 室外石板砖地组 | 户外石砖和铺路砖。 |
| `vegetation_ornamental_01` | `floor_ornamental_garden_01` | 园艺装饰地表组 | 花坛和火烈鸟庭院摆件混合。 |
| `vegetation_farm_01` | `floor_farm_ground_01` | 农场地表组 | 农田和乡村场景地表。 |
| `industry_01` | `floor_industrial_metal_01` | 工业金属地面组 | 金属格栅、工业板面、边条。 |
| `industry_railroad_05` | `floor_industrial_railroad_01` | 铁路工业地面组 | 铁路和列车相关构件地表。 |
| `industry_trucks_01` | `floor_industrial_truck_area_01` | 工业运输区地面组 | 货车或装卸区域地表。 |
| `industry_bunker_01` | `floor_industrial_bunker_01` | 地堡地面组 | 地堡或防御工事地表。 |
| `roofs_01` | `roof_surface_01` | 屋面组 01 | 常规屋面材质。 |
| `roofs_02` | `roof_surface_02` | 屋面组 02 | 常规屋面材质。 |
| `roofs_03` | `roof_surface_03` | 屋面组 03 | 常规屋面材质。 |
| `roofs_04` | `roof_surface_04` | 屋面组 04 | 常规屋面材质。 |
| `roofs_05` | `roof_surface_05` | 屋面组 05 | 常规屋面材质。 |
| `roofs_burnt_01` | `roof_surface_burnt_01` | 烧毁屋面组 | 烧毁后的屋面材质。 |
| `fencing_burnt_01` | `fence_burnt_surface_01` | 烧毁围栏地表组 | 烧毁围栏和边界相关图块。 |
| `fixtures_escalators_01` | `floor_escalator_platform_01` | 自动扶梯地面组 | 自动扶梯平台和落地区域。 |
| `construction_01` | `floor_construction_01` | 施工地面组 | 临时施工面和工地材质。 |
| `carpentry_02` | `floor_carpentry_wood_01` | 木工临时地板组 | 木板地台和木制地面片。 |
| `constructedobjects_01` | `floor_constructed_object_01` | 构筑物地面补图 | 仅 1 张，作为零散补图保留。 |
| `floors_burnt_01` | `floor_burnt_01` | 烧毁地面补图 | 仅 1 张烧毁地表。 |
| `invisible_01` | `floor_invisible_placeholder_01` | 不可见占位地面 | 调试或遮罩用途占位图。 |

## `floors` 场景专用组

| 原前缀 | 建议英文键 | 建议中文名 | 说明 |
| --- | --- | --- | --- |
| `location_trailer_01` | `floor_trailer_01` | 拖车房地面组 01 | 深色拖车房地板和边缘片。 |
| `location_trailer_02` | `floor_trailer_02` | 拖车房地面组 02 | 浅色拖车房地板和边缘片。 |
| `location_shop_mall_01` | `floor_shop_mall_01` | 商场地面组 | 商场风格瓷砖地。 |
| `location_shop_greenes_01` | `floor_shop_greenes_01` | Greenes 店铺地面组 | 店铺专用地面。 |
| `location_shop_generic_01` | `floor_shop_generic_01` | 通用店铺地面组 | 通用商店地面补图。 |
| `location_shop_fossoil_01` | `floor_shop_fossoil_01` | Fossoil 店铺地面组 | 品牌场景地面。 |
| `location_shop_zippee_01` | `floor_shop_zippee_01` | Zippee 店铺地面组 | 品牌场景地面。 |
| `location_restaurant_diner_01` | `floor_restaurant_diner_01` | Diner 餐馆地面组 | 美式餐馆地面。 |
| `location_restaurant_spiffos_01` | `floor_restaurant_spiffos_01` | Spiffo's 餐馆地面组 | 品牌快餐店地面。 |
| `location_restaurant_bar_01` | `floor_restaurant_bar_01` | 酒吧地面组 | 酒吧场景地面补图。 |
| `location_restaurant_pie_01` | `floor_restaurant_pie_01` | Pie 餐馆地面组 | 品牌餐馆地面补图。 |
| `location_restaurant_pileocrepe_01` | `floor_restaurant_pileocrepe_01` | Pile-O-Crepe 餐馆地面组 | 品牌餐馆地面补图。 |
| `location_restaurant_pizzawhirled_01` | `floor_restaurant_pizzawhirled_01` | Pizza Whirled 餐馆地面组 | 品牌餐馆地面补图。 |
| `location_hospitality_sunstarmotel_01` | `floor_sunstar_motel_01` | Sunstar Motel 地面组 01 | 旅馆场景地面。 |
| `location_hospitality_sunstarmotel_02` | `floor_sunstar_motel_02` | Sunstar Motel 地面组 02 | 旅馆场景地面补图。 |
| `location_sewer_01` | `floor_sewer_01` | 下水道地面组 | 下水道和网格盖板地面。 |
| `recreational_sports_01` | `floor_recreation_sports_01` | 运动娱乐地面组 | 球场或运动场景补图。 |

## `trash_01` 实用分层

按视觉上最稳定的用途，`trash_01` 可以先这样理解：

- `0-12`：零散小垃圾、小碎片、带状瓦砾、死鸟
- `16-23`：中到大型混合垃圾堆
- `24-32`：纸张、报纸、信封、布片类地面散落物
- `33-39`：中型横向混合垃圾带
- `40-53`：大型垃圾袋和大型垃圾堆

如果后面真要做物理重命名，建议优先按“主题 + 尺寸 + 序号”改，不要给每张图取过于主观的剧情名。
