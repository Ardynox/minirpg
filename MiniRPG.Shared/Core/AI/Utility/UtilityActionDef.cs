using System.Collections.Generic;

namespace MiniRPG.Core.AI.Utility;

public sealed class UtilityActionDef
{
	public string Id { get; set; } = "";
	public string Executor { get; set; } = "";
	public float BonusScore { get; set; } = 0.5f;
	public List<ConsiderationDef> Considerations { get; set; } = [];
	public List<string> Tags { get; set; } = [];
	public bool RequiresTarget { get; set; }
	public string TargetType { get; set; } = "";
	public int CooldownTurns { get; set; }
}
