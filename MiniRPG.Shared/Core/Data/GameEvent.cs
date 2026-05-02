using System.Collections.Generic;

namespace MiniRPG.Core.Data;

public sealed class KinGriefTarget
{
	public string SurvivorId { get; set; } = "";
	public string Relation { get; set; } = "";
}

/// <summary>
/// 游戏事件：逻辑层产出，副作用层（渲染/日志/音效）消费。
/// </summary>
public class GameEvent
{
	public string Type { get; }
	public int TargetX { get; set; }
	public int TargetY { get; set; }
	public string? InitiatorActorName { get; set; }
	public string? TargetActorName { get; set; }
	public string? InitiatorId { get; set; }
	public string? TargetId { get; set; }
	public string? InitiatorActorTypeId { get; set; }
	public string? TargetActorTypeId { get; set; }
	public string? InitiatorFaction { get; set; }
	public string? TargetFaction { get; set; }

	// ── 交互事件专用 ──
	public string? InteractionDefId { get; set; }
	public string? InteractionName { get; set; }
	public string? EffectType { get; set; }

	// ── 物品事件专用 ──
	/// <summary>物品名称（拾取/丢弃事件使用）。</summary>
	public string? ItemName { get; set; }
	public string? ItemTypeId { get; set; }
	public string? ItemCategory { get; set; }

	// ── 战斗事件专用 ──
	public int Damage { get; set; }
	public int CooldownRemaining { get; set; }
	public string? LimbName { get; set; }
	public string? ActionName { get; set; }
	public int SourceX { get; set; }
	public int SourceY { get; set; }
	public int SourceZ { get; set; }
	public string? DamageType { get; set; }
	public string? SkillId { get; set; }
	public string? FailureReason { get; set; }
	public int TargetZ { get; set; }
	public string? WeatherTypeId { get; set; }
	public string? WeatherIntensityId { get; set; }

	/// <summary>Trust delta for <see cref="Type"/> = relationship_adjust (Initiator→Target).</summary>
	public float RelationshipTrustDelta { get; set; }

	// ── 对话（节点图）────────────────────────────────────
	public string? ConversationDefId { get; set; }
	public string? ConversationNodeId { get; set; }
	public int ConversationBranchIndex { get; set; } = -1;
	public string? ConversationPayload { get; set; }

	public List<KinGriefTarget>? KinGriefTargets { get; set; }

	public GameEvent(string type)
	{
		Type = type;
	}
}
