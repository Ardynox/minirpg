using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Data;

/// <summary>
/// 生物实体。所有生物统一用这一个类，没有子类。
/// 能力由 tag 表决定，tag 表从所有 TagSource 实时计算得出。
/// </summary>
public class Actor
{
	public string Id { get; set; } = "";
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }
	public string Glyph { get; set; } = "?";
	public string DisplayName { get; set; } = "";

	/// <summary>朝向 (dx, dy)：最近一次移动的方向。默认朝南。</summary>
	public int FacingX { get; set; }
	public int FacingY { get; set; } = 1;

	/// <summary>阵营：普通状态字段，随时可改，和种族无关。</summary>
	public string Faction { get; set; } = Factions.Hostile;

	/// <summary>AI 大脑类型 ID。null = 无 AI（玩家）。"simple" = SimpleBrain。</summary>
	public string? BrainId { get; set; }

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

	// ── 对话状态 ─────────────────────────────────────────

	public float DialogMood { get; set; }
	public float DialogAffinity { get; set; }
	public List<string> DialogMemory { get; set; } = [];
	public int DialogTalkCount { get; set; }
	public Dictionary<string, float> DialogPersonality { get; set; } = new();
	public Dictionary<string, float> DialogNeeds { get; set; } = new();

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

	// ── 能力(Capacity)计算 ──────────────────────────────

	/// <summary>
	/// 两轮计算能力值。
	/// 第一轮：基础值 = Σ(肢体权重 × 耐久比例)。
	/// 第二轮：最终值 = 基础值 × Π(依赖能力的基础值)。
	/// 返回值域 0.0~1.0+，表示能力百分比。
	/// </summary>
	public Dictionary<string, float> ComputeCapacities()
	{
		var baseValues = new Dictionary<string, float>();
		foreach (var limb in Limbs)
		{
			var ratio = limb.MaxDurability > 0
				? (float)limb.Durability / limb.MaxDurability : 0f;
			foreach (var (capId, weight) in limb.Capacities)
				baseValues[capId] = baseValues.GetValueOrDefault(capId) + weight * ratio;
		}

		var final = new Dictionary<string, float>();
		foreach (var (capId, baseVal) in baseValues)
		{
			var multiplier = 1.0f;
			var def = PresetDB.GetCapacity(capId);
			if (def != null)
				foreach (var depId in def.Multipliers)
					multiplier *= baseValues.GetValueOrDefault(depId, 1.0f);
			final[capId] = baseVal * multiplier;
		}
		return final;
	}

	/// <summary>查询单个能力的当前值，不存在返回 0。</summary>
	public float GetCapacity(string capId) => ComputeCapacities().GetValueOrDefault(capId);

	/// <summary>
	/// 计算贡献指定能力的肢体的加权平均材质硬度。
	/// 权重 = 肢体对该能力的贡献 × 耐久比例。
	/// </summary>
	public float GetLimbHardness(string capacityId)
	{
		float totalWeight = 0f;
		float totalHardness = 0f;
		foreach (var limb in Limbs)
		{
			if (!limb.Capacities.TryGetValue(capacityId, out var weight) || weight <= 0f)
				continue;
			var ratio = limb.MaxDurability > 0 ? (float)limb.Durability / limb.MaxDurability : 0f;
			var w = weight * ratio;
			var mat = MaterialRegistry.Get(limb.Material);
			totalHardness += mat.Hardness * w;
			totalWeight += w;
		}
		return totalWeight > 0f ? totalHardness / totalWeight : 0f;
	}

	// ── 负重 ────────────────────────────────────────────

	/// <summary>当前携带总重量。</summary>
	[JsonIgnore]
	public float CarryWeight => Inventory.Sum(i => i.EffectiveWeight);

	/// <summary>最大负重 = manipulation × 40。</summary>
	[JsonIgnore]
	public float MaxCarryWeight => GetCapacity(Caps.Manipulation) * 40f;

	/// <summary>是否超重。</summary>
	[JsonIgnore]
	public bool IsOverweight => CarryWeight > MaxCarryWeight;

	// ── 装备槽查询 ──────────────────────────────────────

	/// <summary>获取所有肢体上的装备槽（扁平列表）。</summary>
	[JsonIgnore]
	public IEnumerable<EquipSlot> AllEquipSlots => Limbs.SelectMany(l => l.EquipSlots);

	/// <summary>查找匹配 bodyPart + layer 的空闲装备槽。</summary>
	public EquipSlot? FindFreeSlot(string bodyPart, EquipLayer layer)
		=> AllEquipSlots.FirstOrDefault(s =>
			s.BodyPart == bodyPart && s.Layer == layer && s.ItemId == null);

	/// <summary>查找装备了指定物品 ID 的装备槽。</summary>
	public EquipSlot? FindSlotByItemId(string itemId)
		=> AllEquipSlots.FirstOrDefault(s => s.ItemId == itemId);

	/// <summary>获取指定身体部位上所有已装备物品。</summary>
	public IEnumerable<Item> GetEquippedItemsCovering(string bodyPart)
		=> Inventory.Where(i => i.Equipped && i.CoveredParts.Contains(bodyPart));

	/// <summary>计算指定身体部位的总锐伤抗性。</summary>
	public float GetSharpArmorFor(string bodyPart)
		=> GetEquippedItemsCovering(bodyPart).Sum(i => i.SharpArmor);

	/// <summary>计算指定身体部位的总钝伤抗性。</summary>
	public float GetBluntArmorFor(string bodyPart)
		=> GetEquippedItemsCovering(bodyPart).Sum(i => i.BluntArmor);

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
