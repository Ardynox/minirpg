using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
		< 10 => "陌生人",
		< 30 => "熟人",
		< 60 => "朋友",
		< 85 => "挚友",
		_ => "至交",
	};

	private static string GetMoodDesc(float m) => m switch
	{
		< -0.6f => "暴怒",
		< -0.3f => "烦躁",
		< 0f => "不悦",
		< 0.3f => "平静",
		< 0.6f => "愉悦",
		_ => "兴高采烈",
	};

	private static string GetMoodGreeting(float m) => m switch
	{
		< -0.3f => "……什么事？",
		< 0f => "嗯？",
		< 0.3f => "你好。",
		< 0.6f => "你好啊！",
		_ => "哈哈，见到你真高兴！",
	};

	// ── 性格驱动的语气系统 ────────────────────────────────

	private static string GetTone(DialogContext ctx) =>
		PickFromPool("tone", ctx);

	private static string GetFiller(DialogContext ctx) =>
		PickFromPool("filler", ctx);

	private static string GetSelfRef(DialogContext ctx)
	{
		if (HasTrait(ctx, "greedy", 0.5f)) return "本商人";
		if (HasTrait(ctx, "wise", 0.5f)) return "老夫";
		if (HasTrait(ctx, "proud", 0.5f)) return "本大爷";
		if (HasTrait(ctx, "timid", 0.5f)) return "小、小的";
		return "我";
	}

	private static string GetPlayerRef(DialogContext ctx)
	{
		var aff = ctx.NumTags.GetValueOrDefault("affinity");
		if (aff >= 60) return "老朋友";
		if (aff >= 30) return "朋友";
		if (HasTrait(ctx, "cautious", 0.5f)) return "外来人";
		if (HasTrait(ctx, "friendly", 0.6f)) return "旅行者";
		return "你";
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
		return (pool, trait) switch
		{
			("tone", "greedy") => ["嘿嘿，", "做生意嘛，", "钱嘛，", ""],
			("tone", "wise") => ["年轻人，", "且听我说，", "以我的经验，", ""],
			("tone", "timid") => ["那、那个，", "如果你不介意……", "抱、抱歉，", ""],
			("tone", "friendly") => ["朋友！", "来来来，", "哈哈，", ""],
			("tone", "cautious") => ["嗯……", "让我想想，", "你确定？", ""],
			("tone", "proud") => ["哼，", "本大爷告诉你，", "听好了，", ""],
			("tone", "curious") => ["哦？", "有意思，", "我听说……", ""],
			("tone", _) when mood < -0.3f => ["……", "哼，", ""],
			("tone", _) when mood > 0.5f => ["哈哈，", "嘿！", ""],
			("tone", _) => ["", "", "嗯，"],

			("filler", "greedy") => ["这可不便宜啊", "有钱好办事", "利润才是王道"],
			("filler", "wise") => ["我年轻的时候也是这样", "岁月教会了我很多", "这世上没有白走的路"],
			("filler", "timid") => ["希望不会出什么事", "好、好害怕", "没事吧……"],
			("filler", "friendly") => ["有什么需要尽管说", "我们是朋友嘛", "别客气"],
			("filler", "cautious") => ["小心驶得万年船", "还是谨慎些好", "不要掉以轻心"],
			("filler", "proud") => ["这种小事不在话下", "没有我办不到的", "哼，简单"],
			("filler", "curious") => ["真想去看看", "你见过什么有趣的东西吗", "这个世界真奇妙"],
			("filler", _) => ["是这样的", "嗯", "话说回来"],

			_ => [""],
		};
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
