using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MiniRPG.Core;

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

	/// <summary>材质 ID，对应 MaterialRegistry 中的定义。影响硬度、可燃性等物理属性。</summary>
	public string Material { get; set; } = "flesh";

	/// <summary>对各能力的权重贡献（0.0~1.0），实际贡献 = 权重 * 耐久比例。</summary>
	public Dictionary<string, float> Capacities { get; set; } = new();

	/// <summary>非能力类标记（要害、亡灵、毒性等布尔/枚举标记）。</summary>
	public Dictionary<string, int> Tags { get; set; } = new();

	public Dictionary<string, int> GetTags() => Tags;
}

/// <summary>种族：固定 tag 来源，创建时确定。</summary>
public class Race : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
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

// ── JSON 多态支持 ─────────────────────────────────────

/// <summary>
/// System.Text.Json 需要知道 ITagSource 的具体类型才能正确反序列化。
/// 用 JsonDerivedType 标注所有已知实现。
/// </summary>
[JsonDerivedType(typeof(Limb), "limb")]
[JsonDerivedType(typeof(Race), "race")]
[JsonDerivedType(typeof(Profession), "profession")]
[JsonDerivedType(typeof(Buff), "buff")]
[JsonDerivedType(typeof(Experience), "experience")]
public interface ITagSourcePolymorphic : ITagSource;
