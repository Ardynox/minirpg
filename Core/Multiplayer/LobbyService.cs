using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Map;

namespace MiniRPG.Core.Multiplayer;

public sealed class LobbyCreateRoomRequest
{
	public string RoomDisplayName { get; set; } = "Room";
	public string OwnerDisplayName { get; set; } = "Host";
	public string ServerEndpoint { get; set; } = "enet://127.0.0.1:2455";
	public string PrimaryActorId { get; set; } = GameState.DefaultPlayerId;
	public string? RequestedRoomCode { get; set; }
	public SaveFile? InitialSnapshot { get; set; }
}

public sealed class LobbyJoinRoomRequest
{
	public string RoomId { get; set; } = string.Empty;
	public string DisplayName { get; set; } = "Player";
	public string PrimaryActorId { get; set; } = string.Empty;
}

public sealed class LobbyReconnectClaimRequest
{
	public string RoomId { get; set; } = string.Empty;
	public string ReconnectToken { get; set; } = string.Empty;
}

public sealed class LobbyRoomSummary
{
	public string RoomId { get; init; } = string.Empty;
	public string RoomCode { get; init; } = string.Empty;
	public string RoomDisplayName { get; init; } = string.Empty;
	public string ServerEndpoint { get; init; } = string.Empty;
	public int PlayerCount { get; init; }
	public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class LobbyRoomResolution
{
	public string RoomId { get; init; } = string.Empty;
	public string RoomCode { get; init; } = string.Empty;
	public string RoomDisplayName { get; init; } = string.Empty;
	public string ServerEndpoint { get; init; } = string.Empty;
	public int PlayerCount { get; init; }
}

public sealed class LobbyJoinTicket
{
	public string RoomId { get; init; } = string.Empty;
	public string RoomCode { get; init; } = string.Empty;
	public string RoomDisplayName { get; init; } = string.Empty;
	public string ServerEndpoint { get; init; } = string.Empty;
	public string PlayerSessionId { get; init; } = string.Empty;
	public string DisplayName { get; init; } = string.Empty;
	public string JoinToken { get; init; } = string.Empty;
	public string ReconnectToken { get; init; } = string.Empty;
	public bool IsRoomOwner { get; init; }
	public RoomRuntimeState Room { get; init; } = new();
}

public interface ILobbyService
{
	LobbyJoinTicket CreateRoom(LobbyCreateRoomRequest request);
	IReadOnlyList<LobbyRoomSummary> ListRooms();
	LobbyRoomResolution ResolveRoomCode(string roomCode);
	LobbyJoinTicket JoinRoom(LobbyJoinRoomRequest request);
	LobbyJoinTicket ReconnectClaim(LobbyReconnectClaimRequest request);
	RoomRuntimeState GetRoomState(string roomId);
	void UpdateRoomState(RoomRuntimeState room);
}

public sealed class InMemoryLobbyService : ILobbyService
{
	private readonly object _gate = new();
	private readonly Dictionary<string, LobbyRoomEntry> _rooms = new(StringComparer.Ordinal);
	private readonly Dictionary<string, string> _roomCodeIndex = new(StringComparer.OrdinalIgnoreCase);

	public LobbyJoinTicket CreateRoom(LobbyCreateRoomRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		lock (_gate)
		{
			var roomId = Guid.NewGuid().ToString("N");
			var roomCode = CreateUniqueRoomCode(request.RequestedRoomCode);
			var playerSessionId = Guid.NewGuid().ToString("N");
			var room = new RoomRuntimeState
			{
				RoomId = roomId,
				RoomCode = roomCode,
			};

			var owner = new RoomPlayerState
			{
				PlayerSessionId = playerSessionId,
				DisplayName = string.IsNullOrWhiteSpace(request.OwnerDisplayName) ? "Host" : request.OwnerDisplayName,
				PrimaryActorId = request.PrimaryActorId ?? string.Empty,
				Connected = true,
				IsRoomOwner = true,
				JoinToken = Guid.NewGuid().ToString("N"),
				ReconnectToken = Guid.NewGuid().ToString("N"),
			};
			room.Players[owner.PlayerSessionId] = owner;
			if (!string.IsNullOrWhiteSpace(owner.PrimaryActorId))
				RoomRuntimeModule.AssignPrimaryActor(new GameState { Room = room }, owner.PlayerSessionId, owner.PrimaryActorId);
			room.Players[owner.PlayerSessionId] = owner;
			RoomRuntimeModule.RefreshControlledActorIds(new GameState { Room = room });

			var entry = new LobbyRoomEntry
			{
				RoomDisplayName = string.IsNullOrWhiteSpace(request.RoomDisplayName) ? roomCode : request.RoomDisplayName,
				ServerEndpoint = string.IsNullOrWhiteSpace(request.ServerEndpoint) ? "enet://127.0.0.1:2455" : request.ServerEndpoint,
				CreatedAtUtc = DateTimeOffset.UtcNow,
				Room = room,
			};
			_rooms[roomId] = entry;
			_roomCodeIndex[roomCode] = roomId;
			return BuildTicket(entry, owner);
		}
	}

	public IReadOnlyList<LobbyRoomSummary> ListRooms()
	{
		lock (_gate)
		{
			return _rooms.Values
				.OrderByDescending(static entry => entry.CreatedAtUtc)
				.Select(static entry => new LobbyRoomSummary
				{
					RoomId = entry.Room.RoomId,
					RoomCode = entry.Room.RoomCode,
					RoomDisplayName = entry.RoomDisplayName,
					ServerEndpoint = entry.ServerEndpoint,
					PlayerCount = entry.Room.Players.Count,
					CreatedAtUtc = entry.CreatedAtUtc,
				})
				.ToArray();
		}
	}

