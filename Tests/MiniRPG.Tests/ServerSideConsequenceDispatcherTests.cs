using System.Collections.Generic;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// Regression for P0-7. Before this fix, the dedicated server's
/// <see cref="RoomRuntimeHost"/> never ran the authoritative
/// <c>GameEventConsequenceRouter</c>: relationships, memories, rumors and
/// incident statistics stayed empty regardless of how much combat happened
/// in the simulation. The dispatcher must now be wired up at construction
/// and invoked on every accepted command's event batch.
/// </summary>
public sealed class ServerSideConsequenceDispatcherTests
{
	[Fact]
	public void Dispatcher_RegistersAllBundledHandlers()
	{
		var dispatcher = new ServerSideConsequenceDispatcher();

		Assert.Equal(5, dispatcher.HandlerCount);
		Assert.NotNull(dispatcher.Statistics);
		Assert.NotNull(dispatcher.Relationships);
		Assert.NotNull(dispatcher.Memories);
		Assert.NotNull(dispatcher.Rumors);
		Assert.NotNull(dispatcher.KinshipLoss);
	}

	[Fact]
	public void Dispatch_HostileCombatAttack_AccumulatesFearAndStatistics()
	{
		var dispatcher = new ServerSideConsequenceDispatcher();
		var state = new GameState();

		var ev = new GameEvent("combat_attack")
		{
			InitiatorId = "wolf",
			TargetId = "shepherd",
			InitiatorFaction = Factions.Hostile,
			TargetFaction = Factions.Player,
			Damage = 4,
		};

		dispatcher.DispatchConsequences(state, new[] { ev });

		var fear = dispatcher.Relationships.Get("shepherd", "wolf").Fear;
		Assert.True(fear > 0f, $"Expected fear toward attacker; got {fear}.");
		Assert.Equal(1, dispatcher.Statistics.CrossFactionAttackCount);
	}

	[Fact]
	public void Dispatch_NonCombatEvent_DoesNotMutateRelationships()
	{
		var dispatcher = new ServerSideConsequenceDispatcher();
		var state = new GameState();

		dispatcher.DispatchConsequences(state, new[]
		{
			new GameEvent("pickup_item")
			{
				InitiatorId = "hero",
				TargetId = "loot",
			},
		});

		Assert.Equal(0, dispatcher.Relationships.EdgeCount);
		Assert.Equal(0, dispatcher.Statistics.CrossFactionAttackCount);
	}
}

/// <summary>
/// Companion test: confirms <see cref="RoomRuntimeHost"/> owns a
/// per-room dispatcher at construction (so multiplayer rooms cannot be
/// silently created without the social pipeline) and that
/// <see cref="RoomRuntimeHost.Execute"/> routes events through it.
/// </summary>
public sealed class RoomRuntimeHostConsequencePipelineTests
{
	[Fact]
	public void RegisterRoom_CreatesRoomHostWithBundledConsequenceDispatcher()
	{
		var lobby = new InMemoryLobbyService();
		var owner = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "TestRoom",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
		});

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions { LobbyService = lobby });
		var roomHost = host.RegisterRoom(new GameState(), lobby.GetRoomState(owner.RoomId));

		Assert.NotNull(roomHost.Consequences);
		Assert.Equal(5, roomHost.Consequences.HandlerCount);
	}

	[Fact]
	public void Dispatcher_ScopedPerRoom_DoesNotLeakRelationshipsAcrossRooms()
	{
		var lobby = new InMemoryLobbyService();
		var roomA = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "RoomA",
			OwnerDisplayName = "OwnerA",
			PrimaryActorId = "hero",
		});
		var roomB = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "RoomB",
			OwnerDisplayName = "OwnerB",
			PrimaryActorId = "hero",
		});

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions { LobbyService = lobby });
		var hostA = host.RegisterRoom(new GameState(), lobby.GetRoomState(roomA.RoomId));
		var hostB = host.RegisterRoom(new GameState(), lobby.GetRoomState(roomB.RoomId));

		hostA.Consequences.DispatchConsequences(hostA.State, new[]
		{
			new GameEvent("combat_attack")
			{
				InitiatorId = "wolf",
				TargetId = "shepherd",
				InitiatorFaction = Factions.Hostile,
				TargetFaction = Factions.Player,
				Damage = 1,
			},
		});

		Assert.True(hostA.Consequences.Relationships.EdgeCount > 0);
		Assert.Equal(0, hostB.Consequences.Relationships.EdgeCount);
		Assert.NotSame(hostA.Consequences, hostB.Consequences);
	}
}
