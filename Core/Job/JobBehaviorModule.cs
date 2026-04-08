using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;

namespace MiniRPG.Core.Job;

/// <summary>
/// 工作行为模块：插入 AIDispatcher 行为链。
/// 在 NeedBehavior 之后、Brain 决策之前执行。
/// 只对 BrainId == "domain_worker" 的 NPC 生效。
/// 
/// 行为优先级链：
///   Health → Fire → Temperature → Needs → **Job** → Brain
/// </summary>
public static class JobBehaviorModule
{
	public static ActionExecutionResult TryExecute(
		GameState state,
		Actor actor,
		Perception perception,
		bool tickBuffs,
		AIBehaviorContext? behaviorContext)
	{
		var result = new ActionExecutionResult();

		// 只有工人 AI 才执行工作
		if (!IsWorker(actor))
			return result;

		// 战斗状态不工作
		if (actor.AwarenessState != AwarenessState.Idle)
			return result;

		// 附近有威胁不工作
		if (NeedBehaviorModule.HasNearbyThreat(state, actor, behaviorContext))
			return result;

		// 检查是否已有预留任务
		var ticket = JobScheduler.GetReservedTicket(state, actor);

		// 验证已预留任务是否仍然有效
		if (ticket != null && !IsTicketStillValid(state, ticket))
		{
			JobScheduler.Release(ticket);
			ticket = null;
		}

		// 没有任务则寻找新任务
		if (ticket == null)
		{
			ticket = JobScheduler.FindBestTicket(state, actor);
			if (ticket == null)
				return result; // 无可用任务，交给 Brain 处理（闲逛/回家）

			if (!JobScheduler.TryReserve(state, ticket, actor))
				return result;
		}

		// 执行任务
		var execution = JobExecutor.Execute(state, actor, ticket);
		if (execution.Consumed)
		{
			result.Consumed = true;
			result.Events.AddRange(execution.Events);
			if (tickBuffs)
				actor.TickBuffs();
		}

		return result;
	}

	/// <summary>
	/// 判断 actor 是否是工人（参与工作调度）。
	/// </summary>
	public static bool IsWorker(Actor actor) =>
		string.Equals(actor.BrainId, WorkBrainIds.DomainWorker, StringComparison.Ordinal);

	private static bool IsTicketStillValid(GameState state, WorkTicket ticket)
	{
		// ticket 必须还在 board 上
		if (!state.JobBoardState.Tickets.ContainsKey(ticket.Id))
			return false;

		// 设施必须还存在
		if (!state.Facilities.ContainsKey(ticket.FacilityId))
			return false;

		return true;
	}
}
