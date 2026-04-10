namespace MiniRPG.Module;

/// <summary>
/// 回合写入口：收口玩家动作提交与自动步进，避免入口层直接依赖 TimelineTurnManager。
/// </summary>
public static class TimelineTurnGateway
{
	public static TimelineStepResult SubmitPlayerAction(GameState state, TimelinePlayerAction action) =>
		TimelineTurnManager.SubmitPlayerAction(state, action);

	public static TimelineStepResult AdvanceAuto(GameState state, bool watchModeEnabled, bool fastTurnModeEnabled) =>
		TimelineTurnManager.AdvanceAuto(state, watchModeEnabled, fastTurnModeEnabled);
}
