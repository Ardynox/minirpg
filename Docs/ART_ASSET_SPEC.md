# MiniRPG 美术资源参数规范

这份文档用于给外包、美术同学或素材采购方直接对齐参数。

当前项目事实：

- 地图基础资源已采用 `FantasyKingdom` tileset。
- 地图瓦片切片基准为 `256 x 256`。
- 主窗口基准分辨率为 `1920 x 1080`。
- 资源目录编辑器首版只接受 `PNG`。
- 玩家角色已有 1 套 Spine 资源，当前采购重点不是主角，而是 NPC、怪物和品牌资源。

## 1. 通用标准

| 项目 | 标准 |
|---|---|
| 画风 | 2D 像素风，中世纪奇幻，和现有 `FantasyKingdom` 地图素材不跳风格 |
| 视角 | 俯视偏 3/4 视角，不要正侧视、横版视角、日式大头 Q 版 |
| 颜色 | 中高饱和，但不要霓虹、赛博、现代感配色 |
| 线条 | 允许轻描边，但不要粗黑轮廓，不要模糊抗锯齿边缘 |
| 文件格式 | `PNG`，`RGBA`，`sRGB`，无损导出，不接受 `jpg/webp` 作为正式交付 |
| 背景 | 地图角色、头像、图标、特效必须透明底；主菜单背景除外 |
| 命名 | 英文小写 + 下划线，名称尽量直接对应数据 ID |
| 交付源文件 | 如有源文件，一并附 `aseprite/psd`，但正式导入文件仍以 `PNG` 为准 |
| 禁止项 | 不要写实油画风、不要现代武器、不要科幻 UI、不要带水印、不要把说明文字烤进图片 |

## 2. 地图角色资源

### 2.1 通用参数

| 项目 | 标准 |
|---|---|
| 画布 | `256 x 256`，透明 PNG |
| 对齐 | 底部居中对齐，角色落地点统一按画布底部中心处理 |
| 锚点建议 | 落地点控制在 `x=128`，`y=224 +/- 8` |
| 出图方式 | 单帧可用；如做动画，优先做 `idle`，每个状态 `2-4` 帧 |
| 动画交付 | 多帧时用独立文件，不要拼成长条大图 |
| 文件命名 | `npc_xxx_idle_01.png` / `monster_xxx_idle_01.png` |
| 尺寸控制 | 人形主体建议占 `180-220 px` 高；小型怪物 `120-180 px`；大型怪物可到 `220-240 px` |
| 设计目标 | 地图上一眼能分清身份，优先轮廓和配色识别度，其次再追求动画丰富 |

### 2.2 友方 NPC 地图图

第一批按 `8 张必需 + 2 张可选增强` 采购。

| 优先级 | 资源 ID | 建议文件名 | 规格 | 覆盖对象 |
|---|---|---|---|---|
| P1 | `merchant` | `npc_merchant_idle_01.png` | `256 x 256`，透明底，单帧或 4 帧 idle | `merchant` |
| P1 | `elder` | `npc_elder_idle_01.png` | 同上 | `elder` |
| P1 | `villager` | `npc_villager_idle_01.png` | 同上 | `villager` |
| P1 | `blacksmith_npc` | `npc_blacksmith_idle_01.png` | 同上，建议带围裙/锤子识别 | `blacksmith_npc` |
| P1 | `herbalist_npc` | `npc_herbalist_idle_01.png` | 同上，建议草药包/药瓶识别 | `herbalist_npc` |
| P1 | `cook_npc` | `npc_cook_idle_01.png` | 同上，建议厨帽或锅勺识别 | `cook_npc` |
| P1 | `guard_npc` | `npc_guard_idle_01.png` | 同上，建议甲胄/长枪识别 | `guard_npc` |
| P1 | `tailor_npc` | `npc_tailor_idle_01.png` | 同上，建议布卷/针线识别 | `tailor_npc` |
| P2 | `elf_trader` | `npc_elf_trader_idle_01.png` | 同上，精灵特征明显 | `elf_trader` |
| P2 | `orc_merchant` | `npc_orc_merchant_idle_01.png` | 同上，兽人轮廓明显 | `orc_merchant` |

额外标准：

- 同一阵营角色不要只靠衣服颜色区分，轮廓也要不同。
- 商人、守卫、铁匠、药师必须在缩略尺寸下也能认出来。
- 精灵和兽人不能只是人类换色，至少要改耳朵、体型或头部轮廓。

