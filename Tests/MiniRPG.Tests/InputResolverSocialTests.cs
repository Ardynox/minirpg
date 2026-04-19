using MiniRPG.Core.AI;
using MiniRPG.Core.AI.Utility;
using MiniRPG.Core.Data;
using MiniRPG.Core.Social;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// Guard tests for the seven second-layer social InputResolvers added by
/// the emergent-world roadmap step 2-4 wiring.
///
/// Every resolver MUST tolerate three states without throwing:
///  1. <see cref="InputContext.BehaviorContext"/> is null (e.g. tests that
///     forgot to wire the ambient modules).
///  2. The behaviour context exists but the relevant module instance is
///     null (host opted out of that subsystem).
///  3. The module exists but has no matching data (no edge / memory / rumor).
///
/// All three must return 0; only the data-present case may return a
/// positive value, and the value must stay in the unit interval after the
/// resolver's own clamp.
/// </summary>
public sealed class InputResolverSocialTests
{
	private static Actor MakeActor(string id, int x = 0, int y = 0, int z = 0) =>
		new() { Id = id, X = x, Y = y, Z = z };

	private static InputContext MakeContext(
		Actor self,
		Actor? target = null,
		AIBehaviorContext? behaviorContext = null,
		GameState? state = null) =>
		new()
		{
			Self = self,
			Perception = new Perception { Self = self },
			BehaviorContext = behaviorContext,
			State = state,
			TargetActor = target,
		};

	// ── RelationshipTrust<target> ───────────────────────────────────

	[Fact]
	public void RelationshipTrust_NullBehaviorContext_ReturnsZero()
	{
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"));

		Assert.Equal(0f, InputResolver.Resolve("RelationshipTrust<target>", ctx));
	}

