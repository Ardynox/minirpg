# 博德之门3 草地研究 · 2026-04-20

为 `Scene/BG3GrassPreview.tscn` 做的一次性研究笔记：
**技术侧**我自己扒的（Larian 公开资料 + Digital Foundry + Larian 内部美工 80.lv 自述 + bg3se modding docs），
**审美侧**问了一个 Gemini 3 子代理做视觉拆解。
合起来给一份"当前 preview 差距 + 可落地改法"。

> 决策标尺 0：**N** —— 不直接推可玩闭环，是渲染技术 demo 的改进依据。价值在于让后续任何人动这个 preview 时有实锤参考、不拍脑袋。

---

## 1. 技术事实：BG3 草地到底怎么实现的

来源：Larian Divinity Engine Wiki、Digital Foundry PC tech review、80.lv《Insight into Game-Ready Asset Workflow for Baldur's Gate 3》（Larian 环境美术实习生 Jeroen Toma 访谈）、Norbyte/bg3se modding docs、Graphics Programming Conference 2024 "The Road to Baldur's Gate 3" (W.F. Sten)。

### 1.1 总体渲染管线

- **引擎**：Larian 自研 Divinity 4.0（BG3 是第四代迭代），非 UE / Unity。
- **API**：Vulkan + DX11 双后端（Vulkan 是"主推"，DX11 用于老 CPU 兼容）。
- **渲染路径**：Deferred。
- **GI / RT**：**没有硬件光追，没有 RTXGI**。
  - 由 Digital Foundry 实测确认："shadow maps, screen-space ambient occlusion and just enough texture and geometry quality"。
  - 也没有 Lumen / VXGI / SDFGI 这种软件 GI。
  - 只有传统 **CSM 级联阴影 + SSAO + artist-baked light probes + sky ambient**。
- **结论**：BG3 的"贵感"来自**美术 + 调色 + 布光**，不是图形黑科技。

### 1.2 地形

- **高度**：Heightmap-based sculpting（Divinity Engine 有 Raise / Lower / Flatten / Smooth / Slope / Cut 六种笔刷）。
- **材质**：`TR_*` 命名的地形 material，每块地形可挂 N 张 material（artist 点 "+" 添加）。
  - **重要约束**：每张 material 用 3 张硬编码 layer —— Albedo / Normal / Physical（= roughness/metallic/AO packed）。
  - 贴图用 **Virtual Texture** streaming，DDS BC3，128×128 起步，带 mipmap。
- **刷地形**：`terrain paint interaction mode` 手刷 alpha blend，左键绘，右键擦，`[` `]` 调笔刷大小。
  - "TR_Base_*" 前缀的 material **不能刷**（没 alpha blend channel），只能做底图。
- **Tri-planar**：artist 自述大量用 tri-planar，保证斜坡和接缝无 UV 扭曲。

### 1.3 草 / 植被 scatter

- **工具**：`Instance Painter` —— Larian 自研的笔刷式 scatter 工具，有 4 个子模式：
  - Paint（刷 instance）/ Update（批量改属性）/ Align（沿法线对齐）/ Checkout Cells（64×64m 为一个 cell 锁定批次）。
- **按 cell 分块**：草 / 灌木按 **64m × 64m 的 cell** 组织，每个 cell 一个 instance batch，便于 streaming + frustum culling + checkout 协作。
- **树 != 草**：
  - **草 / 小灌木**：用 Instance Painter 刷。
  - **树 / 大灌木**：用 "root template add 模式"，让 AI grid 识别导航（而不是 Instance Painter）。
- **分布**：**artist 手刷**，不是纯 procedural。这一点和 UE5 Foliage Tool / Nanite landmass 完全一样。

### 1.4 草 mesh 本身

- **不是程序化 quad**，是艺术家建模的**草簇 cluster mesh**（Maya 建模 + ZBrush 雕细节 + Substance Painter 烘 PBR）。
- 每个 cluster ≈ 5-20 片叶子挤在一起，带**顶点色**（R = 风摆权重从根 0 到顶 1，G/B = AO/variation）。
- **大量 double-sided alpha plane**（speedtree / xfrog 风）：细叶 / 小花 / 秸秆 / 藤蔓都是双面裁剪面片，不是真·3D geometry。
- **Material**：PBR tri-planar，**Alpha cutout**（不是 alpha blend，避免排序问题），**subsurface translucency** 开启。

### 1.5 风 shader

