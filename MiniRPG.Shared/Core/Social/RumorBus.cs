using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;

namespace MiniRPG.Core.Social;

/// <summary>
/// Kinds of rumor the simulation can emit today. Kept narrow so the
/// event rules and the consumer side (UtilityAI input resolvers, UI
/// gossip panels) can be evolved together.
/// </summary>
public enum RumorKind
{
	/// <summary>A hostile attack was witnessed in an area.</summary>
	AttackWitnessed,
	/// <summary>A theft was witnessed in an area.</summary>
	TheftWitnessed,
	/// <summary>A death or casualty was reported in an area.</summary>
	CasualtyReported,
}

/// <summary>
/// A piece of information anchored at a world-space point. NPCs within
/// <see cref="SpreadRadius"/> (Chebyshev distance on the same Z layer)
/// are considered "audible" of the rumor; callers do not distinguish
/// witnesses from hearsay at this stage - the <see cref="Credibility"/>
/// axis is the only signal degrade.
/// </summary>
public readonly record struct Rumor(
	long Id,
	string SubjectId,
	RumorKind Kind,
	float Credibility,
	int SourceX,
	int SourceY,
	int SourceZ,
	int SpreadRadius);

/// <summary>
/// Authoritative-side rumor store. Emits rumors in response to events
/// (step 4 of <c>Docs/涌现世界路线图.md</c>) and answers "what can an
/// actor at (x,y,z) hear right now?".
///
/// The first revision is intentionally flat: a single list of active
/// rumors with no per-actor knowledge tracking, no decay and no
/// cross-Z propagation. Audibility is <c>|dx| &lt;= radius</c> AND
/// <c>|dy| &lt;= radius</c> AND <c>sourceZ == observerZ</c>. Decay and
/// per-actor knowledge will land alongside the InputResolver that
/// actually consumes rumors.
/// </summary>
public sealed class RumorBus : IGameEventConsequenceHandler
{
	public const int DefaultSpreadRadius = 8;
	public const float DefaultCredibility = 0.7f;

	private readonly List<Rumor> _active = new();
	private long _nextId = 1;

	/// <summary>Snapshot of active rumors; order is insertion order.</summary>
	public IReadOnlyList<Rumor> Active => _active;

	/// <summary>
	/// Publish a fully-specified rumor onto the bus. Ignored when the
	/// subject id is blank.
	/// </summary>
	public Rumor? Emit(
		string subjectId,
		RumorKind kind,
		int sourceX,
		int sourceY,
		int sourceZ,
		float credibility = DefaultCredibility,
		int spreadRadius = DefaultSpreadRadius)
	{
		if (string.IsNullOrWhiteSpace(subjectId))
			return null;
		if (spreadRadius < 0)
			spreadRadius = 0;

		var rumor = new Rumor(
			Id: _nextId++,
			SubjectId: subjectId,
			Kind: kind,
			Credibility: credibility,
			SourceX: sourceX,
			SourceY: sourceY,
			SourceZ: sourceZ,
			SpreadRadius: spreadRadius);
		_active.Add(rumor);
		return rumor;
	}

	/// <summary>
	/// Enumerate the rumors audible at <paramref name="x"/>, <paramref name="y"/>,
	/// <paramref name="z"/>. Cross-Z listeners are filtered out; Chebyshev
	/// distance is compared against <see cref="Rumor.SpreadRadius"/>.
	/// </summary>
	public IEnumerable<Rumor> QueryAudibleAt(int x, int y, int z)
	{
		for (var i = 0; i < _active.Count; i++)
		{
			var rumor = _active[i];
			if (rumor.SourceZ != z) continue;

			var dx = Math.Abs(rumor.SourceX - x);
			var dy = Math.Abs(rumor.SourceY - y);
			if (dx <= rumor.SpreadRadius && dy <= rumor.SpreadRadius)
				yield return rumor;
		}
	}

	/// <summary>Drop every active rumor; intended for session transitions.</summary>
	public void Reset()
	{
		_active.Clear();
	}

	/// <summary>Fraction of credibility lost per turn.</summary>
	public const float DefaultDecayPerTurn = 0.04f;

	/// <summary>Rumors whose credibility falls below this are dropped.</summary>
	public const float DropThreshold = 0.05f;

	/// <summary>
	/// Decay every active rumor's credibility by <paramref name="decayPerTurn"/>
	/// and drop rumors that fall below <see cref="DropThreshold"/>. Not
	/// wired to <c>TimelineTurnManager</c> automatically yet.
	/// </summary>
	public void Tick(float decayPerTurn = DefaultDecayPerTurn)
	{
		if (_active.Count == 0 || decayPerTurn <= 0f)
			return;

		var factor = Math.Max(0f, 1f - decayPerTurn);
		for (var i = _active.Count - 1; i >= 0; i--)
		{
			var rumor = _active[i];
			var decayed = rumor with { Credibility = rumor.Credibility * factor };
			if (decayed.Credibility < DropThreshold)
				_active.RemoveAt(i);
			else
				_active[i] = decayed;
		}
	}

	public void OnEvent(GameState state, GameEvent ev)
	{
		if (ev == null) return;

		switch (ev.Type)
		{
			case "combat_attack":
				HandleHostileCombatAttack(ev);
				break;
			case "actor_killed":
				HandleActorKilled(ev);
				break;
		}
	}

	private void HandleHostileCombatAttack(GameEvent ev)
	{
		if (string.IsNullOrWhiteSpace(ev.InitiatorId)) return;
		if (string.IsNullOrWhiteSpace(ev.InitiatorFaction) || string.IsNullOrWhiteSpace(ev.TargetFaction))
			return;
		if (!FactionRelation.IsHostile(ev.InitiatorFaction, ev.TargetFaction))
			return;

		Emit(
			subjectId: ev.InitiatorId!,
			kind: RumorKind.AttackWitnessed,
			sourceX: ev.TargetX,
			sourceY: ev.TargetY,
			sourceZ: ev.TargetZ);
	}

	// CombatModule / SurgeryModule emit "actor_killed" with the victim
	// identity populated; the InitiatorId is populated when the death has
	// an identifiable killer (combat / live-harvest), and absent for
	// environmental / surgery-side-effect deaths. We always anchor a
	// CasualtyReported rumor at the death cell with the victim as subject;
	// when the killer is known we additionally emit an AttackWitnessed
	// rumor with the killer as subject and full credibility — this is the
	// channel NPCs use to point at "the one who did it" later.
	private void HandleActorKilled(GameEvent ev)
	{
		if (string.IsNullOrWhiteSpace(ev.TargetId)) return;

		Emit(
			subjectId: ev.TargetId!,
			kind: RumorKind.CasualtyReported,
			sourceX: ev.TargetX,
			sourceY: ev.TargetY,
			sourceZ: ev.TargetZ);

		if (!string.IsNullOrWhiteSpace(ev.InitiatorId))
		{
			Emit(
				subjectId: ev.InitiatorId!,
				kind: RumorKind.AttackWitnessed,
				sourceX: ev.TargetX,
				sourceY: ev.TargetY,
				sourceZ: ev.TargetZ,
				credibility: 1.0f);
		}
	}
}
