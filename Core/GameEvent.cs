namespace MiniRPG.Core;

/// <summary>
/// 游戏事件：逻辑层产出，副作用层（渲染/日志/音效）消费。
/// </summary>
public class GameEvent
{
	public string Type { get; }
	public int TargetX { get; set; }
	public int TargetY { get; set; }
	public string? TargetActorName { get; set; }
	public string? InitiatorId { get; set; }
	public string? TargetId { get; set; }

	// ── 交互事件专用 ──
	public string? InteractionDefId { get; set; }
	public string? InteractionName { get; set; }
	public string? EffectType { get; set; }

	// ── 战斗事件专用 ──
	public int Damage { get; set; }
	public string? LimbName { get; set; }
	public string? ActionName { get; set; }

	public GameEvent(string type)
	{
		Type = type;
	}
}
