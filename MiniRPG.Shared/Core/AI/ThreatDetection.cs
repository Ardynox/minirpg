using System;
using MiniRPG.Core.Combat;

namespace MiniRPG.Core.AI;

public static class ThreatDetection
{
	private const int DefaultRadius = 6;

	public static bool HasNearbyThreat(GameState state, Actor actor, int radius = DefaultRadius) =>
		HasNearbyThreat(state, actor, behaviorContext: null, radius);

	public static bool HasNearbyThreat(GameState state, Actor actor, AIBehaviorContext? behaviorContext, int radius = DefaultRadius)
	{
		if (behaviorContext != null)
			return behaviorContext.HasNearbyThreat(actor, radius);

		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				if (Math.Abs(x - actor.X) + Math.Abs(y - actor.Y) > radius)
					continue;

				foreach (var other in ActorModule.GetAllAt(state, x, y, actor.Z))
				{
					if (other.Id == actor.Id || CombatModule.IsDead(other))
						continue;
					if (!FactionRelation.IsHostile(actor.Faction, other.Faction))
						continue;

					return true;
				}
			}
		}

		return false;
	}
}
