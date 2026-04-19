using System;
using System.Collections.Generic;
using System.Linq;
using enet;
using MiniRPG.Core.Multiplayer;
using static enet.ENet;

namespace MiniRPG.Module.Network;

public sealed unsafe class ENetGameServer : IDisposable
{
	private readonly DedicatedGameServerHost _gameHost;
	private readonly Dictionary<nuint, PeerSession> _sessions = [];
	private ENetHost* _host;

	public ENetGameServer(DedicatedGameServerHost gameHost)
	{
		_gameHost = gameHost ?? throw new ArgumentNullException(nameof(gameHost));
	}

	public bool IsListening => _host != null;

	/// <summary>
	/// Diagnostic sink. Subscribed by the server process (or test harness) so
	/// dirty-packet warnings reach the operator log without polluting the
	/// transport with Godot-specific calls. Raised when a packet's payload
	/// fails to deserialize or when an event handler swallows an exception.
	/// </summary>
	public event Action<string>? Trace;

	public TransportError Listen(string address, int port, int maxClients = 8)
	{
		if (_host != null)
			return TransportError.AlreadyInUse;
		if (string.IsNullOrWhiteSpace(address) || port <= 0 || port > ushort.MaxValue || maxClients <= 0)
			return TransportError.InvalidParameter;

		var initError = ENetNativeLifetime.Acquire();
		if (initError != TransportError.Ok)
			return initError;

		ENetAddress bindAddress = default;
		if (enet_address_set_host_ip(&bindAddress, address) != 0)
		{
			ENetNativeLifetime.Release();
			return TransportError.CantResolve;
		}

		bindAddress.port = (ushort)port;
		_host = enet_host_create(&bindAddress, (nuint)maxClients, 1, 0, 0);
		if (_host == null)
		{
			ENetNativeLifetime.Release();
			return TransportError.CantCreate;
		}

		return TransportError.Ok;
	}

	public void Poll()
	{
		if (_host == null)
			return;

		ENetEvent netEvent = default;
		while (enet_host_service(_host, &netEvent, 0) > 0)
		{
			try
			{
				HandleEvent(&netEvent);
			}
			catch (Exception ex)
			{
				// Top-level guard: a dirty packet or unexpected gameplay-side
				// exception must never crash the dedicated server's poll loop.
				// Anything that escapes the per-handler catches inside the
				// command pipeline lands here and is reported via Trace.
				Trace?.Invoke($"[ENetGameServer] Unhandled exception during HandleEvent: {ex.GetType().Name}: {ex.Message}");
			}
		}

		enet_host_flush(_host);
	}

	public void Dispose()
	{
		if (_host == null)
			return;

		foreach (var session in _sessions.Values.Where(static session => session.RoomHost != null && session.PlayerSessionId != null))
			session.RoomHost!.Disconnect(session.PlayerSessionId!);
		_sessions.Clear();

		enet_host_destroy(_host);
		_host = null;
		ENetNativeLifetime.Release();
	}

	private void HandleEvent(ENetEvent* netEvent)
	{
		switch (netEvent->type)
		{
			case ENetEventType.ENET_EVENT_TYPE_CONNECT:
				_sessions[(nuint)netEvent->peer] = new PeerSession();
				break;

			case ENetEventType.ENET_EVENT_TYPE_RECEIVE:
				HandleReceive(netEvent->peer, netEvent->packet);
				enet_packet_destroy(netEvent->packet);
				break;

			case ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
				HandleDisconnect(netEvent->peer);
				break;
		}
	}

