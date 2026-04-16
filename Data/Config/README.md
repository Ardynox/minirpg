# Config

这个目录放运行时 JSON 配置。
默认先确认配置是由哪条加载链读取，再决定改 JSON 还是改 C# 逻辑。

## 从哪开始读

- 先看 `MiniRPG.Shared/Core/Config/GameDataLocator.cs`：这里决定 Data 配置从哪里读取
- 通用运行时配置入口：`MiniRPG.Shared/Core/Config/GameConfig.cs`
- Utility AI 专用配置入口：`MiniRPG.Shared/Core/AI/Utility/UtilityActionRegistry.cs`、`MiniRPG.Shared/Core/AI/Utility/PersonalityModule.cs`

## 常见改动去哪里

- 玩家视野、AI 感知、世界运行时、天气、火焰、调试、地图生成：改 `GameConfig.cs` 对应的 `*.json`
- NPC 行为分数、曲线、tag、target type：`utility_actions.json`
- 性格轴和种族/职业偏移：`personality_defaults.json`
- 玩家视野：`player_vision.json`
- AI 感知和简化模拟范围：`ai_vision.json`
- chunk 加载、坠落伤害、世界运行时预算：`world_runtime.json`
- 天气环境和温度参数：`weather.json`
- 火焰扩散和火势参数：`fire.json`
- 地图生成参数：`generation/*.json`

## 不要在这里解决什么

- 不把“其实写死在 C# 里的规则”误判成纯配置问题
- 如果改了 JSON 但效果没变，先查对应加载入口和调用方，不要盲目继续调参
- 不把一次性调参实验记录写回这里；实验记录放 `Artifacts/`

## 什么时候更新这份 README

- 只有当配置加载入口、主要配置分工或常见改动入口发生变化时才更新
