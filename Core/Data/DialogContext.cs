using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.Data;

/// <summary>
/// 对话上下文快照。所有维度扁平化为两张表：
///   - BoolTags: 存在性标签（如 "prof:merchant", "race:human", "memory:met_elder", "has_weapon"）
///   - NumTags:  数值标签（如 "affinity"=30, "mood"=0.3, "gold"=50, "talk_count"=2, "p:greedy"=0.7）
/// 规则引擎只看这两张表，不关心数据来源。
/// </summary>
public class DialogContext
{
	public Actor Player { get; set; } = null!;
	public Actor Npc { get; set; } = null!;

	public HashSet<string> BoolTags { get; set; } = [];
	public Dictionary<string, float> NumTags { get; set; } = new();

	public static DialogContext Build(GameState state, Actor player, Actor npc)
	{
		var ctx = new DialogContext { Player = player, Npc = npc };

		// ── 数值维度 ──────────────────────────────────────
		ctx.NumTags["affinity"] = npc.DialogAffinity;
		ctx.NumTags["mood"] = npc.DialogMood;
		ctx.NumTags["talk_count"] = npc.DialogTalkCount;
		ctx.NumTags["gold"] = player.Gold;
		ctx.NumTags["npc_gold"] = npc.Gold;
		ctx.NumTags["floor"] = state.PlayerZ;
		ctx.NumTags["turn"] = state.Turn;
		ctx.NumTags["kill_count"] = state.KillCount;

		var enemyCount = 0;
		foreach (var a in state.Actors.Values)
			if (a.Faction == Factions.Hostile
				&& System.Math.Abs(a.X - player.X) <= 5
				&& System.Math.Abs(a.Y - player.Y) <= 5
				&& a.Z == player.Z)
				enemyCount++;
		ctx.NumTags["nearby_enemies"] = enemyCount;

		// 性格特质 → p:xxx
		foreach (var (k, v) in npc.DialogPersonality)
			ctx.NumTags[$"p:{k}"] = v;

		// 需求 → need:xxx
		foreach (var (k, v) in npc.DialogNeeds)
			ctx.NumTags[$"need:{k}"] = v;

		// ── 存在性标签 ────────────────────────────────────
		if (npc.Profession?.Id is { } profId)
			ctx.BoolTags.Add($"prof:{profId}");
		if (npc.Race?.Id is { } raceId)
			ctx.BoolTags.Add($"race:{raceId}");
		ctx.BoolTags.Add($"faction:{npc.Faction}");

		foreach (var mem in npc.DialogMemory)
			ctx.BoolTags.Add($"memory:{mem}");

		// 玩家装备感知
		var hasWeapon = false;
		var hasArmor = false;
		foreach (var item in player.Inventory)
		{
			if (!item.Equipped) continue;
			if (item.Category == ItemCategories.Weapon) hasWeapon = true;
			if (item.Category == ItemCategories.Armor) hasArmor = true;
		}
		if (hasWeapon) ctx.BoolTags.Add("player_has_weapon");
		if (hasArmor) ctx.BoolTags.Add("player_has_armor");
		if (player.Gold >= 100) ctx.BoolTags.Add("player_rich");
		if (player.Gold <= 5) ctx.BoolTags.Add("player_poor");
		if (enemyCount > 0) ctx.BoolTags.Add("danger_nearby");
		if (state.KillCount >= 10) ctx.BoolTags.Add("veteran");
		if (state.KillCount >= 50) ctx.BoolTags.Add("legendary");
		if (npc.DialogTalkCount == 0) ctx.BoolTags.Add("first_meet");
		if (npc.DialogAffinity >= 50) ctx.BoolTags.Add("is_friend");
		if (npc.DialogAffinity >= 80) ctx.BoolTags.Add("is_close_friend");
		if (npc.DialogMood < -0.3f) ctx.BoolTags.Add("npc_angry");
		if (npc.DialogMood > 0.5f) ctx.BoolTags.Add("npc_happy");
		if (npc.ShopSlots.Count > 0) ctx.BoolTags.Add("is_shopkeeper");

		return ctx;
	}
}
