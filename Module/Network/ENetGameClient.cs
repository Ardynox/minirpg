using System;
using enet;
using Godot;
using MiniRPG.Core.Multiplayer;
using static enet.ENet;

namespace MiniRPG.Module.Network;

public sealed unsafe class ENetGameClient : IDisposable
{
	private ENetHost* _host;
	private ENetPeer* _peer;
	private GameServerConnectRequest? _pendingJoin;
	private string? _disconnectReason;

	public ENetClientState State { get; private set; } = ENetClientState.Disconnected;
	public string? PlayerSessionId { get; private set; }
	public string? RoomId { get; private set; }

	public event Action<ServerMessage>? MessageReceived;
	public event Action? Connected;
	public event Action<string>? Disconnected;

	public Error Connect(string address, int port, GameServerConnectRequest joinRequest)
	{
		ArgumentNullException.ThrowIfNull(joinRequest);
		if (State != ENetClientState.Disconnected)
			return Error.AlreadyInUse;

		if (string.IsNullOrWhiteSpace(address) || port <= 0 || port > ushort.MaxValue)
			return Error.InvalidParameter;

		var initError = ENetNativeLifetime.Acquire();
		if (initError != Error.Ok)
			return initError;

		ENetAddress remote = default;
		if (enet_address_set_host_ip(&remote, address) != 0)
		{
			ENetNativeLifetime.Release();
			return Error.CantResolve;
		}

		remote.port = (ushort)port;
		_host = enet_host_create(null, 1, 1, 0, 0);
		if (_host == null)
		{
			ENetNativeLifetime.Release();
			return Error.CantCreate;
		}

		_peer = enet_host_connect(_host, &remote, 1, 0);
		if (_peer == null)
		{
			enet_host_destroy(_host);
			_host = null;
			ENetNativeLifetime.Release();
			return Error.CantConnect;
		}

		_pendingJoin = joinRequest;
		_disconnectReason = null;
		State = ENetClientState.Connecting;
		return Error.Ok;
	}

	public bool SendCommand(ClientCommand command)
	{
		ArgumentNullException.ThrowIfNull(command);
		if (_peer == null || State != ENetClientState.InRoom)
			return false;

		var payload = ProtocolSerializer.SerializeCommand(command with
		{
			PlayerSessionId = PlayerSessionId ?? command.PlayerSessionId,
		});
		return SendReliable(_peer, payload);
	}

	public void Poll()
	{
		if (_host == null)
			return;

		ENetEvent netEvent = default;
		while (enet_host_service(_host, &netEvent, 0) > 0)
			HandleEvent(&netEvent);

		enet_host_flush(_host);
	}

	public void Disconnect()
	{
		if (_peer != null)
		{
			enet_peer_disconnect_now(_peer, 0);
			_peer = null;
		}

		if (_host != null)
		{
			enet_host_destroy(_host);
			_host = null;
			ENetNativeLifetime.Release();
		}

		var reason = _disconnectReason ?? "Transport closed.";
		_pendingJoin = null;
		PlayerSessionId = null;
		RoomId = null;
		_disconnectReason = null;
		var wasConnected = State != ENetClientState.Disconnected;
		State = ENetClientState.Disconnected;
		if (wasConnected)
			Disconnected?.Invoke(reason);
	}

	public void Dispose() => Disconnect();

	private void HandleEvent(ENetEvent* netEvent)
	{
		switch (netEvent->type)
		{
			case ENetEventType.ENET_EVENT_TYPE_CONNECT:
				State = ENetClientState.Joining;
				Connected?.Invoke();
				if (_pendingJoin != null)
					SendReliable(netEvent->peer, ProtocolSerializer.SerializeConnectRequest(_pendingJoin));
				break;

			case ENetEventType.ENET_EVENT_TYPE_RECEIVE:
				HandleReceive(netEvent->packet);
				enet_packet_destroy(netEvent->packet);
				break;

			case ENetEventType.ENET_EVENT_TYPE_DISCONNECT:
				_disconnectReason ??= "Peer disconnected.";
				Disconnect();
				break;
		}
	}

	private void HandleReceive(ENetPacket* packet)
	{
		if (packet == null || packet->data == null || packet->dataLength == 0)
			return;

		var payload = new ReadOnlySpan<byte>(packet->data, (int)packet->dataLength).ToArray();
		var message = ProtocolSerializer.DeserializeMessage(payload);
		if (message == null)
			return;

		switch (message)
		{
			case JoinAcceptedMessage joinAccepted:
				PlayerSessionId = joinAccepted.PlayerSessionId;
				RoomId = joinAccepted.RoomId;
				break;

			case RoomSnapshotMessage:
				if (State == ENetClientState.Joining)
					State = ENetClientState.InRoom;
				break;

			case CommandRejectedMessage rejected when State == ENetClientState.Joining:
				_disconnectReason = rejected.Reason;
				break;
		}

		MessageReceived?.Invoke(message);
	}

	private static bool SendReliable(ENetPeer* peer, byte[] payload)
	{
		if (peer == null || payload.Length == 0)
			return false;

		fixed (byte* payloadPtr = payload)
		{
			var packet = enet_packet_create(payloadPtr, (nuint)payload.Length, (uint)ENetPacketFlag.ENET_PACKET_FLAG_RELIABLE);
			if (packet == null)
				return false;

			if (enet_peer_send(peer, 0, packet) < 0)
			{
				enet_packet_destroy(packet);
				return false;
			}
		}

		return true;
	}
}

public enum ENetClientState
{
	Disconnected,
	Connecting,
	Joining,
	InRoom,
}
