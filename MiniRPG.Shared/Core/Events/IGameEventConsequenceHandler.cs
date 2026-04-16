using MiniRPG.Core.Data;

namespace MiniRPG.Core.Events;

/// <summary>
/// Consumer of authoritative <see cref="GameEvent"/>s that need to update
/// simulation-layer state (relationships, memory, security tension, etc.)
/// rather than UI / FX / log. Handlers run on the authoritative side
/// (single-player: client; multiplayer: server) so every replica sees the
/// same follow-up state.
/// </summary>
public interface IGameEventConsequenceHandler
{
	/// <summary>
	/// Called once per event in the same order they were produced.
	/// Must not throw; exceptions are caught by the router and logged.
	/// </summary>
	void OnEvent(GameState state, GameEvent ev);
}
