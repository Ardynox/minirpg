using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Map;
using MiniRPG.Module;

namespace MiniRPG.Core.Multiplayer;

public sealed class DedicatedGameServerHostOptions
{
	public string ServerEndpoint { get; set; } = "enet://127.0.0.1:2455";
	public TimeSpan ReconnectGracePeriod { get; set; } = MultiplayerDefaults.ReconnectGracePeriod;
	public ILobbyService? LobbyService { get; set; }
}

public sealed class GameServerConnectRequest
{
	public string RoomId { get; set; } = string.Empty;
	public string Token { get; set; } = string.Empty;
	public bool IsReconnectClaim { get; set; }
}

public sealed class GameServerConnectResult
{
	public bool Ok { get; init; }
	public string? ErrorCode { get; init; }
	public string? ErrorReason { get; init; }
	public List<ServerMessage> Messages { get; init; } = [];
}

public sealed class DedicatedGameServerHost
{
	private readonly Dictionary<string, RoomRuntimeHost> _rooms = new(StringComparer.Ordinal);

	public DedicatedGameServerHost(DedicatedGameServerHostOptions? options = null)
	{
		Options = options ?? new DedicatedGameServerHostOptions();
	}

	public DedicatedGameServerHostOptions Options { get; }

	public RoomRuntimeHost RegisterRoom(GameState initialState, RoomRuntimeState room)
	{
		ArgumentNullException.ThrowIfNull(initialState);
		ArgumentNullException.ThrowIfNull(room);

		var clonedState = CloneState(initialState);
		clonedState.Room = room.Clone();
		RoomRuntimeModule.RefreshControlledActorIds(clonedState);
		RoomRuntimeModule.SyncLegacyPlayerAlias(clonedState);

		var host = new RoomRuntimeHost(clonedState, Options);
		_rooms[clonedState.Room.RoomId] = host;
		Options.LobbyService?.UpdateRoomState(clonedState.Room);
		return host;
	}

	public bool TryGetRoom(string roomId, out RoomRuntimeHost room) =>
		_rooms.TryGetValue(roomId, out room!);

	private static GameState CloneState(GameState source)
	{
		var snapshot = SaveModule.BuildSnapshot(source);
		return CreateStateFromSnapshot(snapshot);
	}

	internal static GameState CreateStateFromSnapshot(SaveFile? snapshot)
	{
		var state = new GameState();
		if (snapshot != null)
			SaveModule.ApplySnapshot(state, snapshot);

		return state;
	}
}

public sealed class RoomRuntimeHost
{
	private readonly object _gate = new();
	private readonly DedicatedGameServerHostOptions _options;

	internal RoomRuntimeHost(GameState state, DedicatedGameServerHostOptions options)
	{
		State = state;
		_options = options;
	}

	public GameState State { get; }

	public void SynchronizeRoom(RoomRuntimeState room)
	{
		ArgumentNullException.ThrowIfNull(room);
		lock (_gate)
		{
			State.Room = room.Clone();
			RoomRuntimeModule.RefreshControlledActorIds(State);
		}
	}

