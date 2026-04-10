using System;
using System.Threading;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Network;

namespace MiniRPG.Core.Session;

public sealed class MultiplayerSessionConnectRequest
{
	public string ServerEndpoint { get; init; } = string.Empty;
	public string RoomId { get; init; } = string.Empty;
	public string Token { get; init; } = string.Empty;
	public bool IsReconnectClaim { get; init; }
	public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(8);
}

public sealed class MultiplayerSessionConnectResult
{
	public bool Success { get; init; }
	public string? FailureReason { get; init; }
	public JoinAcceptedMessage? JoinAccepted { get; init; }
	public RoomSnapshotMessage? InitialSnapshot { get; init; }

	public static MultiplayerSessionConnectResult Ok(
		JoinAcceptedMessage joinAccepted,
		RoomSnapshotMessage initialSnapshot) => new()
		{
			Success = true,
			JoinAccepted = joinAccepted,
			InitialSnapshot = initialSnapshot,
		};

	public static MultiplayerSessionConnectResult Fail(string reason) => new()
	{
		Success = false,
		FailureReason = reason,
	};
}

public sealed class MultiplayerSessionBackend : IGameSessionBackend
{
	private readonly ENetGameClient _client;
	private TaskCompletionSource<JoinAcceptedMessage>? _pendingJoinAccepted;
	private TaskCompletionSource<RoomSnapshotMessage>? _pendingInitialSnapshot;
	private TaskCompletionSource<string>? _pendingFailure;

	public MultiplayerSessionBackend(ENetGameClient? client = null)
	{
		_client = client ?? new ENetGameClient();
		_client.MessageReceived += HandleMessageReceived;
		_client.Disconnected += HandleDisconnected;
	}

	public string? PlayerSessionId => _client.PlayerSessionId;
	public string? RoomId => _client.RoomId;
	public RoomRuntimeState? LastRoom { get; private set; }

	public event Action<GameSessionSnapshotEnvelope>? SnapshotReceived;
	public event Action<GameSessionDeltaEnvelope>? DeltaReceived;
	public event Action<string>? Disconnected;
	public event Action<string>? CommandRejected;
	public event Action<RoomRuntimeState>? RosterChanged;
	public event Action<string, string>? ReconnectClaimed;

