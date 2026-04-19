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
	public MultiplayerTelemetryStore Telemetry { get; set; } = new();
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
		Options.Telemetry.SetPlayerCount(clonedState.Room.RoomId, clonedState.Room.Players.Count);
		host.AppendLifecycleAudit("create", result: "ok", code: null);
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
	private long _serverTick;

	internal RoomRuntimeHost(GameState state, DedicatedGameServerHostOptions options)
	{
		State = state;
		_options = options;
		Consequences = new ServerSideConsequenceDispatcher(
			errorSink: message => AppendLifecycleAudit(
				"consequence_error",
				result: "rejected",
				code: message));
	}

	public GameState State { get; }
	public List<ServerAuditLogEntry> AuditLogs { get; } = [];

	/// <summary>
	/// Per-room authoritative consequence pipeline. Receives every batch of
	/// <see cref="GameEvent"/>s produced by <see cref="ServerActionGateway.Execute"/>
	/// before the snapshot is built and broadcast, so simulation-layer state
	/// (relationships, memories, rumors, incident statistics) can react on the
	/// same tick. Symmetric to the local-session wiring; without this, NPC
	/// combat in multiplayer would never affect the social graph.
	/// </summary>
	public ServerSideConsequenceDispatcher Consequences { get; }

	public void SynchronizeRoom(RoomRuntimeState room)
	{
		ArgumentNullException.ThrowIfNull(room);
		lock (_gate)
		{
			State.Room = room.Clone();
			RoomRuntimeModule.RefreshControlledActorIds(State);
			_options.Telemetry.SetPlayerCount(State.Room.RoomId, State.Room.Players.Count);
			AppendLifecycleAudit("load", result: "ok", code: null);
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
			_options.Telemetry.SetPlayerCount(State.Room.RoomId, State.Room.Players.Count);
			AppendLifecycleAudit(request.IsReconnectClaim ? "reconnect" : "join", player.PlayerSessionId, player.PrimaryActorId, result: "ok", code: null);
			SyncLobbyRoom();

			var messages = new List<ServerMessage>
			{
				new JoinAcceptedMessage
				{
					RoomId = State.Room.RoomId,
					RoomCode = State.Room.RoomCode,
					PlayerSessionId = player.PlayerSessionId,
					ServerTick = _serverTick,
				},
			};
			if (request.IsReconnectClaim && !string.IsNullOrWhiteSpace(player.PrimaryActorId))
			{
				messages.Add(new ReconnectClaimedMessage
				{
					PlayerSessionId = player.PlayerSessionId,
					ActorId = player.PrimaryActorId,
					ServerTick = _serverTick,
				});
			}
			messages.Add(new RoomSnapshotMessage
			{
				Room = State.Room.Clone(),
				Snapshot = SaveModule.BuildSnapshot(State),
				ServerTick = _serverTick,
				SnapshotSequence = State.Room.LastSnapshotSequence,
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
			_serverTick++;
			_options.Telemetry.RecordRttSample(State.Room.RoomId, command.ClientTick, _serverTick);

			if (!string.IsNullOrWhiteSpace(command.ActorId)
				&& State.Room.IsActive
				&& !IsActorAuthorizationBypass(command)
				&& !RoomRuntimeModule.IsAuthorizedToControl(State, command.PlayerSessionId, command.ActorId))
			{
				_options.Telemetry.RecordCommand(State.Room.RoomId, accepted: false);
				AppendAudit(new ServerAuditLogEntry
				{
					Category = ServerAuditCategory.Request,
					Action = command.Kind.ToString(),
					RoomId = State.Room.RoomId,
					PlayerSessionId = command.PlayerSessionId,
					ActorId = command.ActorId,
					RequestId = command.RequestId,
					Code = ErrorCode.UnauthorizedActor.ToWireCode(),
					Result = "rejected",
					ServerTick = _serverTick,
				});
				return
				[
					new CommandRejectedMessage
					{
						RequestId = command.RequestId,
						Reason = "You are not authorized to control this actor.",
						Code = ErrorCode.UnauthorizedActor.ToWireCode(),
						ServerTick = _serverTick,
						SnapshotSequence = State.Room.LastSnapshotSequence,
					},
				];
			}

			var timestamp = now ?? DateTimeOffset.UtcNow;
			RoomRuntimeModule.CleanupExpiredReservations(State, timestamp);

			var modeBefore = State.Room.SimulationMode;
			var result = ServerActionGateway.Execute(State, command, timestamp);
			if (!result.Ok)
			{
				_options.Telemetry.RecordCommand(State.Room.RoomId, accepted: false);
				AppendAudit(new ServerAuditLogEntry
				{
					Category = result.Status == ServerActionStatus.Busy ? ServerAuditCategory.Request : ClassifyCategory(command.Kind),
					Action = command.Kind.ToString(),
					RoomId = State.Room.RoomId,
					PlayerSessionId = command.PlayerSessionId,
					ActorId = command.ActorId,
					RequestId = command.RequestId,
					Code = result.Status == ServerActionStatus.Busy ? ErrorCode.ReservationBusy.ToWireCode() : result.ErrorCode,
					Result = result.Status == ServerActionStatus.Busy ? "busy" : "rejected",
					EventCount = result.Events.Count,
					ServerTick = _serverTick,
				});

				ServerMessage rejectedMessage = result.Status == ServerActionStatus.Busy
					? new ReservationBusyMessage
					{
						RequestId = command.RequestId,
						ReservationKey = result.ReservationKey ?? string.Empty,
						BusyByPlayerSessionId = result.BusyByPlayerSessionId ?? string.Empty,
						ServerTick = _serverTick,
						SnapshotSequence = State.Room.LastSnapshotSequence,
					}
					: new CommandRejectedMessage
					{
						RequestId = command.RequestId,
						Reason = result.Logs.FirstOrDefault() ?? "Command rejected.",
						Code = result.ErrorCode,
						ServerTick = _serverTick,
						SnapshotSequence = State.Room.LastSnapshotSequence,
					};
				return [rejectedMessage];
			}

			_options.Telemetry.RecordCommand(State.Room.RoomId, accepted: true);
			if (result.Events.Any(static evt => string.Equals(evt.Type, "Rollback", StringComparison.OrdinalIgnoreCase)))
				_options.Telemetry.RecordRollback(State.Room.RoomId);
			if (result.Events.Any(static evt => string.Equals(evt.Type, "Resync", StringComparison.OrdinalIgnoreCase)))
				_options.Telemetry.RecordResync(State.Room.RoomId);

			// P0-7: route accepted authoritative events through the
			// per-room consequence dispatcher BEFORE BuildSnapshot so any
			// state mutations the handlers introduce are reflected in the
			// next snapshot/delta sent to clients.
			Consequences.DispatchConsequences(State, result.Events);

			var transition = ResolveModeTransition(command, modeBefore, timestamp);
			if (transition != null)
			{
				if (transition.ToMode == RoomSimulationMode.CombatTurnBased)
					AppendLifecycleAudit("startCombat", command.PlayerSessionId, command.ActorId, command.RequestId, result: "ok", code: null);
				if (transition.ToMode == RoomSimulationMode.ExploreRealtime)
					AppendLifecycleAudit("endCombat", command.PlayerSessionId, command.ActorId, command.RequestId, result: "ok", code: null);
			}
			AppendLifecycleAudit("save", command.PlayerSessionId, command.ActorId, command.RequestId, result: "ok", code: null);
			State.Room.LastSnapshotSequence++;
			SyncLobbyRoom();

			var messages = new List<ServerMessage>
			{
				new RoomSnapshotMessage
				{
					RequestId = command.RequestId,
					Room = State.Room.Clone(),
					Snapshot = SaveModule.BuildSnapshot(State),
					ServerTick = _serverTick,
					SnapshotSequence = State.Room.LastSnapshotSequence,
				},
			};
			if (transition != null)
			{
				messages.Add(new ModeTransitionMessage
				{
					RequestId = command.RequestId,
					FromMode = transition.FromMode,
					ToMode = transition.ToMode,
					Trigger = transition.Trigger,
					TriggerActorId = transition.TriggerActorId,
					TransitionSequence = transition.Sequence,
					ServerTick = _serverTick,
					SnapshotSequence = State.Room.LastSnapshotSequence,
				});
			}
			AppendAudit(new ServerAuditLogEntry
			{
				Category = ClassifyCategory(command.Kind),
				Action = command.Kind.ToString(),
				RoomId = State.Room.RoomId,
				PlayerSessionId = command.PlayerSessionId,
				ActorId = command.ActorId,
				RequestId = command.RequestId,
				Result = "accepted",
				EventCount = result.Events.Count,
				ServerTick = _serverTick,
			});
			if (result.Events.Count > 0)
			{
				messages.Add(new EventBatchMessage
				{
					RequestId = command.RequestId,
					Events = [.. result.Events],
					ServerTick = _serverTick,
					SnapshotSequence = State.Room.LastSnapshotSequence,
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

			_options.Telemetry.SetPlayerCount(State.Room.RoomId, State.Room.Players.Values.Count(static player => player.Connected));
			AppendLifecycleAudit("leave", playerSessionId, null, result: "ok", code: null);
			SyncLobbyRoom();
		}
	}

	internal void AppendLifecycleAudit(
		string action,
		string? playerSessionId = null,
		string? actorId = null,
		string? requestId = null,
		string result = "ok",
		string? code = null)
	{
		AppendAudit(new ServerAuditLogEntry
		{
			Category = ServerAuditCategory.Request,
			Action = action,
			RoomId = State.Room.RoomId,
			PlayerSessionId = playerSessionId,
			ActorId = actorId,
			RequestId = requestId,
			Result = result,
			Code = code,
			ServerTick = _serverTick,
		});
	}

	private RoomModeTransitionRecord? ResolveModeTransition(ClientCommand command, RoomSimulationMode modeBefore, DateTimeOffset timestamp)
	{
		return command.Kind switch
		{
			ClientCommandKind.StartCombat when modeBefore != RoomSimulationMode.CombatTurnBased
				=> RoomRuntimeModule.TryTransitionMode(
					State,
					RoomSimulationMode.CombatTurnBased,
					command.RequestId,
					"StartCombat",
					command.ActorId,
					timestamp),
			ClientCommandKind.EndCombat when modeBefore != RoomSimulationMode.ExploreRealtime
				=> RoomRuntimeModule.TryTransitionMode(
					State,
					RoomSimulationMode.ExploreRealtime,
					command.RequestId,
					"EndCombat",
					command.ActorId,
					timestamp),
			_ => null,
		};
	}

	private void SyncLobbyRoom()
	{
		_options.Telemetry.SetPlayerCount(State.Room.RoomId, State.Room.Players.Count);
		_options.LobbyService?.UpdateRoomState(State.Room);
	}

	private void AppendAudit(ServerAuditLogEntry entry)
	{
		var metrics = _options.Telemetry.GetOrCreateRoom(entry.RoomId);
		MultiplayerTelemetryStore.AttachMetricsMetadata(entry, metrics);
		AuditLogs.Add(entry);
		if (AuditLogs.Count > 1000)
			AuditLogs.RemoveRange(0, AuditLogs.Count - 1000);
	}

	private static bool IsActorAuthorizationBypass(ClientCommand command) => command.Kind switch
	{
		ClientCommandKind.DelegateActor
			or ClientCommandKind.ReclaimPrimaryActor
			or ClientCommandKind.AssignPrimaryActor
			or ClientCommandKind.KickPlayer
			=> true,
		_ => false,
	};

	private static ServerAuditCategory ClassifyCategory(ClientCommandKind kind) => kind switch
	{
		ClientCommandKind.Attack
			or ClientCommandKind.CastSkill
			or ClientCommandKind.StartCombat
			or ClientCommandKind.EndTurn
			or ClientCommandKind.UseSkill
			or ClientCommandKind.EndCombat
			=> ServerAuditCategory.Combat,
		ClientCommandKind.TradeBuy or ClientCommandKind.TradeSell => ServerAuditCategory.Economy,
		_ => ServerAuditCategory.Request,
	};
}
