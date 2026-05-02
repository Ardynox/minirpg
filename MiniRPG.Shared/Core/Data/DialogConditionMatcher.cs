using System.Linq;

namespace MiniRPG.Core.Data;

/// <summary>Shared tag matching for dialog conditions (legacy DialogRuleEngine + conversation branches).</summary>
public static class DialogConditionMatcher
{
	public static bool Matches(DialogContext ctx, DialogCondition? cond)
	{
		if (cond == null)
			return true;

		if (cond.RequiresTags != null)
		{
			foreach (var tag in cond.RequiresTags)
			{
				if (string.IsNullOrWhiteSpace(tag))
					continue;
				if (ctx.BoolTags.Contains(tag))
					continue;
				if (ctx.NumTags.TryGetValue(tag, out var v) && v > 0f)
					continue;
				return false;
			}
		}

		if (cond.ForbidsTags != null)
		{
			foreach (var tag in cond.ForbidsTags)
			{
				if (string.IsNullOrWhiteSpace(tag))
					continue;
				if (ctx.BoolTags.Contains(tag))
					return false;
				if (ctx.NumTags.TryGetValue(tag, out var v) && v > 0f)
					return false;
			}
		}

		if (cond.MinTags != null)
		{
			foreach (var (k, min) in cond.MinTags)
			{
				if (!ctx.NumTags.TryGetValue(k, out var v) || v < min)
					return false;
			}
		}

		if (cond.MaxTags != null)
		{
			foreach (var (k, max) in cond.MaxTags)
			{
				if (!ctx.NumTags.TryGetValue(k, out var v) || v > max)
					return false;
			}
		}

		return true;
	}
}
