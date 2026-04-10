using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MiniRPG.Core.Config;

namespace MiniRPG.Core.Dialog;

/// <summary>
/// 模板渲染器。支持三类变量：
///   {name}       -- 内置命名变量（player_name, npc_name, mood_greeting 等）
///   {num:key}    -- 从 NumTags 读取数值（如 {num:affinity} → "30"）
///   {pick:pool}  -- 从语气词库中按性格/情绪随机选一个短语
/// 未识别的变量原样保留。
/// </summary>
public static class TemplateRenderer
{
	private static readonly Regex VarPattern = new(@"\{([\w:]+)\}", RegexOptions.Compiled);
	private static Random _rng = new();

	public static string Render(string template, DialogContext ctx, Random? rng = null)
	{
		_rng = rng ?? new Random();
		return VarPattern.Replace(template, match =>
		{
			var key = match.Groups[1].Value;
			return ResolveVariable(key, ctx);
		});
	}

	private static string ResolveVariable(string key, DialogContext ctx)
	{
		if (key.StartsWith("num:"))
		{
			var tagKey = key[4..];
			return ctx.NumTags.TryGetValue(tagKey, out var v) ? FormatNum(v) : "0";
		}

		if (key.StartsWith("pick:"))
		{
			var pool = key[5..];
			return PickFromPool(pool, ctx);
		}

		return key switch
		{
			"player_name" => ctx.Player.DisplayName,
			"npc_name" => ctx.Npc.DisplayName,
			"gold" => ctx.Player.Gold.ToString(),
			"npc_gold" => ctx.Npc.Gold.ToString(),
			"floor" => FormatNum(ctx.NumTags.GetValueOrDefault("floor")),
			"kill_count" => FormatNum(ctx.NumTags.GetValueOrDefault("kill_count")),
			"talk_count" => FormatNum(ctx.NumTags.GetValueOrDefault("talk_count")),
			"enemy_count" => FormatNum(ctx.NumTags.GetValueOrDefault("nearby_enemies")),
			"affinity_level" => GetAffinityLevel(ctx.NumTags.GetValueOrDefault("affinity")),
			"mood_desc" => GetMoodDesc(ctx.NumTags.GetValueOrDefault("mood")),
			"mood_greeting" => GetMoodGreeting(ctx.NumTags.GetValueOrDefault("mood")),
			"tone" => GetTone(ctx),
			"filler" => GetFiller(ctx),
			"self_ref" => GetSelfRef(ctx),
			"player_ref" => GetPlayerRef(ctx),
			_ => $"{{{key}}}",
		};
	}

	private static string FormatNum(float v) =>
		v == (int)v ? ((int)v).ToString() : v.ToString("F1");

	// ── 好感度/情绪描述 ──────────────────────────────────

	private static string GetAffinityLevel(float a) => a switch
	{
		< 10 => LocalizationService.T("dialog.affinity.stranger"),
		< 30 => LocalizationService.T("dialog.affinity.acquaintance"),
		< 60 => LocalizationService.T("dialog.affinity.friend"),
		< 85 => LocalizationService.T("dialog.affinity.close_friend"),
		_ => LocalizationService.T("dialog.affinity.dearest"),
	};

	private static string GetMoodDesc(float m) => m switch
	{
		< -0.6f => LocalizationService.T("dialog.mood_desc.furious"),
		< -0.3f => LocalizationService.T("dialog.mood_desc.irritated"),
		< 0f => LocalizationService.T("dialog.mood_desc.unhappy"),
		< 0.3f => LocalizationService.T("dialog.mood_desc.calm"),
		< 0.6f => LocalizationService.T("dialog.mood_desc.pleased"),
		_ => LocalizationService.T("dialog.mood_desc.elated"),
	};

	private static string GetMoodGreeting(float m) => m switch
	{
		< -0.3f => LocalizationService.T("dialog.mood_greeting.bad"),
		< 0f => LocalizationService.T("dialog.mood_greeting.low"),
		< 0.3f => LocalizationService.T("dialog.mood_greeting.neutral"),
		< 0.6f => LocalizationService.T("dialog.mood_greeting.good"),
		_ => LocalizationService.T("dialog.mood_greeting.great"),
	};

	// ── 性格驱动的语气系统 ────────────────────────────────

	private static string GetTone(DialogContext ctx) =>
		PickFromPool("tone", ctx);