	public async ValueTask<MultiplayerSessionConnectResult> ConnectAsync(
		MultiplayerSessionConnectRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		ResetPendingConnectState();
		_pendingJoinAccepted = CreateTcs<JoinAcceptedMessage>();
		_pendingInitialSnapshot = CreateTcs<RoomSnapshotMessage>();
		_pendingFailure = CreateTcs<string>();

		if (!TryParseServerEndpoint(request.ServerEndpoint, out var address, out var port))
		{
			ResetPendingConnectState();
			return MultiplayerSessionConnectResult.Fail("Server endpoint is invalid.");
		}

		var connectError = _client.Connect(address, port, new GameServerConnectRequest
		{
			RoomId = request.RoomId,
			Token = request.Token,
			IsReconnectClaim = request.IsReconnectClaim,
		});
		if (connectError != Godot.Error.Ok)
		{
			ResetPendingConnectState();
			return MultiplayerSessionConnectResult.Fail($"Failed to connect to room server: {connectError}.");
		}

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(request.Timeout);
		try
		{
			while (true)
			{
				timeoutCts.Token.ThrowIfCancellationRequested();
				Poll();

				if (_pendingFailure.Task.IsCompleted)
					return MultiplayerSessionConnectResult.Fail(await _pendingFailure.Task.ConfigureAwait(false));
				if (_pendingJoinAccepted.Task.IsCompleted && _pendingInitialSnapshot.Task.IsCompleted)
				{
					return MultiplayerSessionConnectResult.Ok(
						await _pendingJoinAccepted.Task.ConfigureAwait(false),
						await _pendingInitialSnapshot.Task.ConfigureAwait(false));
				}

				await Task.Delay(10, timeoutCts.Token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			_client.Disconnect();
			return MultiplayerSessionConnectResult.Fail("Connecting to the room timed out.");
		}
		finally
		{
			ResetPendingConnectState();
		}
	}

	public ValueTask<GameSessionStartResult> StartAsync(GameSessionStartRequest request, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(GameSessionStartResult.Fail("Remote multiplayer backend does not support local start requests."));

	public ValueTask<GameSessionCommandSubmitResult> SubmitCommandAsync(ClientCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		if (_client.State != ENetClientState.InRoom)
			return ValueTask.FromResult(GameSessionCommandSubmitResult.Reject("Multiplayer room connection is not active."));

		var accepted = _client.SendCommand(command);
		return ValueTask.FromResult(accepted
			? GameSessionCommandSubmitResult.Ok()
			: GameSessionCommandSubmitResult.Reject("Failed to send multiplayer command."));
	}

	public void Poll() => _client.Poll();

	public ValueTask DisposeAsync()
	{
		_client.MessageReceived -= HandleMessageReceived;
		_client.Disconnected -= HandleDisconnected;
		_client.Dispose();
		return ValueTask.CompletedTask;
	}

	private void HandleMessageReceived(ServerMessage message)
	{
		switch (message)
		{
			case JoinAcceptedMessage joinAccepted:
				_pendingJoinAccepted?.TrySetResult(joinAccepted);
				break;

			case RoomSnapshotMessage snapshot:
				LastRoom = snapshot.Room.Clone();
				if (_pendingInitialSnapshot != null && !_pendingInitialSnapshot.Task.IsCompleted)
				{
					_pendingInitialSnapshot.TrySetResult(snapshot);
					return;
				}

				SnapshotReceived?.Invoke(new GameSessionSnapshotEnvelope
				{
					Snapshot = snapshot.Snapshot,
					Room = snapshot.Room.Clone(),
					PlayerSessionId = _client.PlayerSessionId,
					RequestId = snapshot.RequestId,
				});
				break;

			case EventBatchMessage batch:
				DeltaReceived?.Invoke(new GameSessionDeltaEnvelope
				{
					Events = batch.Events,
				});
				break;

			case CommandRejectedMessage rejected:
				if (_pendingFailure != null && !_pendingFailure.Task.IsCompleted)
				{
					_pendingFailure.TrySetResult(rejected.Reason);
					return;
				}

				CommandRejected?.Invoke(rejected.Reason);
				break;

			case ReservationBusyMessage busy:
				CommandRejected?.Invoke(
					string.IsNullOrWhiteSpace(busy.ReservationKey)
						? "Another player is already using this interaction."
						: $"Another player is already using {busy.ReservationKey}.");
				break;

			case ReconnectClaimedMessage reconnectClaimed:
				ReconnectClaimed?.Invoke(reconnectClaimed.PlayerSessionId, reconnectClaimed.ActorId);
				break;

			case RosterChangedMessage rosterChanged:
				LastRoom = rosterChanged.Room.Clone();
				RosterChanged?.Invoke(LastRoom.Clone());
				break;
		}
	}

	private void HandleDisconnected(string reason)
	{
		if (_pendingFailure != null && !_pendingFailure.Task.IsCompleted)
			_pendingFailure.TrySetResult(reason);

		Disconnected?.Invoke(reason);
	}

	private void ResetPendingConnectState()
	{
		_pendingJoinAccepted = null;
		_pendingInitialSnapshot = null;
		_pendingFailure = null;
	}

	private static TaskCompletionSource<T> CreateTcs<T>() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);

	private static bool TryParseServerEndpoint(string endpoint, out string address, out int port)
	{
		address = string.Empty;
		port = 0;
		if (string.IsNullOrWhiteSpace(endpoint))
			return false;

		const string prefix = "enet://";
		var normalized = endpoint.Trim();
		if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return false;

		var hostPort = normalized[prefix.Length..];
		var separatorIndex = hostPort.LastIndexOf(':');
		if (separatorIndex <= 0 || separatorIndex >= hostPort.Length - 1)
			return false;

		address = hostPort[..separatorIndex];
		return int.TryParse(hostPort[(separatorIndex + 1)..], out port)
			&& port > 0
			&& port <= ushort.MaxValue;
	}
}
