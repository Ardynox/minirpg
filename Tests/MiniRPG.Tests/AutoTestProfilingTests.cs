using System;
using System.Collections.Generic;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Config;
using MiniRPG.Core.Data;
using MiniRPG.Core.Map;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using MiniRPG.Module;
using MiniRPG.Module.Render;
using Xunit;

namespace MiniRPG.Tests;

public sealed class AutoTestProfilingTests
{
	private static readonly string[] RequiredBenchmarkMetricKeys =
	[
		"tick_ms",
		"advance_world_ms",
		"ai_dispatch_ms",
		"awareness_ms",
		"brain_decide_ms",
		"decision_execute_ms",
		"observer_count",
		"candidate_count",
		"shortlist_count",
		"los_checks",
		"visible_actor_count",
		"vision_ms",
		"vision_local_context_ms",
		"vision_candidate_scan_ms",
		"vision_dead_check_ms",
		"vision_sight_capacity_ms",
		"vision_rank_ms",
		"vision_los_ms",
		"dead_checks",
		"capacity_calls",
		"max_candidates_per_observer",
		"max_shortlist_per_observer",
	];

	[Fact]
	public void AutoTestMetricBuilder_CreateTurnTickMetrics_IncludesRequiredKeys()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = new GameState();
		var session = new GameSessionModule(state, new FogOfWarTracker());
		Assert.Equal(SaveLoadStatus.Success, session.LoadPresetScenario("qa_combat_arena"));

		var tick = TurnModule.TickProfiled(state);
		var metrics = AutoTestMetricBuilder.CreateTurnTickMetrics(tick.Metrics);