### 2.3 敌对怪物地图图

第一批按 `10 张必需 + 3 张可选增强` 采购。

| 优先级 | 资源 ID | 建议文件名 | 规格 | 覆盖对象 |
|---|---|---|---|---|
| P1 | `goblin` | `monster_goblin_idle_01.png` | `256 x 256`，透明底，单帧或 4 帧 idle | `goblin` |
| P1 | `slime` | `monster_slime_idle_01.png` | 同上，建议做轻微弹动感 | `slime` |
| P1 | `skeleton` | `monster_skeleton_idle_01.png` | 同上 | `skeleton` |
| P1 | `spider` | `monster_spider_idle_01.png` | 同上，轮廓张开，不要太小 | `spider` |
| P1 | `scorpion` | `monster_scorpion_idle_01.png` | 同上，尾针要明显 | `scorpion` |
| P1 | `wolf` | `monster_wolf_idle_01.png` | 同上 | `wolf` |
| P1 | `bear` | `monster_bear_idle_01.png` | 同上，体型明显大于狼 | `bear` |
| P1 | `rat` | `monster_rat_idle_01.png` | 同上，小体型但不可糊成一团 | `rat` |
| P1 | `orc_warrior` | `monster_orc_warrior_idle_01.png` | 同上，重武装轮廓明显 | `orc_warrior` |
| P1 | `treant` | `monster_treant_idle_01.png` | 同上，树人轮廓明显 | `treant` |
| P2 | `orc_shaman` | `monster_orc_shaman_idle_01.png` | 同上，法杖/头饰要和战士区分 | `orc_shaman` |
| P2 | `elf_ranger` | `monster_elf_ranger_idle_01.png` | 同上，远程轮廓明显 | `elf_ranger` |
| P2 | `goblin_miner` | `monster_goblin_miner_idle_01.png` | 同上，矿工帽/镐子识别 | `goblin_miner` |

额外标准：

- 怪物优先做“种类识别”，不要做成一批相似的人形怪。
- 同类变体可以复用基础身体，但必须保证主武器或职业识别件不同。
- `slime`、`spider`、`rat` 这类小体型怪，画面占比不能太小，否则地图上不可读。

## 3. 主菜单品牌包

### 3.1 Logo

| 项目 | 标准 |
|---|---|
| 文件数 | 1 张主 Logo，必要时可附 1 张简化版 |
| 主文件 | `logo_main.png` |
| 规格 | 透明底 PNG，推荐宽 `1600 px`，高不超过 `600 px` |
| 备选格式 | 如能提供，再附 `SVG` 版本 |
| 内容要求 | 仅 Logo 本体，不要把背景、按钮、边框烤进去 |
| 风格 | 奇幻冒险、轻量 RPG，不要厚重暗黑系，也不要手游氪金感 |

### 3.2 Background

| 项目 | 标准 |
|---|---|
| 文件名 | `menu_background_1920x1080.png` |
| 分辨率 | 必须至少 `1920 x 1080`，推荐 `2560 x 1440` |
| 构图 | 左中或中间留出菜单安全区，避免高对比细节压住按钮文本 |
| 内容 | 奇幻村镇、森林入口、遗迹外景、营地均可 |
| 禁止项 | 不要内嵌标题文字，不要 UI 边框，不要角色立绘贴脸占满画面 |

额外标准：

- 背景要能承受后续叠加纯文字菜单。
- 背景整体亮度不要过暗，避免和当前深色 UI 一起变脏。

## 4. 对话头像包

当前系统还没有正式接头像位，但下一批资源建议按可直接接入的规格来准备。

| 项目 | 标准 |
|---|---|
| 尺寸 | 推荐 `512 x 512`，最低 `256 x 256` |
| 文件格式 | 透明 PNG |
| 构图 | 半身或头像，朝向尽量统一，建议 3/4 朝向 |
| 命名 | `portrait_merchant.png`、`portrait_elder.png` |
| 背景 | 透明底，不带复杂背景 |

建议优先准备：

- `portrait_merchant.png`
- `portrait_elder.png`
- `portrait_villager.png`
- `portrait_blacksmith_npc.png`
- `portrait_herbalist_npc.png`
- `portrait_cook_npc.png`
- `portrait_guard_npc.png`
- `portrait_tailor_npc.png`
- `portrait_elf_trader.png`
- `portrait_orc_merchant.png`

