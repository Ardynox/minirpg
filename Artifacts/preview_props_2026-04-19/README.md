# Prop 演示批 1 — 评估单

> 生成：2026-04-19
> 目的：验证 Cursor 内置 `GenerateImage` 能不能产出与 Banana 总表 §0.2 风格基线一致的 prop 资源
> 范围：3 张 256×256 顶视 prop 候选（营火 / 木柴堆 / 木箱）
> 入仓状态：**未入仓**，仅作演示评估，由人决定下一步

---

## 1. 三张图（按出图顺序）

| 文件 | 类型 | 实际尺寸 | 透明背景 | 视角 | 描边 |
|---|---|---|---|---|---|
| `preview_fixture_campfire_01.png` | fixture/营火 | 1376×768 | OK | 3/4 顶视 | **有黑色描边**（违反基线 §4 反向词） |
| `preview_prop_firewood_stack_01.png` | prop/木柴堆 | 1376×768 | **失败**（深灰底 fallback） | 3/4 顶视 | 无 |
| `preview_fixture_chest_wooden_01.png` | fixture/木箱 | 1376×768 | OK | 3/4 等距 | **有黑色描边**（违反基线 §4 反向词） |

---

## 2. 用了什么 prompt

3 张共用一套 prompt 骨架（沿用 `Artifacts/banana资源生成提示词总表_2026-04-18.md` §F01 格式），只换主体描述。完整 prompt 见本目录下各 PNG 旁的 `prompt.txt` 段落（贴在文末 §6）。

风格基线统一锁：
- `isometric 2.5D 3/4 top-down orthographic, no perspective distortion`
- `grounded semi-realistic, muted earthy palette, low saturation`
- `hand-painted shading with visible soft brush strokes, worn weathered survival feel`
- `soft overcast daylight from above`
- 反向词全套（no anime / cartoon / chibi / bright / glossy / cinematic / bloom / concept art / splash art / heavy black outlines / text / watermark / background / extra objects）

---

## 3. 风格一致性评估

| 维度 | 一致性 | 说明 |
|---|---|---|
| 调色板 | **强一致** | 全部落在棕褐色域（木材、皮带、铁箍），符合现有 `BaseHumanSpriteGenerator` 调色板 |
| 笔触感 | **强一致** | 都是 painterly hand-painted，没有出现卡通/写实跳变 |
| 视角 | **强一致** | 都是 3/4 等距俯视，比例稳定 |
| 描边 | **不一致** | 营火和木箱有黑色描边，木柴堆没有（描边违反反向词） |
| 透明背景 | **不一致** | 3 张里 1 张失败（木柴堆） |
| 与现有项目契合度 | **可接受** | 整体方向对，可以作为风格 anchor 给后续批次参考 |

---

## 4. 按 Assets/Art/README.md §5「读感验收」逐条对照

### 4.1 营火（campfire）

| 验收项 | 通过 | 备注 |
|---|---|---|
| 轮廓一眼能认出是什么 | ✓ | 石圈 + 火 + 柴清晰 |
| 透明底干净 | ✓ | 透明无残底 |
| 不依赖背景光效才能成立 | ✓ | 光源是火本身，不依赖 bloom |
| 不会因为细节太多缩小后糊成一团 | ✓ | 缩到 64×64 主体仍可读 |
| 黑色描边 | **✗** | 与基线 §4 反向词 `no heavy black outlines` 冲突 |
| 像素尺寸 256×256 | **✗** | 实际 1376×768，需要降采样 |

### 4.2 木柴堆（firewood）

| 验收项 | 通过 | 备注 |
|---|---|---|
| 轮廓一眼能认出是什么 | ✓ | 木纹和年轮清晰 |
| 透明底干净 | **✗** | AI fallback 给了深灰底，需要抠图 |
| 不依赖背景光效才能成立 | ✓ | |
| 不会因为细节太多缩小后糊成一团 | ⚠ | 木纹细节多，缩到 32×32 可能糊 |
| 黑色描边 | ✓ | 无 |
| 像素尺寸 256×256 | **✗** | 同上 |

### 4.3 木箱（chest）

| 验收项 | 通过 | 备注 |
|---|---|---|
| 轮廓一眼能认出是什么 | ✓ | 铁箍 + 锁 + 木纹一眼可读 |
| 透明底干净 | ✓ | |
| 不依赖背景光效才能成立 | ✓ | |
| 不会因为细节太多缩小后糊成一团 | ✓ | |
| 黑色描边 | **✗** | 与基线 §4 反向词冲突 |
| 像素尺寸 256×256 | **✗** | 同上 |

---

## 5. 「入仓还差什么」工作量估算

| 后处理项 | 三张都要做 | 单张工作量 | 工具 |
|---|---|---|---|
| 降采样到 256×256 | 是 | 30 秒 | ImageMagick / GIMP |
| 去黑色描边 | 营火 + 木箱 | 10-30 分钟 / 张 | 手 K 或 GIMP 的"按颜色选择"+"边缘羽化" |
| 抠透明背景 | 木柴堆 | 5-15 分钟 | GIMP 魔术棒 + 手修边缘 |
| 微调对比度让缩小后还能读 | 木柴堆 | 2 分钟 | 任意图像编辑器 |
| 改 `Data/items.json` / `entity_render.json` 接线 | 取决于入仓决策 | 5 分钟 | StrReplace |