	private void HandleReceive(ENetPeer* peer, ENetPacket* packet)
	{
		if (!_sessions.TryGetValue((nuint)peer, out var session) || packet == null || packet->data == null || packet->dataLength == 0)
			return;

		var payload = new ReadOnlySpan<byte>(packet->data, (int)packet->dataLength).ToArray();
		if (session.RoomHost == null)
		{
			HandleJoinRequest(peer, session, payload);
			return;
		}

		var command = ProtocolSerializer.DeserializeCommand(payload);
		if (command == null)
		{
			Trace?.Invoke($"[ENetGameServer] Discarded malformed command from peer {(nuint)peer} ({payload.Length} bytes).");
			SendMessage(peer, new CommandRejectedMessage
			{
				Reason = "Failed to deserialize command.",
				Code = ErrorCode.DeserializationError.ToWireCode(),
			});
			return;
		}

		var normalized = command with
		{
			PlayerSessionId = session.PlayerSessionId,
		};
		var messages = session.RoomHost.Execute(normalized);
		if (messages.Count == 0)
			return;

		var shouldBroadcast = messages.Any(static message => message is RoomSnapshotMessage);
		if (shouldBroadcast)
		{
			foreach (var roomPeer in EnumerateRoomPeers(session.RoomId!))
				SendMessages((ENetPeer*)roomPeer, messages);
			return;
		}

		SendMessages(peer, messages);
	}

	private void HandleJoinRequest(ENetPeer* peer, PeerSession session, byte[] payload)
	{
		var request = ProtocolSerializer.DeserializeConnectRequest(payload);
		if (request == null)
		{
			Trace?.Invoke($"[ENetGameServer] Discarded malformed join request from peer {(nuint)peer} ({payload.Length} bytes).");
			SendMessage(peer, new CommandRejectedMessage
			{
				Reason = "Invalid join request.",
				Code = ErrorCode.InvalidJoinRequest.ToWireCode(),
			});
			return;
		}

		if (!_gameHost.TryGetRoom(request.RoomId, out var roomHost))
		{
			SendMessage(peer, new CommandRejectedMessage
			{
				Reason = "Room not found.",
				Code = ErrorCode.RoomNotFound.ToWireCode(),
			});
			return;
		}

		var result = roomHost.Connect(request);
		if (!result.Ok)
		{
			SendMessage(peer, new CommandRejectedMessage
			{
				Reason = result.ErrorReason ?? "Connection failed.",
				Code = result.ErrorCode ?? "connect_failed",
			});
			return;
		}

		session.RoomHost = roomHost;
		session.RoomId = request.RoomId;
		foreach (var message in result.Messages)
		{
			if (message is JoinAcceptedMessage joinAccepted)
				session.PlayerSessionId = joinAccepted.PlayerSessionId;
			SendMessage(peer, message);
		}

		var rosterNotification = new RosterChangedMessage
		{
			Room = roomHost.State.Room.Clone(),
		};
		foreach (var existingPeer in EnumerateRoomPeers(request.RoomId))
		{
			if (existingPeer == (nuint)peer)
				continue;
			SendMessage((ENetPeer*)existingPeer, rosterNotification);
		}
	}

	private void HandleDisconnect(ENetPeer* peer)
	{
		if (!_sessions.Remove((nuint)peer, out var session))
			return;
		if (session.RoomHost == null || string.IsNullOrWhiteSpace(session.PlayerSessionId))
			return;

		session.RoomHost.Disconnect(session.PlayerSessionId);

		var rosterNotification = new RosterChangedMessage
		{
			Room = session.RoomHost.State.Room.Clone(),
		};
		foreach (var remainingPeer in EnumerateRoomPeers(session.RoomId!))
			SendMessage((ENetPeer*)remainingPeer, rosterNotification);
	}

	private IEnumerable<nuint> EnumerateRoomPeers(string roomId)
	{
		foreach (var (peerKey, session) in _sessions)
		{
			if (!string.Equals(session.RoomId, roomId, StringComparison.Ordinal))
				continue;
			yield return peerKey;
		}
	}

	private static void SendMessages(ENetPeer* peer, IReadOnlyList<ServerMessage> messages)
	{
		foreach (var message in messages)
			SendMessage(peer, message);
	}

	private static void SendMessage(ENetPeer* peer, ServerMessage message)
	{
		var payload = ProtocolSerializer.SerializeMessage(message);
		fixed (byte* payloadPtr = payload)
		{
			var packet = enet_packet_create(payloadPtr, (nuint)payload.Length, (uint)ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE);
			if (packet == null)
				return;

			if (enet_peer_send(peer, 0, packet) < 0)
				enet_packet_destroy(packet);
		}
	}

	private sealed class PeerSession
	{
		public RoomRuntimeHost? RoomHost { get; set; }
		public string? RoomId { get; set; }
		public string? PlayerSessionId { get; set; }
	}
}