## 5. 道具 Starter Icons

第一批只做高频道具，不一次做满全部物品。

### 5.1 通用参数

| 项目 | 标准 |
|---|---|
| 尺寸 | 推荐 `128 x 128`，最低 `64 x 64` |
| 格式 | 透明 PNG |
| 构图 | 单物体居中，四周保留 `8-16 px` 安全边距 |
| 光照 | 单方向简单高光即可，不要写实材质球效果 |
| 命名 | `item_potion_hp.png`、`item_sword_iron.png` |

### 5.2 第一批建议清单

| 优先级 | 资源 ID | 建议文件名 |
|---|---|---|
| P2 | `potion_hp` | `item_potion_hp.png` |
| P2 | `potion_str` | `item_potion_str.png` |
| P2 | `herbal_medicine` | `item_herbal_medicine.png` |
| P2 | `antidote` | `item_antidote.png` |
| P2 | `bandage` | `item_bandage.png` |
| P2 | `torch` | `item_torch.png` |
| P2 | `lantern` | `item_lantern.png` |
| P2 | `sword_iron` | `item_sword_iron.png` |
| P2 | `sword_steel` | `item_sword_steel.png` |
| P2 | `shield_iron` | `item_shield_iron.png` |
| P2 | `bow_short` | `item_bow_short.png` |
| P2 | `pickaxe_steel` | `item_pickaxe_steel.png` |
| P2 | `wood_axe` | `item_wood_axe.png` |
| P2 | `war_hammer` | `item_war_hammer.png` |
| P2 | `meal_simple` | `item_meal_simple.png` |
| P2 | `raw_meat` | `item_raw_meat.png` |
| P2 | `mat_iron` | `item_mat_iron.png` |
| P2 | `mat_herb` | `item_mat_herb.png` |

## 6. 战斗和交互特效

第一批建议做 `5-6` 组通用特效。

| 资源 ID | 建议文件名前缀 | 规格 |
|---|---|---|
| 斩击弧光 | `fx_slash_arc_` | `256 x 256`，透明 PNG，`6-8` 帧 |
| 钝击命中 | `fx_hit_blunt_` | 同上 |
| 箭矢飞行 | `fx_arrow_projectile_` | 同上，允许单帧 |
| 毒液喷射 | `fx_poison_spit_` | 同上 |
| 增益闪光 | `fx_buff_flash_` | 同上 |
| 拾取闪点 | `fx_pickup_glint_` | 同上 |

特效标准：

- 动画帧之间不能跳位置，中心点要稳定。
- 避免全屏大面积纯白闪光。
- 和现有 tileset 自带火焰、水波相比，不要做成完全不同的特效语言。

## 7. 命名和交付结构

建议按下面目录交付：

```text
art_delivery/
  npc_map/
  monster_map/
  branding/
  portraits/
  item_icons/
  effects/
```

命名规则：

```text
npc_merchant_idle_01.png
monster_slime_idle_01.png
portrait_blacksmith_npc.png
item_sword_iron.png
fx_slash_arc_01.png
logo_main.png
menu_background_1920x1080.png
```

每个目录最好附：

- 1 张总览图 `preview.png`
- 1 份简短说明 `README.txt` 或 `notes.txt`
- 如有版权要求，附授权说明

## 8. 验收清单

交付后按下面标准验收：

- 文件全部为 `PNG`，能正常打开，无损坏。
- 地图角色资源全部是 `256 x 256` 透明底。
- 图标全部是 `64/128` 方图，透明底。
- 头像全部是 `256/512` 方图，透明底。
- 命名和数据 ID 基本对应，不需要二次猜测。
- 角色和怪物在缩小查看时仍然能靠轮廓区分。
- 没有明显现代、科幻、写实或别的项目风格混入。
- 多帧动画的每帧画布尺寸和锚点一致。
- 背景图不会压住主菜单文字区域。

## 9. 第一批外发最小清单

如果只先发一轮需求，直接发这 3 包：

1. 友方 NPC 地图图：先做 8 张必需项。
2. 敌对怪物地图图：先做 10 张必需项。
3. 主菜单品牌包：`logo_main.png` + `menu_background_1920x1080.png`。

第二轮再补：

1. 对话头像包。
2. 道具 starter icons。
3. 特效包。
