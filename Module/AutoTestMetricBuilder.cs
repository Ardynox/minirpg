using System.Collections.Generic;
using MiniRPG.Core.AI;

namespace MiniRPG.Module;

internal static class AutoTestMetricBuilder
{
	internal static Dictionary<string, double> CreateAIVisionMetrics(AIVisionMetrics metrics) => new()
	{
		["observer_count"] = metrics.ObserverCount,
		["candidate_count"] = metrics.CandidateCount,
		["shortlist_count"] = metrics.ShortlistCount,
		["los_checks"] = metrics.LosChecks,
		["visible_actor_count"] = metrics.VisibleActorCount,
		["vision_ms"] = metrics.ElapsedMs,
		["vision_local_context_ms"] = metrics.LocalContextMs,
		["vision_candidate_scan_ms"] = metrics.CandidateScanMs,
		["vision_dead_check_ms"] = metrics.DeadCheckMs,
		["vision_sight_capacity_ms"] = metrics.SightCapacityMs,
		["vision_rank_ms"] = metrics.RankMs,
		["vision_los_ms"] = metrics.LosMs,
		["dead_checks"] = metrics.DeadChecks,
		["capacity_calls"] = metrics.CapacityCalls,
		["max_candidates_per_observer"] = metrics.MaxCandidatesPerObserver,
		["max_shortlist_per_observer"] = metrics.MaxShortlistPerObserver,
		["los_cache_hits"] = metrics.LosCacheHits,
	};

	internal static Dictionary<string, double> CreateTurnTickMetrics(TurnTickMetrics metrics)
	{
		var result = CreateAIVisionMetrics(metrics.AIDispatch.Vision);
		result["tick_ms"] = metrics.TickMs;
		result["advance_world_ms"] = metrics.AdvanceWorldMs;
		result["ai_dispatch_ms"] = metrics.AIDispatchMs;
		result["awareness_ms"] = metrics.AIDispatch.AwarenessMs;
		result["brain_decide_ms"] = metrics.AIDispatch.BrainDecideMs;
		result["decision_execute_ms"] = metrics.AIDispatch.DecisionExecuteMs;
		return result;
	}
}
