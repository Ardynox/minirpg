using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 内置动作定义注册表。新增动作只需在这里加一条，不改任何生物代码。
/// 后续可改为从 JSON 文件加载。
/// </summary>
public static class ActionDefs
{
	public static readonly List<ActionDef> All =
	[
		new ActionDef
		{
			Id = "melee_attack", Name = "近战攻击",
			Required = new() { ["近战"] = 1, ["力量"] = 1 },
			EffectType = "melee_attack", Power = 1,
		},
		new ActionDef
		{
			Id = "heavy_strike", Name = "重击",
			Required = new() { ["近战"] = 3, ["力量"] = 4 },
			EffectType = "melee_attack", Power = 3,
		},
		new ActionDef
		{
			Id = "block", Name = "格挡",
			Required = new() { ["格挡"] = 1 },
			EffectType = "block", Power = 1,
		},
		new ActionDef
		{
			Id = "poison_spit", Name = "毒液喷射",
			Required = new() { ["毒性"] = 2 },
			EffectType = "poison_attack", Power = 2,
		},
		new ActionDef
		{
			Id = "undead_drain", Name = "亡灵吸取",
			Required = new() { ["亡灵"] = 1, ["近战"] = 2 },
			EffectType = "drain_attack", Power = 2,
		},
		new ActionDef
		{
			Id = "move", Name = "移动",
			Required = new() { ["移动"] = 1 },
			EffectType = "move", Power = 0,
		},
		new ActionDef
		{
			Id = "look", Name = "观察",
			Required = new() { ["视觉"] = 1 },
			EffectType = "look", Power = 0,
		},
	];
}
