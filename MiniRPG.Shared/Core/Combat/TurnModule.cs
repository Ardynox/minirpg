using System;
using System.Collections.Generic;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Event;
using MiniRPG.Core.Farm;
using MiniRPG.Core.Health;

namespace MiniRPG.Core.Combat;

/// <summary>
/// 回合推进模块。
/// </summary>
public static class TurnModule
{
	public static List<GameEvent> AdvanceWorld(GameState state)
	{
		state.Turn++;
		var events = NestModule.Tick(state);
		events.AddRange(WeatherAccumulationSimulator.Advance(state));
		events.AddRange(FireSystem.Advance(state));
		events.AddRange(Storyteller.Tick(state));
		events.AddRange(FarmModule.TickGrowth(state));
		return events;
	}

	public static List<GameEvent> Tick(GameState state)
	{
		var events = new List<GameEvent>();
		events.AddRange(AdvanceWorld(state));

		var viewRange = Math.Max(0, GameConfig.AIVision.ActivationViewRange);
		var anchors = RoomRuntimeModule.GetWorldAnchors(state).Select(static anchor => anchor.Position).ToArray();
		events.AddRange(AIDispatcher.TickAll(state, anchors, viewRange));
		return events;
	}

	public static TurnTickResult TickProfiled(GameState state)
	{
		var tickStart = ProfilingClock.Start();
		var events = new List<GameEvent>();

		var advanceWorldStart = ProfilingClock.Start();
		events.AddRange(AdvanceWorld(state));
		var advanceWorldMs = ProfilingClock.ElapsedMs(advanceWorldStart);

		var viewRange = Math.Max(0, GameConfig.AIVision.ActivationViewRange);
		var anchors = RoomRuntimeModule.GetWorldAnchors(state).Select(static anchor => anchor.Position).ToArray();
		var dispatchResult = AIDispatcher.TickAllProfiled(state, anchors, viewRange);
		events.AddRange(dispatchResult.Events);

		return new TurnTickResult
		{
			Events = events,
			Metrics = new TurnTickMetrics
			{
				TickMs = ProfilingClock.ElapsedMs(tickStart),
				AdvanceWorldMs = advanceWorldMs,
				AIDispatchMs = dispatchResult.Metrics.ElapsedMs,
				AIDispatch = dispatchResult.Metrics,
			},
		};
	}

	public static List<GameEvent> TickWatchMode(GameState state)
	{
		var events = new List<GameEvent>();
		var player = ActorModule.GetPlayer(state);
		if (player != null)
		{
			player.BrainId ??= "simple";
			events.AddRange(AIDispatcher.DecideAndExecuteAny(state, player));
		}

		events.AddRange(Tick(state));
		return events;
	}
}
