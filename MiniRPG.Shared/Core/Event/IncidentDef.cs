using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MiniRPG.Core.Event;

/// <summary>
/// 事件定义：描述一个可触发的世界事件（袭击、商队、疾病、流浪者等）。
/// 数据驱动，可从 JSON 加载。
/// </summary>
public class IncidentDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	[JsonPropertyName("category")]
	public IncidentCategory Category { get; set; } = IncidentCategory.Neutral;

	/// <summary>事件权重（越高越容易被选中）。</summary>
	[JsonPropertyName("weight")]
	public float Weight { get; set; } = 1f;

	/// <summary>最早可触发的回合数。</summary>
	[JsonPropertyName("minTurn")]
	public int MinTurn { get; set; }

	/// <summary>触发后的冷却回合数。100 turn = 10 游戏内小时。（240 turn/day 校准；旧默认 50）</summary>
	[JsonPropertyName("cooldownTurns")]
	public int CooldownTurns { get; set; } = 100;

	/// <summary>最少需要多少队伍成员才能触发。</summary>
	[JsonPropertyName("minPartySize")]
	public int MinPartySize { get; set; }

	/// <summary>威胁点数（用于袭击规模计算）。0 = 非威胁事件。</summary>
	[JsonPropertyName("threatPoints")]
	public float ThreatPoints { get; set; }

	/// <summary>事件执行器 ID（对应 IncidentWorker 注册表）。</summary>
	[JsonPropertyName("workerId")]
	public string WorkerId { get; set; } = "";

	/// <summary>额外参数（事件特定数据）。</summary>
	[JsonPropertyName("params")]
	public Dictionary<string, string> Params { get; set; } = [];
}

public enum IncidentCategory
{
	/// <summary>威胁事件：袭击、疾病、天灾。</summary>
	Threat,

	/// <summary>中性事件：商队到访、旅行者路过。</summary>
	Neutral,

	/// <summary>正面事件：流浪者加入、资源掉落。</summary>
	Positive,
}