总计：3 张图全部入仓 ≈ **40-90 分钟人工**。如果改用 Banana 重出 1 轮（不带描边），可能更快。

---

## 6. 各张图完整 prompt（复用时直接拷）

### 6.1 campfire

```
A single small campfire prop for a 2.5D top-down RPG game asset:
ring of weathered grey river stones surrounding a small pile of charred wooden logs,
warm orange-yellow flame with a few drifting embers and a thin wisp of smoke rising.
Isometric 2.5D 3/4 top-down orthographic view, NO perspective distortion.
Grounded semi-realistic style, muted earthy palette, low saturation,
hand-painted shading with visible soft brush strokes, worn weathered survival feel.
Soft overcast daylight from above, very subtle ambient shadow at the base only.
Centered composition, base of the prop at approximately 85% of image height.
Isolated on fully transparent background, no ground plane, no shadow disk under the object.
HARD NEGATIVES: no anime, no cartoon, no chibi, no bright saturated colors,
no glossy plastic, no cinematic lighting, no bloom, no concept art, no splash art,
no watercolor, no heavy black outlines, no text, no watermark,
no environment background, no extra objects.
```

### 6.2 firewood stack

```
A single stack of split firewood logs for a 2.5D top-down RPG game asset:
roughly 6 short cylindrical wooden logs piled in a small loose heap,
visible wood grain and bits of bark, mix of pale and weathered brown tones,
practical rustic survival feel.
Isometric 2.5D 3/4 top-down orthographic view, NO perspective distortion.
Grounded semi-realistic style, muted earthy palette, low saturation,
hand-painted shading with visible soft brush strokes, worn weathered survival feel.
Soft overcast daylight from above, very subtle ambient shadow at the base only.
Centered composition, base of the prop at approximately 85% of image height.
Isolated on fully transparent background, no ground plane, no shadow disk under the object.
HARD NEGATIVES: no anime, no cartoon, no chibi, no bright saturated colors,
no glossy plastic, no cinematic lighting, no bloom, no concept art, no splash art,
no watercolor, no heavy black outlines, no text, no watermark,
no environment background, no extra objects.
```

### 6.3 wooden chest

```
A single small closed wooden treasure chest prop for a 2.5D top-down RPG game asset:
weathered oak planks held together with dark hand-forged iron banding,
small iron hasp lock at the front, slightly worn corners,
practical medieval mercenary survival aesthetic.
Isometric 2.5D 3/4 top-down orthographic view, NO perspective distortion.
Grounded semi-realistic style, muted earthy palette, low saturation,
hand-painted shading with visible soft brush strokes, worn weathered feel.
Soft overcast daylight from above, very subtle ambient shadow at the base only.
Centered composition, base of the prop at approximately 85% of image height.
Isolated on fully transparent background, no ground plane, no shadow disk under the object.
HARD NEGATIVES: no anime, no cartoon, no chibi, no bright saturated colors,
no glossy plastic, no cinematic lighting, no bloom, no concept art, no splash art,
no watercolor, no heavy black outlines, no text, no watermark,
no environment background, no extra objects.
```

---

## 7. 我学到的（给下一批用）

1. **`GenerateImage` 工具固定输出 1376×768**，不可通过 prompt 强制 256×256。下一批默认接入流程要包含「降采样到 256×256」这一步。
2. **黑色描边是工具默认倾向**，反向词 `no heavy black outlines` 不够强。下一批可以追加 `no line art, no inked outlines, only shape-driven shading`。
3. **透明背景成功率约 2/3**。下一批可以加强 prompt 的透明背景描述，或直接预期 1/3 概率需要抠图。
4. **同一会话风格一致性还行**（调色板、笔触、视角都对得上），但**描边一致性差**（同一 prompt 出 3 张，描边有无不一致）。
5. **细节密度对小尺寸不友好**：木柴堆在原图很好看，缩到 32-64 像素会糊。后续做 prop 时要 prompt 「reduced detail density for 64x64 readability」。

---

## 8. 拍板选项（等用户决定）

- **A. 全部丢弃** — 验证目的达成，知道工具能做到什么档，不入仓
- **B. 选 1-2 个走完整管线** — 选哪个？要做的后处理：降采样 + 去描边 + 抠图 + 接 `Data/items.json` 或 `Data/fixtures.json`
- **C. 改 prompt 重生 1 轮** — 加强反描边、加强透明背景、加细节密度限制后再出 3 张对比
- **D. 转 Banana 出图** — 这 3 张当 reference / mood，去 Banana 用 `Artifacts/banana资源生成提示词总表_2026-04-18.md` §F01-F02 重出，一致性更稳

我的推荐是 **C 或 D**，因为：
- A 浪费已有的演示
- B 描边后处理成本不低，单张 30 分钟，3 张就 1.5 小时人工
- C 几乎零成本（再调一次 prompt 就出新 3 张）
- D 是项目本来定的正路（Banana 总表存在的意义就是这个）
