# WIP 文件承包

本文件是多 AI / 进程协作时的"正在动的文件"登记表。
开工前扫一遍，别碰不属于你这块的；动你这块之外的文件前，在这里登记。

## 格式

```
- <进程名> | <文件/目录 glob> | <预计完成时间 / 当前状态>
```

## 当前登记

<!-- 按开工时间倒序。完成后删除本条。 -->

- cursor-opus-baseline-fix | 紧急 baseline 修复：`MiniRPG.csproj` 临时 `<Compile Remove="App\Previews\**\*.cs" />`，让 dotnet build 通过（GrassSurfacePreview.cs 用了不存在的 Key.BracketLeft/BracketRight；Tools 已早就被 Tools\**\*.cs 整目录排除）。**cursor-opus-grass-preview 修好预览脚本后请删掉 csproj 里的 App\Previews 那行 Compile Remove**；cursor-opus-2.5d-figure 收尾 `System.Drawing.Common` 引用后无须动 csproj（Tools 整目录已排除）。 | 已交付 2026-04-19，等其他进程修好后恢复

- cursor-opus-grass-preview | 草地 SurfaceCover 独立预览场景：新建 `App/Previews/GrassSurfacePreview.cs` + `Scene/GrassSurfacePreview.tscn`，不动 hot file。**注意**：当前 `dotnet build` 失败 22 个错误全在 `Tools/CharacterSpriteUtilities.cs` + `Tools/BaseHumanSpriteGenerator.cs`（cursor-opus-2.5d-figure 批 1 待补 `System.Drawing.Common` NuGet 引用），与本预览无关；本预览代码已自验编译通过（错误数从 23 → 22），等那条修好或临时 Compile Remove 即可看到效果。 | 已交付 2026-04-19，等编译解锁

- cursor-opus-2.5d-figure | 2.5D 捏人系统重构(完整 + 大清扫,6 批):**会动**①`Tools/MonsterMapAssetGenerator.cs`(抽 helper 给 BaseHumanSpriteGenerator 共享) + 新建 `Tools/CharacterSpriteUtilities.cs` + `Tools/BaseHumanSpriteGenerator.cs` + `Tools/CharacterPartGeometry.cs` + `Tools/EquipmentAppearanceCatalog.cs` + `Tools/EquipmentSpriteOverlay.cs` + `Tools/MonsterMapAssetGenerator/Program.cs` 加 generate-humans 命令;②新建 `App/RuntimeUi/MapSpriteRuntimeFactory.cs` + 可能新建 `Module/Render/RuntimeTextureRegistry.cs`;③改 `Module/Render/IsometricVoxelRenderer.cs` `TryResolveActorSpriteVisual` 玩家分支 + `Module/Render/FantasyCharacterAnimatable.cs` 删 SetColorTints 等染色相关;④改 `App/Main.cs` 删 `_playerCharacterVisual` 字段(评估后) + `App/Main.MapAndCamera.cs::RefreshPlayerCharacterVisual` 大改/可能整段删 + `App/Main.Wiring.cs` + `App/Main.Settings.cs` + `App/Main.UiMode.cs`(删 PlayerAppearanceId 引用);⑤改 `Module/CharacterCreationModule.cs` 把 ApplyMapSpritePreview/ApplyMapSpriteTints 切到新 factory;⑥**Shared 大清扫**:`MiniRPG.Shared/Core/Data/{Actor,GameState,PlayerCreationOptions,FaceCustomizationData}.cs`+`MiniRPG.Shared/Core/Map/{SaveSnapshot,SaveModule,MapGenModule}.cs`+`MiniRPG.Shared/Module/GameSessionModule.cs` 删 PlayerAppearanceId 字段相关;⑦**删整文件**:`MiniRPG.Shared/Core/Data/{PlayerAppearanceCatalog,MapSpriteTemplateCatalog,LegacyAppearanceMigration}.cs` + `Data/FaceParts/legacy_presets.json` + `App/RuntimeUi/MapSpriteResolver.cs` + `Assets/Shaders/character_color_mask.gdshader` + `Tests/MiniRPG.Tests/LegacyAppearanceMigrationTests.cs`;⑧改数据 `Data/entity_render.json` player+human entry + `Data/I18n/{zh_CN,en}.json` 删 7 个 data.player_appearance.* key;⑨改测试 `Tests/MiniRPG.Tests/{SavePayloadCoverageTests,FaceCustomizationSaveRoundTripTests}.cs` + 新增 `Tests/MiniRPG.Tests/{BaseHumanSpriteGenerator,MapSpriteRuntimeFactory}Tests.cs`;⑩文档 `Assets/Art/README.md` 第 209 行表格加注 (Docs/界面与面板.md 第 5.4 节修订留到 cursor-opus 完工后做)。**避开 cursor-opus 热点**:`Module/Panel/StatusModule.cs` / `Module/Panel/ActorStatusTextBuilder.cs` / `App/Main.PanelLauncher.cs` / `App/Main.Startup.cs` / `App/Main.Panels.cs` / `Docs/界面与面板.md` 都不动(我的范围不在 tooltip 链路上,Main.Startup 里 PlayerAppearanceCatalog.LoadProjectCatalog 那一行的删除留到 cursor-opus 完工后追加 PR)。 | 批 1 进行中 2026-04-19

## 已完成（最近）

<!--
本节定期清空，避免遮住"现在没人在动什么"。
旧条目按归档日期挪到 Artifacts/wip_archive_<日期>.md，需追溯历史归因时按需查阅。
最近一次归档：[Artifacts/wip_archive_2026-04-19.md](wip_archive_2026-04-19.md)
（覆盖 2026-04-17 至 2026-04-19 全部已完成条目）
-->

（已全部归档，见上方注释）
