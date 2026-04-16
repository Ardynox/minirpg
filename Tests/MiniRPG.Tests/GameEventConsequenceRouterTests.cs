using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;
using Xunit;

namespace MiniRPG.Tests;

public sealed class GameEventConsequenceRouterTests
{
	private sealed class RecordingHandler : IGameEventConsequenceHandler
	{
		public List<string> ObservedEventTypes { get; } = new();

		public void OnEvent(GameState state, GameEvent ev)
		{
			ObservedEventTypes.Add(ev.Type);
		}
	}

	private sealed class ThrowingHandler : IGameEventConsequenceHandler
	{
		public int CallCount { get; private set; }

		public void OnEvent(GameState state, GameEvent ev)
		{
			CallCount++;
			throw new InvalidOperationException("boom");
		}
	}

	[Fact]
	public void Dispatch_WithEmptyEventList_DoesNothing()
	{
		var router = new GameEventConsequenceRouter();
		var handler = new RecordingHandler();
		router.Register(handler);

		router.DispatchConsequences(new GameState(), Array.Empty<GameEvent>());

		Assert.Empty(handler.ObservedEventTypes);
	}

	[Fact]
	public void Dispatch_WithoutHandlers_DoesNotThrow()
	{
		var router = new GameEventConsequenceRouter();
		var events = new[] { new GameEvent("combat_attack") };

		router.DispatchConsequences(new GameState(), events);

		Assert.Equal(0, router.HandlerCount);
	}

	[Fact]
	public void Dispatch_InvokesEveryHandlerForEveryEvent_InRegistrationOrder()
	{
		var router = new GameEventConsequenceRouter();
		var first = new RecordingHandler();
		var second = new RecordingHandler();
		router.Register(first);
		router.Register(second);

		var events = new[]
		{
			new GameEvent("combat_attack"),
			new GameEvent("pickup_item"),
		};

		router.DispatchConsequences(new GameState(), events);

		Assert.Equal(new[] { "combat_attack", "pickup_item" }, first.ObservedEventTypes);
		Assert.Equal(new[] { "combat_attack", "pickup_item" }, second.ObservedEventTypes);
	}

	[Fact]
	public void Dispatch_WhenHandlerThrows_OtherHandlersStillRun_AndErrorSinkReceivesMessage()
	{
		var errors = new List<string>();
		var router = new GameEventConsequenceRouter(errors.Add);
		router.Register(new ThrowingHandler());
		var survivor = new RecordingHandler();
		router.Register(survivor);

		router.DispatchConsequences(new GameState(), new[] { new GameEvent("combat_attack") });

		Assert.Single(errors);
		Assert.Contains("combat_attack", errors[0]);
		Assert.Equal(new[] { "combat_attack" }, survivor.ObservedEventTypes);
	}

	[Fact]
	public void Register_NullHandler_Throws()
	{
		var router = new GameEventConsequenceRouter();
		Assert.Throws<ArgumentNullException>(() => router.Register(null!));
	}
}

public sealed class IncidentStatisticsTests
{
	[Fact]
	public void Record_CrossFactionAttack_IncrementsHostileCounter()
	{
		var stats = new IncidentStatistics();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorFaction = Factions.Player,
			TargetFaction = Factions.Hostile,
			Damage = 5,
		};

		stats.OnEvent(new GameState(), ev);

		Assert.Equal(1, stats.CrossFactionAttackCount);
		Assert.Equal(0, stats.FriendlyFireAttackCount);
	}

	[Fact]
	public void Record_SameFactionAttack_DoesNotIncrementAnyCounter()
	{
		var stats = new IncidentStatistics();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorFaction = Factions.Player,
			TargetFaction = Factions.Player,
			Damage = 5,
		};

		stats.OnEvent(new GameState(), ev);

		Assert.Equal(0, stats.CrossFactionAttackCount);
		Assert.Equal(0, stats.FriendlyFireAttackCount);
	}

	[Fact]
	public void Record_FriendlyFireAcrossNonHostileFactions_IncrementsFriendlyFireCounter()
	{
		// Player vs Friendly is not hostile (see FactionRelation.IsHostile)
		// but the factions are distinct, so it counts as friendly fire.
		var stats = new IncidentStatistics();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorFaction = Factions.Player,
			TargetFaction = Factions.Friendly,
			Damage = 3,
		};

		stats.OnEvent(new GameState(), ev);

		Assert.Equal(0, stats.CrossFactionAttackCount);
		Assert.Equal(1, stats.FriendlyFireAttackCount);
	}

	[Fact]
	public void Record_FatalDamageMarker_IncrementsFatalCounter()
	{
		var stats = new IncidentStatistics();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorFaction = Factions.Hostile,
			TargetFaction = Factions.Player,
			Damage = 999,
		};

		stats.OnEvent(new GameState(), ev);

		Assert.Equal(1, stats.FatalAttackCount);
	}

	[Fact]
	public void Record_NonCombatEvent_IsIgnored()
	{
		var stats = new IncidentStatistics();
		var ev = new GameEvent("pickup_item")
		{
			InitiatorFaction = Factions.Player,
			TargetFaction = Factions.Hostile,
		};

		stats.OnEvent(new GameState(), ev);

		Assert.Equal(0, stats.CrossFactionAttackCount);
	}

	[Fact]
	public void Reset_ClearsAllCounters()
	{
		var stats = new IncidentStatistics();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorFaction = Factions.Player,
			TargetFaction = Factions.Hostile,
			Damage = 999,
		};
		stats.OnEvent(new GameState(), ev);

		stats.Reset();

		Assert.Equal(0, stats.CrossFactionAttackCount);
		Assert.Equal(0, stats.FriendlyFireAttackCount);
		Assert.Equal(0, stats.FatalAttackCount);
	}
}
