# Render

这个目录负责本地运行时渲染实现。
重点是地图体素渲染、天气特效、光照、动画适配和渲染性能记录。

## 从哪开始读

- 先看 `IsometricVoxelRenderer.cs`：当前地图渲染组合入口，也是多数渲染改动的第一落点
- 光照与面着色：`VoxelLightingCalculator.cs`
- 天气覆盖层、屏幕特效、天气 sprite 池：`WeatherFxController.cs`
- 渲染性能快照：`RenderPerfTracer.cs`
- 实体/动画适配：`FantasyCharacterAnimatable.cs`、`SpineAnimatable.cs`、`TileAnimatable.cs`
- 战斗特效：`CombatFxPlayer.cs`、`CombatFxRegistry.cs`

## 常见改动去哪里

- 改地图绘制、hover、高亮、路径预览、编辑器视图切换：`IsometricVoxelRenderer.cs`
- 改昼夜、环境光、面阴影、点光混合：`VoxelLightingCalculator.cs`
- 改天气屏幕效果、天气贴图、天气对象池：`WeatherFxController.cs`
- 改渲染统计或性能观察口径：`RenderPerfTracer.cs`
- 改资源映射、贴图/地形查询：`TerrainAtlas.cs`、`ItemWorldRenderRegistry.cs`、`ResAccess.cs`

## 不要在这里解决什么

- 不把世界状态真相、战争迷雾语义、会话流程放进渲染层
- 视野/迷雾语义看 `MiniRPG.Shared/Module/Render/FogOfWarTracker.cs`
- 输入、菜单、面板开关和流程编排看 `App/RuntimeUi/*`

## 修改提醒

- `IsometricVoxelRenderer.cs` 仍然是渲染热点，大改前先确认是否可以继续外提到专门类
- 优先补局部职责，不要把更多流程判断重新塞回总渲染入口

## 什么时候更新这份 README

- 只有当主渲染入口、已拆分职责、或常见改动落点发生变化时才更新
