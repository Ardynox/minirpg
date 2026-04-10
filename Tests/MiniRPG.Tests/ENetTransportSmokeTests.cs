using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using MiniRPG.Core.Data;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Network;
using Xunit;

namespace MiniRPG.Tests;

public sealed class ENetTransportSmokeTests
{
	[Fact]
	public void ENetTransport_JoinAndCommandRoundTrip_Works()
	{
		using var fixture = CreateRunningFixture();
		var messages = new List<ServerMessage>();
		string? disconnectedReason = null;
		fixture.Client.MessageReceived += message => messages.Add(message);
		fixture.Client.Disconnected += reason => disconnectedReason = reason;

		var connectErr = fixture.Client.Connect("127.0.0.1", fixture.Port, new GameServerConnectRequest
		{
			RoomId = "room-smoke",
			Token = "join-smoke-token",
			IsReconnectClaim = false,
		});
		Assert.Equal(TransportError.Ok, connectErr);

		PumpUntil(() => fixture.Client.State == ENetClientState.InRoom, fixture.Server, fixture.Client, TimeSpan.FromSeconds(5), () => disconnectedReason, messages);
		Assert.Equal(ENetClientState.InRoom, fixture.Client.State);
		Assert.Contains(messages, static message => message is JoinAcceptedMessage);
		Assert.Contains(messages, static message => message is RoomSnapshotMessage);

		messages.Clear();
		var sendOk = fixture.Client.SendCommand(new OpenModalClientCommand
		{
			ModalId = "inventory",
		});
		Assert.True(sendOk);

		PumpUntil(
			() => messages.Count > 0,
			fixture.Server,
			fixture.Client,
			TimeSpan.FromSeconds(5),
			() => disconnectedReason,
			messages);

		Assert.Contains(messages, static message => message is RoomSnapshotMessage);
	}

	[Fact]
	public void ENetTransport_InvalidJoinToken_RejectedWithCode()
	{
		using var fixture = CreateRunningFixture();
		var messages = new List<ServerMessage>();
		string? disconnectedReason = null;
		fixture.Client.MessageReceived += message => messages.Add(message);
		fixture.Client.Disconnected += reason => disconnectedReason = reason;

		var connectErr = fixture.Client.Connect("127.0.0.1", fixture.Port, new GameServerConnectRequest
		{
			RoomId = "room-smoke",
			Token = "bad-join-token",
			IsReconnectClaim = false,
		});
		Assert.Equal(TransportError.Ok, connectErr);

		PumpUntil(
			() => messages.OfType<CommandRejectedMessage>().Any(),
			fixture.Server,
			fixture.Client,
			TimeSpan.FromSeconds(5),
			() => disconnectedReason,
			messages);

		var rejected = Assert.Single(messages.OfType<CommandRejectedMessage>());
		Assert.Equal("invalid_join_token", rejected.Code);
	}

	[Fact]
	public void ENetTransport_InvalidReconnectToken_RejectedWithCode()
	{
		using var fixture = CreateRunningFixture();
		var messages = new List<ServerMessage>();
		string? disconnectedReason = null;
		fixture.Client.MessageReceived += message => messages.Add(message);
		fixture.Client.Disconnected += reason => disconnectedReason = reason;

		var connectErr = fixture.Client.Connect("127.0.0.1", fixture.Port, new GameServerConnectRequest
		{
			RoomId = "room-smoke",
			Token = "bad-reconnect-token",
			IsReconnectClaim = true,
		});
		Assert.Equal(TransportError.Ok, connectErr);

		PumpUntil(
			() => messages.OfType<CommandRejectedMessage>().Any(),
			fixture.Server,
			fixture.Client,
			TimeSpan.FromSeconds(5),
			() => disconnectedReason,
			messages);

		var rejected = Assert.Single(messages.OfType<CommandRejectedMessage>());
		Assert.Equal("invalid_reconnect_token", rejected.Code);
	}

	[Fact]
	public void ENetTransport_ReconnectClaim_WorksAndEmitsReconnectClaimed()
	{
		using var fixture = CreateRunningFixture();
		var messages = new List<ServerMessage>();
		string? disconnectedReason = null;
		fixture.Client.MessageReceived += message => messages.Add(message);
		fixture.Client.Disconnected += reason => disconnectedReason = reason;

		var connectErr = fixture.Client.Connect("127.0.0.1", fixture.Port, new GameServerConnectRequest
		{
			RoomId = "room-smoke",
			Token = "reconnect-smoke-token",
			IsReconnectClaim = true,
		});
		Assert.Equal(TransportError.Ok, connectErr);

		PumpUntil(
			() => fixture.Client.State == ENetClientState.InRoom,
			fixture.Server,
			fixture.Client,
			TimeSpan.FromSeconds(5),
			() => disconnectedReason,
			messages);

		Assert.Contains(messages, static message => message is JoinAcceptedMessage);
		Assert.Contains(messages, static message => message is ReconnectClaimedMessage);
		Assert.Contains(messages, static message => message is RoomSnapshotMessage);
	}

	private static TestFixture CreateRunningFixture()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var host = new DedicatedGameServerHost();
		var room = new RoomRuntimeState
		{
			RoomId = "room-smoke",
			RoomCode = "SMOKE1",
		};
		room.Players["p1"] = new RoomPlayerState
		{
			PlayerSessionId = "p1",
			DisplayName = "SmokeTester",
			JoinToken = "join-smoke-token",
			ReconnectToken = "reconnect-smoke-token",
			Connected = false,
			IsRoomOwner = true,
			PrimaryActorId = "player",
		};
		host.RegisterRoom(new GameState(), room);

		var server = new ENetGameServer(host);
		var port = PickAvailablePort();
		var listenErr = server.Listen("127.0.0.1", port);
		Assert.Equal(TransportError.Ok, listenErr);

		var client = new ENetGameClient();
		return new TestFixture(server, client, port);
	}

	private static int PickAvailablePort()
	{
		for (var i = 0; i < 20; i++)
		{
			var candidate = 24000 + Random.Shared.Next(0, 2000);
			if (candidate != 2455)
				return candidate;
		}

		return 2456;
	}

	private sealed class TestFixture : IDisposable
	{
		public TestFixture(ENetGameServer server, ENetGameClient client, int port)
		{
			Server = server;
			Client = client;
			Port = port;
		}

		public ENetGameServer Server { get; }
		public ENetGameClient Client { get; }
		public int Port { get; }

		public void Dispose()
		{
			Client.Dispose();
			Server.Dispose();
		}
	}

	private static void PumpUntil(
		Func<bool> condition,
		ENetGameServer server,
		ENetGameClient client,
		TimeSpan timeout,
		Func<string?> getDisconnectedReason,
		IReadOnlyList<ServerMessage> messages)
	{
		var sw = Stopwatch.StartNew();
		while (!condition())
		{
			server.Poll();
			client.Poll();

			var disconnectedReason = getDisconnectedReason();
			if (!string.IsNullOrWhiteSpace(disconnectedReason))
			{
				throw new TimeoutException($"Disconnected before condition met: {disconnectedReason}. Messages={messages.Count}");
			}

			if (sw.Elapsed > timeout)
				throw new TimeoutException($"Timed out waiting for ENet condition. State={client.State}, Messages={messages.Count}");

			Thread.Sleep(1);
		}
	}
}
