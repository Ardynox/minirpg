using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.Data;
using MiniRPG.Core.Job;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI;

/// <summary>
/// Builds AI perception, runs the selected brain, and executes the chosen action.
/// </summary>
public static class AIDispatcher
{
	private static readonly Dictionary<string, IBrainModule> _brains = new();

	static AIDispatcher()
	{
		Register("simple", new SimpleBrain());
		Register(WorkBrainIds.DomainWorker, new SimpleBrain());
		Register(FollowerBrain.BrainId, new FollowerBrain());
	}

	public static void Register(string id, IBrainModule brain) => _brains[id] = brain;

	public static List<GameEvent> TickAll(GameState state, int viewCenterX, int viewCenterY, int viewRange) =>
		TickAll(state, [new WorldCoord(viewCenterX, viewCenterY, state.PlayerZ)], viewRange);

	public static AIDispatchResult TickAllProfiled(GameState state, int viewCenterX, int viewCenterY, int viewRange) =>
		TickAllProfiled(state, [new WorldCoord(viewCenterX, viewCenterY, state.PlayerZ)], viewRange);

	public static List<GameEvent> TickAll(GameState state, IReadOnlyList<WorldCoord> viewAnchors, int viewRange) =>
		TickAllInternal(state, viewAnchors, viewRange, captureProfile: false).Events;

	public static AIDispatchResult TickAllProfiled(GameState state, IReadOnlyList<WorldCoord> viewAnchors, int viewRange) =>
		TickAllInternal(state, viewAnchors, viewRange, captureProfile: true);

	private static AIDispatchResult TickAllInternal(
		GameState state,
		IReadOnlyList<WorldCoord> viewAnchors,
		int viewRange,
		bool captureProfile)
	{
		var events = new List<GameEvent>();
		var requests = new List<AIVisionRequest>();
		var activeActors = new List<Actor>();
		double awarenessMs = 0d;
		double healthBehaviorMs = 0d;
		double fireBehaviorMs = 0d;
		double temperatureBehaviorMs = 0d;
		double needBehaviorMs = 0d;
		double jobBehaviorMs = 0d;
		double brainDecideMs = 0d;
		double decisionExecuteMs = 0d;
		var dispatchStart = captureProfile ? ProfilingClock.Start() : 0L;

		var activePartyId = PartyModule.GetActiveId(state);
		var actors = state.Actors.Values
			.Where(actor => actor.BrainId != null
				&& !string.Equals(actor.Id, activePartyId, StringComparison.Ordinal))
			.ToList();
		var awarenessContext = AwarenessModule.CreateTurnContext(state);
		var behaviorContext = new AIBehaviorContext(state);

		foreach (var actor in actors)
		{
			if (!state.Actors.ContainsKey(actor.Id) || CombatModule.IsDead(actor))
				continue;

			var detail = Classify(state, actor, viewAnchors, viewRange);
			if (detail == SimDetail.Summary && actor.AwarenessState != AwarenessState.Idle)
				detail = SimDetail.Simplified;
			if (detail == SimDetail.Summary)
				continue;

			var simplifiedUpdateInterval = Math.Max(1, GameConfig.AIVision.SimplifiedUpdateIntervalTurns);
			if (detail == SimDetail.Simplified
				&& actor.AwarenessState == AwarenessState.Idle
				&& state.Turn % simplifiedUpdateInterval != 0)
				continue;

			if (!_brains.ContainsKey(actor.BrainId!))
				continue;

			requests.Add(new AIVisionRequest(actor, detail));
			activeActors.Add(actor);
		}

		var perceptions = captureProfile
			? AIVisionBatch.BuildProfiled(state, requests)
			: PerceptionBuilder.BuildBatch(state, requests);
		foreach (var actor in activeActors)
		{
			if (!state.Actors.ContainsKey(actor.Id) || CombatModule.IsDead(actor))
				continue;
			if (!_brains.TryGetValue(actor.BrainId!, out var brain))
				continue;
			if (!perceptions.TryGetValue(actor.Id, out var perception))
				continue;

			List<GameEvent> awarenessEvents;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				awarenessEvents = AwarenessModule.UpdateForTurn(state, actor, perception, awarenessContext);
				awarenessMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				awarenessEvents = AwarenessModule.UpdateForTurn(state, actor, perception, awarenessContext);
			}
			events.AddRange(awarenessEvents);

			ActionExecutionResult healthResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				healthResult = HealthBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
				healthBehaviorMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				healthResult = HealthBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
			}
			if (healthResult.Consumed)
			{
				events.AddRange(healthResult.Events);
				continue;
			}