	public LobbyRoomResolution ResolveRoomCode(string roomCode)
	{
		lock (_gate)
		{
			var roomId = ResolveRoomIdByCode(roomCode);
			var entry = _rooms[roomId];
			return new LobbyRoomResolution
			{
				RoomId = entry.Room.RoomId,
				RoomCode = entry.Room.RoomCode,
				RoomDisplayName = entry.RoomDisplayName,
				ServerEndpoint = entry.ServerEndpoint,
				PlayerCount = entry.Room.Players.Count,
			};
		}
	}

	public LobbyJoinTicket JoinRoom(LobbyJoinRoomRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		lock (_gate)
		{
			var entry = GetRoomEntry(request.RoomId);
			var playerSessionId = Guid.NewGuid().ToString("N");
			var player = new RoomPlayerState
			{
				PlayerSessionId = playerSessionId,
				DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? $"Player {entry.Room.Players.Count + 1}" : request.DisplayName,
				PrimaryActorId = request.PrimaryActorId ?? string.Empty,
				Connected = true,
				IsRoomOwner = false,
				JoinToken = Guid.NewGuid().ToString("N"),
				ReconnectToken = Guid.NewGuid().ToString("N"),
			};
			entry.Room.Players[playerSessionId] = player;
			if (!string.IsNullOrWhiteSpace(player.PrimaryActorId))
				entry.Room.ActorControlBindings[player.PrimaryActorId] = new ActorControlBinding
				{
					PrimaryOwnerPlayerId = player.PlayerSessionId,
				};
			RoomRuntimeModule.RefreshControlledActorIds(new GameState { Room = entry.Room });
			return BuildTicket(entry, player);
		}
	}

	public LobbyJoinTicket ReconnectClaim(LobbyReconnectClaimRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		lock (_gate)
		{
			var entry = GetRoomEntry(request.RoomId);
			var player = entry.Room.Players.Values.FirstOrDefault(candidate =>
				string.Equals(candidate.ReconnectToken, request.ReconnectToken, StringComparison.Ordinal));
			if (player == null)
				throw new InvalidOperationException($"Reconnect token is invalid for room '{request.RoomId}'.");
			if (player.ReconnectDeadlineUtc is { } reconnectDeadlineUtc
				&& DateTimeOffset.UtcNow > reconnectDeadlineUtc)
			{
				throw new InvalidOperationException($"Reconnect token expired for room '{request.RoomId}'.");
			}

			player.Connected = true;
			player.JoinToken = Guid.NewGuid().ToString("N");
			player.ReconnectToken = Guid.NewGuid().ToString("N");
			player.ReconnectDeadlineUtc = null;
			RoomRuntimeModule.RefreshControlledActorIds(new GameState { Room = entry.Room });
			return BuildTicket(entry, player);
		}
	}

	public RoomRuntimeState GetRoomState(string roomId)
	{
		lock (_gate)
			return GetRoomEntry(roomId).Room.Clone();
	}

	public void UpdateRoomState(RoomRuntimeState room)
	{
		ArgumentNullException.ThrowIfNull(room);
		ArgumentException.ThrowIfNullOrWhiteSpace(room.RoomId);

		lock (_gate)
		{
			var entry = GetRoomEntry(room.RoomId);
			entry.Room = room.Clone();
		}
	}

	private LobbyRoomEntry GetRoomEntry(string roomId)
	{
		if (!_rooms.TryGetValue(roomId, out var entry))
			throw new InvalidOperationException($"Unknown room id: {roomId}");

		return entry;
	}

	private string ResolveRoomIdByCode(string roomCode)
	{
		if (string.IsNullOrWhiteSpace(roomCode))
			throw new InvalidOperationException("Room code cannot be empty.");
		if (!_roomCodeIndex.TryGetValue(roomCode.Trim(), out var roomId))
			throw new InvalidOperationException($"Unknown room code: {roomCode}");

		return roomId;
	}

	private string CreateUniqueRoomCode(string? requestedRoomCode)
	{
		if (!string.IsNullOrWhiteSpace(requestedRoomCode))
		{
			var normalized = requestedRoomCode.Trim().ToUpperInvariant();
			if (_roomCodeIndex.ContainsKey(normalized))
				throw new InvalidOperationException($"Room code '{normalized}' is already in use.");
			return normalized;
		}

		const int maxAttempts = 100;
		for (var i = 0; i < maxAttempts; i++)
		{
			var candidate = Convert.ToHexString(Guid.NewGuid().ToByteArray()[..3]);
			if (_roomCodeIndex.ContainsKey(candidate))
				continue;

			return candidate;
		}

		throw new InvalidOperationException("Failed to generate a unique room code after maximum attempts.");
	}

	private static LobbyJoinTicket BuildTicket(LobbyRoomEntry entry, RoomPlayerState player) => new()
	{
		RoomId = entry.Room.RoomId,
		RoomCode = entry.Room.RoomCode,
		RoomDisplayName = entry.RoomDisplayName,
		ServerEndpoint = entry.ServerEndpoint,
		PlayerSessionId = player.PlayerSessionId,
		DisplayName = player.DisplayName,
		JoinToken = player.JoinToken,
		ReconnectToken = player.ReconnectToken,
		IsRoomOwner = player.IsRoomOwner,
		Room = entry.Room.Clone(),
	};

	private sealed class LobbyRoomEntry
	{
		public string RoomDisplayName { get; init; } = string.Empty;
		public string ServerEndpoint { get; init; } = string.Empty;
		public DateTimeOffset CreatedAtUtc { get; init; }
		public RoomRuntimeState Room { get; set; } = new();
	}
}