	public GameServerConnectResult Connect(GameServerConnectRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		lock (_gate)
		{
			var timestamp = DateTimeOffset.UtcNow;
			if (!string.Equals(State.Room.RoomId, request.RoomId, StringComparison.Ordinal))
			{
				return new GameServerConnectResult
				{
					ErrorCode = "room_not_found",
					ErrorReason = "Room does not exist on this dedicated server.",
				};
			}

			var player = request.IsReconnectClaim
				? State.Room.Players.Values.FirstOrDefault(candidate =>
					string.Equals(candidate.ReconnectToken, request.Token, StringComparison.Ordinal))
				: State.Room.Players.Values.FirstOrDefault(candidate =>
					string.Equals(candidate.JoinToken, request.Token, StringComparison.Ordinal));
			if (player == null)
			{
				return new GameServerConnectResult
				{
					ErrorCode = request.IsReconnectClaim ? "invalid_reconnect_token" : "invalid_join_token",
					ErrorReason = "The supplied join token is invalid.",
				};
			}
			if (request.IsReconnectClaim
				&& player.ReconnectDeadlineUtc is { } reconnectDeadlineUtc
				&& timestamp > reconnectDeadlineUtc)
			{
				return new GameServerConnectResult
				{
					ErrorCode = "reconnect_expired",
					ErrorReason = "The reconnect claim has expired.",
				};
			}

			player.Connected = true;
			player.ReconnectDeadlineUtc = null;
			RoomRuntimeModule.RefreshControlledActorIds(State);
			if (request.IsReconnectClaim && !string.IsNullOrWhiteSpace(player.PrimaryActorId))
				RoomRuntimeModule.ReclaimPrimaryActor(State, player.PrimaryActorId, player.PlayerSessionId);
			SyncLobbyRoom();

			var messages = new List<ServerMessage>
			{
				new JoinAcceptedMessage
				{
					RoomId = State.Room.RoomId,
					RoomCode = State.Room.RoomCode,
					PlayerSessionId = player.PlayerSessionId,
				},
			};
			if (request.IsReconnectClaim && !string.IsNullOrWhiteSpace(player.PrimaryActorId))
			{
				messages.Add(new ReconnectClaimedMessage
				{
					PlayerSessionId = player.PlayerSessionId,
					ActorId = player.PrimaryActorId,
				});
			}
			messages.Add(new RoomSnapshotMessage
			{
				Room = State.Room.Clone(),
				Snapshot = SaveModule.BuildSnapshot(State),
			});

			return new GameServerConnectResult
			{
				Ok = true,
				Messages = messages,
			};
		}
	}

	public IReadOnlyList<ServerMessage> Execute(ClientCommand command, DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(command);
		lock (_gate)
		{
			if (!string.IsNullOrWhiteSpace(command.ActorId)
				&& State.Room.IsActive
				&& !RoomRuntimeModule.IsAuthorizedToControl(State, command.PlayerSessionId, command.ActorId))
			{
				return
				[
					new CommandRejectedMessage
					{
						RequestId = command.RequestId,
						Reason = "You are not authorized to control this actor.",
						Code = "unauthorized_actor",
					},
				];
			}

			var timestamp = now ?? DateTimeOffset.UtcNow;
			RoomRuntimeModule.CleanupExpiredReservations(State, timestamp);

			var result = ServerActionGateway.Execute(State, command, timestamp);
			if (!result.Ok)
			{
				ServerMessage rejectedMessage = result.Status == ServerActionStatus.Busy
					? new ReservationBusyMessage
					{
						RequestId = command.RequestId,
						ReservationKey = result.ReservationKey ?? string.Empty,
						BusyByPlayerSessionId = result.BusyByPlayerSessionId ?? string.Empty,
					}
					: new CommandRejectedMessage
					{
						RequestId = command.RequestId,
						Reason = result.Logs.FirstOrDefault() ?? "Command rejected.",
						Code = result.ErrorCode,
					};
				return [rejectedMessage];
			}

			State.Room.LastSnapshotSequence++;
			SyncLobbyRoom();

			var messages = new List<ServerMessage>
			{
				new RoomSnapshotMessage
				{
					RequestId = command.RequestId,
					Room = State.Room.Clone(),
					Snapshot = SaveModule.BuildSnapshot(State),
				},
			};
			if (result.Events.Count > 0)
			{
				messages.Add(new EventBatchMessage
				{
					RequestId = command.RequestId,
					Events = [.. result.Events],
				});
			}

			return messages;
		}
	}

	public void Disconnect(string playerSessionId, DateTimeOffset? now = null)
	{
		lock (_gate)
		{
			RoomRuntimeModule.SetPlayerConnection(
				State,
				playerSessionId,
				connected: false,
				now ?? DateTimeOffset.UtcNow,
				_options.ReconnectGracePeriod);

			var reservationKeys = State.Room.InteractionReservations
				.Where(entry => string.Equals(entry.Value.PlayerSessionId, playerSessionId, StringComparison.Ordinal))
				.Select(static entry => entry.Key)
				.ToArray();
			foreach (var reservationKey in reservationKeys)
				State.Room.InteractionReservations.Remove(reservationKey);

			SyncLobbyRoom();
		}
	}

	private void SyncLobbyRoom() => _options.LobbyService?.UpdateRoomState(State.Room);
}
