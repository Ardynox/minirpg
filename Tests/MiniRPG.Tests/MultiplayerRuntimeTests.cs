using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MultiplayerRuntimeTests
{
	[Fact]
	public void SaveModule_RoundTripsRoomRuntimeMetadata()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		RoomRuntimeModule.AssignPrimaryActor(state, "guest", "scout");
		Assert.True(RoomRuntimeModule.DelegateActor(state, "hero", "guest"));
		state.Room.RoomId = "room-alpha";
		state.Room.RoomCode = "AB12CD";
		state.Room.LastSnapshotSequence = 17;
		state.Room.InteractionReservations["chest:1"] = new InteractionReservation
		{
			ReservationKey = "chest:1",
			PlayerSessionId = "guest",
			LastHeartbeatUtc = new DateTimeOffset(2026, 4, 9, 12, 0, 0, TimeSpan.Zero),
			ExpiresAtUtc = new DateTimeOffset(2026, 4, 9, 12, 0, 15, TimeSpan.Zero),
		};

		var saveFile = SaveModule.BuildSnapshot(state);
		var restored = new GameState();
		SaveModule.ApplySnapshot(restored, saveFile);

		Assert.Equal("room-alpha", restored.Room.RoomId);
		Assert.Equal("AB12CD", restored.Room.RoomCode);
		Assert.Equal(17, restored.Room.LastSnapshotSequence);
		Assert.Equal(2, restored.Room.Players.Count);
		Assert.Equal("hero", restored.Room.Players["owner"].PrimaryActorId);
		Assert.Contains("hero", restored.Room.Players["guest"].CurrentControllerActorIds);
		Assert.Single(restored.Room.InteractionReservations);
		Assert.Equal("guest", restored.Room.InteractionReservations["chest:1"].PlayerSessionId);
	}

	[Fact]
	public void RoomRuntimeModule_DelegationReconnectAndReservations_WorkTogether()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		RoomRuntimeModule.AssignPrimaryActor(state, "guest", "scout");

		Assert.Contains("hero", state.Room.Players["owner"].CurrentControllerActorIds);
		Assert.True(RoomRuntimeModule.DelegateActor(state, "hero", "guest"));
		Assert.DoesNotContain("hero", state.Room.Players["owner"].CurrentControllerActorIds);
		Assert.Contains("hero", state.Room.Players["guest"].CurrentControllerActorIds);

		var disconnectAt = new DateTimeOffset(2026, 4, 9, 13, 0, 0, TimeSpan.Zero);
		RoomRuntimeModule.SetPlayerConnection(state, "owner", connected: false, disconnectAt);
		Assert.False(state.Room.Players["owner"].Connected);
		Assert.Equal(disconnectAt + MultiplayerDefaults.ReconnectGracePeriod, state.Room.Players["owner"].ReconnectDeadlineUtc);

		RoomRuntimeModule.SetPlayerConnection(state, "owner", connected: true, disconnectAt.AddMinutes(1));
		Assert.True(RoomRuntimeModule.ReclaimPrimaryActor(state, "hero", "owner"));
		Assert.Contains("hero", state.Room.Players["owner"].CurrentControllerActorIds);
		Assert.DoesNotContain("hero", state.Room.Players["guest"].CurrentControllerActorIds);

		Assert.True(RoomRuntimeModule.TryReserveInteraction(state, "npc:merchant", "owner", disconnectAt, out var conflict));
		Assert.Null(conflict);
		Assert.False(RoomRuntimeModule.TryReserveInteraction(state, "npc:merchant", "guest", disconnectAt, out conflict));
		Assert.NotNull(conflict);
		Assert.True(RoomRuntimeModule.ReleaseInteraction(state, "npc:merchant", "owner"));
		Assert.True(RoomRuntimeModule.TryReserveInteraction(state, "npc:merchant", "guest", disconnectAt.AddSeconds(1), out conflict));
	}

	[Fact]
	public void FogOfWarTracker_UsesSharedTeamVisionAcrossControlledActors()
	{
		var state = CreateState();
		RoomRuntimeModule.GetOrCreatePlayer(state, "owner", "Owner");
		RoomRuntimeModule.GetOrCreatePlayer(state, "guest", "Guest");
		RoomRuntimeModule.AssignPrimaryActor(state, "owner", "hero");
		RoomRuntimeModule.AssignPrimaryActor(state, "guest", "scout");
		state.PlayerId = "hero";
		state.PlayerX = state.Actors["hero"].X;
		state.PlayerY = state.Actors["hero"].Y;
		state.PlayerZ = 0;

		state.World!.GetTerrain(1, 1, 0);
		state.World.GetTerrain(12, 1, 0);

		var tracker = new FogOfWarTracker { BaseVisionRadius = 6 };
		tracker.Update(state);

		Assert.NotEqual(PlayerVisionBand.Unknown, tracker.GetVisionBand(1, 1, 0));
		Assert.NotEqual(PlayerVisionBand.Unknown, tracker.GetVisionBand(12, 1, 0));
	}

	[Fact]
	public void LobbyAndDedicatedServer_SupportCreateJoinReconnectAndAuthorityCommands()
	{
		var state = CreateState();
		var lobby = new InMemoryLobbyService();
		var ownerTicket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Alpha",
			OwnerDisplayName = "Owner",
			ServerEndpoint = "enet://127.0.0.1:2455",
			PrimaryActorId = "hero",
		});
		var guestTicket = lobby.JoinRoom(new LobbyJoinRoomRequest
		{
			RoomId = ownerTicket.RoomId,
			DisplayName = "Guest",
			PrimaryActorId = "scout",
		});

		var resolution = lobby.ResolveRoomCode(ownerTicket.RoomCode);
		Assert.Equal(ownerTicket.RoomId, resolution.RoomId);
		Assert.Equal(2, resolution.PlayerCount);

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions
		{
			ServerEndpoint = ownerTicket.ServerEndpoint,
			LobbyService = lobby,
		});
		var roomHost = host.RegisterRoom(state, lobby.GetRoomState(ownerTicket.RoomId));

		var ownerConnect = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = ownerTicket.RoomId,
			Token = ownerTicket.JoinToken,
		});
		Assert.True(ownerConnect.Ok);
		Assert.Contains(ownerConnect.Messages, static message => message is JoinAcceptedMessage);

		var guestConnect = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = guestTicket.RoomId,
			Token = guestTicket.JoinToken,
		});
		Assert.True(guestConnect.Ok);

		roomHost.State.Actors["hero"].Inventory.Add(CreateEquippableItem());
		var messages = roomHost.Execute(new InventoryToggleEquipClientCommand
		{
			RequestId = "req-equip",
			PlayerSessionId = ownerTicket.PlayerSessionId,
			ActorId = "hero",
			InventoryIndex = 0,
		});
		var snapshot = Assert.IsType<RoomSnapshotMessage>(messages[0]);
		Assert.Equal(1, snapshot.Room.LastSnapshotSequence);
		Assert.True(roomHost.State.Actors["hero"].Inventory[0].Equipped);

		roomHost.Disconnect(ownerTicket.PlayerSessionId, DateTimeOffset.UtcNow);
		Assert.False(roomHost.State.Room.Players[ownerTicket.PlayerSessionId].Connected);

		var reconnectTicket = lobby.ReconnectClaim(new LobbyReconnectClaimRequest
		{
			RoomId = ownerTicket.RoomId,
			ReconnectToken = ownerTicket.ReconnectToken,
		});
		roomHost.SynchronizeRoom(lobby.GetRoomState(ownerTicket.RoomId));
		var reconnect = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = reconnectTicket.RoomId,
			Token = reconnectTicket.JoinToken,
		});
		Assert.True(reconnect.Ok);
	}

	[Fact]
	public void LobbyCreateRoom_FromSnapshot_AutoAssignsPrimaryActors()
	{
		var state = CreateState();
		var lobby = new InMemoryLobbyService();

		var ownerTicket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Alpha",
			OwnerDisplayName = "Owner",
			ServerEndpoint = "enet://127.0.0.1:2455",
			PrimaryActorId = string.Empty,
			InitialSnapshot = SaveModule.BuildSnapshot(state),
		});
		var guestTicket = lobby.JoinRoom(new LobbyJoinRoomRequest
		{
			RoomId = ownerTicket.RoomId,
			DisplayName = "Guest",
			PrimaryActorId = string.Empty,
		});

		Assert.Equal("hero", ownerTicket.PrimaryActorId);
		Assert.Equal("scout", guestTicket.PrimaryActorId);

		var roomState = lobby.GetRoomState(ownerTicket.RoomId);
		Assert.Equal("hero", roomState.Players[ownerTicket.PlayerSessionId].PrimaryActorId);
		Assert.Equal("scout", roomState.Players[guestTicket.PlayerSessionId].PrimaryActorId);
	}

	[Fact]
	public void DedicatedHost_BusyReservation_ReturnsActualOwnerPlayerSessionId()
	{
		var lobby = new InMemoryLobbyService();
		var ownerTicket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "Alpha",
			OwnerDisplayName = "Owner",
			ServerEndpoint = "enet://127.0.0.1:2455",
			PrimaryActorId = "hero",
		});
		var guestTicket = lobby.JoinRoom(new LobbyJoinRoomRequest
		{
			RoomId = ownerTicket.RoomId,
			DisplayName = "Guest",
			PrimaryActorId = "scout",
		});
		var state = CreateState();

		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions
		{
			ServerEndpoint = ownerTicket.ServerEndpoint,
			LobbyService = lobby,
		});
		var roomHost = host.RegisterRoom(state, lobby.GetRoomState(ownerTicket.RoomId));

		Assert.True(roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = ownerTicket.RoomId,
			Token = ownerTicket.JoinToken,
		}).Ok);
		Assert.True(roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = guestTicket.RoomId,
			Token = guestTicket.JoinToken,
		}).Ok);

		var reservationKey = "container:ground:1:1:0:shared_chest";
		var ownerTake = roomHost.Execute(new OpenModalClientCommand
		{
			RequestId = "req-owner",
			PlayerSessionId = ownerTicket.PlayerSessionId,
			ModalId = reservationKey,
		});
		Assert.IsType<RoomSnapshotMessage>(ownerTake[0]);

		var guestBusy = roomHost.Execute(new OpenModalClientCommand
		{
			RequestId = "req-guest",
			PlayerSessionId = guestTicket.PlayerSessionId,
			ModalId = reservationKey,
		});

		var busy = Assert.IsType<ReservationBusyMessage>(Assert.Single(guestBusy));
		Assert.Equal(ownerTicket.PlayerSessionId, busy.BusyByPlayerSessionId);
		Assert.Equal(reservationKey, busy.ReservationKey);
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

		ActorModule.Add(state, CreatePlayerActor("hero", 1, 1, 0));
		ActorModule.Add(state, CreatePlayerActor("scout", 12, 1, 0));
		return state;
	}

	private static Actor CreatePlayerActor(string id, int x, int y, int z) => new()
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

	private static Item CreateEquippableItem() => new()
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