	[Fact]
	public void RelationshipTrust_NoRelationshipsModule_ReturnsZero()
	{
		var state = new GameState();
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state), state);

		Assert.Equal(0f, InputResolver.Resolve("RelationshipTrust<target>", ctx));
	}

	[Fact]
	public void RelationshipTrust_NoEdge_ReturnsZero()
	{
		var state = new GameState();
		var module = new RelationshipModule();
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { Relationships = module }, state);

		Assert.Equal(0f, InputResolver.Resolve("RelationshipTrust<target>", ctx));
	}

	[Fact]
	public void RelationshipTrust_HasEdge_ReturnsClampedTrust()
	{
		var state = new GameState();
		var module = new RelationshipModule();
		module.Adjust("a", "b", trustDelta: 0.7f);
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { Relationships = module }, state);

		Assert.Equal(0.7f, InputResolver.Resolve("RelationshipTrust<target>", ctx), 3);
	}

	[Fact]
	public void RelationshipTrust_NoTargetActor_ReturnsZero()
	{
		var state = new GameState();
		var module = new RelationshipModule();
		module.Adjust("a", "b", trustDelta: 0.5f);
		var ctx = MakeContext(MakeActor("a"), target: null,
			new AIBehaviorContext(state) { Relationships = module }, state);

		Assert.Equal(0f, InputResolver.Resolve("RelationshipTrust<target>", ctx));
	}

	// ── RelationshipFear<target> ────────────────────────────────────

	[Fact]
	public void RelationshipFear_NullBehaviorContext_ReturnsZero()
	{
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"));

		Assert.Equal(0f, InputResolver.Resolve("RelationshipFear<target>", ctx));
	}

	[Fact]
	public void RelationshipFear_HasEdge_ReturnsClampedFear()
	{
		var state = new GameState();
		var module = new RelationshipModule();
		module.Adjust("a", "b", fearDelta: 0.4f);
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { Relationships = module }, state);

		Assert.Equal(0.4f, InputResolver.Resolve("RelationshipFear<target>", ctx), 3);
	}

	[Fact]
	public void RelationshipFear_OverflowingValue_IsClampedToOne()
	{
		var state = new GameState();
		var module = new RelationshipModule();
		module.Adjust("a", "b", fearDelta: 5f);
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { Relationships = module }, state);

		Assert.Equal(1f, InputResolver.Resolve("RelationshipFear<target>", ctx), 3);
	}

	// ── MemoryFearTowards<target> ───────────────────────────────────

	[Fact]
	public void MemoryFearTowards_NullBehaviorContext_ReturnsZero()
	{
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"));

		Assert.Equal(0f, InputResolver.Resolve("MemoryFearTowards<target>", ctx));
	}

	[Fact]
	public void MemoryFearTowards_NoMemories_ReturnsZero()
	{
		var state = new GameState();
		var module = new ActorMemoryModule();
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { ActorMemories = module }, state);

		Assert.Equal(0f, InputResolver.Resolve("MemoryFearTowards<target>", ctx));
	}

	[Fact]
	public void MemoryFearTowards_SumsHostileAttackByEntries()
	{
		var state = new GameState();
		var module = new ActorMemoryModule();
		module.Record("a", new ActorMemory("b", ActorMemoryKind.HostileAttackBy, 0.3f));
		module.Record("a", new ActorMemory("b", ActorMemoryKind.HostileAttackBy, 0.4f));
		// Different kind / different subject must not contribute.
		module.Record("a", new ActorMemory("b", ActorMemoryKind.BetrayalBy, 0.5f));
		module.Record("a", new ActorMemory("c", ActorMemoryKind.HostileAttackBy, 0.6f));
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { ActorMemories = module }, state);

		Assert.Equal(0.7f, InputResolver.Resolve("MemoryFearTowards<target>", ctx), 3);
	}

	// ── MemoryDebtOwedTo<target> ────────────────────────────────────

	[Fact]
	public void MemoryDebtOwedTo_NullBehaviorContext_ReturnsZero()
	{
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"));

		Assert.Equal(0f, InputResolver.Resolve("MemoryDebtOwedTo<target>", ctx));
	}

	[Fact]
	public void MemoryDebtOwedTo_HasMatchingMemory_ReturnsStrength()
	{
		var state = new GameState();
		var module = new ActorMemoryModule();
		module.Record("a", new ActorMemory("b", ActorMemoryKind.DebtOwedTo, 0.55f));
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { ActorMemories = module }, state);

		Assert.Equal(0.55f, InputResolver.Resolve("MemoryDebtOwedTo<target>", ctx), 3);
	}

	// ── MemoryBetrayalBy<target> ────────────────────────────────────

	[Fact]
	public void MemoryBetrayalBy_NullBehaviorContext_ReturnsZero()
	{
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"));

		Assert.Equal(0f, InputResolver.Resolve("MemoryBetrayalBy<target>", ctx));
	}

	[Fact]
	public void MemoryBetrayalBy_HasMatchingMemory_ReturnsStrength()
	{
		var state = new GameState();
		var module = new ActorMemoryModule();
		module.Record("a", new ActorMemory("b", ActorMemoryKind.BetrayalBy, 0.8f));
		var ctx = MakeContext(MakeActor("a"), MakeActor("b"),
			new AIBehaviorContext(state) { ActorMemories = module }, state);

		Assert.Equal(0.8f, InputResolver.Resolve("MemoryBetrayalBy<target>", ctx), 3);
	}

	// ── NearbyRumorSeverity ─────────────────────────────────────────

	[Fact]
	public void NearbyRumorSeverity_NullBehaviorContext_ReturnsZero()
	{
		var ctx = MakeContext(MakeActor("a", 0, 0, 0));

		Assert.Equal(0f, InputResolver.Resolve("NearbyRumorSeverity", ctx));
	}

	[Fact]
	public void NearbyRumorSeverity_EmptyBus_ReturnsZero()
	{
		var state = new GameState();
		var bus = new RumorBus();
		var ctx = MakeContext(MakeActor("a", 0, 0, 0), behaviorContext:
			new AIBehaviorContext(state) { Rumors = bus }, state: state);

		Assert.Equal(0f, InputResolver.Resolve("NearbyRumorSeverity", ctx));
	}

	[Fact]
	public void NearbyRumorSeverity_AudibleRumors_AggregateSeverity()
	{
		var state = new GameState();
		var bus = new RumorBus();
		// Audible at (0,0,0):
		//   AttackWitnessed @ cred 0.5 → 0.6 * 0.5 = 0.30
		//   CasualtyReported @ cred 0.4 → 1.0 * 0.4 = 0.40
		// Total = 0.70 (within radius defaults 8)
		bus.Emit("attacker", RumorKind.AttackWitnessed, 2, 2, 0, credibility: 0.5f);
		bus.Emit("victim", RumorKind.CasualtyReported, 0, 0, 0, credibility: 0.4f);
		// Out-of-range / cross-Z rumors must not contribute.
		bus.Emit("offmap", RumorKind.AttackWitnessed, 100, 100, 0, credibility: 1f);
		bus.Emit("upstairs", RumorKind.CasualtyReported, 0, 0, 1, credibility: 1f);

		var ctx = MakeContext(MakeActor("listener", 0, 0, 0), behaviorContext:
			new AIBehaviorContext(state) { Rumors = bus }, state: state);

		Assert.Equal(0.70f, InputResolver.Resolve("NearbyRumorSeverity", ctx), 3);
	}

	[Fact]
	public void NearbyRumorSeverity_OverflowsClampToOne()
	{
		var state = new GameState();
		var bus = new RumorBus();
		// Three CasualtyReported rumors at full credibility = 3.0 raw.
		bus.Emit("v1", RumorKind.CasualtyReported, 0, 0, 0, credibility: 1f);
		bus.Emit("v2", RumorKind.CasualtyReported, 1, 0, 0, credibility: 1f);
		bus.Emit("v3", RumorKind.CasualtyReported, 0, 1, 0, credibility: 1f);

		var ctx = MakeContext(MakeActor("listener", 0, 0, 0), behaviorContext:
			new AIBehaviorContext(state) { Rumors = bus }, state: state);

		Assert.Equal(1f, InputResolver.Resolve("NearbyRumorSeverity", ctx), 3);
	}

	// ── HasRecentTheftRumor ─────────────────────────────────────────

	[Fact]
	public void HasRecentTheftRumor_NullBehaviorContext_ReturnsZero()
	{
		var ctx = MakeContext(MakeActor("a", 0, 0, 0));

		Assert.Equal(0f, InputResolver.Resolve("HasRecentTheftRumor", ctx));
	}

	[Fact]
	public void HasRecentTheftRumor_NoTheftRumor_ReturnsZero()
	{
		var state = new GameState();
		var bus = new RumorBus();
		// Attack / casualty rumors must not trigger the theft predicate.
		bus.Emit("a", RumorKind.AttackWitnessed, 0, 0, 0, credibility: 1f);
		bus.Emit("v", RumorKind.CasualtyReported, 0, 0, 0, credibility: 1f);
		var ctx = MakeContext(MakeActor("listener", 0, 0, 0), behaviorContext:
			new AIBehaviorContext(state) { Rumors = bus }, state: state);

		Assert.Equal(0f, InputResolver.Resolve("HasRecentTheftRumor", ctx));
	}

	[Fact]
	public void HasRecentTheftRumor_TheftRumorInRange_ReturnsOne()
	{
		var state = new GameState();
		var bus = new RumorBus();
		bus.Emit("thief", RumorKind.TheftWitnessed, 1, 1, 0, credibility: 0.5f);
		var ctx = MakeContext(MakeActor("listener", 0, 0, 0), behaviorContext:
			new AIBehaviorContext(state) { Rumors = bus }, state: state);

		Assert.Equal(1f, InputResolver.Resolve("HasRecentTheftRumor", ctx));
	}

	[Fact]
	public void HasRecentTheftRumor_TheftRumorOutOfRange_ReturnsZero()
	{
		var state = new GameState();
		var bus = new RumorBus();
		// Default spread radius is 8; Chebyshev distance 100 stays out.
		bus.Emit("thief", RumorKind.TheftWitnessed, 100, 100, 0, credibility: 1f);
		var ctx = MakeContext(MakeActor("listener", 0, 0, 0), behaviorContext:
			new AIBehaviorContext(state) { Rumors = bus }, state: state);

		Assert.Equal(0f, InputResolver.Resolve("HasRecentTheftRumor", ctx));
	}
}
