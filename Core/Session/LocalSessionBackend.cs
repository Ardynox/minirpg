using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module;

namespace MiniRPG.Core.Session;

public sealed class LocalSessionBackend : IGameSessionBackend
{
	private readonly GameSessionModule _session;
	private readonly GameState _state;
	private readonly Action<List<GameEvent>> _dispatch;

	public LocalSessionBackend(GameSessionModule session, GameState state, Action<List<GameEvent>> dispatch)
	{
		_session = session;
		_state = state;
		_dispatch = dispatch;
	}

	public event Action<GameSessionSnapshotEnvelope>? SnapshotReceived;
	public event Action<GameSessionDeltaEnvelope>? DeltaReceived;
	public event Action<string>? Disconnected;

	public ValueTask<GameSessionStartResult> StartAsync(GameSessionStartRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);

		switch (request.Kind)
		{
			case GameSessionStartKind.NewGame:
				_session.NewGame(request.PlayerCreationOptions ?? PlayerCreationOptions.CreateDefault());
				break;
			case GameSessionStartKind.BlankEditor:
				_session.NewBlankEditorMap();
				break;
			case GameSessionStartKind.WorldCharacter:
				if (string.IsNullOrWhiteSpace(request.WorldId))
					return ValueTask.FromResult(GameSessionStartResult.Fail("World id is required."));
				var entry = _session.StartWorldCharacter(request.WorldId, request.PlayerCreationOptions ?? PlayerCreationOptions.CreateDefault());
				EmitFullSnapshot();
				return ValueTask.FromResult(GameSessionStartResult.Ok(entry));
			default:
				return ValueTask.FromResult(GameSessionStartResult.Fail("Unsupported local start kind."));
		}

		EmitFullSnapshot();
		return ValueTask.FromResult(GameSessionStartResult.Ok());
	}

	public ValueTask<GameSessionCommandSubmitResult> SubmitCommandAsync(ClientCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var result = ServerActionGateway.Execute(_state, command);
		if (result.Events.Count > 0)
		{
			_dispatch(result.Events);
			DeltaReceived?.Invoke(new GameSessionDeltaEnvelope
			{
				Events = result.Events,
			});
		}

		return ValueTask.FromResult(result.Ok
			? GameSessionCommandSubmitResult.Ok()
			: GameSessionCommandSubmitResult.Reject(result.Logs.Count > 0 ? result.Logs[0] : "Command rejected."));
	}

	public void Poll()
	{
	}

	public ValueTask DisposeAsync()
	{
		Disconnected?.Invoke("Local session closed.");
		return ValueTask.CompletedTask;
	}

	private void EmitFullSnapshot()
	{
		var snapshot = SaveModule.BuildSnapshot(_state);
		SnapshotReceived?.Invoke(new GameSessionSnapshotEnvelope
		{
			Snapshot = snapshot,
			Room = _state.Room.Clone(),
		});
	}
}
