using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using MiniRPG.Core.Genetics;

namespace MiniRPG.Core.Data;

public enum AwarenessState
{
	Idle,
	Suspicious,
	Alerted,
	Searching,
}

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
	public string TemplateId { get; set; } = "";

	/// <summary>朝向 (dx, dy)：最近一次移动的方向。默认朝南。</summary>
	public int FacingX { get; set; }
	public int FacingY { get; set; } = 1;

	/// <summary>阵营：普通状态字段，随时可改，和种族无关。</summary>
	public string Faction { get; set; } = Factions.Hostile;

	/// <summary>AI 大脑类型 ID。null = 无 AI（玩家）。"simple" = SimpleBrain。</summary>
	public string? BrainId { get; set; }
	public string PrimaryDomainId { get; set; } = DomainIds.Public;
	public HashSet<string> AccessibleDomainIds { get; set; } = new(StringComparer.Ordinal);
	public string WorkBrainId { get; set; } = "";
	public string CurrentWorkTicketId { get; set; } = "";
	public AwarenessState AwarenessState { get; set; }
	public bool HasHomePosition { get; set; }
	public int HomeX { get; set; }
	public int HomeY { get; set; }
	public int HomeZ { get; set; }
	public string? AlertTargetActorId { get; set; }
	public int LastKnownTargetX { get; set; }
	public int LastKnownTargetY { get; set; }
	public int LastKnownTargetZ { get; set; }
	public int StateTurns { get; set; }
	public int SearchTurnsRemaining { get; set; }

	// ── 经济 / 背包 ─────────────────────────────────────

	public int Gold { get; set; }

	/// <summary>背包：玩家持有的物品列表（事实源；网格占位由 <see cref="GridInventories"/> 索引）。</summary>
	public List<Item> Inventory { get; set; } = [];

	/// <summary>
	/// 网格背包：每个键是一个容器 id（"pockets" 或 <see cref="GridInventory.MakeEquippedId"/>
	/// 之类的前缀化 id），值是该容器内物品占格情况。
	/// 由 <c>InventoryModule</c> 在 Add/Remove/Equip/Unequip 时同步；<see cref="Inventory"/>
	/// 是 truth，本字典只是空间索引，UI / 协议层用其驱动拖拽与占位渲染。
	/// </summary>
	public Dictionary<string, GridInventory> GridInventories { get; set; } = new(StringComparer.Ordinal);

	/// <summary>商人货架：非商人此列表为空。</summary>
	public List<ShopSlot> ShopSlots { get; set; } = [];

	/// <summary>
	/// 捏脸数据（外观）。可空：null 时按 <see cref="FaceCustomizationData.CreateDefault"/> 渲染。
	/// 写入路径：① CharacterCreation 确认时由 <c>PlayerCreationOptions.FaceCustomization</c> 透传给玩家 actor；
	/// ② NPC 在 <c>PresetDB.SpawnActor</c> 末尾按 instanceId hash 调
	///    <see cref="FaceCustomizationData.CreateRandom"/> 派生稳定随机外观；
	/// ③ <c>SaveModule</c> 双向 round-trip。
	/// </summary>
	public FaceCustomizationData? FaceCustomization { get; set; }

	// ── tag 来源 ─────────────────────────────────────────

	/// <summary>肢体列表：单独存，方便增删改查。也会自动注册到 TagSources。</summary>
	public List<Limb> Limbs { get; set; } = [];

	public Race? Race { get; set; }
	public Profession? Profession { get; set; }
	public List<Buff> Buffs { get; set; } = [];
	public List<Experience> Experiences { get; set; } = [];
	public Dictionary<string, int> SkillCooldowns { get; set; } = new(StringComparer.Ordinal);

	// ── 对话状态 ─────────────────────────────────────────

	public float DialogMood { get; set; }
	public float DialogAffinity { get; set; }
	public List<string> DialogMemory { get; set; } = [];
	public int DialogTalkCount { get; set; }
	public Dictionary<string, float> DialogPersonality { get; set; } = new();
	public Dictionary<string, float> DialogNeeds { get; set; } = new();
	public Dictionary<string, NeedState> Needs { get; set; } = new(StringComparer.Ordinal);
	public List<ThoughtState> Thoughts { get; set; } = [];
	public float MoodValue { get; set; } = 50f;
	public int NeedsLastUpdatedTurn { get; set; }
	public MiniRPG.Core.Needs.MentalBreakState? MentalBreak { get; set; }
	public int LastMentalBreakTurn { get; set; }
	public List<HealthConditionState> HealthConditions { get; set; } = [];
	public float PainValue { get; set; }
	public float BloodLossValue { get; set; }
	public float WetnessValue { get; set; }
	public int HealthLastUpdatedTurn { get; set; }

	// ── 复活历史 ────────────────────────────────────────
	/// <summary>
	/// 这个角色已经被复活过的次数。0 表示从未复活；每次 <c>ReviveService.TryRevive</c>
	/// 成功后 +1，作为 <see cref="MiniRPG.Core.Revival.RevivalCostModel"/> 的输入，
	/// 让代价 / 失败概率 / 永久死亡阈值随次数累加。
	/// </summary>
	public int RevivalCount { get; set; }

	// ── 人口学 / 基因 / 家系 ────────────────────────────

	/// <summary>生物性别。受孕 / 出生流程必读；UI 显示。</summary>
	public Sex Sex { get; set; } = Sex.Female;

	/// <summary>角色出生时的 <see cref="GameState.Turn"/>。-1 表示 legacy 老存档没记录，按常量年龄兜底。</summary>
	public int BirthTurn { get; set; } = -1;

	/// <summary>怀孕剩余 turn 数。null 表示未怀孕。每日 tick -1，到 0 触发出生。仅 Female 有意义。</summary>
	public int? PregnancyTicksRemaining { get; set; }

	/// <summary>当前配偶 actor id（爱人/夫妻/伴侣）。null = 单身。</summary>
	public string? MateActorId { get; set; }

	/// <summary>母亲 actor id（被亲属关系系统使用，写入后不应再改）。</summary>
	public string? MotherActorId { get; set; }

	/// <summary>父亲 actor id（被亲属关系系统使用，写入后不应再改）。</summary>
	public string? FatherActorId { get; set; }

	/// <summary>
	/// 基因池（孟德尔位点 → 等位基因对）。null 时不贡献 tag。
	/// 由 <c>GeneSpawnService.CreateForRace</c> 在 spawn 时填，由
	/// <c>GeneInheritanceService.Inherit</c> 在出生时填给孩子。
	/// 通过 <see cref="GenePool.GetTags"/> 自动接入 <see cref="ComputeTags"/>。
	/// </summary>
	public GenePool? Genome { get; set; }

	/// <summary>
	/// 上一次 <c>LifeStageTransitionService</c> 解析过的生命阶段。用于侦测 day boundary
	/// 上的阶段跨越（Infant→Child / Adult→Elder 等）并 emit lifestage_transition 事件。
	/// 默认 Adult，避免新建 NPC 默认走"刚出生"路径。
	/// </summary>
	public LifeStage LastResolvedLifeStage { get; set; } = LifeStage.Adult;

	/// <summary>谁正抱着我。null = 没人抱。仅 Infant 阶段有意义。</summary>
	public string? CarriedByActorId { get; set; }

	/// <summary>我正抱着谁。null = 没抱孩子。母亲在 InfantCarryModule.Sync 时被设。</summary>
	public string? CarriedInfantId { get; set; }

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
		if (Genome != null) Merge(Genome);
		foreach (var buff in Buffs) Merge(buff);
		foreach (var exp in Experiences) Merge(exp);
		foreach (var item in Inventory) if (item.Equipped) Merge(item);

		return tags;
	}

	/// <summary>查询单个 tag 的当前值，不存在返回 0。</summary>
	public int GetTag(string key) => ComputeTags().GetValueOrDefault(key, 0);

	public int GetSkillCooldown(string skillId)
	{
		if (string.IsNullOrEmpty(skillId))
			return 0;

		return SkillCooldowns.GetValueOrDefault(skillId, 0);
	}

	public bool IsSkillOnCooldown(string skillId) => GetSkillCooldown(skillId) > 0;

	public void StartSkillCooldown(string skillId, int remainingActions)
	{
		if (string.IsNullOrEmpty(skillId))
			return;

		if (remainingActions <= 0)
		{
			SkillCooldowns.Remove(skillId);
			return;
		}

		SkillCooldowns[skillId] = remainingActions;
	}

	public void TickSkillCooldowns()
	{
		if (SkillCooldowns.Count == 0)
			return;

		foreach (var skillId in SkillCooldowns.Keys.ToList())
		{
			var remaining = SkillCooldowns[skillId] - 1;
			if (remaining <= 0)
				SkillCooldowns.Remove(skillId);
			else
				SkillCooldowns[skillId] = remaining;
		}
	}

	public void SetHomePosition(int x, int y, int z)
	{
		HasHomePosition = true;
		HomeX = x;
		HomeY = y;
		HomeZ = z;
	}

	public void RememberAlertTarget(Actor target)
	{
		AlertTargetActorId = target.Id;
		LastKnownTargetX = target.X;
		LastKnownTargetY = target.Y;
		LastKnownTargetZ = target.Z;
	}

	public void ClearAlertTarget()
	{
		AlertTargetActorId = null;
		LastKnownTargetX = 0;
		LastKnownTargetY = 0;
		LastKnownTargetZ = 0;
		SearchTurnsRemaining = 0;
	}

	// ── 能力(Capacity)计算 ──────────────────────────────

	/// <summary>
	/// 两轮计算能力值。
	/// 第一轮：基础值 = Σ(肢体权重 × 耐久比例)。
	/// 第二轮：最终值 = 基础值 × Π(依赖能力的基础值)。
	/// 返回值域 0.0~1.0+，表示能力百分比。
	/// </summary>
	public bool CanAccessDomain(string domainId)
	{
		if (string.IsNullOrWhiteSpace(domainId))
			return true;

		if (string.Equals(domainId, DomainIds.Public, StringComparison.Ordinal))
			return true;

		if (string.Equals(PrimaryDomainId, domainId, StringComparison.Ordinal))
			return true;

		return AccessibleDomainIds.Contains(domainId);
	}

	// ── 能力缓存（脏标记）──────────────────────────────
	// 肢体耐久、健康/需求状态、Buff 变化时需调 InvalidateCapacityCache()。
	[JsonIgnore] private Dictionary<string, float>? _capacitiesCache;
	[JsonIgnore] private bool _capacitiesDirty = true;

	// 生命状态缓存：与能力缓存共用脏标记。由 CombatModule.CheckVitalStatus 填充，
	// 消除 PickReadyActor 热路径中每格重复计算 vital status 的成本。
	[JsonIgnore] private string? _vitalStatusCache;
	[JsonIgnore] private bool _vitalStatusCacheSet;

	public void InvalidateCapacityCache()
	{
		_capacitiesDirty = true;
		_vitalStatusCacheSet = false;
	}

	/// <summary>获取缓存的 vital status（若已被 CheckVitalStatus 填充且未失效），否则返回 null 标记未命中。</summary>
	[JsonIgnore]
	public bool HasCachedVitalStatus => _vitalStatusCacheSet && !_capacitiesDirty;

	[JsonIgnore]
	public string? CachedVitalStatus => _vitalStatusCache;

	public void SetCachedVitalStatus(string? status)
	{
		_vitalStatusCache = status;
		_vitalStatusCacheSet = true;
	}

	public Dictionary<string, float> ComputeCapacities()
	{
		if (!_capacitiesDirty && _capacitiesCache != null)
			return _capacitiesCache;

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
			multiplier *= NeedSystem.GetCapacityMultiplier(this, capId);
			multiplier *= HealthSystem.GetCapacityMultiplier(this, capId);
			final[capId] = baseVal * multiplier;
		}
		_capacitiesCache = final;
		_capacitiesDirty = false;
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
	// 度量衡口径：1 weight = 1 kg；MaxCarryWeight = manipulation × 40 → 满 manipulation 最多扛 ~40 kg。
	// 详见 Docs/开发约定.md「度量衡口径」节。

	/// <summary>当前携带总重量（kg）。</summary>
	[JsonIgnore]
	public float CarryWeight => Inventory.Sum(i => i.EffectiveWeight);

	/// <summary>最大负重（kg）= manipulation × 40。满 manipulation 即 ~40 kg 现实搬运极限。</summary>
	[JsonIgnore]
	public float MaxCarryWeight => GetCapacity(Caps.Manipulation) * 40f;

	/// <summary>是否超重。</summary>
	[JsonIgnore]
	public bool IsOverweight => CarryWeight > MaxCarryWeight;

	// ── 装备槽查询 ──────────────────────────────────────

	/// <summary>获取所有肢体上的装备槽（扁平列表）。</summary>
	[JsonIgnore]
	public IEnumerable<EquipSlot> AllEquipSlots => Limbs.SelectMany(l => l.EquipSlots);

	private static readonly IReadOnlyDictionary<string, float> InsulationWeights = new Dictionary<string, float>(StringComparer.Ordinal)
	{
		[BodyParts.Torso] = 0.35f,
		[BodyParts.Head] = 0.20f,
		[BodyParts.Leg] = 0.18f,
		[BodyParts.Foot] = 0.10f,
		[BodyParts.Arm] = 0.10f,
		[BodyParts.Hand] = 0.07f,
	};

	/// <summary>查找匹配 bodyPart + layer 的空闲装备槽。</summary>
	public EquipSlot? FindFreeSlot(string bodyPart, EquipLayer layer)
		=> AllEquipSlots.FirstOrDefault(s =>
			s.BodyPart == bodyPart && s.Layer == layer && s.ItemId == null);

	/// <summary>查找装备了指定物品 ID 的装备槽。</summary>
	public EquipSlot? FindSlotByItemId(string itemId)
		=> AllEquipSlots.FirstOrDefault(s => s.ItemId == itemId);

	/// <summary>获取指定身体部位上所有已装备物品。</summary>
	public IEnumerable<Item> GetEquippedItemsCovering(string bodyPart)
		=> Inventory.Where(i => i.Equipped && CoversBodyPart(i, bodyPart));

	/// <summary>计算指定身体部位的总锐伤抗性。</summary>
	public float GetSharpArmorFor(string bodyPart)
		=> GetEquippedItemsCovering(bodyPart).Sum(i => i.SharpArmor);

	/// <summary>计算指定身体部位的总钝伤抗性。</summary>
	public float GetBluntArmorFor(string bodyPart)
		=> GetEquippedItemsCovering(bodyPart).Sum(i => i.BluntArmor);

	public float GetColdInsulation() =>
		GetWeightedProtection(static item => item.ColdInsulation);

	public float GetHeatInsulation() =>
		GetWeightedProtection(static item => item.HeatInsulation);

	public float GetWaterproofing() =>
		GetWeightedProtection(static item => item.Waterproofing);

	public float GetPortableWarmthC() =>
		Inventory.Where(static item => item.Equipped && item.PortableWarmthC > 0f)
			.Sum(static item => item.PortableWarmthC);

	public float GetPortableDryingBonus() =>
		Inventory.Where(static item => item.Equipped && item.PortableDryingBonus > 0f)
			.Sum(static item => item.PortableDryingBonus);

	// ── 肢体操作 ─────────────────────────────────────────

	public void AttachLimb(Limb limb) { Limbs.Add(limb); _capacitiesDirty = true; }

	public void DetachLimb(Limb limb) { Limbs.Remove(limb); _capacitiesDirty = true; }

	public void DetachLimb(string limbId) { Limbs.RemoveAll(l => l.Id == limbId); _capacitiesDirty = true; }

	// ── Buff 操作 ────────────────────────────────────────

	public void AddBuff(Buff buff) { Buffs.Add(buff); _capacitiesDirty = true; }

	public void RemoveBuff(string buffId) { Buffs.RemoveAll(b => b.Id == buffId); _capacitiesDirty = true; }

	/// <summary>回合结束时调用：Buff 计时器 -1，到期自动移除。</summary>
	public void TickBuffs()
	{
		var hadExpired = false;
		for (var i = Buffs.Count - 1; i >= 0; i--)
		{
			if (Buffs[i].RemainingTurns < 0) continue;
			Buffs[i].RemainingTurns--;
			if (Buffs[i].RemainingTurns <= 0)
			{
				Buffs.RemoveAt(i);
				hadExpired = true;
			}
		}
		if (hadExpired) _capacitiesDirty = true;
	}

	private static bool CoversBodyPart(Item item, string bodyPart)
	{
		if (item.CoveredParts.Count > 0)
			return item.CoveredParts.Any(part => string.Equals(part, bodyPart, StringComparison.Ordinal));

		return item.IsEquippable && string.Equals(item.BodyPart, bodyPart, StringComparison.Ordinal);
	}

	private float GetWeightedProtection(Func<Item, float> selector)
	{
		var totalWeight = 0f;
		var weightedValue = 0f;
		foreach (var bodyPart in Limbs
			.Select(static limb => limb.BodyPart)
			.Where(static part => !string.IsNullOrWhiteSpace(part))
			.Distinct(StringComparer.Ordinal))
		{
			if (!InsulationWeights.TryGetValue(bodyPart, out var weight) || weight <= 0f)
				continue;

			var partValue = GetEquippedItemsCovering(bodyPart).Sum(selector);
			weightedValue += partValue * weight;
			totalWeight += weight;
		}

		if (totalWeight <= 0f)
			return 0f;

		return Item.ClampProtection(weightedValue / totalWeight);
	}
}
