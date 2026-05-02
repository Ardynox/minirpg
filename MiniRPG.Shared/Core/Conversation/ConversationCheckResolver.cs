using System;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Conversation;

/// <summary>
/// BG3 风格技能检定结果：1d20 + ability/skill modifier vs DC，
/// nat 1 必败 / nat 20 必胜。修正值从 actor tag 表里取
/// （<c>check_&lt;ability&gt;_bonus</c> + <c>check_&lt;skill&gt;_bonus</c>）。
/// </summary>
public readonly record struct ConversationCheckResult(bool Passed, int Roll, int Modifier, int Total);

/// <summary>
/// 对话内技能检定的统一入口。Resolve 用 RNG 抽 1d20，EstimateSuccessPercent
/// 给 UI 提示用，不消耗 RNG。
/// </summary>
public static class ConversationCheckResolver
{
	private const int CritFail = 1;
	private const int CritSuccess = 20;
	private const int MinSuccessPercent = 5;
	private const int MaxSuccessPercent = 95;

	public static ConversationCheckResult Resolve(GameState state, Actor player, SkillCheckDef check, Random rng)
	{
		var roll = rng.Next(1, 21);
		var modifier = ComputeModifier(state, player, check);
		var total = roll + modifier;
		var passed = roll == CritSuccess || (roll != CritFail && total >= check.Dc);
		return new ConversationCheckResult(passed, roll, modifier, total);
	}

	public static int EstimateSuccessPercent(GameState state, Actor player, SkillCheckDef check, Random rng)
	{
		_ = rng;
		var modifier = ComputeModifier(state, player, check);
		var minRollNeeded = Math.Clamp(check.Dc - modifier, 2, 20);
		var successCount = 21 - minRollNeeded;
		return Math.Clamp(successCount * 5, MinSuccessPercent, MaxSuccessPercent);
	}

	private static int ComputeModifier(GameState state, Actor player, SkillCheckDef check)
	{
		_ = state;
		var modifier = 0;
		var tags = player.ComputeTags();
		if (!string.IsNullOrWhiteSpace(check.Ability)
			&& tags.TryGetValue($"check_{check.Ability.ToLowerInvariant()}_bonus", out var abilityBonus))
			modifier += abilityBonus;
		if (!string.IsNullOrWhiteSpace(check.Skill)
			&& tags.TryGetValue($"check_{check.Skill.ToLowerInvariant()}_bonus", out var skillBonus))
			modifier += skillBonus;
		return modifier;
	}
}
