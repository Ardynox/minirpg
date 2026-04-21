# 资源生成任务总览与分工（2026-04-21）

> 角色：总负责人编排结论 + 子代理（只读）核验。  
> 对接护栏：`Tools/validate_art_res_paths.py` — 与 `Docs/运行与测试.md` 中的命令一致。

## 决策标尺 0

本稿以「清点 + 对接工具 + 后续分包」为主：**Y** — 缺图时在 CI/本地能立刻红脸，避免 silent 空引用；后续按批次生成不会反复撞同一类坑。

## 现状结论（子代理 + 本机校验）

| 维度 | 结论 |
|------|------|
| P0 占位（清单 §1.1） | `npc_map`×10、`portraits`×10、战斗单帧 FX×7、`item_icons`×130、`branding`×2、`item_world/category_*`×9、天气条目共 9 张与 `WeatherFxController` 对齐 — **文件齐全** |
| `combat_fx.json` ↔ `resource_catalog.json` | `resourceId` 均在 catalog 有 `id`（`python Tools/validate_art_res_paths.py --combat-audit`） |
| 磁盘路径 ↔ JSON | `entity_render.json`、`item_world_render.json` 内 `res://Assets/Art/...` 均指向存在文件；加 `--catalog` 可走全量 `resource_catalog` 帧路径 |

## 下一波生成（按 `Artifacts/素材需求清单_2026-04-20.md` §5）

**已闭合批次（勿重复开荒）**：B1（驱动 + 手搓七特效）、B5/B6/B7/B8、`b2_item_world` 分类图。

| 优先级 | 批次 | 内容 | 建议执行方式 |
|--------|------|------|----------------|
| P1 | **B4** | `Assets/Faces/` 人脸部件约 64 张 | 按类拆通道（§5 建议 8 路）；先补资产卡再跑 `banana_gen.py` |
| P1 | **B11** | facility 蓝图 10 张 | 可评估 shader 替代；若出图则单列目录 `Generated/facilities_blueprint/` |
| P2 | **B9** | 装备 overlay | **依赖于** B4 与玩家素体定型 |
| P2 | **B10** | Generated 全量风格重刷 | 清单写明等 B0–B2 锚点稳定后再开 |
| P2 | **B12** | 生活细节 overlay + 联机色带 | 路线图项，不与 P0 抢通道 |

## 并行分工（可复制给各通道）

1. **生成通道**（需计费 API + 代理）：只跑 `Tools/banana/README.md` 既定驱动；每批结束追加 `Tools/banana/.cache/<batch>_driver.log` 一行 summary（`ok/fail/skip`）。
2. **对接通道**（可离线）：改 `Data/*.json` / `PlaceholderUiIconCatalog` 等；**合并前必跑**  
   `python Tools\validate_art_res_paths.py --catalog --combat-audit`
3. **避让**：开工前读 `Artifacts/wip.md`；勿与 `qtwx-mcp-*` 已登记热点撞车。

## 协作事故预防

- 资源进 `Assets/Art` 前走 `Assets/Art/README.md` §读感验收。  
- 勿覆盖 `.png.import` 除非有意重导。  
- `resource_catalog.json` 与 `combat_fx.json` 的 `resourceId` 必须同步（脚本已覆盖审计）。