- **Vertex shader 里**按世界坐标驱动 noise 做摆动，顶点色 R 通道做"根部弱顶部强"权重（artist 自己画的，不是靠 VERTEX.y）。
- 相位由每 instance 的 world pivot 做 offset，避免同步。
- 主频 + 低频阵风叠加（和我们 preview 里的做法一样）。
- **无玩家交互**：公开资料和游戏实测都没看到"角色走过压草"（只有 bg3 贴图 grass trample VFX，没有 runtime 压弯 mesh）。

### 1.6 光照 / 氛围

- **DirectionalLight + CSM**：4 级 cascade，shadow bias 调得很严。
- **SSAO**：Digital Foundry 实测确认，不是 GTAO，不是 HBAO+。
- **Ambient**：sky contribution 占比高，给草地提供基础亮度。
- **Tonemap**：ACES-like，有明显"lift shadows + 降远景对比"的 LUT 风格。
- **Fog**：
  - 大气散射模式（蓝色远雾，前景暖色），非体积雾。
  - 远景 fog 色 + 色温偏蓝，近景暖黄。
- **DoF**：Circular depth of field（真·透镜模糊），对话 / cinematic 模式开启，游戏内俯视默认关。
- **Bloom**：有但克制，阈值高（只亮部才发光）。

### 1.7 优化

- 64×64 cell 配合 frustum culling 按 cell 批 cull。
- 远处草地转为 decal + normal map，没有真 mesh。
- 按 detail distance / instance distance 分控（玩家可调，PC 最高档不高）。
- 无 raytracing shadow —— 所以草下阴影是 CSM 烘出来的 fake，细节不够锐。

### 1.8 一句话技术总结

> BG3 的草地是 **"64m cell 上 artist-painted 的 PBR cluster mesh + 双面 alpha 面片混搭，vertex-color 驱动风摆 + 两层透射 SSS + CSM + SSAO + 大气雾 + ACES"**。
> 没有程序化 scatter、没有 RT、没有软件 GI、没有踩踏交互。
> **它不是靠图形技术赢的，是靠艺术家 + 调色赢的。**

---

## 2. 审美事实：Gemini 视觉拆解摘要

（问过一轮 gemini-3-flash 子代理；下面是它给的要点，我做了压缩）

### 2.1 四种 mood 的 hex 色卡

| mood | 主色 | 次色（高光） | 阴影 |
|---|---|---|---|
| 日间标准原野 | 暖橄榄绿 `#6B8E23` | 干米黄绿 `#A2AD91` | 深褐绿 `#3D441F` |
| 黄昏金色时刻 | 铁锈黄绿 `#8B7D3A` | 浓橙金 `#D4A017` | 蓝紫阴影 `#3A324D` |
| 翠冷林地 | 翡翠绿 `#2E5A39` | 荧光青绿 `#48A860` | 极深蓝绿 `#1A2F23` |
| 病态（幽影诅咒） | 死青灰 `#4F5D53` | 枯黄 `#7E8A3D` | 腐烂暗紫黑 `#2A242D` |

量化建议：亮部与暗部色温差保持约 1500K；黄昏模式下**阴影饱和度比日间高 20%**（补色原理）；病态模式整体饱和度 −40~50%、对比度拉高。

### 2.2 光线处理（BG3 草地"贵"的六个细节）

1. **SSS 背光透射**：对着太阳时草叶**变亮不变黑**。透射光强度 = 直射的 0.6~0.8 倍，颜色比主色更黄更亮。
2. **Rim Light 侧逆光边缘**：草叶边缘 ≤ 2 px 的金白色高亮。让草"跳"出复杂背景。
3. **Contact Shadow / Micro-AO**：草根和地面接触处极深。**没有这一抹黑草就会浮**。
4. **Flecks / Pollen 尘粒**：阳光区域有 1-3mm 亮粒子，亮度 ≥ 场景均亮的 3×，布朗运动。
5. **God Rays 交互**：草丛遮挡体积雾形成细密明暗条纹。
6. （Gemini 没明说，技术侧补）**LUT 风格**：lift shadows + 远景蓝雾 + ACES。

### 2.3 近 / 中 / 远三层层次

| 层次 | 视觉目标 | 关键参数 |
|---|---|---|
| 近景 0-5m | "每一根草都有个性" | **clump 分布（5-8 根一簇）**，高度差 15-40cm 剧跳；花朵 3-5%，枯叶 10% |
| 中景 5-20m | "波动感 / rhythm" | 绿色健康区 70%，枯黄斑块 20%，裸露地表 10% |
| 远景 20m+ | "颜色梯度平滑" | 细节消失，color band 主导，normal map 模拟起伏 |

