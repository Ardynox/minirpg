namespace MiniRPG.Module;

/// <summary>
/// 运行时派生状态写入口：统一同步 Health/Need，避免 UI/入口层散落调用。
/// </summary>
public static class ActorDerivedStateUpdater
{
	public static void SyncPlayerUiState(GameState state)
	{
		var player = ActorModule.GetPlayer(state);
		if (player != null)
			SyncActor(state, player);
	}

	public static void SyncInspectActor(GameState state, Actor? actor)
	{
		if (actor != null)
			SyncActor(state, actor);
	}

	public static void SyncDialogParticipants(GameState state, Actor? player, Actor? npc)
	{
		if (player != null)
			SyncActor(state, player);

		if (npc != null && !ReferenceEquals(npc, player))
			SyncActor(state, npc);
	}

	public static void SyncAllActorsForSession(GameState state)
	{
		foreach (var actor in state.Actors.Values)
			SyncActor(state, actor);
	}

	public static void SyncActor(GameState state, Actor actor)
	{
		var exposure = DefaultEnvironmentExposureProvider.Instance.Capture(state, actor);
		HealthSystem.Sync(actor, state.Turn, exposure);
	}
}
