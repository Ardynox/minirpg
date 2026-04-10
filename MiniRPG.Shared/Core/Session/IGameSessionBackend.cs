using System;
using System.Threading;
using System.Threading.Tasks;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module;
using MiniRPG;

namespace MiniRPG.Core.Session;

public interface IGameSessionBackend : IAsyncDisposable
{
	event Action<GameSessionSnapshotEnvelope>? SnapshotReceived;
	event Action<GameSessionDeltaEnvelope>? DeltaReceived;
	event Action<string>? Disconnected;

	ValueTask<GameSessionStartResult> StartAsync(GameSessionStartRequest request, CancellationToken cancellationToken = default);
	ValueTask<GameSessionCommandSubmitResult> SubmitCommandAsync(ClientCommand command, CancellationToken cancellationToken = default);
	void Poll();
}

public enum GameSessionStartKind
{
	NewGame,
	BlankEditor,
	WorldCharacter,
}

public sealed class GameSessionStartRequest
{
	public GameSessionStartKind Kind { get; init; }
	public PlayerCreationOptions? PlayerCreationOptions { get; init; }
	public string? WorldId { get; init; }
}

public sealed class GameSessionStartResult
{
	public bool Success { get; init; }
	public string? FailureReason { get; init; }
	public WorldCharacterEntryInfo? WorldCharacterEntry { get; init; }

	public static GameSessionStartResult Ok(WorldCharacterEntryInfo? worldCharacterEntry = null) => new()
	{
		Success = true,
		WorldCharacterEntry = worldCharacterEntry,
	};

	public static GameSessionStartResult Fail(string reason) => new()
	{
		Success = false,
		FailureReason = reason,
	};
}

public sealed class GameSessionCommandSubmitResult
{
	public bool Accepted { get; init; }
	public string? FailureReason { get; init; }

	public static GameSessionCommandSubmitResult Ok() => new()
	{
		Accepted = true,
	};

	public static GameSessionCommandSubmitResult Reject(string reason) => new()
	{
		Accepted = false,
		FailureReason = reason,
	};
}

public sealed class GameSessionSnapshotEnvelope
{
	public SaveFile Snapshot { get; init; } = null!;
	public RoomRuntimeState? Room { get; init; }
	public string? PlayerSessionId { get; init; }
	public string? RequestId { get; init; }
	public long ServerTick { get; init; }
	public long SnapshotSequence { get; init; }
}

public sealed class GameSessionDeltaEnvelope
{
	public System.Collections.Generic.IReadOnlyList<GameEvent> Events { get; init; } = Array.Empty<GameEvent>();
	public string? RequestId { get; init; }
	public long ServerTick { get; init; }
	public long SnapshotSequence { get; init; }
}