### 2.4 地面过渡的点缀率

- 玩家走动路径 / 树荫下 / 岩石边 —— 草自然稀疏，泥土色**比草根再深一个色阶**（约 `#2B1E16`），边缘有"半枯萎短草"过渡带。
- **碎石 / 岩石 15%**，**倒木 / 断枝 5%**（做构图指向线），**苔藓 / 藤蔓 20%**。
- 混合权重用 Perlin + 高度图，**绝不能出现圆滑笔刷痕迹**。

### 2.5 构图 / 相机 / DoF

- 相机高度 **30-60cm 低机位**（不是俯视 10m 高）。
- 俯角 **15°-30° 浅俯**，同时能看到叶面和地面阴影。
- FOV **45°-60°**（35-50mm 等效），避免广角畸变。
- **极浅 DoF**：近景草丛糊成色块，焦点留 2-3m，模糊半径达屏宽 2-5% → 柔光斑效果。

### 2.6 10 个失败模式

1. 均匀密度（插秧感）
2. 纯色不分段（根到顶一种绿）
3. 顶端太整齐（像修剪过的草坪）
4. 风摆同步（广播体操）
5. 没 backlight（塑料片）
6. 根部没 AO（悬浮）
7. 全垂直生长（违反重力，应 5-15° 随机倾斜）
8. 远景 aliasing 闪烁
9. 没玩家交互（瞬间出戏）
10. 过度饱和（荧光玩具绿）

### 2.7 三条 punchline（贴在 shader 头上）

- **"不要做绿色的地毯，要做一万个正在呼吸的半透明翡翠片。"**（SSS + 个体差异）
- **"阴影和根部的'黑'比叶片的'绿'更能决定草地的真实感。"**（AO + 接触阴影）
- **"风不是在吹草，而是在草地上画出流动的波浪。"**（宏观风力场，不是叶级别摆动）

---

## 3. 我们当前 preview 的差距对照

以下只列"我们和 BG3 视觉差距明显"或"BG3 技术事实要求，我们没做"的项，已做到的不重复列。

### 3.1 草 mesh / 分布

| 项 | BG3 事实 | 我们现在 | 差距 |
|---|---|---|---|
| 草 mesh 形态 | 5-20 叶 cluster mesh + 大量双面 alpha plane | 单叶 procedural quad（1 叶 × 40k instance） | **单叶 quad 天然"插秧"，没 cluster 感** |
| 分布 | artist 手刷 + Poisson/blue-noise | 纯均匀 random | 缺 clump，触发失败模式 #1 |
| 高度分布 | 近景 15-40cm 剧跳 | 0.85-1.35m 线性随机 | 跳变幅度不够，缺近景个体感 |
| 倾斜 | 5-15° 随机倒伏 | 100% 垂直 | 触发失败模式 #7 |
| 点缀 | 花 3-5%、枯 10%、岩石 15%、倒木 5%、苔 20% | 无 | 全都没有，视觉单调 |
| 顶点色权重 | R 通道 artist 画的根→顶权重 | 用 `VERTEX.y` 代替（所有叶一样） | 所有叶摆动权重曲线一致，缺差异 |

### 3.2 光照 / shader

| 项 | BG3 事实 | 我们现在 | 差距 |
|---|---|---|---|
| SSS | 背光透射两层（强度 0.6-0.8x，色更黄） | 有一版 `light()` 里做了背光透射 | 系数可能太弱，需要实际看效果 |
| Rim | ≤ 2px 金白边缘 | 无 | **缺 rim**，叶子"贴"在背景上 |
| Contact shadow / AO | 根部一抹黑 | shader 里 `ao = mix(0.55, 1.0, v_height)` 做了根部压暗，但**不是真的屏幕空间 contact shadow**，只是颜色乘法 | 没有实际的"草-地"接缝黑影 |
| 粒子 flecks | 阳光区有 1-3mm 尘粒 | 无 | 缺"阳光质感" |
| Fog | 蓝雾远景 + 暖前景 | ground shader 里有 `fog_color` 混合 | OK，但近景没暖调 pop |
| 配色 mood | 日间/黄昏/翠冷/病态 4 档 | 日 + 黄昏 2 档 | 少两档 |

### 3.3 地形

