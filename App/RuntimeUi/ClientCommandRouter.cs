using System;
using MiniRPG.Core.Multiplayer;
using MiniRPG.Module.Session;

namespace MiniRPG;

/// <summary>
/// Routes a <see cref="ClientCommand"/> to the correct backend:
/// multiplayer transport when a room session is active, otherwise the
/// local <see cref="IGameSessionBackend"/>.
/// </summary>
/// <remarks>
/// Extracted from <c>Main</c> so the client no longer owns the
/// single-player vs. multiplayer routing decision inline. Main keeps
/// thin forwarding methods only for backwards compatibility with the
/// IGameUI / panel host interfaces.
/// </remarks>
internal sealed class ClientCommandRouter
{
	private readonly LogModule _log;
	private readonly IGameSessionBackend _sessionBackend;
	private readonly Func<bool> _isMultiplayerSession;

	private MultiplayerRuntimeCoordinator? _multiplayerRuntime;

	public ClientCommandRouter(
		LogModule log,
		IGameSessionBackend sessionBackend,
		Func<bool> isMultiplayerSession)
	{
		_log = log;
		_sessionBackend = sessionBackend;
		_isMultiplayerSession = isMultiplayerSession;
	}

	/// <summary>Attach the multiplayer coordinator once it has been constructed (later during Init).</summary>
	public void AttachMultiplayerRuntime(MultiplayerRuntimeCoordinator coordinator)
	{
		_multiplayerRuntime = coordinator;
	}

	/// <summary>Try to submit the command through the multiplayer transport; returns true when the coordinator took ownership.</summary>
	public bool TrySubmit(ClientCommand command)
	{
		if (_multiplayerRuntime != null)
			return _multiplayerRuntime.TrySubmitClientCommand(command, _isMultiplayerSession());
		return false;
	}

	/// <summary>
	/// Unified submission: multiplayer routes via the backend transport
	/// while single-player routes through <see cref="IGameSessionBackend.SubmitCommandAsync"/>.
	/// </summary>
	public void Submit(ClientCommand command)
	{
		if (TrySubmit(command))
			return;

		var result = _sessionBackend.SubmitCommandAsync(command).GetAwaiter().GetResult();
		if (!result.Accepted && !string.IsNullOrEmpty(result.FailureReason))
			_log.Add(result.FailureReason);
	}
}
