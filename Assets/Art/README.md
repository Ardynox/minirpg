# Assets/Art

这个目录放项目实际使用或准备用于项目的美术资源。
这里不做“大而全美术设计文档”，只回答两个问题：

1. 各类美术资源应该放哪里
2. 用 AI（当前以 Banana 为主）产出资源时，稳定可复用的 SOP 是什么

## 目录分工

| 目录 | 用途 | 什么时候放这里 |
| --- | --- | --- |
| `Generated/` | 已接受、准备接入或已经接入游戏的 AI/脚本生成资源 | 新做的最终图、经过筛选的正式候选 |
| `Placeholders/` | 临时占位资源 | 玩法先通、视觉后补时 |
| `PZ_Tiles/` | 当前在用的上游拆分图块库 | 复用现成场景图块时 |
| `Tilesets/` | 第三方整包 tileset / character sheet | 角色表、整包素材保留原组织时 |
| `_Archive/` | 已废弃但暂时不删的旧资源 | 已下线但短期还想留参考时 |

额外约束：

- 一次性预览图、对比图、contact sheet 放 `Artifacts/`，不要塞进这里。
- 不要把“只为了试 prompt 的垃圾图”直接放 `Generated/`。
- 如果资源还只是“可能以后会用”，先别进仓库。

## Banana 适用边界

### 适合直接产出的

- 单个静态道具：木柴、箱子、篮子、营火、工作台、草丛、灌木
- 地表底图：草地、泥地、浅水、深水、沙地、雪地
- 地表过渡：草到泥、草到水、泥到水、路到草
- 小型 overlay：高草、落叶、碎石、岸边植物
- 小型静态特效：命中闪、毒液团、拾取闪光

### 适合“先出底稿，再人工收口”的

- 成套家具/建筑 kit
- 多帧特效
- 单张角色立绘或怪物地图像
- 风格统一的一组同主题物件

### 不适合直接一把生成成品的

- 8 方向角色动作表
- 要求严格无缝的复杂 auto-tile 组
- 需要精确交互边界的 UI 九宫格
- “一次生成十几个道具并且都能单独用”

原则很简单：

- Banana 更适合“单资产、强风格、低逻辑约束”。
- 越是需要精确拼接、严格动画对齐、程序约束强的资源，越不要指望一把出最终版。

## 产出 SOP

### 1. 先写资产卡，不先写 prompt

每个准备生成的资源，先补这 6 个字段：

- `资产名`：例如“草地到水边过渡 01”
- `资源类型`：terrain / overlay / prop / fixture / entity / fx / ui
- `使用位置`：在哪个场景或系统里出现
- `目标目录`：最终准备放到哪个目录
- `接线文件`：生成后要改哪个 JSON / 配置
- `验收重点`：例如“不要有明显拼贴重复”“岸边不能硬切”

没有这 6 个字段，不开图。

### 2. 先定风格锚点，再写具体 prompt

当前项目如果要压低“AI 味”，默认朝这个方向靠：

- 手绘 2D 插画 / 游戏贴图
- 低饱和土色系
- 清楚轮廓，细棕线，不要黑粗线
- 扁平色 + 轻阴影，不要高光塑料感
- 生存沙盒感，实用，不花哨

写 prompt 时按这个顺序：

1. 先写“是什么”
2. 再写“视角”
3. 再写“线稿和上色方式”
4. 再写“颜色和材质”
5. 最后写“背景要求 / 无缝 / 透明底”

不要一上来写这些空词：

- `stylized`
- `game-ready`
- `cinematic`
- `ultra detailed`
- `epic`
- `beautiful lighting`

这些词最容易把图带成通用 AI 海报味。

### 3. 一次只生成一个资产，不拼盘

正确做法：

- 一次出 1 个草丛
- 一次出 1 张草地底图
- 一次出 1 张草地到水边过渡
- 一次出 1 个木箱

错误做法：

- “生成一组生存道具”
- “生成一个完整营地”
- “生成一整套 RPG 素材”

拼盘图最大的问题不是不好看，而是后面没法切、没法复用、没法统一锚点。

### 4. 每个资产至少出 4 个候选，只收 1 到 2 个

默认流程：

1. 同一提示词出 4 个候选
2. 只保留最接近目标的 1 到 2 个
3. 其他直接丢，不进仓库

如果 4 个都不行：

- 先改 prompt，不要开始“从烂图里挑能用的”

### 5. 先过“读感验收”，再决定要不要接线

#### terrain / transition

