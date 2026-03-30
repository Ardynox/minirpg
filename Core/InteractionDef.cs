using System.Collections.Generic;

namespace MiniRPG.Core;

/// <summary>
/// 交互定义：动作系统的双向版本。
/// Required = 发起者 tag 条件，TargetRequired = 目标条件。
/// 条件 key 约定：普通 key = tag 最低值，"@faction:X" = 阵营匹配，"@max:tag:N" = tag 上限。
/// </summary>
public class InteractionDef
{
	public string Id { get; set; } = "";
	public string Name { get; set; } = "";
	public Dictionary<string, int> Required { get; set; } = new();
	public Dictionary<string, int> TargetRequired { get; set; } = new();
	public string EffectType { get; set; } = "";
	public int Power { get; set; }
}