| 项 | BG3 事实 | 我们现在 | 差距 |
|---|---|---|---|
| Splat layer | 3 张（草/土/岩）按 artist-painted weight 混合 | 按 height + slope 程序分带（3 种颜色：泥土/草/干草） | 程序分带容易出"圆润色带"，**需要加 Perlin jitter 破形状** |
| 远景 | 转 decal + normal | 远处草 mesh 依然画满 | 性能 + 视觉都差 |
| 路径稀疏 | 玩家常走处草稀、土露 | 全均匀 | 没有"叙事性" |
| 点缀物 | 岩石 / 断枝 / 苔藓 | 无 | 地面太干净 |

### 3.4 构图 / 相机

| 项 | BG3 事实 | 我们现在 | 差距 |
|---|---|---|---|
| 初始相机 | 30-60cm 低机位 + 15-30° 浅俯 | 距离 22m，俯角 ≈ 18°，y 约 6-7m | **高度太高，看不到个体叶面**，像俯瞰地图 |
| FOV | 45-60° | 55° | OK |
| DoF | 近景糊成色块 | **完全没开** | 缺电影感 |
| Tonemap | ACES + lift shadow | ACES | OK（但没 lift shadow） |

### 3.5 整体 "假感" 自查表

对照 Gemini 10 个失败模式，我们当前 preview 至少中了：
- #1（均匀分布）
- #2（纯色不分段 —— 好在 shader 已经做了根暗顶亮，但 hue 偏移不够）
- #3（顶端整齐）
- #4（风摆相位虽然随机但没有"宏观波浪"）
- #6（根部 AO 只是颜色乘法，不是真接缝黑）
- #7（全垂直）
- #9（没交互，不重要）
- #10（颜色偏饱和了一点，需要去 10-15%）

---

## 4. 可落地改法（按影响力 P0 → P2）

### P0：改完立竿见影（1-2 小时）

**P0-1. Cluster 分布代替均匀随机**
- 把 `RebuildBlades()` 的单叶均匀撒改成 **clump scatter**：
  - 先按 blue-noise / jittered grid 生成 "clump center"（约 1/8 草总数）
  - 每个 center 周围再撒 5-12 根叶子，半径 20-40cm 高斯分布
  - 这样 40k 叶 ≈ 5000 clump，立刻出"丛"感
- 影响 Gemini 失败模式 #1。

**P0-2. 随机倾斜 5-15°**
- 每 instance 的 transform basis 加 `Basis.FromEuler((tiltX, 0, tiltZ))`，tiltX/tiltZ ∈ [-15°, 15°] 随机。
- 立刻解决失败模式 #7。

**P0-3. 相机初始低机位 + 加 DoF**
- 初始 `_distance` 从 22 改 5，`_pitch` 从 18° 改 10°，focus.y 小幅上抬。
- `WorldEnvironment` 的 `Environment` 开 `dof_blur_far_enabled = true`，far_distance ≈ 8m，transition ≈ 4m。
- 立刻出"前景草丛糊成色块、远山清晰"的 BG3 cinematic 感。

**P0-4. 顶端随机高度 + 切更猛的分布**
- blade_height_scale max 从 1.35 改 1.8，min 从 0.85 改 0.5，跨度拉大。
- 让近景有明显高矮差。
- 解决失败模式 #3。

**P0-5. 降 10% 饱和度**
- `base_color_high` 从 `#B7DB5C` 降到 `#9CB94B`（-15% sat）。
- 日间配色从"有点塑料"回到"自然橄榄绿"。

### P1：做完能看出明显"贵感"（2-4 小时）

**P1-1. Rim light 加到 fragment 里**
- blade shader fragment 里 `light()` 函数加一条：
  ```
  vec3 H = normalize(VIEW - LIGHT);
  float rim = pow(1.0 - max(dot(NORMAL, VIEW), 0.0), 4.0) * max(dot(-NORMAL, LIGHT), 0.0);
  SPECULAR_LIGHT += LIGHT_COLOR * rim * 0.8;
  ```
- 逆光时每根草勾金边，立刻"跳"出来。

**P1-2. 接触阴影（真的不是颜色乘法）**
- 根部做一个"半球状黑色 decal"：在 ground 位置按 blade root 投一个半透黑色 `SpriteBase3D` 或 ground shader 里按 `dist < 0.15m` 到最近 blade 叠一层暗。
- 更简单的做法：MultiMesh instance 加一个根部小圆盘 mesh（5cm 直径，黑色半透，cast 光后叠在草上），作为"fake AO 碟"。
- 解决失败模式 #6 + Gemini punchline #2。

