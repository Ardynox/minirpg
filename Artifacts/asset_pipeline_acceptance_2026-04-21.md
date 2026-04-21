# 资源生成与对接 — 验收记录（2026-04-21）

## 决策标尺 0：**Y**

B4 人脸占位落盘 + 校验脚本覆盖 `Assets/Faces`，角色定制里「有图可加载」不再依赖程序几何 fallback 一条腿；对接面可自动化验证，减少缺图 silent 失败。

## 已执行项

| 验收项 | 命令 / 证据 | 期望 | 结果 |
|--------|-------------|------|------|
| B4 本地占位 | `python Tools/banana/b4_faces_fill_local.py` | 与 `Data/FaceParts/*.json` 非空 `imagePath` 同数 | **62/62** 写入 `Assets/Faces/**`；日志 `Tools/banana/.cache/b4_faces_fill.log` |
| 资源路径一致性 | `python Tools/validate_art_res_paths.py --catalog --combat-audit` | Exit 0 | **OK**（含 FaceParts + 全 catalog 帧路径 + combat 与 catalog id 对齐） |
| 面容相关单测 | `dotnet test ... --filter FullyQualifiedName~FaceCustomization` | 全通过 | **16/16 通过** |
| 文档与协调单 | `Tools/banana/README.md` §2.5、`Artifacts/asset_pipeline_coordination_2026-04-21.md` | B4 状态更新 | **已更新** |

## 已知约定（非阻塞）

1. **Godot `.import`**：新 PNG 首次进工程后应用编辑器打开项目或运行导入，由引擎生成/更新 `.png.import`。CI 若以无 Godot 方式仅验文件存在，当前 `validate_art_res_paths.py` 已足够。
2. **B4 美学**：本批为**土色块占位**，与 Banana 成品并存策略 — 有 API 后按 `Artifacts/banana资源生成提示词总表_2026-04-18.md` 与人脸资产卡分批替换，勿删 `b4_faces_fill_local.py`。
3. **后续批次**：B9 仍为 **运行时几何**（`MapSpriteRuntimeFactory` / `EquipmentAppearanceData`），无单独 PNG 批次硬依赖；**B10 / B12** 仍见 `Artifacts/素材需求清单_2026-04-20.md` §5（B11 已闭合）。

## B11 追认（2026-04-21 晚）

| 项 | 命令 / 说明 |
|----|-------------|
| 占位 10 张 | `python Tools/banana/b11_facility_blueprint_fill_local.py` → `Assets/Art/Generated/facilities_blueprint/facility_*_blueprint.png` |
| 渲染接线 | `FacilityStage.Blueprint` 且存在上述路径时优先加载，`SkipFacilityStageTint` 避免蓝叠蓝 |
| 校验 | `python Tools/validate_art_res_paths.py --facilities-blueprint` |
| 单测 | `IsometricRender` 过滤 53/53 通过（与构建同批） |

## 签字栏

- **资源路径 & 脚本**：见本仓库 commit（`b4_faces_fill_local.py`、`validate_art_res_paths.py` 扩展、PNG 资产）。
- **总负责人结论**：**B4 + B11** 占位与对接已完成可签；下一里程碑优先 **Banana 换血 B4/B11 真图**，或 **B10 Generated 风格重刷**（等视觉锚点稳定）。
