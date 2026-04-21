# B4 · 人脸部件（style lock）

> 对应 `Artifacts/素材身份卡表_2026-04-20.md` §B4。画布 **256×256** 透明 PNG，与 `ProceduralFacePartRenderer` / `PortraitComposer` 一致。

## 风格咨询清单 — 当前仓库结论（可随 Banana 批次修订）

1. **与 face canvas 对齐**：正式美术以 `ProceduralFacePartRenderer.HeadCenter`（约 `(128, 130)` 在 256 画布上）为眼鼻嘴参考；耳朵/发须可略外扩但必须透明底。
2. **线色 vs 全彩**：目标为**可染色**的清晰线稿 + 淡填充（肤色/发色由运行时或后续 shader 控制）；当前 **本地 PIL 兜底**（`Tools/banana/b4_faces_fill_local.py`）为土色线框 + hash 色块，仅保证管线可读。
3. **头型**：最底层肤色块；其余层透明底、对准同一坐标系。
4. **`bald` / `clean_shaven`**：`imagePath` 为 `null`，**不出图**（与主表一致）。

## 发包短句（接 Banana 时用）

```
256×256 transparent PNG, face part only, align features near canvas center (128,130) like a layered portrait sheet,
thin brown outlines, low-saturation earth tones, B0 style lock. No full-face contact sheet.
```
