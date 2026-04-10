using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

public sealed class RoomRuntimeModuleTests
{
	[Fact]
	public void SetPlayerConnection_DisconnectAssignsFallbackControllerForDelegableBindings()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		state.Room.Players["guest"].IsRoomOwner = true;
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		state.Room.ActorControlBindings["mule"] = new ActorControlBinding
		{
			PrimaryOwnerPlayerId = "owner",
			CanBeDelegated = false,
		};
		RoomRuntimeModule.RefreshControlledActorIds(state);

		var disconnectedAt = new DateTimeOffset(2026, 4, 10, 1, 0, 0, TimeSpan.Zero);
		RoomRuntimeModule.SetPlayerConnection(state, "owner", connected: false, disconnectedAt);

		Assert.False(state.Room.Players["owner"].Connected);
		Assert.Equal("guest", state.Room.ActorControlBindings["hero"].TemporaryControllerPlayerId);
		Assert.Null(state.Room.ActorControlBindings["mule"].TemporaryControllerPlayerId);
	}

	[Fact]
	public void GetWorldAnchors_ConnectedOnlyIncludesFallbackControlledActors()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		RoomRuntimeModule.AssignPrimaryActor(state, "guest", "scout");
		RoomRuntimeModule.SetPlayerConnection(
			state,
			"owner",
			connected: false,
			new DateTimeOffset(2026, 4, 10, 1, 0, 0, TimeSpan.Zero));

		var anchors = RoomRuntimeModule.GetWorldAnchors(state, connectedOnly: true);

		Assert.Equal(2, anchors.Count);
		Assert.All(anchors, anchor => Assert.Equal("guest", anchor.PlayerSessionId));
		Assert.Contains(anchors, anchor => anchor.ActorId == "hero");
		Assert.Contains(anchors, anchor => anchor.ActorId == "scout");
	}

	[Fact]
	public void GetVisionActors_DeduplicatesActorsAcrossPrimaryAndDelegatedLists()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		state.Room.Players["guest"].DelegatedActorIds.Add("hero");
		RoomRuntimeModule.RefreshControlledActorIds(state);

		var actors = RoomRuntimeModule.GetVisionActors(state, connectedOnly: false);

		Assert.Single(actors.Where(actor => actor.Id == "hero"));
	}

	[Fact]
	public void GetWorldAnchors_ConnectedOnlyFalse_IncludesDisconnectedOwnerAnchor()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		RoomRuntimeModule.SetPlayerConnection(
			state,
			"owner",
			connected: false,
			new DateTimeOffset(2026, 4, 10, 1, 0, 0, TimeSpan.Zero));

		var anchors = RoomRuntimeModule.GetWorldAnchors(state, connectedOnly: false);
		var ownerAnchor = Assert.Single(anchors, anchor => anchor.ActorId == "hero");
		Assert.Equal("owner", ownerAnchor.PlayerSessionId);
		Assert.False(ownerAnchor.Connected);
	}

	[Fact]
	public void Reservations_RenewForSamePlayer_ReportConflicts_AndCleanupExpiredEntries()
	{
		var state = CreateState();
		var createdAt = new DateTimeOffset(2026, 4, 10, 2, 0, 0, TimeSpan.Zero);

		Assert.True(RoomRuntimeModule.TryReserveInteraction(state, "npc:merchant", "owner", createdAt, out var conflict));
		Assert.Null(conflict);
		var originalExpiry = state.Room.InteractionReservations["npc:merchant"].ExpiresAtUtc;

		var renewedAt = createdAt.AddSeconds(3);
		Assert.True(RoomRuntimeModule.TryReserveInteraction(state, "npc:merchant", "owner", renewedAt, out conflict));
		Assert.Null(conflict);
		Assert.True(state.Room.InteractionReservations["npc:merchant"].ExpiresAtUtc > originalExpiry);

		Assert.False(RoomRuntimeModule.TryReserveInteraction(state, "npc:merchant", "guest", renewedAt, out conflict));
		Assert.NotNull(conflict);
		Assert.Equal("owner", conflict!.PlayerSessionId);

		RoomRuntimeModule.CleanupExpiredReservations(state, renewedAt.AddMinutes(1));
		Assert.Empty(state.Room.InteractionReservations);
	}

	[Fact]
	public void ReleaseAndReclaim_RejectWrongPlayerOwnership()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		Assert.True(RoomRuntimeModule.TryReserveInteraction(
			state,
			"container:1",
			"owner",
			new DateTimeOffset(2026, 4, 10, 2, 30, 0, TimeSpan.Zero),
			out _));

		Assert.False(RoomRuntimeModule.ReleaseInteraction(state, "container:1", "guest"));
		Assert.False(RoomRuntimeModule.ReclaimPrimaryActor(state, "hero", "guest"));
		Assert.True(RoomRuntimeModule.ReleaseInteraction(state, "container:1", "owner"));
	}

	[Fact]
	public void LobbyService_RejectsInvalidRoomIdentifiers_AndDuplicateRequestedCodes()
	{
		var lobby = new InMemoryLobbyService();
		var ticket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Alpha",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
			RequestedRoomCode = "AB12CD",
		});

		Assert.Equal("AB12CD", ticket.RoomCode);
		Assert.Throws<InvalidOperationException>(() => lobby.ResolveRoomCode("missing"));
		Assert.Throws<InvalidOperationException>(() => lobby.ReconnectClaim(new LobbyReconnectClaimRequest
		{
			RoomId = ticket.RoomId,
			ReconnectToken = "wrong-token",
		}));
		Assert.Throws<InvalidOperationException>(() => lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RequestedRoomCode = "AB12CD",
		}));
	}

	[Fact]
	public void LobbyService_ReconnectClaim_RejectsExpiredReconnectDeadline()
	{
		var lobby = new InMemoryLobbyService();
		var ticket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Alpha",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
		});

		var room = lobby.GetRoomState(ticket.RoomId);
		room.Players[ticket.PlayerSessionId].Connected = false;
		room.Players[ticket.PlayerSessionId].ReconnectDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
		lobby.UpdateRoomState(room);

		Assert.Throws<InvalidOperationException>(() => lobby.ReconnectClaim(new LobbyReconnectClaimRequest
		{
			RoomId = ticket.RoomId,
			ReconnectToken = ticket.ReconnectToken,
		}));
	}

	[Fact]
	public void DedicatedHost_RejectsUnknownRooms_AndInvalidJoinTokens()
	{
		var state = CreateState();
		var lobby = new InMemoryLobbyService();
		var ticket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Alpha",
			OwnerDisplayName = "Owner",
			ServerEndpoint = "enet://127.0.0.1:2455",
			PrimaryActorId = "hero",
		});
		var host = new DedicatedGameServerHost();
		var roomHost = host.RegisterRoom(state, lobby.GetRoomState(ticket.RoomId));

		var missingRoom = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = "missing-room",
			Token = ticket.JoinToken,
		});

		Assert.False(missingRoom.Ok);
		Assert.Equal("room_not_found", missingRoom.ErrorCode);
		Assert.Empty(missingRoom.Messages);

		var invalidJoin = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = ticket.RoomId,
			Token = "wrong-join-token",
		});

		Assert.False(invalidJoin.Ok);
		Assert.Equal("invalid_join_token", invalidJoin.ErrorCode);
		Assert.Empty(invalidJoin.Messages);

		var invalidReconnect = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = ticket.RoomId,
			Token = "wrong-reconnect-token",
			IsReconnectClaim = true,
		});

		Assert.False(invalidReconnect.Ok);
		Assert.Equal("invalid_reconnect_token", invalidReconnect.ErrorCode);
		Assert.Empty(invalidReconnect.Messages);
	}

	[Fact]
	public void DisconnectedPlayer_ReconnectClaim_RestoresConnectionFlags()
	{
		var state = CreateState();
		var lobby = new InMemoryLobbyService();
		var ticket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Alpha",
			OwnerDisplayName = "Owner",
			ServerEndpoint = "enet://127.0.0.1:2455",
			PrimaryActorId = "hero",
		});
		var host = new DedicatedGameServerHost();
		var roomHost = host.RegisterRoom(state, lobby.GetRoomState(ticket.RoomId));

		var joined = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = ticket.RoomId,
			Token = ticket.JoinToken,
		});
		Assert.True(joined.Ok);

		roomHost.Disconnect(ticket.PlayerSessionId, new DateTimeOffset(2026, 4, 10, 3, 0, 0, TimeSpan.Zero));
		Assert.False(roomHost.State.Room.Players[ticket.PlayerSessionId].Connected);

		var reconnectTicket = lobby.ReconnectClaim(new LobbyReconnectClaimRequest
		{
			RoomId = ticket.RoomId,
			ReconnectToken = ticket.ReconnectToken,
		});
		roomHost.SynchronizeRoom(lobby.GetRoomState(ticket.RoomId));

		var reconnect = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = reconnectTicket.RoomId,
			Token = reconnectTicket.JoinToken,
			IsReconnectClaim = false,
		});

		Assert.True(reconnect.Ok);
		Assert.True(roomHost.State.Room.Players[ticket.PlayerSessionId].Connected);
		Assert.Null(roomHost.State.Room.Players[ticket.PlayerSessionId].ReconnectDeadlineUtc);
	}

	[Fact]
	public void RoomRuntimeHost_StartCombatAndEndCombat_TransitionsModeAndBroadcastsTransitionMessage()
	{
		var state = CreateState();
		var lobby = new InMemoryLobbyService();
		var ticket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Combat",
			OwnerDisplayName = "Owner",
			ServerEndpoint = "enet://127.0.0.1:2455",
			PrimaryActorId = "hero",
		});
		state.Room = lobby.GetRoomState(ticket.RoomId).Clone();
		RoomRuntimeModule.AssignPrimaryActor(state, ticket.PlayerSessionId, "hero");
		state.Room.Players[ticket.PlayerSessionId].Connected = true;

		var host = new DedicatedGameServerHost();
		var roomHost = host.RegisterRoom(state, state.Room);
		roomHost.State.Actors["scout"].Faction = Factions.Hostile;

		var startMessages = roomHost.Execute(new StartCombatClientCommand
		{
			RequestId = "req-start",
			PlayerSessionId = ticket.PlayerSessionId,
			ActorId = "hero",
			TargetActorId = "scout",
		});

		var startTransition = Assert.IsType<ModeTransitionMessage>(Assert.Single(startMessages.Where(msg => msg is ModeTransitionMessage)));
		Assert.Equal(RoomSimulationMode.ExploreRealtime, startTransition.FromMode);
		Assert.Equal(RoomSimulationMode.CombatTurnBased, startTransition.ToMode);
		Assert.Equal("StartCombat", startTransition.Trigger);
		Assert.Equal(RoomSimulationMode.CombatTurnBased, roomHost.State.Room.SimulationMode);
		Assert.Single(roomHost.State.Room.ModeTransitions);

		roomHost.State.Actors.Remove("scout");
		var endMessages = roomHost.Execute(new EndCombatClientCommand
		{
			RequestId = "req-end",
			PlayerSessionId = ticket.PlayerSessionId,
			ActorId = "hero",
		});

		var endTransition = Assert.IsType<ModeTransitionMessage>(Assert.Single(endMessages.Where(msg => msg is ModeTransitionMessage)));
		Assert.Equal(RoomSimulationMode.CombatTurnBased, endTransition.FromMode);
		Assert.Equal(RoomSimulationMode.ExploreRealtime, endTransition.ToMode);
		Assert.Equal("EndCombat", endTransition.Trigger);
		Assert.Equal(RoomSimulationMode.ExploreRealtime, roomHost.State.Room.SimulationMode);
		Assert.Equal(2, roomHost.State.Room.ModeTransitions.Count);
		Assert.Equal("req-end", roomHost.State.Room.ModeTransitions[^1].RequestId);
	}

	[Fact]
	public void ServerActionGateway_CombatCommandsRequireCombatModeForTurnActions()
	{
		var state = CreateState();
		state.Room.SimulationMode = RoomSimulationMode.ExploreRealtime;

		var endTurnResult = ServerActionGateway.Execute(state, new EndTurnClientCommand
		{
			RequestId = "req-end-turn",
			ActorId = "hero",
		});
		Assert.False(endTurnResult.Ok);
		Assert.Equal(ErrorCode.NotInCombat.ToWireCode(), endTurnResult.ErrorCode);

		var useSkillResult = ServerActionGateway.Execute(state, new UseSkillClientCommand
		{
			RequestId = "req-use-skill",
			ActorId = "hero",
			SkillId = "sword_parry",
			TargetType = SkillTargetType.Self,
		});
		Assert.False(useSkillResult.Ok);
		Assert.Equal(ErrorCode.NotInCombat.ToWireCode(), useSkillResult.ErrorCode);
	}

	private static GameState CreateState()
	{
		var state = new GameState
		{
			WorldSeed = 424242,
			PlayerId = "hero",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "test",
			ViewModeId = "single_layer",
			Actors = new Dictionary<string, Actor>(StringComparer.Ordinal),
			World = new WorldMap(424242, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(424242),
		};

		ActorModule.Add(state, CreateActor("hero", 1, 1, 0));
		ActorModule.Add(state, CreateActor("scout", 8, 1, 0));
		ActorModule.Add(state, CreateActor("mule", 5, 5, 0));
		return state;
	}

	private static Actor CreateActor(string id, int x, int y, int z) => new()
	{
		Id = id,
		TemplateId = "player",
		X = x,
		Y = y,
		Z = z,
		Glyph = "@",
		DisplayName = id,
		Faction = Factions.Player,
		FacingX = 1,
		FacingY = 0,
		Limbs =
		[
			new Limb
			{
				Id = $"{id}_core",
				Name = "Core",
				MaxDurability = 10,
				Durability = 10,
				Material = "flesh",
				BodyPart = BodyParts.Torso,
				Capacities = new Dictionary<string, float>
				{
					[Caps.Consciousness] = 1f,
					[Caps.Sight] = 1f,
				},
			},
		],
	};

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
