using MiniRPG.Core.Data;

namespace MiniRPG.Core.AI.Utility;

public sealed class UtilityEvalResult
{
	public static readonly UtilityEvalResult None = new() { Score = -1f };

	public UtilityActionDef? Action { get; init; }
	public float Score { get; init; }
	public Actor? TargetActor { get; init; }
	public WorkTicket? TargetTicket { get; init; }
	public (int X, int Y)? TargetPos { get; init; }
	public int? TargetZ { get; init; }
	public string? SkillId { get; init; }
}
