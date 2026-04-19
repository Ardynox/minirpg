using System;
using System.Text;
using MiniRPG.Core.Config;

namespace MiniRPG.Module.Panel;

/// <summary>
/// 技能详情 BBCode 公共构建器。给 <c>SkillBarModule</c> / <c>SkillManagerModule</c> /
/// 任何要 hover tooltip 的技能控件共享同一份文案，避免详情面板和 tooltip 双份逻辑漂移。
///
/// <para>
/// 输出结构（与原 <c>SkillManagerModule.RenderDetail</c> 一致）：
/// 名称（按分类着色） / 分类标签 / 状态行（armed/cooldown/ready）/
/// power / cooldown / range / damageType / effectType / 描述 /
/// 需求与容量需求 / 来源（装备 / 天生）。
/// </para>
/// </summary>
public static class SkillTooltipBuilder
{
	public static string BuildSkillDetailBbcode(
		InteractionDef skill,
		Actor? actor,
		GameState? state,
		string? armedSkillId)
	{
		var cooldownRemaining = actor?.GetSkillCooldown(skill.Id) ?? 0;
		var statusKey = string.Equals(skill.Id, armedSkillId, StringComparison.Ordinal)
			? "ui.skill.detail.status.armed"
			: cooldownRemaining > 0
				? "ui.skill.detail.status.cooldown"
				: "ui.skill.detail.status.ready";

		var color = skill.Category switch
		{
			"combat" => "#ff6666",
			"utility" => "#66ccff",
			"social" => "#66ff88",
			_ => "#cccccc",
		};
		var categoryLabel = GameLocalizer.LocalizeSkillCategory(skill.Category);

		var sb = new StringBuilder();
		sb.AppendLine($"[b][color={color}]{skill.Name}[/color][/b]");
		sb.AppendLine($"[color=#888888]{LocalizationService.T("ui.skill.detail.category_line", ("category", categoryLabel))}[/color]");
		sb.AppendLine();
		sb.AppendLine($"[color=#ffdd88]{LocalizationService.T(statusKey, ("value", cooldownRemaining))}[/color]");
		sb.AppendLine();

		if (skill.Power > 0)
			sb.AppendLine($"[color=#ffcc00]{LocalizationService.T("ui.skill.detail.power", ("value", skill.Power))}[/color]");
		if (skill.Cooldown > 0)
			sb.AppendLine($"[color=#aaaaaa]{LocalizationService.T("ui.skill.detail.cooldown", ("value", skill.Cooldown))}[/color]");
		sb.AppendLine($"[color=#aaaaaa]{LocalizationService.T("ui.skill.detail.range", ("value", GameLocalizer.LocalizeRange(skill.Range)))}[/color]");

		if (!string.IsNullOrEmpty(skill.DamageType))
			sb.AppendLine($"[color=#cc8866]{LocalizationService.T("ui.skill.detail.damage_type", ("value", GameLocalizer.LocalizeDamageType(skill.DamageType)))}[/color]");
		if (!string.IsNullOrEmpty(skill.EffectType))
			sb.AppendLine($"[color=#aaaaaa]{LocalizationService.T("ui.skill.detail.effect", ("value", GameLocalizer.LocalizeEffectType(skill.EffectType)))}[/color]");

		sb.AppendLine();
		if (!string.IsNullOrEmpty(skill.Description))
		{
			sb.AppendLine(skill.Description);
			sb.AppendLine();
		}

		if (skill.Required.Count > 0)
		{
			sb.AppendLine($"[color=#ffcc00]{LocalizationService.T("ui.skill.detail.requirements")}[/color]");
			foreach (var (key, value) in skill.Required)
				sb.AppendLine($"  [color=#aaaaaa]{GameLocalizer.LocalizeTagKey(key)} >= {value}[/color]");
		}

		if (skill.CapacityRequired.Count > 0)
		{
			sb.AppendLine($"[color=#ffcc00]{LocalizationService.T("ui.skill.detail.capacity_requirements")}[/color]");
			foreach (var (key, value) in skill.CapacityRequired)
			{
				var def = PresetDB.GetCapacity(key);
				var name = GameLocalizer.LocalizeCapacityName(key, def?.Name ?? key);
				sb.AppendLine($"  [color=#aaaaaa]{name} >= {value * 100:F0}%[/color]");
			}
		}

		var source = GetSkillSourceLabel(skill, actor, state);
		if (!string.IsNullOrEmpty(source))
		{
			sb.AppendLine();
			sb.AppendLine($"[color=#666666]{LocalizationService.T("ui.skill.detail.source", ("value", source))}[/color]");
		}

		return sb.ToString();
	}

	private static string GetSkillSourceLabel(InteractionDef skill, Actor? actor, GameState? state)
	{
		if (actor == null)
			return string.Empty;

		foreach (var item in actor.Inventory)
		{
			if (item.Equipped && item.GrantedSkills.Contains(skill.Id))
				return LocalizationService.T("ui.skill.source.equipment", ("item", ItemFormatHelper.GetDisplayName(state, item)));
		}

		return LocalizationService.T("ui.skill.source.innate");
	}
}
