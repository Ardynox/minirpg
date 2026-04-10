using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using Xunit;

namespace MiniRPG.Tests;

[Trait("Suite", "MultiplayerRegression")]
public sealed class MultiplayerRegressionSuiteTests
{
	[Fact]
	public void Idempotency_InteractionReservation_SamePlayerRepeatClaim_DoesNotDuplicateEntries()
	{
		var state = CreateState();
		var at = new DateTimeOffset(2026, 4, 10, 9, 0, 0, TimeSpan.Zero);

		Assert.True(RoomRuntimeModule.TryReserveInteraction(state, "modal:trade:hero", "owner", at, out var firstConflict));
		Assert.Null(firstConflict);
		var firstExpiresAt = state.Room.InteractionReservations["modal:trade:hero"].ExpiresAtUtc;

		Assert.True(RoomRuntimeModule.TryReserveInteraction(state, "modal:trade:hero", "owner", at.AddSeconds(2), out var secondConflict));
		Assert.Null(secondConflict);

		Assert.Single(state.Room.InteractionReservations);
		Assert.True(state.Room.InteractionReservations["modal:trade:hero"].ExpiresAtUtc > firstExpiresAt);
	}

	[Fact]
	public void Authorization_UnauthorizedActorCommand_IsRejectedAndAuditedWithRequestId()
	{
		var lobby = new InMemoryLobbyService();
		var owner = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Auth",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
		});
		var guest = lobby.JoinRoom(new LobbyJoinRoomRequest
		{
			RoomId = owner.RoomId,
			DisplayName = "Guest",
			PrimaryActorId = "scout",
		});

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions { LobbyService = lobby });
		var roomHost = host.RegisterRoom(CreateState(), lobby.GetRoomState(owner.RoomId));

		Assert.True(roomHost.Connect(new GameServerConnectRequest { RoomId = owner.RoomId, Token = owner.JoinToken }).Ok);
		Assert.True(roomHost.Connect(new GameServerConnectRequest { RoomId = guest.RoomId, Token = guest.JoinToken }).Ok);

		var requestId = "req-auth-unauthorized";
		var messages = roomHost.Execute(new InventoryToggleEquipClientCommand
		{
			RequestId = requestId,
			PlayerSessionId = guest.PlayerSessionId,
			ActorId = "hero",
			InventoryIndex = 0,
		});

		var rejected = Assert.IsType<CommandRejectedMessage>(Assert.Single(messages));
		Assert.Equal(requestId, rejected.RequestId);
		Assert.Equal(ErrorCode.UnauthorizedActor.ToWireCode(), rejected.Code);

		var audit = Assert.Single(roomHost.AuditLogs.Where(entry => string.Equals(entry.RequestId, requestId, StringComparison.Ordinal)));
		Assert.Equal("rejected", audit.Result);
		Assert.Equal(ErrorCode.UnauthorizedActor.ToWireCode(), audit.Code);
	}

	[Fact]
	public void Reconnect_ExpiredClaim_IsRejectedByDedicatedHost()
	{
		var now = new DateTimeOffset(2026, 4, 10, 10, 0, 0, TimeSpan.Zero);
		var lobby = new InMemoryLobbyService();
		var owner = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Reconnect",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
		});

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions
		{
			LobbyService = lobby,
			ReconnectGracePeriod = TimeSpan.FromSeconds(5),
		});
		var roomHost = host.RegisterRoom(CreateState(), lobby.GetRoomState(owner.RoomId));
		Assert.True(roomHost.Connect(new GameServerConnectRequest { RoomId = owner.RoomId, Token = owner.JoinToken }).Ok);

		roomHost.Disconnect(owner.PlayerSessionId, now);
		roomHost.State.Room.Players[owner.PlayerSessionId].ReconnectDeadlineUtc = now.AddSeconds(-1);
		var expiredResult = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = owner.RoomId,
			Token = owner.ReconnectToken,
			IsReconnectClaim = true,
		});

		Assert.False(expiredResult.Ok);
		Assert.Equal(ErrorCode.ReconnectExpired.ToWireCode(), expiredResult.ErrorCode);
		Assert.Empty(expiredResult.Messages);
	}

	[Fact]
	public void SaveLoadRecovery_RestoredRoomState_CanContinueAuthoritySequence()
	{
		var lobby = new InMemoryLobbyService();
		var owner = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Recovery",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
		});

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions { LobbyService = lobby });
		var initialState = CreateState();
		initialState.Actors["hero"].Inventory.Add(CreateEquippableItem());
		var roomHost = host.RegisterRoom(initialState, lobby.GetRoomState(owner.RoomId));
		Assert.True(roomHost.Connect(new GameServerConnectRequest { RoomId = owner.RoomId, Token = owner.JoinToken }).Ok);

		var firstMessages = roomHost.Execute(new InventoryToggleEquipClientCommand
		{
			RequestId = "req-save-before",
			PlayerSessionId = owner.PlayerSessionId,
			ActorId = "hero",
			InventoryIndex = 0,
		});
		Assert.IsType<RoomSnapshotMessage>(firstMessages[0]);
		Assert.True(roomHost.State.Actors["hero"].Inventory[0].Equipped);

		var persisted = SaveModule.BuildSnapshot(roomHost.State);
		var recoveredState = new GameState();
		SaveModule.ApplySnapshot(recoveredState, persisted);

		var recoveredHost = new DedicatedGameServerHost(new DedicatedGameServerHostOptions { LobbyService = lobby });
		var recoveredRoomHost = recoveredHost.RegisterRoom(recoveredState, roomHost.State.Room);
		Assert.True(recoveredRoomHost.Connect(new GameServerConnectRequest { RoomId = owner.RoomId, Token = owner.JoinToken }).Ok);

		var secondMessages = recoveredRoomHost.Execute(new InventoryToggleEquipClientCommand
		{
			RequestId = "req-save-after",
			PlayerSessionId = owner.PlayerSessionId,
			ActorId = "hero",
			InventoryIndex = 0,
		});
		Assert.IsType<RoomSnapshotMessage>(secondMessages[0]);
		Assert.False(recoveredRoomHost.State.Actors["hero"].Inventory[0].Equipped);
	}

	[Fact]
	public void LanSmoke_2Clients_OwnerAndGuestConnect_AndTickSequenceAdvances()
	{
		var roomHost = CreateConnectedRoomHost(clientCount: 2, out var tickets);

		var owner = tickets[0];
		roomHost.State.Actors["hero"].Inventory.Add(CreateEquippableItem());
		var messages = roomHost.Execute(new InventoryToggleEquipClientCommand
		{
			RequestId = "smoke-2p-equip",
			PlayerSessionId = owner.PlayerSessionId,
			ActorId = "hero",
			InventoryIndex = 0,
		});

		var snapshot = Assert.IsType<RoomSnapshotMessage>(messages[0]);
		Assert.True(roomHost.State.Actors["hero"].Inventory[0].Equipped);
		Assert.True(snapshot.Room.LastSnapshotSequence >= 1);
	}

	[Fact]
	public void LanSmoke_3Clients_DelegationAndReclaim_MaintainsAuthority()
	{
		var roomHost = CreateConnectedRoomHost(clientCount: 3, out var tickets);
		var owner = tickets[0];
		var guestA = tickets[1];

		var delegateMessages = roomHost.Execute(new DelegateActorClientCommand
		{
			RequestId = "smoke-3p-delegate",
			PlayerSessionId = owner.PlayerSessionId,
			ActorId = "hero",
			TargetPlayerSessionId = guestA.PlayerSessionId,
		});
		Assert.IsType<RoomSnapshotMessage>(delegateMessages[0]);
		Assert.Equal(guestA.PlayerSessionId, RoomRuntimeModule.GetCurrentControllerPlayerId(roomHost.State, "hero"));

		var reclaimMessages = roomHost.Execute(new ReclaimPrimaryActorClientCommand
		{
			RequestId = "smoke-3p-reclaim",
			PlayerSessionId = owner.PlayerSessionId,
			ActorId = "hero",
		});
		Assert.IsType<RoomSnapshotMessage>(reclaimMessages[0]);
		Assert.Equal(owner.PlayerSessionId, RoomRuntimeModule.GetCurrentControllerPlayerId(roomHost.State, "hero"));
	}

	[Fact]
	public void LanSmoke_4Clients_ReservationContention_ReportsBusyOwnerPrecisely()
	{
		var roomHost = CreateConnectedRoomHost(clientCount: 4, out var tickets);
		var owner = tickets[0];
		var contender = tickets[3];
		const string reservationKey = "modal:chest:1:1:0";

		var ownerOpen = roomHost.Execute(new OpenModalClientCommand
		{
			RequestId = "smoke-4p-open-owner",
			PlayerSessionId = owner.PlayerSessionId,
			ModalId = reservationKey,
		});
		Assert.IsType<RoomSnapshotMessage>(ownerOpen[0]);

		var contenderOpen = roomHost.Execute(new OpenModalClientCommand
		{
			RequestId = "smoke-4p-open-contender",
			PlayerSessionId = contender.PlayerSessionId,
			ModalId = reservationKey,
		});

		var busy = Assert.IsType<ReservationBusyMessage>(Assert.Single(contenderOpen));
		Assert.Equal(reservationKey, busy.ReservationKey);
		Assert.Equal(owner.PlayerSessionId, busy.BusyByPlayerSessionId);
	}

	private static RoomRuntimeHost CreateConnectedRoomHost(int clientCount, out List<LobbyJoinTicket> tickets)
	{
		var lobby = new InMemoryLobbyService();
		var owner = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = $"Smoke-{clientCount}",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
			InitialSnapshot = SaveModule.BuildSnapshot(CreateState()),
		});
		tickets = [owner];

		var actorPool = new[] { "scout", "mule", "ally" };
		for (var i = 1; i < clientCount; i++)
		{
			tickets.Add(lobby.JoinRoom(new LobbyJoinRoomRequest
			{
				RoomId = owner.RoomId,
				DisplayName = $"Guest-{i}",
				PrimaryActorId = actorPool[i - 1],
			}));
		}

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions { LobbyService = lobby });
		var roomHost = host.RegisterRoom(CreateState(), lobby.GetRoomState(owner.RoomId));
		foreach (var ticket in tickets)
		{
			Assert.True(roomHost.Connect(new GameServerConnectRequest
			{
				RoomId = ticket.RoomId,
				Token = ticket.JoinToken,
			}).Ok);
		}

		return roomHost;
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
		ActorModule.Add(state, CreateActor("scout", 5, 1, 0));
		ActorModule.Add(state, CreateActor("mule", 7, 1, 0));
		ActorModule.Add(state, CreateActor("ally", 9, 1, 0));
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
				Id = $"{id}_arm",
				Name = "Arm",
				MaxDurability = 10,
				Durability = 10,
				Material = "flesh",
				BodyPart = BodyParts.Arm,
				EquipLayers = [EquipLayer.Middle],
				EquipSlots =
				[
					new EquipSlot
					{
						LimbId = $"{id}_arm",
						BodyPart = BodyParts.Arm,
						Layer = EquipLayer.Middle,
					},
				],
				Capacities = new Dictionary<string, float>
				{
					[Caps.Consciousness] = 1f,
					[Caps.Manipulation] = 1f,
				},
			},
		],
	};

	private static Item CreateEquippableItem()
	{
		var item = new Item
		{
			Id = "arm_wrap",
			Name = "Arm Wrap",
			Price = 8,
			Category = ItemCategories.Clothing,
			Weight = 0.5f,
			BodyPart = BodyParts.Arm,
			Layer = EquipLayer.Middle,
			CoveredParts = [BodyParts.Arm],
			GrantedSkills = [],
			Tags = new Dictionary<string, int>(),
		};
		item.EnsureRuntimeState();
		return item;
	}

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
