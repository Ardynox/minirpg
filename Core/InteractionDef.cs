using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 交互定义：统一的行为描述。
/// 涵盖战斗动作（近战攻击、格挡、毒液喷射…）、社交（对话、交易、驯服…）、
/// 工具（挖掘、移动、观察…）等所有 Actor 可执行的行为。
/// Required = 发起者 tag 条件，TargetRequired = 目标条件。
/// 条件 key 约定：普通 key = tag 最低值，"@faction:X" = 阵营匹配，"@max:tag:N" = tag 上限。
/// </summary>
public class InteractionDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string Description { get; set; } = "";

	/// <summary>分类：combat / utility / social。用于 UI 分组。</summary>
	public string Category { get; set; } = "combat";

	/// <summary>发起者 tag 前置条件。</summary>
	public Dictionary<string, int> Required { get; set; } = new();
	/// <summary>发起者能力要求：key = 能力 ID, value = 最低百分比。</summary>
	public Dictionary<string, float> CapacityRequired { get; set; } = new();
	/// <summary>目标条件。战斗技能用 @faction:hostile，自身技能留空。</summary>
	public Dictionary<string, int> TargetRequired { get; set; } = new();

	public string EffectType { get; set; } = "";
	/// <summary>伤害类型：sharp/blunt/poison。空 = 由武器决定或默认 blunt。</summary>
	public string DamageType { get; set; } = "";
	public int Power { get; set; }

	/// <summary>冷却回合数。0 = 无冷却。</summary>
	public int Cooldown { get; set; }
	/// <summary>射程。0 = 自身, 1 = 相邻（默认）。</summary>
	public int Range { get; set; } = 1;
	/// <summary>隐藏技能不出现在技能列表 UI 中（如 move、look）。</summary>
	public bool Hidden { get; set; }

	/// <summary>
	/// 地形材质过滤（仅 EffectType=dig 时有效）。
	/// 空字符串 = 可作用于所有可破坏地形。
	/// 非空 = 只能作用于该材质的地形（如 "wood"、"stone"）。
	/// </summary>
	public string TerrainMaterial { get; set; } = "";
}
