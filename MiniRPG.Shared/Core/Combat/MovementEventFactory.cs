using MiniRPG.Core.Data;

namespace MiniRPG.Core.Combat;

internal static class MovementEventFactory
{
	public static GameEvent CreateActorMoved(
		Actor actor,
		int sourceX,
		int sourceY,
		int sourceZ,
		int targetX,
		int targetY) =>
		new("actor_moved")
		{
			InitiatorId = actor.Id,
			SourceX = sourceX,
			SourceY = sourceY,
			SourceZ = sourceZ,
			TargetX = targetX,
			TargetY = targetY,
			TargetZ = sourceZ,
		};

	public static GameEvent CreateActorClimbed(
		Actor actor,
		int sourceX,
		int sourceY,
		int sourceZ,
		int targetZ,
		int dz) =>
		new("actor_climbed")
		{
			InitiatorId = actor.Id,
			SourceX = sourceX,
			SourceY = sourceY,
			SourceZ = sourceZ,
			TargetX = sourceX,
			TargetY = sourceY,
			TargetZ = targetZ,
			Damage = dz,
		};
}
