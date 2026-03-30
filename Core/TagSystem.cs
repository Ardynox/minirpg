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

/// <summary>肢体：可挂载/移除/替换，每个肢体贡献一组 tag。</summary>
public class Limb : ITagSource
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
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

// ── 动作定义 ──────────────────────────────────────────

/// <summary>
/// 动作定义：数据驱动。一组前置 tag 要求 + 效果描述。
/// 新增动作只需注册新定义，不改任何生物代码。
/// </summary>
public class ActionDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	/// <summary>前置 tag 要求：key = tag 名, value = 最低强度。</summary>
	public Dictionary<string, int> Required { get; set; } = new();
	/// <summary>效果类型，由事件系统消费（"melee_attack", "poison_spit"…）。</summary>
	public string EffectType { get; set; } = "";
	/// <summary>效果强度倍率，具体含义由 EffectType 决定。</summary>
	public int Power { get; set; }
}

// ── 动作查询 ──────────────────────────────────────────

public static class ActionQuery
{
	/// <summary>根据 Actor 当前 tag 表过滤出所有满足条件的动作。</summary>
	public static List<ActionDef> GetAvailable(Actor actor, IReadOnlyList<ActionDef> allActions)
	{
		var tags = actor.ComputeTags();
		return allActions
			.Where(a => a.Required.All(r => tags.GetValueOrDefault(r.Key, 0) >= r.Value))
			.ToList();
	}
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
