using System;
using System.Collections.Generic;
using MiniRPG.Core.Data;
using MiniRPG.Core.Events;
using MiniRPG.Core.Social;

namespace MiniRPG.Core.Multiplayer;

/// <summary>
/// Authoritative-side bundle that owns the simulation-layer event handlers
/// (incident statistics, relationships, actor memories, rumors) for a single
/// dedicated server room. Symmetric to the wiring inside
/// <c>App/Main.Startup.InitializeCoreServices</c> and
/// <c>LocalSessionBackend.AttachConsequenceRouter</c>: single-player runs the
/// same set on the client backend, multiplayer runs it on the server so every
/// replica observes consistent post-event state.
///
/// Owned per-room by <see cref="RoomRuntimeHost"/>; modules are intentionally
/// in-memory only at this revision (see <c>Docs/涌现世界路线图.md</c> for the
/// persistence rollout plan).
/// </summary>
public sealed class ServerSideConsequenceDispatcher
{
	private readonly GameEventConsequenceRouter _router;

	public ServerSideConsequenceDispatcher(Action<string>? errorSink = null)
	{
		Statistics = new IncidentStatistics();
		Relationships = new RelationshipModule();
		Memories = new ActorMemoryModule();
		Rumors = new RumorBus();

		_router = new GameEventConsequenceRouter(errorSink);
		_router.Register(Statistics);
		_router.Register(Relationships);
		_router.Register(Memories);
		_router.Register(Rumors);
	}

	public IncidentStatistics Statistics { get; }
	public RelationshipModule Relationships { get; }
	public ActorMemoryModule Memories { get; }
	public RumorBus Rumors { get; }

	/// <summary>
	/// Number of registered handlers; useful for tests / debug to confirm the
	/// dispatcher was wired up without re-checking each module reference.
	/// </summary>
	public int HandlerCount => _router.HandlerCount;

	/// <summary>
	/// Dispatch a batch of events to every registered handler. Safe to call
	/// with an empty list; no-op when there are no events. Handlers that
	/// throw are caught by the inner router and reported via
	/// <c>errorSink</c> instead of aborting the batch.
	/// </summary>
	public void DispatchConsequences(GameState state, IReadOnlyList<GameEvent> events)
		=> _router.DispatchConsequences(state, events);
}
