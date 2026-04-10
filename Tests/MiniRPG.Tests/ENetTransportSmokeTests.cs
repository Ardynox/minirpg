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
		};
		host.RegisterRoom(new GameState(), room);

		using var server = new ENetGameServer(host);
		var port = PickAvailablePort();
		var listenErr = server.Listen("127.0.0.1", port);
		Assert.Equal(TransportError.Ok, listenErr);

		using var client = new ENetGameClient();
		var messages = new List<ServerMessage>();
		string? disconnectedReason = null;
		client.MessageReceived += message => messages.Add(message);
		client.Disconnected += reason => disconnectedReason = reason;

		var connectErr = client.Connect("127.0.0.1", port, new GameServerConnectRequest
		{
			RoomId = "room-smoke",
			Token = "join-smoke-token",
			IsReconnectClaim = false,
		});
		Assert.Equal(TransportError.Ok, connectErr);

		PumpUntil(() => client.State == ENetClientState.InRoom, server, client, TimeSpan.FromSeconds(5), () => disconnectedReason, messages);
		Assert.Equal(ENetClientState.InRoom, client.State);
		Assert.Contains(messages, static message => message is JoinAcceptedMessage);
		Assert.Contains(messages, static message => message is RoomSnapshotMessage);

		messages.Clear();
		var sendOk = client.SendCommand(new OpenModalClientCommand
		{
			ModalId = "inventory",
		});
		Assert.True(sendOk);

		PumpUntil(
			() => messages.Count > 0,
			server,
			client,
			TimeSpan.FromSeconds(5),
			() => disconnectedReason,
			messages);

		Assert.True(messages.Count > 0, "Expected at least one server response for command roundtrip.");
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