			ActionExecutionResult fireResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				fireResult = FireBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
				fireBehaviorMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				fireResult = FireBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
			}
			if (fireResult.Consumed)
			{
				events.AddRange(fireResult.Events);
				continue;
			}

			ActionExecutionResult temperatureResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				temperatureResult = TemperatureBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
				temperatureBehaviorMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				temperatureResult = TemperatureBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
			}
			if (temperatureResult.Consumed)
			{
				events.AddRange(temperatureResult.Events);
				continue;
			}

			ActionExecutionResult needResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				needResult = NeedBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
				needBehaviorMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				needResult = NeedBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
			}
			if (needResult.Consumed)
			{
				events.AddRange(needResult.Events);
				continue;
			}

			// ── Job 行为：工人自动执行工作任务 ──
			ActionExecutionResult jobResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				jobResult = JobBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
				jobBehaviorMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				jobResult = JobBehaviorModule.TryExecute(state, actor, perception, tickBuffs: true, behaviorContext);
			}
			if (jobResult.Consumed)
			{
				events.AddRange(jobResult.Events);
				continue;
			}

			var rng = new Random(state.RngSeed + state.Turn + actor.Id.GetHashCode());
			Decision decision;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				decision = brain.Decide(perception, rng);
				brainDecideMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				decision = brain.Decide(perception, rng);
			}

			ActionExecutionResult executionResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				executionResult = ExecuteDecision(state, actor, decision, tickBuffs: true);
				decisionExecuteMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				executionResult = ExecuteDecision(state, actor, decision, tickBuffs: true);
			}
			events.AddRange(executionResult.Events);
		}

		return new AIDispatchResult
		{
			Events = events,
			Metrics = new AIDispatchMetrics
			{
				Vision = AIVisionBatch.LastMetrics,
				ElapsedMs = captureProfile && requests.Count > 0 && state.World != null
					? ProfilingClock.ElapsedMs(dispatchStart)
					: 0d,
				AwarenessMs = awarenessMs,
				HealthBehaviorMs = healthBehaviorMs,
				FireBehaviorMs = fireBehaviorMs,
				TemperatureBehaviorMs = temperatureBehaviorMs,
				NeedBehaviorMs = needBehaviorMs,
				JobBehaviorMs = jobBehaviorMs,
				BrainDecideMs = brainDecideMs,
				DecisionExecuteMs = decisionExecuteMs,
			},
		};
	}

	public static List<GameEvent> DecideAndExecuteOne(GameState state, Actor actor) =>
		DecideAndExecuteOne(state, actor, tickBuffs: true);

	public static List<GameEvent> DecideAndExecuteOne(GameState state, Actor actor, bool tickBuffs) =>
		DecideAndExecuteOneResult(state, actor, tickBuffs).Events;

	public static ActionExecutionResult DecideAndExecuteOneResult(GameState state, Actor actor, bool tickBuffs)
	{
		var brainId = actor.BrainId ?? "simple";
		if (!_brains.TryGetValue(brainId, out var brain))
			return new ActionExecutionResult();

		var perception = PerceptionBuilder.Build(state, actor, SimDetail.Full);
		var behaviorContext = new AIBehaviorContext(state);
		var awarenessEvents = AwarenessModule.UpdateForTurn(state, actor, perception, AwarenessModule.CreateTurnContext(state));
		var healthExecution = HealthBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (healthExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(healthExecution.Events);
			return shortCircuit;
		}
		var fireExecution = FireBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (fireExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(fireExecution.Events);
			return shortCircuit;
		}
		var temperatureExecution = TemperatureBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (temperatureExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(temperatureExecution.Events);
			return shortCircuit;
		}
		var needExecution = NeedBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (needExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(needExecution.Events);
			return shortCircuit;
		}
		var jobExecution = JobBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (jobExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(jobExecution.Events);
			return shortCircuit;
		}
		var rng = new Random(state.RngSeed + state.Turn + actor.Id.GetHashCode());
		var decision = brain.Decide(perception, rng);
		var result = new ActionExecutionResult();
		result.Events.AddRange(awarenessEvents);
		if (decision.Type != DecisionType.Attack)
			return result;

		var execution = ExecuteDecision(state, actor, decision, tickBuffs);
		result.Consumed = execution.Consumed;
		result.Events.AddRange(execution.Events);
		return result;
	}

	public static List<GameEvent> DecideAndExecuteAny(GameState state, Actor actor) =>
		DecideAndExecuteAny(state, actor, tickBuffs: true);

	public static List<GameEvent> DecideAndExecuteAny(GameState state, Actor actor, bool tickBuffs) =>
		DecideAndExecuteAnyResult(state, actor, tickBuffs).Events;

	public static ActionExecutionResult DecideAndExecuteAnyResult(GameState state, Actor actor, bool tickBuffs)
	{
		var brainId = actor.BrainId ?? "simple";
		if (!_brains.TryGetValue(brainId, out var brain))
			return new ActionExecutionResult();

		var perception = PerceptionBuilder.Build(state, actor, SimDetail.Full);
		var behaviorContext = new AIBehaviorContext(state);
		var awarenessEvents = AwarenessModule.UpdateForTurn(state, actor, perception, AwarenessModule.CreateTurnContext(state));
		var healthExecution = HealthBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (healthExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(healthExecution.Events);
			return shortCircuit;
		}
		var fireExecution = FireBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (fireExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(fireExecution.Events);
			return shortCircuit;
		}
		var temperatureExecution = TemperatureBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (temperatureExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(temperatureExecution.Events);
			return shortCircuit;
		}
		var needExecution = NeedBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (needExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(needExecution.Events);
			return shortCircuit;
		}
		var jobExecution = JobBehaviorModule.TryExecute(state, actor, perception, tickBuffs, behaviorContext);
		if (jobExecution.Consumed)
		{
			var shortCircuit = new ActionExecutionResult { Consumed = true };
			shortCircuit.Events.AddRange(awarenessEvents);
			shortCircuit.Events.AddRange(jobExecution.Events);
			return shortCircuit;
		}
		var rng = new Random(state.RngSeed + state.Turn + actor.Id.GetHashCode());
		var decision = brain.Decide(perception, rng);
		var execution = ExecuteDecision(state, actor, decision, tickBuffs);
		var result = new ActionExecutionResult
		{
			Consumed = execution.Consumed,
		};
		result.Events.AddRange(awarenessEvents);
		result.Events.AddRange(execution.Events);
		return result;
	}

	internal static SimDetail Classify(GameState state, Actor actor, int cx, int cy, int range)
	{
		return Classify(state, actor, [new WorldCoord(cx, cy, actor.Z)], range);
	}

	internal static SimDetail Classify(GameState state, Actor actor, IReadOnlyList<WorldCoord> anchors, int range)
	{
		if (anchors.Count == 0)
			return SimDetail.Summary;

		var simplifiedMultiplier = Math.Max(1, GameConfig.AIVision.SimplifiedActivationRangeMultiplier);
		var scaledRange = VisionRangeScaler.ScaleRadius(range, actor.GetCapacity(Caps.Sight));
		if (actor.Z == 0
			&& state.World != null
			&& state.World.IsWeatherExposed(actor.X, actor.Y, actor.Z))
		{
			var weather = WeatherRules.GetLocalWeather(state, actor.X, actor.Y, actor.Z);
			scaledRange = Math.Max(1, (int)MathF.Round(scaledRange * WeatherRules.GetAiVisionMultiplier(weather)));
		}

		var bestDist = int.MaxValue;
		foreach (var anchor in anchors)
		{
			if (anchor.Z != actor.Z)
				continue;

			var anchorDistance = Math.Abs(actor.X - anchor.X) + Math.Abs(actor.Y - anchor.Y);
			if (anchorDistance < bestDist)
				bestDist = anchorDistance;
		}

		if (bestDist == int.MaxValue)
			return SimDetail.Summary;

		var dist = bestDist;
		if (dist <= scaledRange)
			return SimDetail.Full;
		if (dist <= scaledRange * simplifiedMultiplier)
			return SimDetail.Simplified;
		return SimDetail.Summary;
	}

	private static ActionExecutionResult ExecuteDecision(GameState state, Actor actor, Decision decision, bool tickBuffs)
	{
		var result = new ActionExecutionResult();
		switch (decision.Type)
		{
			case DecisionType.Wander:
			case DecisionType.MoveTo:
			case DecisionType.Flee:
				if (decision.TargetPos is var (targetX, targetY))
				{
					var dx = targetX - actor.X;
					var dy = targetY - actor.Y;
					result.Events.AddRange(ActionModule.TryMove(state, actor, dx, dy));
					result.Consumed = true;
				}
				break;

			case DecisionType.MoveVertical:
				result = ExecuteVerticalMove(state, actor, decision.TargetZ);
				break;

			case DecisionType.Attack:
				result = ExecuteAttack(state, actor, decision);
				break;

			case DecisionType.Idle:
				result.Consumed = true;
				break;
		}

		if (tickBuffs && result.Consumed)
			actor.TickBuffs();

		return result;
	}

	private static ActionExecutionResult ExecuteVerticalMove(GameState state, Actor actor, int? targetZ)
	{
		var result = new ActionExecutionResult();
		if (targetZ == null || targetZ.Value == actor.Z)
			return result;

		var goDown = targetZ.Value > actor.Z;
		if (!VerticalTraversalService.TryMoveActorVertical(state, actor, goDown))
			return result;

		result.Consumed = true;
		return result;
	}

	private static ActionExecutionResult ExecuteAttack(GameState state, Actor actor, Decision decision)
	{
		if (string.IsNullOrEmpty(decision.ActionDefId))
			return new ActionExecutionResult();

		return ActionModule.TryCastSkill(
			state,
			actor,
			decision.ActionDefId,
			SkillTargetType.Actor,
			targetActorId: decision.TargetActorId,
			targetLimbId: decision.TargetLimbId);
	}
}
