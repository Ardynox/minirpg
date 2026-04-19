using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Social;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RumorBusTests
{
	[Fact]
	public void Emit_PublishesRumorWithMonotonicId()
	{
		var bus = new RumorBus();

		var first = bus.Emit("actor_1", RumorKind.AttackWitnessed, 10, 10, 0);
		var second = bus.Emit("actor_2", RumorKind.TheftWitnessed, 0, 0, 0);

		Assert.NotNull(first);
		Assert.NotNull(second);
		Assert.True(second!.Value.Id > first!.Value.Id);
		Assert.Equal(2, bus.Active.Count);
	}

	[Fact]
	public void Emit_BlankSubject_IsIgnored()
	{
		var bus = new RumorBus();

		var result = bus.Emit(string.Empty, RumorKind.CasualtyReported, 0, 0, 0);

		Assert.Null(result);
		Assert.Empty(bus.Active);
	}

	[Fact]
	public void QueryAudibleAt_WithinRadius_ReturnsRumor()
	{
		var bus = new RumorBus();
		bus.Emit("thief", RumorKind.TheftWitnessed, 0, 0, 0, spreadRadius: 5);

		var audible = bus.QueryAudibleAt(3, 4, 0).ToList();

		Assert.Single(audible);
		Assert.Equal(RumorKind.TheftWitnessed, audible[0].Kind);
	}

	[Fact]
	public void QueryAudibleAt_BeyondRadius_ReturnsEmpty()
	{
		var bus = new RumorBus();
		bus.Emit("thief", RumorKind.TheftWitnessed, 0, 0, 0, spreadRadius: 3);

		Assert.Empty(bus.QueryAudibleAt(5, 0, 0));
	}

	[Fact]
	public void QueryAudibleAt_DifferentZLayer_FiltersOut()
	{
		var bus = new RumorBus();
		bus.Emit("ghoul", RumorKind.AttackWitnessed, 0, 0, 1, spreadRadius: 99);

		Assert.Empty(bus.QueryAudibleAt(0, 0, 0));
	}

	[Fact]
	public void QueryAudibleAt_UsesChebyshevDistance()
	{
		// At radius 5 the audible square extends 5 cells in all directions
		// (Chebyshev). A point at (5, 5, 0) must still count as audible
		// even though its Manhattan distance is 10.
		var bus = new RumorBus();
		bus.Emit("a", RumorKind.AttackWitnessed, 0, 0, 0, spreadRadius: 5);

		Assert.Single(bus.QueryAudibleAt(5, 5, 0));
		Assert.Empty(bus.QueryAudibleAt(6, 0, 0));
	}

	[Fact]
	public void Reset_ClearsActiveRumors()
	{
		var bus = new RumorBus();
		bus.Emit("a", RumorKind.AttackWitnessed, 0, 0, 0);

		bus.Reset();

		Assert.Empty(bus.Active);
	}

	[Fact]
	public void OnEvent_HostileCombatAttack_EmitsAttackWitnessedRumor()
	{
		var bus = new RumorBus();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorId = "raider_7",
			InitiatorFaction = Factions.Hostile,
			TargetId = "villager_3",
			TargetFaction = Factions.Player,
			TargetX = 12,
			TargetY = 5,
			TargetZ = 0,
		};

		bus.OnEvent(new GameState(), ev);

		Assert.Single(bus.Active);
		var rumor = bus.Active[0];
		Assert.Equal("raider_7", rumor.SubjectId);
		Assert.Equal(RumorKind.AttackWitnessed, rumor.Kind);
		Assert.Equal(12, rumor.SourceX);
		Assert.Equal(5, rumor.SourceY);
		Assert.Equal(0, rumor.SourceZ);
		Assert.Equal(RumorBus.DefaultCredibility, rumor.Credibility, 3);
		Assert.Equal(RumorBus.DefaultSpreadRadius, rumor.SpreadRadius);
	}

	[Fact]
	public void OnEvent_SameFactionAttack_DoesNotEmit()
	{
		var bus = new RumorBus();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorId = "a",
			InitiatorFaction = Factions.Player,
			TargetId = "b",
			TargetFaction = Factions.Player,
			TargetX = 0, TargetY = 0, TargetZ = 0,
		};

		bus.OnEvent(new GameState(), ev);

		Assert.Empty(bus.Active);
	}

	[Fact]
	public void OnEvent_NonCombatEvent_DoesNotEmit()
	{
		var bus = new RumorBus();
		var ev = new GameEvent("pickup_item")
		{
			InitiatorId = "a",
			InitiatorFaction = Factions.Player,
			TargetId = "b",
			TargetFaction = Factions.Hostile,
		};

		bus.OnEvent(new GameState(), ev);

		Assert.Empty(bus.Active);
	}

	[Fact]
	public void OnEvent_ActorKilled_EmitsCasualtyReportedRumor()
	{
		var bus = new RumorBus();
		var ev = new GameEvent("actor_killed")
		{
			TargetId = "villager_3",
			TargetActorName = "Villager",
			TargetFaction = Factions.Player,
			TargetX = 7,
			TargetY = 9,
			TargetZ = 1,
			Damage = 5,
		};

		bus.OnEvent(new GameState(), ev);

		Assert.Single(bus.Active);
		var rumor = bus.Active[0];
		Assert.Equal("villager_3", rumor.SubjectId);
		Assert.Equal(RumorKind.CasualtyReported, rumor.Kind);
		Assert.Equal(7, rumor.SourceX);
		Assert.Equal(9, rumor.SourceY);
		Assert.Equal(1, rumor.SourceZ);
	}

	[Fact]
	public void OnEvent_ActorKilled_BlankTargetId_DoesNotEmit()
	{
		var bus = new RumorBus();
		var ev = new GameEvent("actor_killed")
		{
			TargetX = 0,
			TargetY = 0,
			TargetZ = 0,
		};

		bus.OnEvent(new GameState(), ev);

		Assert.Empty(bus.Active);
	}

	[Fact]
	public void OnEvent_ActorKilled_WithInitiator_EmitsCasualtyAndAttackWitnessed()
	{
		var bus = new RumorBus();
		var ev = new GameEvent("actor_killed")
		{
			InitiatorId = "raider_7",
			InitiatorFaction = Factions.Hostile,
			TargetId = "villager_3",
			TargetFaction = Factions.Player,
			TargetX = 7,
			TargetY = 9,
			TargetZ = 1,
			Damage = 5,
		};

		bus.OnEvent(new GameState(), ev);

		Assert.Equal(2, bus.Active.Count);
		var casualty = bus.Active.Single(r => r.Kind == RumorKind.CasualtyReported);
		Assert.Equal("villager_3", casualty.SubjectId);
		Assert.Equal(7, casualty.SourceX);
		Assert.Equal(9, casualty.SourceY);
		Assert.Equal(1, casualty.SourceZ);

		var attack = bus.Active.Single(r => r.Kind == RumorKind.AttackWitnessed);
		Assert.Equal("raider_7", attack.SubjectId);
		Assert.Equal(7, attack.SourceX);
		Assert.Equal(9, attack.SourceY);
		Assert.Equal(1, attack.SourceZ);
		Assert.Equal(1.0f, attack.Credibility, 3);
	}

	[Fact]
	public void OnEvent_ActorKilled_BlankInitiator_OnlyEmitsCasualtyReported()
	{
		var bus = new RumorBus();
		var ev = new GameEvent("actor_killed")
		{
			InitiatorId = string.Empty,
			TargetId = "wolf_1",
			TargetX = 3,
			TargetY = 4,
			TargetZ = 0,
		};

		bus.OnEvent(new GameState(), ev);

		Assert.Single(bus.Active);
		Assert.Equal(RumorKind.CasualtyReported, bus.Active[0].Kind);
	}

	[Fact]
	public void Tick_ReducesCredibility()
	{
		var bus = new RumorBus();
		bus.Emit("x", RumorKind.AttackWitnessed, 0, 0, 0, credibility: 1f);

		bus.Tick(decayPerTurn: 0.25f);

		Assert.Equal(0.75f, bus.Active[0].Credibility, 3);
	}

	[Fact]
	public void Tick_DropsRumorsBelowThreshold()
	{
		var bus = new RumorBus();
		bus.Emit("x", RumorKind.AttackWitnessed, 0, 0, 0, credibility: 0.06f);

		bus.Tick(decayPerTurn: 0.5f);

		Assert.Empty(bus.Active);
	}

	[Fact]
	public void Tick_ZeroDecay_IsNoOp()
	{
		var bus = new RumorBus();
		bus.Emit("x", RumorKind.AttackWitnessed, 0, 0, 0, credibility: 0.5f);

		bus.Tick(decayPerTurn: 0f);

		Assert.Equal(0.5f, bus.Active[0].Credibility, 3);
	}
}
