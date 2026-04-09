using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Module;

namespace MiniRPG.Core.Multiplayer;

public sealed class DedicatedGameServerHostOptions
{
	public string ServerEndpoint { get; set; } = "enet://127.0.0.1:2455";
	public TimeSpan ReconnectGracePeriod { get; set; } = MultiplayerDefaults.ReconnectGracePeriod;
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
		return host;
	}

	public bool TryGetRoom(string roomId, out RoomRuntimeHost room) =>
		_rooms.TryGetValue(roomId, out room!);

	private static GameState CloneState(GameState source)
	{
		var snapshot = SaveModule.BuildSnapshot(source);
		var clone = new GameState();
		SaveModule.ApplySnapshot(clone, snapshot);
		return clone;
	}
}

public sealed class RoomRuntimeHost
{
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
		State.Room = room.Clone();
		RoomRuntimeModule.RefreshControlledActorIds(State);
	}

	public GameServerConnectResult Connect(GameServerConnectRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
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

		player.Connected = true;
		player.ReconnectDeadlineUtc = null;
		RoomRuntimeModule.RefreshControlledActorIds(State);
		if (request.IsReconnectClaim && !string.IsNullOrWhiteSpace(player.PrimaryActorId))
			RoomRuntimeModule.ReclaimPrimaryActor(State, player.PrimaryActorId, player.PlayerSessionId);

		return new GameServerConnectResult
		{
			Ok = true,
			Messages =
			[
				new JoinAcceptedMessage
				{
					RoomId = State.Room.RoomId,
					RoomCode = State.Room.RoomCode,
					PlayerSessionId = player.PlayerSessionId,
				},
				new RoomSnapshotMessage
				{
					Room = State.Room.Clone(),
					Snapshot = SaveModule.BuildSnapshot(State),
				},
			],
		};
	}

	public IReadOnlyList<ServerMessage> Execute(ClientCommand command, DateTimeOffset? now = null)
	{
		ArgumentNullException.ThrowIfNull(command);
		var timestamp = now ?? DateTimeOffset.UtcNow;
		RoomRuntimeModule.CleanupExpiredReservations(State, timestamp);

		var result = ServerActionGateway.Execute(State, command, timestamp);
		if (!result.Ok)
		{
			ServerMessage rejectedMessage = result.Status == ServerActionStatus.Busy
				? new ReservationBusyMessage
				{
					RequestId = command.RequestId,
					ReservationKey = result.ErrorCode ?? string.Empty,
					BusyByPlayerSessionId = command.PlayerSessionId ?? string.Empty,
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
		return
		[
			new StateDeltaMessage
			{
				RequestId = command.RequestId,
				Sequence = State.Room.LastSnapshotSequence,
				Events = [.. result.Events],
			},
			new EventBatchMessage
			{
				RequestId = command.RequestId,
				Events = [.. result.Events],
			},
		];
	}

	public void Disconnect(string playerSessionId, DateTimeOffset? now = null)
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
	}
}
