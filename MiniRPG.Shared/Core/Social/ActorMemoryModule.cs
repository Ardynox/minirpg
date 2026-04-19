using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;

namespace MiniRPG.Core.Social;

/// <summary>
/// Kind of personal memory an actor keeps about another actor. Kept
/// narrow on purpose - new kinds land alongside the event rules that
/// produce them so both sides move together.
/// </summary>
public enum ActorMemoryKind
{
	/// <summary>Remembered as someone who attacked us.</summary>
	HostileAttackBy,
	/// <summary>Remembered as someone who owes us something.</summary>
	DebtOwedTo,
	/// <summary>Remembered as someone who helped us.</summary>
	KindnessReceived,
	/// <summary>Remembered as someone who betrayed us.</summary>
	BetrayalBy,
}

/// <summary>
/// A single memory entry owned by an observer about <see cref="SubjectId"/>.
/// <see cref="Strength"/> is unbounded; consumers decide how to clamp or
/// combine multiple entries of the same kind.
/// </summary>
public readonly record struct ActorMemory(
	string SubjectId,
	ActorMemoryKind Kind,
	float Strength);

/// <summary>
/// Per-actor bounded list of <see cref="ActorMemory"/> entries. Distinct
/// from <c>thoughts.json</c> which describes self-directed mood tags; this
/// module remembers "what I know about X". Data is authoritative-side and
/// keyed by actor id string so it survives small index shifts when the
/// actor roster mutates.
/// </summary>
/// <remarks>
/// Step 3 of <c>Docs/涌现世界路线图.md</c>. First revision is in-memory
/// only; session transitions call <see cref="Reset"/> to drop everything.
/// Persistence and decay-per-turn follow the same deferral policy as
/// <see cref="RelationshipModule"/>: add them when an InputResolver is
/// actually reading the list.
/// </remarks>
public sealed class ActorMemoryModule : IGameEventConsequenceHandler
{
	/// <summary>Per-actor cap; older entries get trimmed FIFO.</summary>
	public const int MaxMemoriesPerActor = 32;

	/// <summary>Strength of the memory written when a hostile attack lands.</summary>
	public const float HostileAttackMemoryStrength = 0.5f;

	private readonly Dictionary<string, List<ActorMemory>> _byActor
		= new(StringComparer.Ordinal);

	/// <summary>Total number of actors holding at least one memory entry.</summary>
	public int ActorsWithMemories => _byActor.Count;

	/// <summary>
	/// Read a copy of the memory list held by <paramref name="actorId"/>;
	/// returns empty when the actor has none.
	/// </summary>
	public IReadOnlyList<ActorMemory> GetMemories(string actorId)
	{
		if (string.IsNullOrWhiteSpace(actorId))
			return Array.Empty<ActorMemory>();
		return _byActor.TryGetValue(actorId, out var list)
			? list
			: (IReadOnlyList<ActorMemory>)Array.Empty<ActorMemory>();
	}

	/// <summary>
	/// Sum the <see cref="ActorMemory.Strength"/> of every entry held by
	/// <paramref name="observerId"/> about <paramref name="subjectId"/>
	/// whose <see cref="ActorMemory.Kind"/> matches <paramref name="kind"/>.
	/// Returns 0 when either id is blank or no matching entry exists; the
	/// returned magnitude is unbounded (callers clamp / soft-cap as needed).
	/// </summary>
	/// <remarks>
	/// Read-only query intended for AI <c>InputResolver</c>s. Does not
	/// touch the underlying list; safe to call repeatedly per turn.
	/// </remarks>
	public float SumStrength(string observerId, string subjectId, ActorMemoryKind kind)
	{
		if (string.IsNullOrWhiteSpace(observerId) || string.IsNullOrWhiteSpace(subjectId))
			return 0f;
		if (!_byActor.TryGetValue(observerId, out var list) || list.Count == 0)
			return 0f;

		var total = 0f;
		for (var i = 0; i < list.Count; i++)
		{
			var entry = list[i];
			if (entry.Kind != kind) continue;
			if (!string.Equals(entry.SubjectId, subjectId, StringComparison.Ordinal)) continue;
			total += entry.Strength;
		}
		return total;
	}

	/// <summary>
	/// Append <paramref name="memory"/> to <paramref name="actorId"/>'s
	/// list; oldest entries are evicted once the list exceeds
	/// <see cref="MaxMemoriesPerActor"/>. No-op for blank ids or self
	/// references.
	/// </summary>
	public void Record(string actorId, ActorMemory memory)
	{
		if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(memory.SubjectId))
			return;
		if (string.Equals(actorId, memory.SubjectId, StringComparison.Ordinal))
			return;

		if (!_byActor.TryGetValue(actorId, out var list))
		{
			list = new List<ActorMemory>();
			_byActor[actorId] = list;
		}

		list.Add(memory);
		if (list.Count > MaxMemoriesPerActor)
			list.RemoveAt(0);
	}

	/// <summary>Drop every memory entry; intended for session transitions.</summary>
	public void Reset()
	{
		_byActor.Clear();
	}

	/// <summary>Fraction of remembered strength lost per turn.</summary>
	public const float DefaultDecayPerTurn = 0.01f;

	/// <summary>Entries whose strength falls below this are forgotten outright.</summary>
	public const float ForgetThreshold = 0.02f;

	/// <summary>
	/// Decay the strength of every stored memory by
	/// <paramref name="decayPerTurn"/> and forget entries that fall below
	/// <see cref="ForgetThreshold"/>. Not wired automatically yet.
	/// </summary>
	public void Tick(float decayPerTurn = DefaultDecayPerTurn)
	{
		if (_byActor.Count == 0 || decayPerTurn <= 0f)
			return;

		var factor = Math.Max(0f, 1f - decayPerTurn);
		List<string>? emptyActors = null;
		foreach (var kvp in _byActor)
		{
			var list = kvp.Value;
			for (var i = list.Count - 1; i >= 0; i--)
			{
				var decayed = list[i] with { Strength = list[i].Strength * factor };
				if (Math.Abs(decayed.Strength) < ForgetThreshold)
					list.RemoveAt(i);
				else
					list[i] = decayed;
			}
			if (list.Count == 0)
				(emptyActors ??= new List<string>()).Add(kvp.Key);
		}

		if (emptyActors != null)
		{
			for (var i = 0; i < emptyActors.Count; i++)
				_byActor.Remove(emptyActors[i]);
		}
	}

	public void OnEvent(GameState state, GameEvent ev)
	{
		if (ev == null) return;

		if (string.Equals(ev.Type, "combat_attack", StringComparison.Ordinal))
			HandleCombatAttack(ev);
	}

	private void HandleCombatAttack(GameEvent ev)
	{
		if (string.IsNullOrWhiteSpace(ev.InitiatorId) || string.IsNullOrWhiteSpace(ev.TargetId))
			return;
		if (string.IsNullOrWhiteSpace(ev.InitiatorFaction) || string.IsNullOrWhiteSpace(ev.TargetFaction))
			return;
		if (!FactionRelation.IsHostile(ev.InitiatorFaction, ev.TargetFaction))
			return;

		Record(
			ev.TargetId!,
			new ActorMemory(ev.InitiatorId!, ActorMemoryKind.HostileAttackBy, HostileAttackMemoryStrength));
	}
}
