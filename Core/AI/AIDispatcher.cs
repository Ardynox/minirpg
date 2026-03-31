using System;
using System.Collections.Generic;
using System.Linq;

namespace MiniRPG.Core.AI;

/// <summary>
/// AI 调度器：每回合为所有非玩家 Actor 生成决策并执行。
/// 职责：精度判断 → 构造 Perception → 调用 IBrainModule → 通过 ActionModule 执行 Decision。
/// 纯 Core 逻辑，不做任何 UI 层事件转换。
/// </summary>
public static class AIDispatcher
{
	private static readonly Dictionary<string, IBrainModule> _brains = new();

	static AIDispatcher()
	{
		Register("simple", new SimpleBrain());
	}

	public static void Register(string id, IBrainModule brain) => _brains[id] = brain;

	/// <summary>
	/// 每回合调用：为所有有大脑的非玩家 Actor 执行 AI 决策。
	/// </summary>
	public static List<GameEvent> TickAll(
		GameState state, int viewCenterX, int viewCenterY, int viewRange)
	{
		var events = new List<GameEvent>();

		var actors = state.Actors.Values
			.Where(a => a.BrainId != null && a.Id != state.PlayerId)
			.ToList();

		foreach (var actor in actors)
		{
			if (!state.Actors.ContainsKey(actor.Id)) continue;
			if (CombatModule.IsDead(actor)) continue;

			var detail = Classify(actor, viewCenterX, viewCenterY, viewRange);
			if (detail == SimDetail.Summary) continue;
			if (detail == SimDetail.Simplified && state.Turn % 3 != 0) continue;

			if (!_brains.TryGetValue(actor.BrainId!, out var brain)) continue;

			var perception = PerceptionBuilder.Build(state, actor, detail);
			var rng = new Random(state.RngSeed + state.Turn + actor.Id.GetHashCode());
			var decision = brain.Decide(perception, rng);
			events.AddRange(ExecuteDecision(state, actor, decision));
		}

		return events;
	}

	/// <summary>为单个 Actor 执行一次 AI 决策，仅限攻击（供战斗反击复用）。</summary>
	public static List<GameEvent> DecideAndExecuteOne(GameState state, Actor actor)
	{
		var brainId = actor.BrainId ?? "simple";
		if (!_brains.TryGetValue(brainId, out var brain))
			return [];

		var perception = PerceptionBuilder.Build(state, actor, SimDetail.Full);
		var rng = new Random(state.RngSeed + state.Turn + actor.Id.GetHashCode());
		var decision = brain.Decide(perception, rng);

		if (decision.Type != DecisionType.Attack)
			return [];

		return ExecuteDecision(state, actor, decision);
	}

	/// <summary>为单个 Actor 执行一次完整 AI 决策（不限决策类型，看海模式用）。</summary>
	public static List<GameEvent> DecideAndExecuteAny(GameState state, Actor actor)
	{
		var brainId = actor.BrainId ?? "simple";
		if (!_brains.TryGetValue(brainId, out var brain))
			return [];

		var perception = PerceptionBuilder.Build(state, actor, SimDetail.Full);
		var rng = new Random(state.RngSeed + state.Turn + actor.Id.GetHashCode());
		var decision = brain.Decide(perception, rng);
		return ExecuteDecision(state, actor, decision);
	}

	private static SimDetail Classify(Actor actor, int cx, int cy, int range)
	{
		var dist = Math.Abs(actor.X - cx) + Math.Abs(actor.Y - cy);
		return dist <= range ? SimDetail.Full : SimDetail.Simplified;
	}

	// ── Decision → ActionModule 执行 ─────────────────────

	private static List<GameEvent> ExecuteDecision(GameState state, Actor actor, Decision d)
	{
		var events = new List<GameEvent>();

		switch (d.Type)
		{
			case DecisionType.Wander:
			case DecisionType.MoveTo:
			case DecisionType.Flee:
				if (d.TargetPos is var (tx, ty))
				{
					var dx = tx - actor.X;
					var dy = ty - actor.Y;
					events.AddRange(ActionModule.TryMove(state, actor, dx, dy));
				}
				break;

			case DecisionType.Attack:
				ExecuteAttack(state, actor, d, events);
				break;
		}

		actor.TickBuffs();
		return events;
	}

	private static void ExecuteAttack(GameState state, Actor actor, Decision d, List<GameEvent> events)
	{
		if (d.TargetActorId == null) return;
		var target = ActorModule.GetById(state, d.TargetActorId);
		if (target == null) return;

		var action = d.ActionDefId != null
			? ActionDefs.All.FirstOrDefault(a => a.Id == d.ActionDefId)
			: null;
		var limb = d.TargetLimbId != null
			? target.Limbs.FirstOrDefault(l => l.Id == d.TargetLimbId)
			: null;

		events.AddRange(ActionModule.TryAttack(state, actor, target, action, limb));
	}
}
