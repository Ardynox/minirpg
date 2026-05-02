# B9 · 装备 8 向 overlay（style lock）

继承 `Artifacts/style_locks_B0.md`。

## 运行时契约（已定）

- 路径：`Assets/Art/Generated/equipment_overlays/`（`EquipmentOverlayPaths.BasePath`）。
- 命名：`equip_weapon_<sword|axe|bow|staff|mace>_<dir>.png`，`equip_cloak_<short|long|hooded>_<dir>.png`，`equip_helmet_<cap|full|crown>_<dir>.png`。
- `<dir>` 顺序与 `DirectionalSpriteHelper` 行 0..7 一致：**s, sw, w, nw, n, ne, e, se**。
- 画布：与 `MapSpriteRuntimeFactory.ExportSize` 一致（当前 **256×256**）；若磁盘为 128 等，运行时会最近邻放大到 256 再叠。
- **凑齐某类 8 张**才启用该类 PNG；否则该类仍走程序几何（`MapSpriteRuntimeFactory.DrawEquipment*`）。
- 占位生成：`python Tools/banana/b9_equipment_overlay_fill_local.py`。
- **Banana 换血**：`python Tools/banana/b9_equipment_overlay_driver.py`（**88** 张，覆盖同目录）。

## 风格咨询清单 — 待 Gemini（正式美术）

武器是否只保留「举起」一帧、披风静态、与素体锚点严格对齐等，按身份卡 B9 节问清后写此处。
