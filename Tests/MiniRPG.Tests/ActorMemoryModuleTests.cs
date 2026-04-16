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
}
