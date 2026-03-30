using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 内置交互定义。新增交互只加条目，不改任何模块代码。
/// </summary>
public static class InteractionDefs
{
	public static readonly List<InteractionDef> All =
	[
		new InteractionDef
		{
			Id = "talk", Name = "对话",
			Required = new() { ["视觉"] = 1 },
			TargetRequired = new() { ["@faction:friendly"] = 1 },
			EffectType = "talk",
		},
		new InteractionDef
		{
			Id = "trade", Name = "交易",
			Required = new() { ["视觉"] = 1 },
			TargetRequired = new() { ["交易"] = 1 },
			EffectType = "trade",
		},
		new InteractionDef
		{
			Id = "attack", Name = "攻击",
			Required = new() { ["近战"] = 1 },
			TargetRequired = new() { ["@faction:hostile"] = 1 },
			EffectType = "combat",
		},
		new InteractionDef
		{
			Id = "tame", Name = "驯服",
			Required = new() { ["驯服"] = 2 },
			TargetRequired = new() { ["@max:野性:3"] = 1, ["@faction:hostile"] = 1 },
			EffectType = "tame",
		},
	];
}
