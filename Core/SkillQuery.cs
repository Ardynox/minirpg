using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core;

/// <summary>
/// 技能查询：从统一的 InteractionDef 列表中筛选 Actor 可用的技能。
/// 技能 = Actor 满足 Required 条件的交互定义。
/// </summary>
public static class SkillQuery
{
	/// <summary>
	/// 获取 Actor 满足发起者条件的所有非隐藏交互（= 技能列表）。
	/// </summary>
	public static List<InteractionDef> GetSkills(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		return InteractionDefs.All
			.Where(d => !d.Hidden && CheckRequired(tags, caps, d))
			.ToList();
	}

	/// <summary>按分类过滤技能。</summary>
	public static List<InteractionDef> GetByCategory(Actor actor, string category)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		return InteractionDefs.All
			.Where(d => !d.Hidden && d.Category == category && CheckRequired(tags, caps, d))
			.ToList();
	}

	/// <summary>
	/// 获取 Actor 满足发起者条件的所有交互（含隐藏，如 move/look）。
	/// 用于 AI 和内部逻辑。
	/// </summary>
	public static List<InteractionDef> GetAll(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		return InteractionDefs.All
			.Where(d => CheckRequired(tags, caps, d))
			.ToList();
	}

	/// <summary>
	/// 获取可对某个 Actor 目标使用的技能（满足发起者 + 目标条件）。
	/// </summary>
	public static List<InteractionDef> GetUsableAgainst(Actor actor, Actor target)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var tTags = target.ComputeTags();

		return InteractionDefs.All
			.Where(d =>
				!d.Hidden &&
				CheckRequired(tags, caps, d) &&
				CheckTargetRequired(tTags, d.TargetRequired, target.Faction))
			.ToList();
	}

	/// <summary>获取可对地形格使用的技能（EffectType=dig 等）。</summary>
	public static List<InteractionDef> GetCellSkills(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		return InteractionDefs.All
			.Where(d => !d.Hidden && d.EffectType == "dig" && CheckRequired(tags, caps, d))
			.ToList();
	}

	/// <summary>
	/// 获取攻击类技能（combat 分类中 EffectType 为攻击/毒/吸取的）。
	/// 替代原 CombatModule.GetAttackActions。
	/// </summary>
	public static List<InteractionDef> GetAttackSkills(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		return InteractionDefs.All
			.Where(d =>
				d.EffectType is "melee_attack" or "poison_attack" or "drain_attack" &&
				CheckRequired(tags, caps, d))
			.ToList();
	}

	// ── 内部工具 ─────────────────────────────────────────

	private static bool CheckRequired(Dictionary<string, int> tags,
		Dictionary<string, float> caps, InteractionDef def)
	{
		foreach (var (key, val) in def.Required)
			if (tags.GetValueOrDefault(key, 0) < val) return false;
		foreach (var (key, val) in def.CapacityRequired)
			if (caps.GetValueOrDefault(key, 0f) < val) return false;
		return true;
	}

	private static bool CheckTargetRequired(Dictionary<string, int> tags,
		Dictionary<string, int> requirements, string faction)
	{
		foreach (var (key, val) in requirements)
		{
			if (key.StartsWith("@faction:"))
			{
				if (faction != key["@faction:".Length..]) return false;
			}
			else if (key.StartsWith("@max:"))
			{
				var parts = key["@max:".Length..].Split(':');
				if (parts.Length != 2 || !int.TryParse(parts[1], out var maxVal)) return false;
				if (tags.GetValueOrDefault(parts[0], 0) > maxVal) return false;
			}
			else
			{
				if (tags.GetValueOrDefault(key, 0) < val) return false;
			}
		}
		return true;
	}
}