	private static string GetFiller(DialogContext ctx) =>
		PickFromPool("filler", ctx);

	private static string GetSelfRef(DialogContext ctx)
	{
		if (HasTrait(ctx, "greedy", 0.5f)) return LocalizationService.T("dialog.self_ref.greedy");
		if (HasTrait(ctx, "wise", 0.5f)) return LocalizationService.T("dialog.self_ref.wise");
		if (HasTrait(ctx, "proud", 0.5f)) return LocalizationService.T("dialog.self_ref.proud");
		if (HasTrait(ctx, "timid", 0.5f)) return LocalizationService.T("dialog.self_ref.timid");
		return LocalizationService.T("dialog.self_ref.default");
	}

	private static string GetPlayerRef(DialogContext ctx)
	{
		var aff = ctx.NumTags.GetValueOrDefault("affinity");
		if (aff >= 60) return LocalizationService.T("dialog.player_ref.old_friend");
		if (aff >= 30) return LocalizationService.T("dialog.player_ref.friend");
		if (HasTrait(ctx, "cautious", 0.5f)) return LocalizationService.T("dialog.player_ref.outsider");
		if (HasTrait(ctx, "friendly", 0.6f)) return LocalizationService.T("dialog.player_ref.traveler");
		return LocalizationService.T("dialog.player_ref.default");
	}

	private static string PickFromPool(string pool, DialogContext ctx)
	{
		var dominant = GetDominantTrait(ctx);
		var mood = ctx.NumTags.GetValueOrDefault("mood");

		var candidates = GetPoolPhrases(pool, dominant, mood);
		return candidates[_rng.Next(candidates.Length)];
	}

	private static string[] GetPoolPhrases(string pool, string trait, float mood)
	{
		var fallback = (pool, trait) switch
		{
			("tone", "greedy") => "嘿嘿，|做生意嘛，|钱嘛，|",
			("tone", "wise") => "年轻人，|且听我说，|以我的经验，|",
			("tone", "timid") => "那、那个，|如果你不介意……|抱、抱歉，|",
			("tone", "friendly") => "朋友！|来来来，|哈哈，|",
			("tone", "cautious") => "嗯……|让我想想，|你确定？|",
			("tone", "proud") => "哼，|本大爷告诉你，|听好了，|",
			("tone", "curious") => "哦？|有意思，|我听说……|",
			("tone", _) when mood < -0.3f => "……|哼，|",
			("tone", _) when mood > 0.5f => "哈哈，|嘿！|",
			("tone", _) => "||嗯，",

			("filler", "greedy") => "这可不便宜啊|有钱好办事|利润才是王道",
			("filler", "wise") => "我年轻的时候也是这样|岁月教会了我很多|这世上没有白走的路",
			("filler", "timid") => "希望不会出什么事|好、好害怕|没事吧……",
			("filler", "friendly") => "有什么需要尽管说|我们是朋友嘛|别客气",
			("filler", "cautious") => "小心驶得万年船|还是谨慎些好|不要掉以轻心",
			("filler", "proud") => "这种小事不在话下|没有我办不到的|哼，简单",
			("filler", "curious") => "真想去看看|你见过什么有趣的东西吗|这个世界真奇妙",
			("filler", _) => "是这样的|嗯|话说回来",

			_ => "",
		};

		var key = pool switch
		{
			"tone" => $"dialog.pool.tone.{ResolvePoolKey(trait, mood)}",
			"filler" => $"dialog.pool.filler.{(string.IsNullOrWhiteSpace(trait) ? "default" : trait)}",
			_ => $"dialog.pool.{pool}.{trait}",
		};
		return LocalizationService.GetList(key, fallback);
	}

	private static string ResolvePoolKey(string trait, float mood)
	{
		if (!string.IsNullOrWhiteSpace(trait))
			return trait;
		if (mood < -0.3f)
			return "bad_mood";
		if (mood > 0.5f)
			return "good_mood";
		return "default";
	}

	private static string GetDominantTrait(DialogContext ctx)
	{
		string best = "";
		float bestVal = 0;
		foreach (var (k, v) in ctx.NumTags)
		{
			if (!k.StartsWith("p:")) continue;
			if (v > bestVal) { bestVal = v; best = k[2..]; }
		}
		return best;
	}

	private static bool HasTrait(DialogContext ctx, string trait, float min) =>
		ctx.NumTags.TryGetValue($"p:{trait}", out var v) && v >= min;
}
