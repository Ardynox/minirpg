using System.Collections.Generic;

namespace MiniRPG.Core.Combat;

public enum SkillTargetType
{
	Self,
	Actor,
	Cell,
	Item,
}

public enum SkillCastFailureReason
{
	UnknownSkill,
	Unavailable,
	Unsupported,
	Cooldown,
	InvalidTargetType,
	MissingTarget,
	InvalidTarget,
	OutOfRange,
	NoLineOfSight,
	InvalidTerrain,
	InvalidTerrainMaterial,
}

public sealed class ActionExecutionResult
{
	public List<GameEvent> Events { get; } = [];
	public bool Consumed { get; set; }
}
