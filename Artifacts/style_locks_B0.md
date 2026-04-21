# B0 · 全局风格锚点（style lock）

> 对应 `Artifacts/素材身份卡表_2026-04-20.md` §9 依赖图里的 **B0**。  
> 本文件由仓库已有基线整理而成，**不替代**发包前向 Gemini 复述；多通道出图时凡写「与 B0 / style_lock 一致」即指本节。

## 已锁定（与 `Assets/Art/README.md`、`Artifacts/素材需求清单_2026-04-20.md` §0 对齐）

- **透视**：2.5D 等距 / 3/4 俯视正交；生存沙盒感，实用不花哨。
- **渲染语言**：手绘 2D 插画 / 游戏贴图感；**低饱和土色系**；柔和阴天，**地面避免硬投影**。
- **线面**：清楚轮廓，**细棕线**，避免黑粗描边；扁平色 + **轻阴影**，避免高光塑料感。
- **透明**：默认 **alpha=0** 干净透明底；仅天气地表叠层、品牌主菜单背景等允许不透明（见各批身份卡备注）。
- **文件**：小写 + 下划线；单资产单文件；**禁止** contact sheet / 拼盘；**不要**手写 `.png.import`。
- **一致性**：同系列同会话出图；漂移时追加英文：`match the exact style, palette and lighting of the previous image`。

## 发包时复述用（短 prompt 块，可直接粘贴）

```
Follow the project's B0 style lock: hand-painted 2D game art, low-saturation earth-tone palette,
soft overcast lighting (no harsh ground shadows), thin brown outlines (not heavy black),
flat colors with light shading only, transparent PNG unless the asset card explicitly allows opacity.
Survival-sandbox practical look, not flashy.
```

## 待各批「风格咨询清单」拍板后写入的子锁

各批在 `Artifacts/style_locks_registry.md` 登记；拍板结论写入 `Artifacts/style_locks_B1.md` … `B12.md`（按需新建），并在 registry 勾「已锁定」。
