using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;

namespace MiniRPG.Core.Data;

/// <summary>
/// Skill queries built on top of the shared InteractionDef list.
/// </summary>
public static class SkillQuery
{
	public static bool CanUse(Actor actor, InteractionDef skill)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var granted = GetGrantedSkillIds(actor);
		return RequiresGrantedSkill(skill)
			? granted.Contains(skill.Id)
			: CheckRequired(tags, caps, skill) || granted.Contains(skill.Id);
	}

	public static bool CanUseAgainst(Actor actor, Actor target, InteractionDef skill)
	{
		if (!CanUse(actor, skill))
			return false;
		if (skill.EffectType == "tend")
			return !FactionRelation.IsHostile(actor.Faction, target.Faction);

		return CheckTargetRequired(target.ComputeTags(), skill.TargetRequired, target.Faction);
	}

	public static bool IsAttackSkill(InteractionDef skill) =>
		skill.EffectType is "melee_attack" or "poison_attack" or "drain_attack" or "heavy_attack" or "ranged_attack";

	public static bool IsSocialSkill(InteractionDef skill) =>
		skill.EffectType is "talk" or "trade" or "tame" || skill.Category == "social";

	public static bool IsUnifiedCastSkill(InteractionDef skill) =>
		!IsSocialSkill(skill)
		&& skill.EffectType is
			"melee_attack" or
			"heavy_attack" or
			"poison_attack" or
			"drain_attack" or
			"ranged_attack" or
			"reload" or
			"operate" or
			"block" or
			"dig" or
			"identify" or
			"tend" or
			"light_fire" or
			"extinguish_fire";

	public static List<InteractionDef> GetSkills(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var granted = GetGrantedSkillIds(actor);
		return InteractionDefs.All
			.Where(def => !def.Hidden && CanUseFromRequirements(def, tags, caps, granted))
			.ToList();
	}

	public static List<InteractionDef> GetByCategory(Actor actor, string category)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var granted = GetGrantedSkillIds(actor);
		return InteractionDefs.All
			.Where(def => !def.Hidden && def.Category == category
				&& CanUseFromRequirements(def, tags, caps, granted))
			.ToList();
	}

	public static List<InteractionDef> GetAll(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var granted = GetGrantedSkillIds(actor);
		return InteractionDefs.All
			.Where(def => CanUseFromRequirements(def, tags, caps, granted))
			.ToList();
	}

	public static List<InteractionDef> GetUsableAgainst(Actor actor, Actor target)
		=> InteractionDefs.All
			.Where(def => !def.Hidden && CanUseAgainst(actor, target, def))
			.ToList();

	public static List<InteractionDef> GetCellSkills(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var granted = GetGrantedSkillIds(actor);
		return InteractionDefs.All
			.Where(def => !def.Hidden && def.EffectType is "dig" or "light_fire" or "extinguish_fire"
				&& CanUseFromRequirements(def, tags, caps, granted))
			.ToList();
	}

	public static List<InteractionDef> GetAttackSkills(Actor actor)
	{
		var tags = actor.ComputeTags();
		var caps = actor.ComputeCapacities();
		var granted = GetGrantedSkillIds(actor);
		return InteractionDefs.All
			.Where(def =>
				IsAttackSkill(def) &&
				CanUseFromRequirements(def, tags, caps, granted))
			.ToList();
	}

	private static HashSet<string> GetGrantedSkillIds(Actor actor)
	{
		var ids = new HashSet<string>();
		if (string.Equals(actor.TemplateId, Factions.Player, StringComparison.Ordinal))
			ids.Add("identify");

		foreach (var item in actor.Inventory)
		{
			if (!item.Equipped || item.GrantedSkills.Count == 0)
				continue;

			foreach (var skillId in item.GrantedSkills)
				ids.Add(skillId);
		}

		return ids;
	}

	private static bool CanUseFromRequirements(
		InteractionDef def,
		Dictionary<string, int> tags,
		Dictionary<string, float> caps,
		HashSet<string> granted)
	{
		if (RequiresGrantedSkill(def))
			return granted.Contains(def.Id);

		return CheckRequired(tags, caps, def) || granted.Contains(def.Id);
	}

	private static bool RequiresGrantedSkill(InteractionDef def)
		=> ExplicitGrantOnlySkillIds.Contains(def.Id);

	private static readonly HashSet<string> ExplicitGrantOnlySkillIds = new(StringComparer.Ordinal)
	{
		"operate",
		"reload_firearm",
		"revolver_shot",
		"bolt_rifle_shot",
		"shotgun_blast",
	};

	private static bool CheckRequired(Dictionary<string, int> tags, Dictionary<string, float> caps, InteractionDef def)
	{
		foreach (var (key, val) in def.Required)
		{
			if (tags.GetValueOrDefault(key, 0) < val)
				return false;
		}

		foreach (var (key, val) in def.CapacityRequired)
		{
			if (caps.GetValueOrDefault(key, 0f) < val)
				return false;
		}

		return true;
	}

	private static bool CheckTargetRequired(Dictionary<string, int> tags, Dictionary<string, int> requirements, string faction)
	{
		foreach (var (key, val) in requirements)
		{
			if (key.StartsWith("@faction:"))
			{
				if (faction != key["@faction:".Length..])
					return false;
			}
			else if (key.StartsWith("@max:"))
			{
				var parts = key["@max:".Length..].Split(':');
				if (parts.Length != 2 || !int.TryParse(parts[1], out var maxVal))
					return false;
				if (tags.GetValueOrDefault(parts[0], 0) > maxVal)
					return false;
			}
			else if (tags.GetValueOrDefault(key, 0) < val)
			{
				return false;
			}
		}

		return true;
	}
}
