using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MiniRPG.Core;

/// <summary>
/// 能力定义：数据驱动的生物能力系统。
/// 每个能力的实际值由肢体耐久比例加权求和，再乘以依赖能力的乘数。
/// </summary>
public class CapacityDef
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = "";

	[JsonPropertyName("name")]
	public string Name { get; set; } = "";

	/// <summary>
	/// 能力归零时的致命效果。
	/// null = 无致命效果, "death_instant" = 立即死亡,
	/// "incapacitate" = 昏迷, "death_slow" = 缓慢死亡
	/// </summary>
	[JsonPropertyName("vitalEffect")]
	public string? VitalEffect { get; set; }

	[JsonPropertyName("zeroThreshold")]
	public float ZeroThreshold { get; set; }

	/// <summary>依赖的其他能力 ID，最终值 = 基础值 * 各依赖能力值的乘积。</summary>
	[JsonPropertyName("multipliers")]
	public List<string> Multipliers { get; set; } = [];

	[JsonPropertyName("description")]
	public string Description { get; set; } = "";
}
