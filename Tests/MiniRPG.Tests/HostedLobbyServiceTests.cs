using System;
using MiniRPG.Core.Multiplayer;
using Xunit;

namespace MiniRPG.Tests;

/// <summary>
/// Regression for P0-6: <see cref="HostedLobbyService.JoinRoom"/> previously
/// minted a fresh PlayerSessionId / JoinToken in the inner lobby without
/// pushing the updated <see cref="RoomRuntimeState"/> back to the
/// <see cref="DedicatedGameServerHost"/>. Subsequent
/// <see cref="RoomRuntimeHost.Connect"/> calls then failed with
/// invalid_join_token, so the second player could never enter the room.
/// </summary>
public sealed class HostedLobbyServiceTests
{
	[Fact]
	public void JoinRoom_SecondPlayer_GameHostAcceptsJoinTokenAfterSync()
	{
		var (lobby, host, ownerTicket) = CreateLobby();

		var guestTicket = lobby.JoinRoom(new LobbyJoinRoomRequest
		{
			RoomId = ownerTicket.RoomId,
			DisplayName = "Guest",
			PrimaryActorId = "scout",
		});

		Assert.True(host.TryGetRoom(ownerTicket.RoomId, out var roomHost));
		Assert.Contains(guestTicket.PlayerSessionId, roomHost.State.Room.Players.Keys);
		Assert.Equal(
			guestTicket.JoinToken,
			roomHost.State.Room.Players[guestTicket.PlayerSessionId].JoinToken);

		var ownerConnect = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = ownerTicket.RoomId,
			Token = ownerTicket.JoinToken,
		});
		Assert.True(ownerConnect.Ok, ownerConnect.ErrorReason);

		var guestConnect = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = guestTicket.RoomId,
			Token = guestTicket.JoinToken,
		});

		Assert.True(
			guestConnect.Ok,
			$"Guest join must succeed after HostedLobbyService.SynchronizeRoom; "
			+ $"got code={guestConnect.ErrorCode}, reason={guestConnect.ErrorReason}");
		Assert.NotEqual("invalid_join_token", guestConnect.ErrorCode);
	}

	[Fact]
	public void ReconnectClaim_SyncsRotatedReconnectTokenIntoGameHost()
	{
		var (lobby, host, ownerTicket) = CreateLobby();
		Assert.True(host.TryGetRoom(ownerTicket.RoomId, out var roomHost));

		var initialOwnerJoin = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = ownerTicket.RoomId,
			Token = ownerTicket.JoinToken,
		});
		Assert.True(initialOwnerJoin.Ok, initialOwnerJoin.ErrorReason);

		roomHost.Disconnect(ownerTicket.PlayerSessionId, DateTimeOffset.UtcNow);

		var reconnectTicket = lobby.ReconnectClaim(new LobbyReconnectClaimRequest
		{
			RoomId = ownerTicket.RoomId,
			ReconnectToken = ownerTicket.ReconnectToken,
		});

		var rotatedToken = roomHost.State.Room.Players[ownerTicket.PlayerSessionId].JoinToken;
		Assert.Equal(reconnectTicket.JoinToken, rotatedToken);

		var reconnectResult = roomHost.Connect(new GameServerConnectRequest
		{
			RoomId = reconnectTicket.RoomId,
			Token = reconnectTicket.JoinToken,
		});
		Assert.True(
			reconnectResult.Ok,
			$"Reconnect must succeed after HostedLobbyService.SynchronizeRoom; "
			+ $"got code={reconnectResult.ErrorCode}, reason={reconnectResult.ErrorReason}");
	}

	[Fact]
	public void JoinRoom_AppendsLifecycleAuditAfterSync()
	{
		var (lobby, host, ownerTicket) = CreateLobby();
		Assert.True(host.TryGetRoom(ownerTicket.RoomId, out var roomHost));

		var auditCountBeforeJoin = roomHost.AuditLogs.Count;
		var guestTicket = lobby.JoinRoom(new LobbyJoinRoomRequest
		{
			RoomId = ownerTicket.RoomId,
			DisplayName = "Guest",
			PrimaryActorId = "scout",
		});

		Assert.True(roomHost.AuditLogs.Count > auditCountBeforeJoin);
		Assert.Contains(
			roomHost.AuditLogs,
			entry => string.Equals(entry.Action, "join", StringComparison.Ordinal)
				&& string.Equals(entry.PlayerSessionId, guestTicket.PlayerSessionId, StringComparison.Ordinal));
	}

	private static (HostedLobbyService Lobby, DedicatedGameServerHost Host, LobbyJoinTicket OwnerTicket) CreateLobby()
	{
		var inner = new InMemoryLobbyService();
		var host = new DedicatedGameServerHost(new DedicatedGameServerHostOptions());
		var lobby = new HostedLobbyService(inner, host);
		host.Options.LobbyService = lobby;

		var ownerTicket = lobby.CreateRoom(new LobbyCreateRoomRequest
		{
			RoomDisplayName = "TestRoom",
			OwnerDisplayName = "Owner",
			PrimaryActorId = "hero",
		});

		return (lobby, host, ownerTicket);
	}
}
