using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;

namespace MiniRPG.Core.Events;

/// <summary>
/// Authoritative-side bus that dispatches <see cref="GameEvent"/>s to
/// registered <see cref="IGameEventConsequenceHandler"/> instances.
///
/// Sits symmetric to <c>App/RuntimeUi/GameEventPresentationRouter</c>:
/// the presentation router translates events into UI / FX / log (client
/// side only); the consequence router lets simulation systems react
/// (relationship drift, memory, security tension, economy) and lives in
/// <c>MiniRPG.Shared</c> so both the single-player local backend and the
/// multiplayer server can run the same consequences.
/// </summary>
/// <remarks>
/// Roadmap: see <c>Docs/涌现世界路线图.md</c>. This is step 1 of the
/// second layer work; concrete handlers (relationships, memory, rumor)
/// land as they are implemented.
/// </remarks>
public sealed class GameEventConsequenceRouter
{
	private readonly List<IGameEventConsequenceHandler> _handlers = new();
	private readonly Action<string>? _errorSink;

	public GameEventConsequenceRouter(Action<string>? errorSink = null)
	{
		_errorSink = errorSink;
	}

	/// <summary>Number of registered handlers; useful for tests and debug.</summary>
	public int HandlerCount => _handlers.Count;

	/// <summary>
	/// Register a handler. Handlers are invoked in registration order per
	/// event; keep that order deterministic when registering at startup.
	/// </summary>
	public void Register(IGameEventConsequenceHandler handler)
	{
		if (handler == null) throw new ArgumentNullException(nameof(handler));
		_handlers.Add(handler);
	}

	/// <summary>
	/// Dispatch one batch of events. Each handler sees every event in the
	/// order provided. A throwing handler does not abort the batch; the
	/// exception is forwarded to <c>errorSink</c> (if any) and dispatch
	/// continues with the next handler.
	/// </summary>
	public void DispatchConsequences(GameState state, IReadOnlyList<GameEvent> events)
	{
		if (state == null) throw new ArgumentNullException(nameof(state));
		if (events == null || events.Count == 0 || _handlers.Count == 0)
			return;

		for (var i = 0; i < events.Count; i++)
		{
			var ev = events[i];
			for (var h = 0; h < _handlers.Count; h++)
			{
				try
				{
					_handlers[h].OnEvent(state, ev);
				}
				catch (Exception ex)
				{
					_errorSink?.Invoke($"Consequence handler #{h} failed on event '{ev.Type}': {ex.Message}");
				}
			}
		}
	}
}
