using MiniRPG.Core.Data;
using MiniRPG.Core.Social;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RelationshipModuleTests
{
	[Fact]
	public void Get_MissingEdge_ReturnsDefault()
	{
		var module = new RelationshipModule();

		var state = module.Get("a", "b");

		Assert.Equal(RelationshipState.Default, state);
	}

	[Fact]
	public void Adjust_AccumulatesDeltasAcrossCalls()
	{
		var module = new RelationshipModule();

		module.Adjust("a", "b", trustDelta: 0.5f);
		module.Adjust("a", "b", trustDelta: -0.2f, fearDelta: 0.3f);

		var state = module.Get("a", "b");
		Assert.Equal(0.3f, state.Trust, 3);
		Assert.Equal(0.3f, state.Fear, 3);
		Assert.Equal(0f, state.Debt, 3);
	}

	[Fact]
	public void Adjust_IsDirectional_ReverseEdgeIndependent()
	{
		var module = new RelationshipModule();

		module.Adjust("a", "b", trustDelta: 0.5f);

		Assert.Equal(0.5f, module.Get("a", "b").Trust, 3);
		Assert.Equal(0f, module.Get("b", "a").Trust, 3);
	}

	[Theory]
	[InlineData("", "b")]
	[InlineData("a", "")]
	[InlineData(null, "b")]
	[InlineData("a", null)]
	[InlineData("a", "a")]
	public void Adjust_InvalidIds_AreIgnored(string? observer, string? subject)
	{
		var module = new RelationshipModule();

		module.Adjust(observer!, subject!, trustDelta: 1f);

		Assert.Equal(0, module.EdgeCount);
	}

	[Fact]
	public void Adjust_AllZeroDeltas_DoesNotCreateEdge()
	{
		var module = new RelationshipModule();

		module.Adjust("a", "b", 0f, 0f, 0f);

		Assert.Equal(0, module.EdgeCount);
	}

	[Fact]
	public void Reset_ClearsAllEdges()
	{
		var module = new RelationshipModule();
		module.Adjust("a", "b", fearDelta: 0.4f);
		module.Adjust("c", "d", trustDelta: 0.2f);

		module.Reset();

		Assert.Equal(0, module.EdgeCount);
		Assert.Equal(RelationshipState.Default, module.Get("a", "b"));
	}

	[Fact]
	public void OnEvent_HostileCombatAttack_IncreasesVictimFearTowardAttacker()
	{
		var module = new RelationshipModule();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorId = "attacker",
			InitiatorFaction = Factions.Hostile,
			TargetId = "victim",
			TargetFaction = Factions.Player,
		};

		module.OnEvent(new GameState(), ev);

		Assert.Equal(RelationshipModule.HostileAttackFearGain, module.Get("victim", "attacker").Fear, 3);
		// Reverse direction is not touched by the handler.
		Assert.Equal(0f, module.Get("attacker", "victim").Fear, 3);
	}

	[Fact]
	public void OnEvent_FriendlyFireAttack_IsIgnored()
	{
		// Player vs Friendly is not hostile; the handler does not emit
		// an automatic relationship change, that requires richer event
		// tagging (e.g. observed_as_betrayal) added later on the roadmap.
		var module = new RelationshipModule();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorId = "attacker",
			InitiatorFaction = Factions.Player,
			TargetId = "victim",
			TargetFaction = Factions.Friendly,
		};

		module.OnEvent(new GameState(), ev);

		Assert.Equal(0, module.EdgeCount);
	}

	[Fact]
	public void OnEvent_NonCombatEvent_IsIgnored()
	{
		var module = new RelationshipModule();
		var ev = new GameEvent("pickup_item")
		{
			InitiatorId = "a",
			InitiatorFaction = Factions.Player,
			TargetId = "b",
			TargetFaction = Factions.Hostile,
		};

		module.OnEvent(new GameState(), ev);

		Assert.Equal(0, module.EdgeCount);
	}

	[Fact]
	public void Tick_DecaysEveryEdgeTowardZero()
	{
		var module = new RelationshipModule();
		module.Adjust("a", "b", fearDelta: 1f);
		module.Adjust("c", "d", trustDelta: -0.5f);

		module.Tick(decayPerTurn: 0.1f);

		Assert.Equal(0.9f, module.Get("a", "b").Fear, 3);
		Assert.Equal(-0.45f, module.Get("c", "d").Trust, 3);
	}

	[Fact]
	public void Tick_DropsEdgesBelowCleanupThreshold()
	{
		var module = new RelationshipModule();
		module.Adjust("a", "b", trustDelta: 0.001f);

		module.Tick(decayPerTurn: 0.5f);

		Assert.Equal(0, module.EdgeCount);
	}

	[Fact]
	public void Tick_ZeroOrNegativeDecay_IsNoOp()
	{
		var module = new RelationshipModule();
		module.Adjust("a", "b", fearDelta: 0.5f);

		module.Tick(decayPerTurn: 0f);
		module.Tick(decayPerTurn: -1f);

		Assert.Equal(0.5f, module.Get("a", "b").Fear, 3);
	}
}
