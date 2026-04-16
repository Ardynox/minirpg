namespace MiniRPG.Module.Render;

internal enum ActorMotionTimingTier
{
	PlayerSlow,
	PlayerFast,
	NpcFast,
}

internal static class ActorMotionTiming
{
	public const float PlayerSlowSeconds = 0.16f;
	public const float PlayerFastSeconds = 0.11f;
	public const float NpcFastSeconds = 0.08f;

	public static ActorMotionTimingTier ResolveManualPlayerTier() => ActorMotionTimingTier.PlayerSlow;

	public static ActorMotionTimingTier ResolveAutoNavigationTier(int pathLength) =>
		pathLength >= 4 ? ActorMotionTimingTier.PlayerFast : ActorMotionTimingTier.PlayerSlow;

	public static float ResolveDurationSeconds(ActorMotionTimingTier tier) => tier switch
	{
		ActorMotionTimingTier.PlayerFast => PlayerFastSeconds,
		ActorMotionTimingTier.NpcFast => NpcFastSeconds,
		_ => PlayerSlowSeconds,
	};
}

internal readonly record struct ActorMotionPresentationRequest(
	string ActorId,
	int SourceX,
	int SourceY,
	int SourceZ,
	int TargetX,
	int TargetY,
	int TargetZ,
	ActorMotionTimingTier TimingTier,
	bool Blocking);