		foreach (var key in RequiredBenchmarkMetricKeys)
			Assert.True(metrics.ContainsKey(key), $"Expected benchmark metric key '{key}' to be present.");
	}

	[Fact]
	public void TurnModule_TickProfiled_WithoutWorld_ZeroesAiMetrics()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = new GameState();
		var result = TurnModule.TickProfiled(state);
		var metrics = AutoTestMetricBuilder.CreateTurnTickMetrics(result.Metrics);

		Assert.True(metrics["tick_ms"] >= 0d);
		Assert.Equal(0d, metrics["ai_dispatch_ms"]);
		Assert.Equal(0d, metrics["observer_count"]);
		Assert.Equal(0d, metrics["vision_ms"]);
		Assert.Equal(0d, metrics["vision_local_context_ms"]);
		Assert.Equal(0d, metrics["vision_candidate_scan_ms"]);
		Assert.Equal(0d, metrics["vision_dead_check_ms"]);
		Assert.Equal(0d, metrics["vision_sight_capacity_ms"]);
		Assert.Equal(0d, metrics["vision_rank_ms"]);
		Assert.Equal(0d, metrics["vision_los_ms"]);
		Assert.Equal(0d, metrics["dead_checks"]);
		Assert.Equal(0d, metrics["capacity_calls"]);
	}

	[Fact]
	public void AIDispatcher_TickAllProfiled_WithNoObservers_ZeroesVisionMetrics()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = new GameState
		{
			WorldSeed = 20260408,
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "flat_floor",
			ViewModeId = "single_layer",
			World = new WorldMap(20260408, new FlatFloorGenerator()),
		};
		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		ActorModule.Add(state, player);

		var result = AIDispatcher.TickAllProfiled(
			state,
			state.PlayerX,
			state.PlayerY,
			Math.Max(0, GameConfig.AIVision.ActivationViewRange));

		Assert.Empty(result.Events);
		Assert.Equal(0, result.Metrics.Vision.ObserverCount);
		Assert.Equal(0d, result.Metrics.ElapsedMs);
		Assert.Equal(0d, result.Metrics.Vision.ElapsedMs);
		Assert.Equal(0d, result.Metrics.Vision.LocalContextMs);
		Assert.Equal(0d, result.Metrics.Vision.CandidateScanMs);
		Assert.Equal(0d, result.Metrics.Vision.DeadCheckMs);
		Assert.Equal(0d, result.Metrics.Vision.SightCapacityMs);
		Assert.Equal(0d, result.Metrics.Vision.RankMs);
		Assert.Equal(0d, result.Metrics.Vision.LosMs);
	}

	[Fact]
	public void AIVisionBatch_BuildProfiled_AmortizesActorSnapshotWorkAcrossObservers()
	{
		TestSupport.EnsureGameplayDataLoaded();

		var state = CreateProfiledVisionState();
		var observerA = state.Actors["observer_a"];
		var observerB = state.Actors["observer_b"];
		var deadTarget = state.Actors["dead_target"];
		var requests = new List<AIVisionRequest>
		{
			new(observerA, SimDetail.Full),
			new(observerB, SimDetail.Full),
		};

		var perceptions = AIVisionBatch.BuildProfiled(state, requests);
		var metrics = AIVisionBatch.LastMetrics;
		var repeatedDeadChecksWithoutSnapshot = requests.Count * (state.Actors.Count - 1);

		Assert.True(perceptions.ContainsKey(observerA.Id));
		Assert.True(perceptions.ContainsKey(observerB.Id));
		Assert.DoesNotContain(perceptions[observerA.Id].NearbyActors, actor => actor.Id == deadTarget.Id);
		Assert.DoesNotContain(perceptions[observerB.Id].NearbyActors, actor => actor.Id == deadTarget.Id);
		Assert.InRange(metrics.DeadChecks, 0, state.Actors.Count);
		Assert.InRange(metrics.CapacityCalls, 0, state.Actors.Count);
		Assert.True(metrics.DeadChecks < repeatedDeadChecksWithoutSnapshot);
		Assert.True(metrics.CapacityCalls <= state.Actors.Count);
	}

	[Fact]
	public void AutoTestLogWriter_BuildText_IncludesBenchmarkBreakdown()
	{
		var report = new AutoTestRunReport
		{
			RunId = "run-1",
			StartedAtUtc = DateTimeOffset.Parse("2026-04-08T04:00:00Z"),
			FinishedAtUtc = DateTimeOffset.Parse("2026-04-08T04:00:01Z"),
			ContinueOnFailure = true,
			StepDelaySeconds = 0.05f,
			StructuredLogEnabled = false,
			ResourceSmokeEnabled = true,
			DisplayServerName = "headless",
			HeadlessMode = true,
			InvokedFromCli = true,
		};
		var scenario = new AutoTestScenarioResult
		{
			ScenarioId = "qa_combat_arena",
			DisplayName = "QA Combat Arena",
			StartedAtUtc = report.StartedAtUtc,
			FinishedAtUtc = report.FinishedAtUtc,
		};
		scenario.Cases.Add(new AutoTestCaseResult
		{
			RunId = report.RunId,
			ScenarioId = scenario.ScenarioId,
			CaseId = "qa_combat_arena.benchmark.count_50.thresholds",
			Status = "warn",
			Severity = "warning",
			Message = "benchmark warning",
			Turn = 1,
			Metrics = new Dictionary<string, double>
			{
				["spawned_count"] = 50,
				["tick_ms"] = 81.760,
				["advance_world_ms"] = 1.250,
				["ai_dispatch_ms"] = 80.510,
				["vision_ms"] = 50.498,
				["vision_dead_check_ms"] = 12.345,
				["vision_rank_ms"] = 4.321,
				["vision_los_ms"] = 31.234,
				["observer_count"] = 54,
				["candidate_count"] = 120,
				["shortlist_count"] = 80,
			},
			Snapshot = new AutoTestRuntimeSnapshot(),
			TimestampUtc = report.FinishedAtUtc!.Value,
		});
		report.Scenarios.Add(scenario);

		var text = AutoTestLogWriter.BuildText(report);

		Assert.Contains("benchmark_breakdown:", text);
		Assert.Contains("display_server_name: headless", text);
		Assert.Contains("headless_mode: True", text);
		Assert.Contains("invoked_from_cli: True", text);
		Assert.Contains("count=50", text);
		Assert.Contains("tick_ms=81.760", text);
		Assert.Contains("vision_dead_check_ms=12.345", text);
		Assert.Contains("observer_count=54", text);
	}

	private static GameState CreateProfiledVisionState()
	{
		var state = new GameState
		{
			WorldSeed = 20260408,
			PlayerId = "player",
			PlayerX = 1,
			PlayerY = 1,
			PlayerZ = 0,
			GeneratorId = "flat_floor",
			ViewModeId = "single_layer",
			World = new WorldMap(20260408, new FlatFloorGenerator()),
			Weather = WeatherState.CreateDefault(20260408),
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X = state.PlayerX;
		player.Y = state.PlayerY;
		player.Z = state.PlayerZ;
		player.FacingX = 1;
		player.FacingY = 0;
		ActorModule.Add(state, player);

		var observerA = PresetDB.SpawnActor("goblin", "observer_a");
		observerA.X = 4;
		observerA.Y = 1;
		observerA.Z = 0;
		observerA.FacingX = -1;
		observerA.FacingY = 0;
		ActorModule.Add(state, observerA);

		var observerB = PresetDB.SpawnActor("goblin", "observer_b");
		observerB.X = 4;
		observerB.Y = 2;
		observerB.Z = 0;
		observerB.FacingX = -1;
		observerB.FacingY = 0;
		ActorModule.Add(state, observerB);

		var aliveTarget = PresetDB.SpawnActor("goblin", "alive_target");
		aliveTarget.X = 2;
		aliveTarget.Y = 1;
		aliveTarget.Z = 0;
		aliveTarget.FacingX = 1;
		aliveTarget.FacingY = 0;
		ActorModule.Add(state, aliveTarget);

		var deadTarget = PresetDB.SpawnActor("goblin", "dead_target");
		deadTarget.X = 3;
		deadTarget.Y = 1;
		deadTarget.Z = 0;
		deadTarget.Limbs.Clear();
		ActorModule.Add(state, deadTarget);

		return state;
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id => "flat_floor";
		public string Name => "Flat Floor";

		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		}

		public void PopulateChunk(ChunkData chunk, int worldSeed)
		{
		}
	}
}
