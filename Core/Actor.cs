using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core;

/// <summary>
/// 生物实体。所有生物统一用这一个类，没有子类。
/// 能力由 tag 表决定，tag 表从所有 TagSource 实时计算得出。
/// </summary>
public class Actor
{
	public string Id { get; set; } = "";
	public int X { get; set; }
	public int Y { get; set; }
	public string Glyph { get; set; } = "?";
	public string DisplayName { get; set; } = "";

	/// <summary>阵营：普通状态字段，随时可改，和种族无关。</summary>
	public string Faction { get; set; } = "hostile";

	// ── 经济 / 背包 ─────────────────────────────────────

	public int Gold { get; set; }

	/// <summary>背包：玩家持有的物品列表。</summary>
	public List<Item> Inventory { get; set; } = [];

	/// <summary>商人货架：非商人此列表为空。</summary>
	public List<ShopSlot> ShopSlots { get; set; } = [];

	// ── tag 来源 ─────────────────────────────────────────

	/// <summary>肢体列表：单独存，方便增删改查。也会自动注册到 TagSources。</summary>
	public List<Limb> Limbs { get; set; } = [];

	public Race? Race { get; set; }
	public Profession? Profession { get; set; }
	public List<Buff> Buffs { get; set; } = [];
	public List<Experience> Experiences { get; set; } = [];

	// ── tag 表计算 ───────────────────────────────────────

	/// <summary>
	/// 实时计算 tag 表。不缓存——来源随时可能增减（挂肢体、加 Buff 等），
	/// 每次需要 tag 时现算，保证一致性。
	/// </summary>
	[JsonIgnore]
	public Dictionary<string, int> Tags => ComputeTags();

	public Dictionary<string, int> ComputeTags()
	{
		var tags = new Dictionary<string, int>();
		void Merge(ITagSource source)
		{
			foreach (var (key, val) in source.GetTags())
				tags[key] = tags.GetValueOrDefault(key, 0) + val;
		}

		foreach (var limb in Limbs) Merge(limb);
		if (Race != null) Merge(Race);
		if (Profession != null) Merge(Profession);
		foreach (var buff in Buffs) Merge(buff);
		foreach (var exp in Experiences) Merge(exp);
		foreach (var item in Inventory) if (item.Equipped) Merge(item);

		return tags;
	}

	/// <summary>查询单个 tag 的当前值，不存在返回 0。</summary>
	public int GetTag(string key) => ComputeTags().GetValueOrDefault(key, 0);

	// ── 肢体操作 ─────────────────────────────────────────

	public void AttachLimb(Limb limb) => Limbs.Add(limb);

	public void DetachLimb(Limb limb) => Limbs.Remove(limb);

	public void DetachLimb(string limbId) => Limbs.RemoveAll(l => l.Id == limbId);

	// ── Buff 操作 ────────────────────────────────────────

	public void AddBuff(Buff buff) => Buffs.Add(buff);

	public void RemoveBuff(string buffId) => Buffs.RemoveAll(b => b.Id == buffId);

	/// <summary>回合结束时调用：Buff 计时器 -1，到期自动移除。</summary>
	public void TickBuffs()
	{
		for (var i = Buffs.Count - 1; i >= 0; i--)
		{
			if (Buffs[i].RemainingTurns < 0) continue;
			Buffs[i].RemainingTurns--;
			if (Buffs[i].RemainingTurns <= 0)
				Buffs.RemoveAt(i);
		}
	}
}
