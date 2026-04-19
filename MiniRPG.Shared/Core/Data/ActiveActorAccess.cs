namespace MiniRPG.Core.Data;

/// <summary>
/// 焦点角色（玩家此刻实际控制的那个 actor）的统一访问入口。
/// </summary>
/// <remarks>
/// 设计意图（见 <c>Docs/产品愿景.md</c> 玩家控制模型 / 死亡与复活）：
/// <para>
/// 玩家其实控制的是 <see cref="PartyState"/>，"当前接受输入的那个角色"是 <see cref="PartyState.ActiveId"/>。
/// 但仓库历史里有大量 HUD/输入/日志/多人预测路径还在直接读 <see cref="GameState.PlayerId"/>，
/// 一旦 Tab 切焦点就全部错位（HUD 显示队长的需求、命令派发到队长、日志只过滤队长身上的事件）。
/// </para>
/// <para>
/// 本类把"焦点角色"的访问收敛到一个调用点：
/// 优先返回 <see cref="PartyModule.TryGetActiveActor"/> 命中的 active actor；
/// 仅当 Party 未初始化（典型：老存档加载首帧 / 多人 snapshot 未对齐）时，才回退到 <see cref="ActorModule.GetPlayer"/>。
/// 这样调用点既不必关心是不是多人 / 是不是开局首帧，也不会在切焦点后继续指向旧角色。
/// </para>
/// <para>
/// <see cref="GameState.PlayerX"/>/<see cref="GameState.PlayerY"/>/<see cref="GameState.PlayerZ"/> 在多人下
/// 是相机锚点缓存（见 <c>Docs/多人联机契约.md</c>），不是焦点角色的权威坐标——需要权威位置请走本类。
/// </para>
/// </remarks>
public static class ActiveActorAccess
{
	/// <summary>
	/// 返回当前焦点角色实体；Party 未初始化时回退到 <see cref="ActorModule.GetPlayer"/>。
	/// 仍然可能为 null（连 PlayerId 对应的 actor 也不存在，例如开局极早期）。
	/// </summary>
	public static Actor? GetActive(GameState state) =>
		PartyModule.TryGetActiveActor(state, out var active)
			? active
			: ActorModule.GetPlayer(state);

	/// <summary>
	/// 返回当前焦点角色 id；Party 未初始化时回退到 <see cref="GameState.PlayerId"/>。
	/// </summary>
	public static string GetActiveId(GameState state) =>
		PartyModule.TryGetActiveActor(state, out var active)
			? active.Id
			: state.PlayerId;
}
