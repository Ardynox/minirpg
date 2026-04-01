using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.Dialog;

/// <summary>
/// 通用标签规则引擎。
/// 从候选列表中筛选所有条件满足的 DialogEntry，按 Priority 降序，同优先级随机选一个。
/// </summary>
public static class DialogRuleEngine
{
	public static DialogEntry? SelectEntry(DialogContext ctx, List<DialogEntry> candidates, Random rng)
	{
		var matched = candidates.Where(e => Matches(ctx, e.Condition)).ToList();
		if (matched.Count == 0) return null;

		var maxPriority = matched.Max(e => e.Priority);
		var top = matched.Where(e => e.Priority == maxPriority).ToList();
		return top[rng.Next(top.Count)];
	}

	public static List<DialogOption> FilterOptions(DialogContext ctx, List<DialogOption> options)
	{
		return options.Where(o => Matches(ctx, o.Condition)).ToList();
	}

	public static bool Matches(DialogContext ctx, DialogCondition? cond)
	{
		if (cond == null) return true;

		if (cond.RequiresTags != null)
			foreach (var tag in cond.RequiresTags)
				if (!ctx.BoolTags.Contains(tag)) return false;

		if (cond.ForbidsTags != null)
			foreach (var tag in cond.ForbidsTags)
				if (ctx.BoolTags.Contains(tag)) return false;

		if (cond.MinTags != null)
			foreach (var (key, min) in cond.MinTags)
				if (!ctx.NumTags.TryGetValue(key, out var val) || val < min) return false;

		if (cond.MaxTags != null)
			foreach (var (key, max) in cond.MaxTags)
				if (!ctx.NumTags.TryGetValue(key, out var val) || val > max) return false;

		return true;
	}
}
