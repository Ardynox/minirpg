using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Data;

// ── ITagSource: 任何能贡献 tag 的东西 ──────────────────

/// <summary>
/// tag 来源接口。肢体、种族、职业、Buff、经历……
/// 任何能贡献 tag 的对象都实现此接口。
/// </summary>
public interface ITagSource
{
	Dictionary<string, int> GetTags();
}

// ── 具体来源 ──────────────────────────────────────────

/// <summary>肢体/器官：可挂载/移除/替换。耐久比例影响能力(Capacity)贡献。</summary>
public class Limb : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public int MaxDurability { get; set; } = 5;
	public int Durability { get; set; } = 5;
	public int PermanentDamage { get; set; }

	/// <summary>材质 ID，对应 MaterialRegistry 中的定义。影响硬度、可燃性等物理属性。</summary>
	public string Material { get; set; } = "flesh";

	/// <summary>身体部位 ID（head/torso/arm/hand/leg/foot），决定该肢体能装什么。</summary>
	public string BodyPart { get; set; } = "";

	/// <summary>该肢体提供的装备层列表（如人类手臂提供 Skin+Middle+Shell）。</summary>
	public List<EquipLayer> EquipLayers { get; set; } = [];

	/// <summary>运行时装备槽，由 EquipLayers 在 Actor 初始化时生成。</summary>
	public List<EquipSlot> EquipSlots { get; set; } = [];

	/// <summary>对各能力的权重贡献（0.0~1.0），实际贡献 = 权重 * 耐久比例。</summary>
	public Dictionary<string, float> Capacities { get; set; } = new();

	/// <summary>非能力类标记（要害、亡灵、毒性等布尔/枚举标记）。</summary>
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;

	/// <summary>根据 EquipLayers 初始化 EquipSlots（仅当 EquipSlots 为空时）。</summary>
	public void InitEquipSlots()
	{
		if (EquipSlots.Count > 0 || EquipLayers.Count == 0) return;
		foreach (var layer in EquipLayers)
			EquipSlots.Add(new EquipSlot { LimbId = Id, BodyPart = BodyPart, Layer = layer });
	}
}

/// <summary>
/// 装备槽：绑定到具体肢体的某一装备层。
/// 一个肢体可以有多个装备槽（对应不同层级）。
/// </summary>
public class EquipSlot
{
	/// <summary>所属肢体 ID。</summary>
	public string LimbId { get; set; } = "";
	/// <summary>身体部位（冗余，方便查询）。</summary>
	public string BodyPart { get; set; } = "";
	/// <summary>装备层。</summary>
	public EquipLayer Layer { get; set; }
	/// <summary>已装备物品的 ID（对应 Actor.Inventory 中的 Item.Id），null = 空槽。</summary>
	public string? ItemId { get; set; }
}

/// <summary>种族：固定 tag 来源，创建时确定。</summary>
public class Race : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public string NeedProfileId { get; set; } = "";
	public string HealthProfileId { get; set; } = "";
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;
}

/// <summary>职业：可在游戏中转职，改变 tag 贡献。</summary>
public class Profession : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;
}

/// <summary>Buff/Debuff：有持续回合数，到期自动移除。</summary>
public class Buff : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public int RemainingTurns { get; set; } = -1;
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;
}

/// <summary>经历/成就：永久性 tag 来源，记录生物的历史。</summary>
public class Experience : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;
}

