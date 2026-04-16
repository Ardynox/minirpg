using System;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Events;

/// <summary>
/// In-memory counters that summarize recent authoritative events. This is
/// the first concrete <see cref="IGameEventConsequenceHandler"/> example
/// and the target consumer for future InputResolvers (for example an
/// "environment turbulence" signal that raises NPC caution bonus when the
/// cross-faction attack rate spikes).
/// </summary>
/// <remarks>
/// Kept deliberately flat: no district / chunk partitioning, no
/// serialization. The second-layer roadmap will either replace this with
/// a richer <c>DistrictTension</c> model or extend it with windowed
/// buckets; until then this only proves the router wiring works.
/// Counters are runtime-only and reset when <see cref="Reset"/> is called
/// (e.g. on session transitions).
/// </remarks>
public sealed class IncidentStatistics : IGameEventConsequenceHandler
{
	public long CrossFactionAttackCount { get; private set; }
	public long FriendlyFireAttackCount { get; private set; }
	public long FatalAttackCount { get; private set; }

	/// <summary>Drop all counters; intended for session transitions.</summary>
	public void Reset()
	{
		CrossFactionAttackCount = 0;
		FriendlyFireAttackCount = 0;
		FatalAttackCount = 0;
	}

	public void OnEvent(GameState state, GameEvent ev)
	{
		if (ev == null) return;

		if (string.Equals(ev.Type, "combat_attack", StringComparison.Ordinal))
			TrackCombatAttack(ev);
	}

	private void TrackCombatAttack(GameEvent ev)
	{
		if (!string.IsNullOrWhiteSpace(ev.InitiatorFaction) && !string.IsNullOrWhiteSpace(ev.TargetFaction))
		{
			if (FactionRelation.IsHostile(ev.InitiatorFaction, ev.TargetFaction))
				CrossFactionAttackCount++;
			else if (!string.Equals(ev.InitiatorFaction, ev.TargetFaction, StringComparison.Ordinal))
				FriendlyFireAttackCount++;
		}

		// Fatal hits are signalled by a separate "combat_death" event but we
		// approximate by looking at damage reported on the attack itself; if
		// the event stream ever stops carrying damage for killing blows this
		// counter stays at zero rather than guessing.
		if (ev.Damage >= 999)
			FatalAttackCount++;
	}
}
