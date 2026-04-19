using System;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Social;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ActorMemoryModuleTests
{
	[Fact]
	public void GetMemories_Unknown_ReturnsEmpty()
	{
		var module = new ActorMemoryModule();

		Assert.Empty(module.GetMemories("nobody"));
	}

	[Fact]
	public void Record_AddsEntryAndReportsActorCount()
	{
		var module = new ActorMemoryModule();

		module.Record("alice", new ActorMemory("bob", ActorMemoryKind.HostileAttackBy, 0.3f));

		var memories = module.GetMemories("alice");
		Assert.Single(memories);
		Assert.Equal("bob", memories[0].SubjectId);
		Assert.Equal(ActorMemoryKind.HostileAttackBy, memories[0].Kind);
		Assert.Equal(1, module.ActorsWithMemories);
	}

	[Theory]
	[InlineData("", "bob")]
	[InlineData("alice", "")]
	[InlineData(null, "bob")]
	[InlineData("alice", null)]
	[InlineData("alice", "alice")]
	public void Record_InvalidIds_AreIgnored(string? actorId, string? subjectId)
	{
		var module = new ActorMemoryModule();

		module.Record(actorId!, new ActorMemory(subjectId!, ActorMemoryKind.DebtOwedTo, 1f));

		Assert.Empty(module.GetMemories(actorId ?? string.Empty));
		Assert.Equal(0, module.ActorsWithMemories);
	}

	[Fact]
	public void Record_EvictsOldestAtCap()
	{
		var module = new ActorMemoryModule();

		// Fill to cap + 1 so the first entry must be evicted.
		for (var i = 0; i < ActorMemoryModule.MaxMemoriesPerActor + 1; i++)
		{
			module.Record(
				"alice",
				new ActorMemory($"subject{i}", ActorMemoryKind.KindnessReceived, i));
		}

		var memories = module.GetMemories("alice");
		Assert.Equal(ActorMemoryModule.MaxMemoriesPerActor, memories.Count);
		Assert.Equal("subject1", memories[0].SubjectId);
		Assert.Equal($"subject{ActorMemoryModule.MaxMemoriesPerActor}", memories[^1].SubjectId);
	}

	[Fact]
	public void Reset_ClearsEveryActor()
	{
		var module = new ActorMemoryModule();
		module.Record("alice", new ActorMemory("bob", ActorMemoryKind.HostileAttackBy, 1f));
		module.Record("carol", new ActorMemory("dave", ActorMemoryKind.BetrayalBy, 1f));

		module.Reset();

		Assert.Equal(0, module.ActorsWithMemories);
		Assert.Empty(module.GetMemories("alice"));
	}

	[Fact]
	public void OnEvent_HostileCombatAttack_WritesMemoryOnVictim()
	{
		var module = new ActorMemoryModule();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorId = "raider",
			InitiatorFaction = Factions.Hostile,
			TargetId = "villager",
			TargetFaction = Factions.Player,
		};

		module.OnEvent(new GameState(), ev);

		var memories = module.GetMemories("villager");
		Assert.Single(memories);
		Assert.Equal("raider", memories[0].SubjectId);
		Assert.Equal(ActorMemoryKind.HostileAttackBy, memories[0].Kind);
		Assert.Equal(ActorMemoryModule.HostileAttackMemoryStrength, memories[0].Strength, 3);

		// Attacker does not automatically get a mirror memory - asymmetric on purpose.
		Assert.Empty(module.GetMemories("raider"));
	}

	[Fact]
	public void OnEvent_SameFactionAttack_IsIgnored()
	{
		var module = new ActorMemoryModule();
		var ev = new GameEvent("combat_attack")
		{
			InitiatorId = "a",
			InitiatorFaction = Factions.Player,
			TargetId = "b",
			TargetFaction = Factions.Player,
		};

		module.OnEvent(new GameState(), ev);

		Assert.Equal(0, module.ActorsWithMemories);
	}

	[Fact]
	public void OnEvent_NonCombatEvent_IsIgnored()
	{
		var module = new ActorMemoryModule();
		var ev = new GameEvent("pickup_item")
		{
			InitiatorId = "a",
			InitiatorFaction = Factions.Player,
			TargetId = "b",
			TargetFaction = Factions.Hostile,
		};

		module.OnEvent(new GameState(), ev);

		Assert.Equal(0, module.ActorsWithMemories);
	}

	[Fact]
	public void OnEvent_ActorKilled_RecordsCasualtyMemoryOnNearbyBystanders()
	{
		var module = new ActorMemoryModule();
		var state = new GameState();
		state.Actors["bystander_close"] = new Actor { Id = "bystander_close", X = 6, Y = 6, Z = 0 };
		state.Actors["bystander_far"] = new Actor { Id = "bystander_far", X = 99, Y = 99, Z = 0 };
		state.Actors["bystander_other_z"] = new Actor { Id = "bystander_other_z", X = 5, Y = 5, Z = 1 };
		state.Actors["victim"] = new Actor { Id = "victim", X = 5, Y = 5, Z = 0 };

		var ev = new GameEvent("actor_killed")
		{
			TargetId = "victim",
			TargetX = 5,
			TargetY = 5,
			TargetZ = 0,
		};

		module.OnEvent(state, ev);

		var bystanderMemories = module.GetMemories("bystander_close");
		Assert.Single(bystanderMemories);
		Assert.Equal("victim", bystanderMemories[0].SubjectId);
		Assert.Equal(ActorMemoryKind.CasualtyWitnessed, bystanderMemories[0].Kind);
		Assert.Equal(ActorMemoryModule.CasualtyWitnessedMemoryStrength, bystanderMemories[0].Strength, 3);

		// Out-of-range neighbours and cross-Z observers stay unaffected.
		Assert.Empty(module.GetMemories("bystander_far"));
		Assert.Empty(module.GetMemories("bystander_other_z"));
		// The victim does not record a memory about itself.
		Assert.Empty(module.GetMemories("victim"));
	}

	[Fact]
	public void OnEvent_ActorKilled_BlankVictimId_IsIgnored()
	{
		var module = new ActorMemoryModule();
		var state = new GameState();
		state.Actors["bystander"] = new Actor { Id = "bystander", X = 0, Y = 0, Z = 0 };

		var ev = new GameEvent("actor_killed")
		{
			TargetX = 0,
			TargetY = 0,
			TargetZ = 0,
		};

		module.OnEvent(state, ev);

		Assert.Equal(0, module.ActorsWithMemories);
	}

	[Fact]
	public void OnEvent_ActorKilled_WithInitiator_RecordsKilledByOnBystanders()
	{
		var module = new ActorMemoryModule();
		var state = new GameState();
		state.Actors["bystander"] = new Actor { Id = "bystander", X = 6, Y = 6, Z = 0 };
		state.Actors["bystander_far"] = new Actor { Id = "bystander_far", X = 99, Y = 99, Z = 0 };
		state.Actors["bystander_other_z"] = new Actor { Id = "bystander_other_z", X = 5, Y = 5, Z = 1 };
		state.Actors["killer"] = new Actor { Id = "killer", X = 4, Y = 5, Z = 0 };
		state.Actors["victim"] = new Actor { Id = "victim", X = 5, Y = 5, Z = 0 };

		var ev = new GameEvent("actor_killed")
		{
			InitiatorId = "killer",
			TargetId = "victim",
			TargetX = 5,
			TargetY = 5,
			TargetZ = 0,
		};

		module.OnEvent(state, ev);

		var bystander = module.GetMemories("bystander");
		Assert.Equal(2, bystander.Count);
		Assert.Contains(bystander, m =>
			m.SubjectId == "victim"
			&& m.Kind == ActorMemoryKind.CasualtyWitnessed
			&& Math.Abs(m.Strength - ActorMemoryModule.CasualtyWitnessedMemoryStrength) < 0.001f);
		Assert.Contains(bystander, m =>
			m.SubjectId == "killer"
			&& m.Kind == ActorMemoryKind.KilledBy
			&& Math.Abs(m.Strength - ActorMemoryModule.KilledByMemoryStrength) < 0.001f);

		// Killer is in range as a bystander too — but they must NOT grow a
		// KilledBy memory of themselves. They DO see the casualty though.
		var killerMemories = module.GetMemories("killer");
		Assert.Single(killerMemories);
		Assert.Equal(ActorMemoryKind.CasualtyWitnessed, killerMemories[0].Kind);
		Assert.Equal("victim", killerMemories[0].SubjectId);

		Assert.Empty(module.GetMemories("bystander_far"));
		Assert.Empty(module.GetMemories("bystander_other_z"));
		Assert.Empty(module.GetMemories("victim"));
	}

	[Fact]
	public void OnEvent_ActorKilled_BlankInitiator_StillRecordsCasualtyOnly()
	{
		var module = new ActorMemoryModule();
		var state = new GameState();
		state.Actors["bystander"] = new Actor { Id = "bystander", X = 5, Y = 5, Z = 0 };
		state.Actors["victim"] = new Actor { Id = "victim", X = 5, Y = 5, Z = 0 };

		var ev = new GameEvent("actor_killed")
		{
			InitiatorId = string.Empty,
			TargetId = "victim",
			TargetX = 5,
			TargetY = 5,
			TargetZ = 0,
		};

		module.OnEvent(state, ev);

		var memories = module.GetMemories("bystander");
		Assert.Single(memories);
		Assert.Equal(ActorMemoryKind.CasualtyWitnessed, memories[0].Kind);
	}

	[Fact]
	public void OnEvent_GiftGiven_RecordsKindnessOnRecipient()
	{
		var module = new ActorMemoryModule();
		var ev = new GameEvent("gift_given")
		{
			InitiatorId = "giver",
			InitiatorFaction = Factions.Player,
			TargetId = "recipient",
			TargetFaction = Factions.Friendly,
			ItemTypeId = "berry",
			ItemName = "Berry",
		};

		module.OnEvent(new GameState(), ev);

		var memories = module.GetMemories("recipient");
		Assert.Single(memories);
		Assert.Equal("giver", memories[0].SubjectId);
		Assert.Equal(ActorMemoryKind.KindnessReceived, memories[0].Kind);
		Assert.Equal(ActorMemoryModule.KindnessReceivedMemoryStrength, memories[0].Strength, 3);

		// Asymmetric: the giver does not write a mirror entry.
		Assert.Empty(module.GetMemories("giver"));
	}

	[Fact]
	public void OnEvent_GiftGiven_StacksMultipleGifts()
	{
		// Each gift_given event appends a fresh KindnessReceived entry;
		// SumStrength over the kind aggregates them so the AI consumer
		// side can read "how much do I owe X" as a single scalar.
		var module = new ActorMemoryModule();
		var ev = new GameEvent("gift_given")
		{
			InitiatorId = "giver",
			TargetId = "recipient",
		};

		module.OnEvent(new GameState(), ev);
		module.OnEvent(new GameState(), ev);

		Assert.Equal(2, module.GetMemories("recipient").Count);
		Assert.Equal(
			ActorMemoryModule.KindnessReceivedMemoryStrength * 2f,
			module.SumStrength("recipient", "giver", ActorMemoryKind.KindnessReceived),
			3);
	}

	[Theory]
	[InlineData(null, "recipient")]
	[InlineData("giver", null)]
	[InlineData("", "recipient")]
	[InlineData("giver", "")]
	public void OnEvent_GiftGiven_BlankIds_IsIgnored(string? initiatorId, string? targetId)
	{
		var module = new ActorMemoryModule();
		var ev = new GameEvent("gift_given")
		{
			InitiatorId = initiatorId,
			TargetId = targetId,
		};

		module.OnEvent(new GameState(), ev);

		Assert.Equal(0, module.ActorsWithMemories);
	}

	[Fact]
	public void Tick_ReducesMemoryStrength()
	{
		var module = new ActorMemoryModule();
		module.Record("alice", new ActorMemory("bob", ActorMemoryKind.KindnessReceived, 1f));

		module.Tick(decayPerTurn: 0.2f);

		Assert.Equal(0.8f, module.GetMemories("alice")[0].Strength, 3);
	}

	[Fact]
	public void Tick_ForgetsMemoriesBelowThreshold_AndDropsEmptyActors()
	{
		var module = new ActorMemoryModule();
		module.Record("alice", new ActorMemory("bob", ActorMemoryKind.HostileAttackBy, 0.03f));

		module.Tick(decayPerTurn: 0.5f);

		Assert.Empty(module.GetMemories("alice"));
		Assert.Equal(0, module.ActorsWithMemories);
	}

	[Fact]
	public void Tick_ZeroDecay_IsNoOp()
	{
		var module = new ActorMemoryModule();
		module.Record("alice", new ActorMemory("bob", ActorMemoryKind.DebtOwedTo, 1f));

		module.Tick(decayPerTurn: 0f);

		Assert.Equal(1f, module.GetMemories("alice")[0].Strength, 3);
	}
}
