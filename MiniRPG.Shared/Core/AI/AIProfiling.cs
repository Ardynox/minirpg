using System.Collections.Generic;
using System.Diagnostics;
using MiniRPG.Core.Health;
using MiniRPG.Core.Data;
using MiniRPG.Core.Social;

namespace MiniRPG.Core.AI;

public sealed class AIDispatchMetrics
{
	public AIVisionMetrics Vision { get; init; } = new();
	public double ElapsedMs { get; init; }
	public double AwarenessMs { get; init; }
	public double BrainDecideMs { get; init; }
	public double DecisionExecuteMs { get; init; }
}

public sealed class AIDispatchResult
{
	public List<GameEvent> Events { get; init; } = [];
	public AIDispatchMetrics Metrics { get; init; } = new();
}

public sealed class TurnTickMetrics
{
	public double TickMs { get; init; }
	public double AdvanceWorldMs { get; init; }
	public double AIDispatchMs { get; init; }
	public AIDispatchMetrics AIDispatch { get; init; } = new();
}

public sealed class TurnTickResult
{
	public List<GameEvent> Events { get; init; } = [];
	public TurnTickMetrics Metrics { get; init; } = new();
}

public sealed class AIBehaviorContext
{
	private readonly GameState _state;
	private readonly Dictionary<(string ActorId, int Radius), bool> _nearbyThreatCache = new();
	private readonly Dictionary<(string ActorId, int X, int Y, int Z), EnvironmentExposureSnapshot> _exposureCache = new();

	public AIBehaviorContext(GameState state)
	{
		_state = state;
	}

	/// <summary>Optional social graph used by relationship-based input resolvers.</summary>
	public RelationshipModule? Relationships { get; init; }

	/// <summary>Optional per-actor memory store used by memory-based input resolvers.</summary>
	public ActorMemoryModule? ActorMemories { get; init; }

	/// <summary>Optional rumor bus used by rumor-based input resolvers.</summary>
	public RumorBus? Rumors { get; init; }

	public bool HasNearbyThreat(Actor actor, int radius)
	{
		var cacheKey = (actor.Id, radius);
		if (_nearbyThreatCache.TryGetValue(cacheKey, out var cached))
			return cached;

		for (var y = actor.Y - radius; y <= actor.Y + radius; y++)
		{
			for (var x = actor.X - radius; x <= actor.X + radius; x++)
			{
				if (Math.Abs(x - actor.X) + Math.Abs(y - actor.Y) > radius)
					continue;

				foreach (var other in ActorModule.GetAllAt(_state, x, y, actor.Z))
				{
					if (other.Id == actor.Id || CombatModule.IsDead(other))
						continue;
					if (!FactionRelation.IsHostile(actor.Faction, other.Faction))
						continue;

					_nearbyThreatCache[cacheKey] = true;
					return true;
				}
			}
		}

		_nearbyThreatCache[cacheKey] = false;
		return false;
	}

	public EnvironmentExposureSnapshot GetCurrentExposure(Actor actor) =>
		GetExposure(actor, actor.X, actor.Y, actor.Z);

	public EnvironmentExposureSnapshot GetExposure(Actor actor, int x, int y, int z)
	{
		var cacheKey = (actor.Id, x, y, z);
		if (_exposureCache.TryGetValue(cacheKey, out var cached))
			return cached;

		var exposure = DefaultEnvironmentExposureProvider.Instance.Capture(_state, actor, x, y, z);
		_exposureCache[cacheKey] = exposure;
		return exposure;
	}
}

internal static class ProfilingClock
{
	public static long Start() => Stopwatch.GetTimestamp();

	public static double ElapsedMs(long startTimestamp) =>
		(Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
}
