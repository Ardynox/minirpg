using System.Reflection;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Session;
using MiniRPG.Module.Network;
using Xunit;

namespace MiniRPG.Tests;

public sealed class MultiplayerSessionBackendTests
{
	[Fact]
	public async Task RosterChangedMessage_UpdatesLastRoomAndRaisesEvent()
	{
		await using var backend = new MultiplayerSessionBackend(new ENetGameClient());
		RoomRuntimeState? observedRoom = null;
		backend.RosterChanged += room => observedRoom = room.Clone();

		var room = new RoomRuntimeState
		{
			RoomId = "room-alpha",
			RoomCode = "AB12CD",
		};
		room.Players["owner"] = new RoomPlayerState
		{
			PlayerSessionId = "owner",
			DisplayName = "Owner",
			Connected = true,
		};

		DispatchMessage(backend, new RosterChangedMessage
		{
			Room = room,
		});

		Assert.NotNull(backend.LastRoom);
		Assert.NotNull(observedRoom);
		Assert.Equal("room-alpha", backend.LastRoom!.RoomId);
		Assert.Equal("AB12CD", observedRoom!.RoomCode);
		Assert.True(observedRoom.Players["owner"].Connected);
		Assert.NotSame(room, backend.LastRoom);
		Assert.NotSame(backend.LastRoom, observedRoom);
	}

	[Fact]
	public async Task ReconnectClaimedMessage_RaisesEvent()
	{
		await using var backend = new MultiplayerSessionBackend(new ENetGameClient());
		string? observedPlayerSessionId = null;
		string? observedActorId = null;
		backend.ReconnectClaimed += (playerSessionId, actorId) =>
		{
			observedPlayerSessionId = playerSessionId;
			observedActorId = actorId;
		};

		DispatchMessage(backend, new ReconnectClaimedMessage
		{
			PlayerSessionId = "guest",
			ActorId = "scout",
		});

		Assert.Equal("guest", observedPlayerSessionId);
		Assert.Equal("scout", observedActorId);
	}

	private static void DispatchMessage(MultiplayerSessionBackend backend, ServerMessage message)
	{
		var method = typeof(MultiplayerSessionBackend).GetMethod("HandleMessageReceived", BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method!.Invoke(backend, [message]);
	}
}
