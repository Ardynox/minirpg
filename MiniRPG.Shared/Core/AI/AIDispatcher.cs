using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI.Utility;
using MiniRPG.Core.Data;
using MiniRPG.Core.Health;
using MiniRPG.Core.Needs;
using MiniRPG.Core.World;

namespace MiniRPG.Core.AI;

public static class AIDispatcher
{
	private static readonly UtilityBrain _utilityBrain = new();
	private static readonly Dictionary<string, UtilityDecisionCache> _decisionCaches = new(StringComparer.Ordinal);

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
		UtilityActionRegistry.EnsureLoaded();
		ExecutorRegistry.EnsureInitialized();

		var events = new List<GameEvent>();
		var requests = new List<AIVisionRequest>();
		var activeActors = new List<Actor>();
		var actorDetails = new Dictionary<string, SimDetail>(StringComparer.Ordinal);
		double awarenessMs = 0d;
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

			requests.Add(new AIVisionRequest(actor, detail));
			activeActors.Add(actor);
			actorDetails[actor.Id] = detail;
		}

		var perceptions = captureProfile
			? AIVisionBatch.BuildProfiled(state, requests)
			: PerceptionBuilder.BuildBatch(state, requests);

		foreach (var actor in activeActors)
		{
			if (!state.Actors.ContainsKey(actor.Id) || CombatModule.IsDead(actor))
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

			var exposure = behaviorContext.GetCurrentExposure(actor);
			HealthSystem.Sync(actor, state.Turn, exposure, events, state);
			NeedSystem.Sync(actor, state.Turn, events, state);

			var detail = actorDetails.TryGetValue(actor.Id, out var d) ? d : SimDetail.Full;
			_decisionCaches.TryGetValue(actor.Id, out var cache);

			UtilityEvalResult evalResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				evalResult = EvaluateActor(state, actor, perception, behaviorContext, detail, cache);
				brainDecideMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				evalResult = EvaluateActor(state, actor, perception, behaviorContext, detail, cache);
			}

			if (evalResult.Action == null)
				continue;

			if (evalResult.Action != null)
				_decisionCaches[actor.Id] = UtilityCache.CreateCache(state, actor, perception, evalResult);

			ActionExecutionResult executionResult;
			if (captureProfile)
			{
				var stageStart = ProfilingClock.Start();
				executionResult = ExecuteUtilityAction(state, actor, perception, evalResult);
				decisionExecuteMs += ProfilingClock.ElapsedMs(stageStart);
			}
			else
			{
				executionResult = ExecuteUtilityAction(state, actor, perception, evalResult);
			}

			if (executionResult.Consumed)
				actor.TickBuffs();
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
				BrainDecideMs = brainDecideMs,
				DecisionExecuteMs = decisionExecuteMs,
			},
		};
	}

	private static UtilityEvalResult EvaluateActor(
		GameState state,
		Actor actor,
		Perception perception,
		AIBehaviorContext behaviorContext,
		SimDetail detail,
		UtilityDecisionCache? cache)
	{
		var rng = CreateActorRng(state, actor);

		if (cache != null && !UtilityCache.NeedsQuickReevaluation(state, actor, perception, detail, cache))
		{
			var cachedAction = UtilityActionRegistry.Get(cache.ActionId);
			if (cachedAction != null)
			{
				return new UtilityEvalResult
				{
					Action = cachedAction,
					Score = cache.Score,
					TargetActor = cache.TargetActorId != null && state.Actors.TryGetValue(cache.TargetActorId, out var ta) ? ta : null,
					TargetTicket = cache.TargetTicketId != null && state.JobBoardState.Tickets.TryGetValue(cache.TargetTicketId, out var tt) ? tt : null,
					TargetPos = cache.TargetPos,
				};
			}
		}

		IReadOnlyList<UtilityActionDef>? subset = null;
		if (!UtilityCache.NeedsFullReevaluation(state, actor, perception, detail, cache))
			subset = UtilityCache.GetContextualSubset(actor, perception);

		return _utilityBrain.Evaluate(state, actor, perception, behaviorContext, detail, rng, subset);
	}

	private static ActionExecutionResult ExecuteUtilityAction(
		GameState state,
		Actor actor,
		Perception perception,
		UtilityEvalResult eval)
	{
		if (eval.Action == null)
			return new ActionExecutionResult();

		var executor = ExecutorRegistry.Get(eval.Action.Executor);
		if (executor == null)
			return new ActionExecutionResult();

		return executor.Execute(state, actor, perception, eval);
	}

	public static List<GameEvent> DecideAndExecuteOne(GameState state, Actor actor) =>
		DecideAndExecuteOne(state, actor, tickBuffs: true);

	public static List<GameEvent> DecideAndExecuteOne(GameState state, Actor actor, bool tickBuffs) =>
		DecideAndExecuteOneResult(state, actor, tickBuffs).Events;

	public static ActionExecutionResult DecideAndExecuteOneResult(GameState state, Actor actor, bool tickBuffs)
	{
		UtilityActionRegistry.EnsureLoaded();
		ExecutorRegistry.EnsureInitialized();

		var perception = ResolvePerception(state, actor);
		var awarenessContext = state.AwarenessContextCache ?? AwarenessModule.CreateTurnContext(state);
		var awarenessEvents = AwarenessModule.UpdateForTurn(state, actor, perception, awarenessContext);
		var behaviorContext = state.BehaviorContextCache ?? new AIBehaviorContext(state);

		var exposure = behaviorContext.GetCurrentExposure(actor);
		HealthSystem.Sync(actor, state.Turn, exposure, new List<GameEvent>(), state);

		var rng = CreateActorRng(state, actor);
		var eval = _utilityBrain.Evaluate(state, actor, perception, behaviorContext, SimDetail.Full, rng);

		var result = new ActionExecutionResult();
		result.Events.AddRange(awarenessEvents);

		if (eval.Action == null)
		{
			result.Consumed = true;
			return result;
		}

		var execution = ExecuteUtilityAction(state, actor, perception, eval);
		if (execution.Consumed && tickBuffs)
			actor.TickBuffs();
		result.Consumed = execution.Consumed;
		result.Events.AddRange(execution.Events);
		if (!result.Consumed)
			result.Consumed = true;
		return result;
	}

	public static List<GameEvent> DecideAndExecuteAny(GameState state, Actor actor) =>
		DecideAndExecuteAny(state, actor, tickBuffs: true);

	public static List<GameEvent> DecideAndExecuteAny(GameState state, Actor actor, bool tickBuffs) =>
		DecideAndExecuteAnyResult(state, actor, tickBuffs).Events;

	public static ActionExecutionResult DecideAndExecuteAnyResult(GameState state, Actor actor, bool tickBuffs)
	{
		UtilityActionRegistry.EnsureLoaded();
		ExecutorRegistry.EnsureInitialized();

		var perception = ResolvePerception(state, actor);
		var awarenessContext = state.AwarenessContextCache ?? AwarenessModule.CreateTurnContext(state);
		var awarenessEvents = AwarenessModule.UpdateForTurn(state, actor, perception, awarenessContext);
		var behaviorContext = state.BehaviorContextCache ?? new AIBehaviorContext(state);

		var exposure = behaviorContext.GetCurrentExposure(actor);
		HealthSystem.Sync(actor, state.Turn, exposure, new List<GameEvent>(), state);

		var rng = CreateActorRng(state, actor);
		var eval = _utilityBrain.Evaluate(state, actor, perception, behaviorContext, SimDetail.Full, rng);

		var result = new ActionExecutionResult();
		result.Events.AddRange(awarenessEvents);

		if (eval.Action == null)
		{
			result.Consumed = true;
			return result;
		}

		var execution = ExecuteUtilityAction(state, actor, perception, eval);
		if (execution.Consumed && tickBuffs)
			actor.TickBuffs();
		result.Consumed = true;
		result.Events.AddRange(execution.Events);
		return result;
	}

	private static Perception ResolvePerception(GameState state, Actor actor)
	{
		var cache = state.PerceptionCache;
		if (cache == null)
			return PerceptionBuilder.Build(state, actor, SimDetail.Full);

		if (cache.TryGetValue(actor.Id, out var cached))
			return cached;

		var requests = new List<AIVisionRequest>();
		foreach (var entry in state.Timeline.Actors)
		{
			if (cache.ContainsKey(entry.ActorId))
				continue;
			if (!state.Actors.TryGetValue(entry.ActorId, out var a))
				continue;
			if (a.BrainId == null && !string.Equals(a.Id, actor.Id, StringComparison.Ordinal))
				continue;
			if (CombatModule.IsDead(a))
				continue;
			requests.Add(new AIVisionRequest(a, SimDetail.Full));
		}

		if (requests.Count == 0)
			return PerceptionBuilder.Build(state, actor, SimDetail.Full);

		var batch = PerceptionBuilder.BuildBatch(state, requests);
		foreach (var kv in batch)
			cache[kv.Key] = kv.Value;

		return cache.TryGetValue(actor.Id, out var result)
			? result
			: PerceptionBuilder.Build(state, actor, SimDetail.Full);
	}

	public static Random CreateActorRng(GameState state, Actor actor)
	{
		var stableActorHash = ComputeStableStringHash(actor.Id);
		var seed = unchecked(state.RngSeed * 31 + state.Turn * 17 + stableActorHash);
		return new Random(seed);
	}

	private static int ComputeStableStringHash(string value)
	{
		unchecked
		{
			var hash = (int)2166136261;
			for (var i = 0; i < value.Length; i++)
				hash = (hash ^ value[i]) * 16777619;
			return hash;
		}
	}

	public static SimDetail Classify(GameState state, Actor actor, int cx, int cy, int range)
	{
		return Classify(state, actor, [new WorldCoord(cx, cy, actor.Z)], range);
	}

	public static SimDetail Classify(GameState state, Actor actor, IReadOnlyList<WorldCoord> anchors, int range)
	{
		if (anchors.Count == 0)
			return SimDetail.Summary;

		var simplifiedMultiplier = Math.Max(1, GameConfig.AIVision.SimplifiedActivationRangeMultiplier);
		var maxVerticalLayers = Math.Max(0, GameConfig.AIVision.MaxVerticalVisionLayers);
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
			var dz = Math.Abs(anchor.Z - actor.Z);
			if (dz > maxVerticalLayers)
				continue;

			var anchorDistance = Math.Abs(actor.X - anchor.X) + Math.Abs(actor.Y - anchor.Y) + dz;
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

	public static void ClearCaches()
	{
		_decisionCaches.Clear();
	}
}
