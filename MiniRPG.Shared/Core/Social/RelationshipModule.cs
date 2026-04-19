using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;

namespace MiniRPG.Core.Social;

/// <summary>
/// Lightweight per-observer directional relationship state toward another
/// actor: how much the observer trusts / fears / feels in debt to them.
/// All axes are unbounded floats; callers are expected to read raw values
/// and interpret them as bonuses / penalties in their own scoring.
/// </summary>
public readonly record struct RelationshipState(float Trust, float Fear, float Debt)
{
	public static RelationshipState Default => new(0f, 0f, 0f);

	public RelationshipState WithTrust(float value) => this with { Trust = value };
	public RelationshipState WithFear(float value) => this with { Fear = value };
	public RelationshipState WithDebt(float value) => this with { Debt = value };
}

/// <summary>
/// In-memory graph of <c>(observerActorId → subjectActorId) → RelationshipState</c>.
/// Implements <see cref="IGameEventConsequenceHandler"/> so it can slot
/// directly into <c>GameEventConsequenceRouter</c>; updates are driven by
/// authoritative <see cref="GameEvent"/> streams (combat_attack today,
/// more event types as the second-layer roadmap fills out).
/// </summary>
/// <remarks>
/// This is step 2 of <c>Docs/涌现世界路线图.md</c>. The first revision
/// intentionally stores state in memory only: session transitions call
/// <see cref="Reset"/> to clear the graph. Persistence (hookup into
/// <c>SaveFile</c>, multiplayer snapshot) is deferred until the downstream
/// UtilityAI input resolvers are actually reading these values; see the
/// roadmap doc for the order.
/// </remarks>
public sealed class RelationshipModule : IGameEventConsequenceHandler
{
	/// <summary>
	/// How much fear the victim gains toward a hostile attacker per hit.
	/// Defensive event heuristic; can be tuned once concrete AI consumers
	/// exist and expose friction.
	/// </summary>
	public const float HostileAttackFearGain = 0.10f;

	/// <summary>
	/// How much trust the recipient of a gift gains toward the giver per
	/// gift_given event. Symmetric to <see cref="HostileAttackFearGain"/>:
	/// the friendly counterpart that lets a player feel "I gave them a
	/// thing, they remember me".
	/// </summary>
	public const float GiftReceivedTrustGain = 0.10f;

	private readonly Dictionary<(string Observer, string Subject), RelationshipState> _graph
		= new();

	/// <summary>Number of directed edges held; for tests / debug only.</summary>
	public int EdgeCount => _graph.Count;

	/// <summary>
	/// Get the relationship <paramref name="observer"/> holds toward
	/// <paramref name="subject"/>. Missing edges return
	/// <see cref="RelationshipState.Default"/> so callers can read blindly.
	/// </summary>
	public RelationshipState Get(string observer, string subject)
	{
		if (string.IsNullOrWhiteSpace(observer) || string.IsNullOrWhiteSpace(subject))
			return RelationshipState.Default;
		return _graph.TryGetValue((observer, subject), out var state)
			? state
			: RelationshipState.Default;
	}

	/// <summary>
	/// Add the supplied deltas into the existing relationship (creating an
	/// edge when missing). Deltas may be negative. No-op for blank ids or
	/// when observer equals subject.
	/// </summary>
	public void Adjust(
		string observer,
		string subject,
		float trustDelta = 0f,
		float fearDelta = 0f,
		float debtDelta = 0f)
	{
		if (string.IsNullOrWhiteSpace(observer) || string.IsNullOrWhiteSpace(subject))
			return;
		if (string.Equals(observer, subject, StringComparison.Ordinal))
			return;
		if (trustDelta == 0f && fearDelta == 0f && debtDelta == 0f)
			return;

		var key = (observer, subject);
		var current = _graph.TryGetValue(key, out var existing) ? existing : RelationshipState.Default;
		_graph[key] = new RelationshipState(
			current.Trust + trustDelta,
			current.Fear + fearDelta,
			current.Debt + debtDelta);
	}

	/// <summary>Drop all edges; intended for session transitions.</summary>
	public void Reset()
	{
		_graph.Clear();
	}

	/// <summary>Per-turn decay rate; relationships drift toward zero by this factor each call to <see cref="Tick"/>.</summary>
	public const float DefaultDecayPerTurn = 0.02f;

	/// <summary>Absolute magnitude below which an edge is dropped instead of carrying a near-zero.</summary>
	public const float CleanupThreshold = 0.005f;

	/// <summary>
	/// Decay every edge toward zero by <paramref name="decayPerTurn"/> and
	/// drop edges whose magnitude falls below <see cref="CleanupThreshold"/>.
	/// Intended to be called once per simulation turn by the timeline
	/// system; not wired automatically yet, see Docs/涌现世界路线图.md.
	/// </summary>
	public void Tick(float decayPerTurn = DefaultDecayPerTurn)
	{
		if (_graph.Count == 0 || decayPerTurn <= 0f)
			return;

		var factor = Math.Max(0f, 1f - decayPerTurn);
		List<(string, string)>? toRemove = null;
		foreach (var kvp in _graph)
		{
			var decayed = new RelationshipState(
				kvp.Value.Trust * factor,
				kvp.Value.Fear * factor,
				kvp.Value.Debt * factor);

			if (IsBelowThreshold(decayed))
				(toRemove ??= new List<(string, string)>()).Add(kvp.Key);
			else
				_graph[kvp.Key] = decayed;
		}

		if (toRemove != null)
		{
			for (var i = 0; i < toRemove.Count; i++)
				_graph.Remove(toRemove[i]);
		}
	}

	private static bool IsBelowThreshold(RelationshipState state) =>
		Math.Abs(state.Trust) < CleanupThreshold
		&& Math.Abs(state.Fear) < CleanupThreshold
		&& Math.Abs(state.Debt) < CleanupThreshold;

	public void OnEvent(GameState state, GameEvent ev)
	{
		if (ev == null) return;

		switch (ev.Type)
		{
			case "combat_attack":
				HandleCombatAttack(ev);
				break;
			case "gift_given":
				HandleGiftGiven(ev);
				break;
		}
	}

	private void HandleCombatAttack(GameEvent ev)
	{
		if (string.IsNullOrWhiteSpace(ev.InitiatorId) || string.IsNullOrWhiteSpace(ev.TargetId))
			return;
		if (string.IsNullOrWhiteSpace(ev.InitiatorFaction) || string.IsNullOrWhiteSpace(ev.TargetFaction))
			return;
		if (!FactionRelation.IsHostile(ev.InitiatorFaction, ev.TargetFaction))
			return;

		// Victim gains fear toward the attacker. We do not automatically
		// adjust trust or debt; those come from more specific events
		// (gift given, debt repaid, betrayal) that will be added later.
		Adjust(ev.TargetId!, ev.InitiatorId!, fearDelta: HostileAttackFearGain);
	}

	// gift_given is the friendly mirror of combat_attack: SocialModule.TryGiveItem
	// emits it after a successful inventory transfer between two actors.
	// Recipient (TargetId) gains trust toward the giver (InitiatorId). We do
	// not require a faction relation here on purpose - hostile NPCs can also
	// be bribed (a future quest mechanic) and the consumer side decides
	// whether trust outweighs fear when reading both axes.
	private void HandleGiftGiven(GameEvent ev)
	{
		if (string.IsNullOrWhiteSpace(ev.InitiatorId) || string.IsNullOrWhiteSpace(ev.TargetId))
			return;

		Adjust(ev.TargetId!, ev.InitiatorId!, trustDelta: GiftReceivedTrustGain);
	}
}
