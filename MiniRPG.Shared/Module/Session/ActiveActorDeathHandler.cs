using System.Collections.Generic;
using MiniRPG.Core.Data;

namespace MiniRPG.Module.Session;

/// <summary>
/// 决策"当一个角色死了、而且这个角色正好是玩家控制的 active 角色"接下来怎么走：
/// 切换到下一个活着的队员，还是进入全灭流程。
/// </summary>
/// <remarks>
/// 设计意图（见 <c>Docs/产品愿景.md</c> 玩家控制模型 / 死亡与复活）：
/// 玩家死亡不应直接把画面踢回主菜单。
/// Main.HandlePlayerDeath 在未来被接入时，应该只调一行 <see cref="Handle"/>，
/// 然后根据返回的 <see cref="ActiveActorDeathOutcome"/> 决定：
/// <list type="bullet">
/// <item><see cref="ActiveActorDeathOutcome.PromotedNextMember"/>：不回主菜单，切换视角到新的 active；UI 应该演"焦点转移"。</item>
/// <item><see cref="ActiveActorDeathOutcome.AllMembersDead"/>：走全灭流程——当前是回主菜单（允许读档）。</item>
/// <item><see cref="ActiveActorDeathOutcome.NotActive"/>：死的不是当前 active，不需要切换，只要让 UI 知道一个队员死了。</item>
/// </list>
/// 本类只做数据决策和发 <see cref="GameEvent"/>，不碰 UI / 不调 backend——
/// 让 App 层按自己的 UI 时序演出。
/// </remarks>
public static class ActiveActorDeathHandler
{
	/// <summary>
	/// 处理一个死亡事件对 Party 焦点的影响。
	/// </summary>
	/// <param name="state">全局状态。</param>
	/// <param name="deadActorId">刚死的 actor id。</param>
	/// <param name="events">结果事件写入这里（调用方自己 Dispatch）。</param>
	public static ActiveActorDeathOutcome Handle(
		GameState state,
		string deadActorId,
		List<GameEvent> events)
	{
		PartyModule.EnsureValid(state);

		var activeId = PartyModule.GetActiveId(state);
		if (!string.Equals(deadActorId, activeId, System.StringComparison.Ordinal))
		{
			// 死的不是当前焦点——发个低层事件但不切换视角。
			EmitPartyMemberLost(state, deadActorId, wasActive: false, events);
			return ActiveActorDeathOutcome.NotActive;
		}

		// 死的是当前 active：先广播"焦点倒下"。
		EmitPartyMemberLost(state, deadActorId, wasActive: true, events);

		var promoted = PartyModule.TryPromoteNextLivingMember(state);
		if (!promoted)
		{
			EmitPartyWiped(state, events);
			return ActiveActorDeathOutcome.AllMembersDead;
		}

		var newActiveId = PartyModule.GetActiveId(state);
		EmitActiveActorSwitched(state, deadActorId, newActiveId, events);
		return ActiveActorDeathOutcome.PromotedNextMember;
	}

	private static void EmitPartyMemberLost(
		GameState state,
		string deadActorId,
		bool wasActive,
		List<GameEvent> events)
	{
		var evt = new GameEvent("party_member_lost")
		{
			EffectType = wasActive ? "was_active" : "non_active",
			ActionName = deadActorId,
		};
		var deadActor = ActorModule.GetById(state, deadActorId);
		if (deadActor != null)
			IdentificationModule.PopulateTargetIdentity(evt, state, deadActor);

		events.Add(evt);
	}

	private static void EmitActiveActorSwitched(
		GameState state,
		string fromActorId,
		string toActorId,
		List<GameEvent> events)
	{
		var evt = new GameEvent("active_actor_switched")
		{
			ActionName = fromActorId,
			ItemName = toActorId,
		};
		var toActor = ActorModule.GetById(state, toActorId);
		if (toActor != null)
			IdentificationModule.PopulateTargetIdentity(evt, state, toActor);

		events.Add(evt);
	}

	private static void EmitPartyWiped(GameState state, List<GameEvent> events)
	{
		var evt = new GameEvent("party_wiped")
		{
			ActionName = state.Turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
		};
		events.Add(evt);
	}
}

/// <summary>
/// <see cref="ActiveActorDeathHandler.Handle"/> 的三种判定结果。
/// </summary>
public enum ActiveActorDeathOutcome
{
	/// <summary>死的不是当前 active，不切换焦点。</summary>
	NotActive,
	/// <summary>死的是当前 active，已自动切到下一个活着的队员。</summary>
	PromotedNextMember,
	/// <summary>全队已死，应进入全灭流程（如回主菜单 / 永久死）。</summary>
	AllMembersDead,
}