**P1-3. 花朵点缀（3-5%）**
- 再开一个 MultiMesh，用同 shader 但 albedo 改红/白/蓝 3 色，instance_count = 总叶数的 4%。
- 位置分布跟随 clump center 周边（花长在草丛边缘更自然）。
- 立刻多"生机感"。

**P1-4. Ground 加 Perlin 破分带**
- ground shader 的 dirt/grass/dry 过渡混入更高频 noise（fbm(pw * 3.0)），权重 0.3。
- 破掉现在过于平滑的 color band。

**P1-5. 远景 LOD：20m 以外草降密度 50%**
- RebuildBlades 里按 `length(pos) > 20m` 时 2/3 概率跳过。
- 性能 + 远景"淡出"自然。

### P2：加了更好看但工作量中（4-8 小时）

**P2-1. Cluster mesh 代替单 quad**
- 用 ArrayMesh 程序生成一个 "3-5 叶 cross-quad" 单元（经典 speedtree low-poly 做法：3 片 quad 互成 60° 交叉，共享中心），当作 MultiMesh 的 base mesh。
- 一个 instance = 一小簇草，视觉复杂度翻 3-5 倍，但 GPU 负担只翻 3-5 倍三角形数（可接受）。
- 效果接近 BG3 的 cluster mesh。

**P2-2. 顶点色驱动风摆权重**
- 给 QuadMesh 每个顶点染色（底部 R=0，顶部 R=1，侧面再按 random noise 染点差异），shader 改用 COLOR.r 代替 VERTEX.y 做风摆权重。
- 不同叶片摆动曲线有差异。

**P2-3. 岩石 / 倒木 / 苔藓 scatter**
- 用 Godot 4 的 `BoxMesh` + PBR material 程序生成几块岩石 mesh（表面用 tri-planar noise 做凹凸），按 15% / 5% / 20% 密度再开三个 MultiMesh 摊。

**P2-4. 路径稀疏**
- 画一张 2D "trampled mask"（程序生成一条弯弯的路径，按距离 fade），在 RebuildBlades 里查 mask，路径上跳过 blade 生成。
- 手法同 BG3 的"走动路径草稀疏"。

**P2-5. Flecks 粒子**
- GPUParticles3D 飘在 3-8m 高度，billboard 小白点，emit 1-3mm，rate 50/s。
- 阳光斜射感瞬间到位。

### P3（不做）

- **玩家交互压草**：BG3 都没做。且我们当前 preview 没玩家。不做。
- **体积雾 / god rays**：Godot 4 的 VolumetricFog 在 40k 草上性能差；BG3 也不靠这个。不做。
- **SDFGI**：我们 tscn 里已经开了，但对 MultiMesh 草没贡献（草 GIMode=Disabled）。对地面有意义，保留。

---

## 5. Punchline（贴在 blade shader 顶部）

把下面三句话贴在 `App/Previews/bg3_grass_blade.gdshader` 第一行注释里，下一个改 shader 的人会少走弯路：

```
// 不要做绿色的地毯，要做一万个正在呼吸的半透明翡翠片。（SSS + 个体差异）
// 阴影和根部的"黑"比叶片的"绿"更能决定草地的真实感。（AO + 接触阴影）
// 风不是在吹草，而是在草地上画出流动的波浪。（宏观风力场，不是叶级别摆动）
```

---

## 参考

- Larian Divinity Engine Wiki: [Part 4: painting your terrain](https://docs.larian.game/Part_4:_painting_your_terrain), [Interaction modes](https://docs.larian.game/Interaction_modes), [Visual resource panel](https://docs.larian.game/Visual_resource_panel)
- Norbyte/bg3se: [VirtualTextures.md](https://github.com/Norbyte/bg3se/blob/main/Docs/VirtualTextures.md)
- Digital Foundry: [Baldur's Gate 3 PC tech review](https://eurogamer.net/digitalfoundry-2023-baldurs-gate-3-pc-tech-review-polish-that-puts-other-aaa-games-to-shame)
- 80.lv: [Insight into Game-Ready Asset Workflow for Baldur's Gate 3](https://80.lv/articles/insight-into-game-ready-asset-workflow-for-baldur-s-gate-3)
- YouTube: [The Road to Baldur's Gate 3](https://www.youtube.com/watch?v=zuDjcoabX7U) (GPC 2024, W.F. Sten)
- Gemini 3 Flash 子代理的 BG3 草地视觉审美拆解（本次本地对话）