- 缩小到游戏内尺寸后还能看清材质差别
- 没有中心构图
- 没有明显大块重复形状
- 边缘不是硬切
- 如果是 tile，必须能重复铺

#### overlay / prop / fixture

- 轮廓一眼能认出是什么
- 透明底干净
- 不依赖背景光效才能成立
- 不会因为细节太多缩小后糊成一团

#### fx

- 主体读得出来
- 不靠 bloom / 炫光撑效果
- 多帧时节奏一致，尺寸别乱跳

### 6. 通过后再入仓

入仓时按这个规则：

- 最终采用：放 `Assets/Art/Generated/<pack>/`
- 只是占位：放 `Assets/Art/Placeholders/<category>/`
- 第三方原包：放 `Assets/Art/Tilesets/` 或保留在现有目录
- 预览拼图：放 `Artifacts/`

## 推荐目录命名

如果是新做资源，优先用描述性命名，不继续堆黑盒编号：

- `Generated/terrain/terrain_grass_base_01.png`
- `Generated/terrain/terrain_shore_grass_water_01.png`
- `Generated/overlays/overlay_grass_tall_01.png`
- `Generated/props/prop_firewood_stack_01.png`
- `Generated/fixtures/fixture_workbench_01.png`
- `Generated/fx/fx_hit_blunt_01_00.png`

不要用：

- `final_final_2.png`
- `new_grass.png`
- `banana_test_ok.png`

## Prompt 模板

### 道具模板

```text
single <asset>, hand-drawn 2D survival game prop, 3/4 top-down view, thin brown outline, muted earthy palette, flat colors with soft painted shading, simple readable shapes, practical rustic survival style, transparent background
```

### 地表模板

```text
seamless top-down <terrain> tile, hand-drawn 2D survival game texture, muted natural palette, soft painted shading, low contrast, simple readable shapes, natural variation, designed for repetition, no focal object
```

### 过渡模板

```text
seamless top-down <terrain A> to <terrain B> transition tile, irregular natural edge, muted earthy palette, hand-drawn 2D survival game texture, soft shading, low contrast, designed for repetition
```

### 统一负面词

```text
no photorealistic, no 3d render, no glossy plastic, no dramatic lighting, no bloom, no depth of field, no concept art, no splash art, no text, no watermark, no extra objects, no busy background
```

## 接入游戏时改哪里

| 资源类型 | 常见落点 | 常改文件 |
| --- | --- | --- |
| terrain 2D | 地表顶视图 | `Data/tile_mapping.json` |
| terrain voxel / iso | 顶面、侧面、颜色侧壁 | `Data/voxel_tile_mapping.json`、`Data/terrains.json`、必要时 `Module/Render/VoxelTilePathResolver.cs` |
| 场景 overlay / 自然 patch | 草丛、灌木、散布物 | `Data/pz_world_visual_registry.json` |
| 角色地图表现 | 玩家 / NPC / 怪物 | `Data/entity_render.json` |
| 世界物品图 | 掉落物、地上物品 | `Data/item_world_render.json` |
| 特效帧 | 命中、投射物、buff | `Data/resource_catalog.json`、`Data/combat_fx.json` |
| UI 皮肤 | 面板、按钮、主题 | `Assets/UI/Themes/*` 及相关场景/代码引用 |

## Banana 批量出图时的务实规则

- 同一批只做一个小包：例如“草地包”或“营地基础道具包”
- 每个包先做 5 到 8 个最常见资产，不追一口气全覆盖
- 先解决“场景能不能立住”，再做边缘小道具
- 同一批资源固定一张参考图做 style reference，只借风格，不借构图
- 不满意就重开，不从一张明显 AI 味很重的图上硬修

## 当前建议优先级

如果下一轮要正式开始重做当前项目美术，建议顺序是：

1. terrain base：草、土、水、沙、石、雪
2. terrain transition：草-水、草-泥、泥-水、路-草
3. overlay：高草、碎石、落叶、岸边植物
4. fixture core：营火、工作台、箱子、床、门、架子、楼梯
5. entity：玩家/NPC/怪物地图表现
6. fx：命中、投射物、拾取、buff

## 完成定义

一个新资源只有同时满足下面 4 条，才算“产出完成”：

1. 图本身过了读感验收
2. 放到了正确目录
3. 改好了对应映射/配置
4. 进游戏看过实际效果，没有明显尺寸、锚点、拼贴或风格跳变问题

没进游戏看过的图，不算完成，只算“生成完成”。
