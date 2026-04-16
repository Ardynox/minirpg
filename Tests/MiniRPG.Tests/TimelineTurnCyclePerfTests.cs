using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MiniRPG.Core.AI;
using MiniRPG.Core.Combat;
using MiniRPG.Core.Data;
using MiniRPG.Core.Event;
using MiniRPG.Core.Farm;
using MiniRPG.Core.Health;
using MiniRPG.Core.Weather;
using MiniRPG.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace MiniRPG.Tests;

/// <summary>
/// 过回合性能日志测试：模拟玩家结束回合后 AI 行动直到轮到玩家为止，
/// 分阶段计时，将详情打印到测试输出供分析。
/// </summary>
public sealed class TimelineTurnCyclePerfTests(ITestOutputHelper output)
{
	[Fact] public void PerfLog_30Enemies()  => RunPerfLog(30);
	[Fact] public void PerfLog_60Enemies()  => RunPerfLog(60);
	[Fact] public void PerfLog_100Enemies() => RunPerfLog(100);

	[Fact] public void PerfLog_60Enemies_MultiLayer()  => RunPerfLog3D(60, 5);
	[Fact] public void PerfLog_100Enemies_MultiLayer() => RunPerfLog3D(100, 5);
	[Fact] public void PerfLog_200Enemies_MultiLayer() => RunPerfLog3D(200, 5);

	[Fact] public void PerfLog_Vision3D_Isolated() => RunVisionIsolated();

	// ──────────────────────────────────────────────
	//  核心测试逻辑
	// ──────────────────────────────────────────────

	private void RunPerfLog(int enemyCount)
	{
		TestSupport.EnsureGameplayDataLoaded();

		// 1. 子系统微基准（独立 state，不污染主测试）
		var benchState = CreatePerfState(enemyCount);
		TimelineTurnManager.Reset(benchState);

		var syncMs   = MeasureMs(50,  () => TimelineTurnManager.SyncActors(benchState));
		var worldMs  = MeasureMs(20,  () => TurnModule.AdvanceWorld(benchState));

		// AI path micro-benchmarks: measure per-actor AI cost
		var benchEnemy = benchState.Actors.Values.First(a => a.Faction == Factions.Hostile);
		benchState.SnapshotCache = new TimelineSnapshotCache();
		var visionMs = MeasureMs(50, () => PerceptionBuilder.Build(benchState, benchEnemy, SimDetail.Full));
		var perception = PerceptionBuilder.Build(benchState, benchEnemy, SimDetail.Full);
		var aiFullMs = MeasureMs(50, () => AIDispatcher.DecideAndExecuteAnyResult(benchState, benchEnemy, tickBuffs: false));

		// 分项微基准：隔离各阶段成本
		var awarenessCtx = AwarenessModule.CreateTurnContext(benchState);
		var awarenessMs = MeasureMs(100, () => AwarenessModule.UpdateForTurn(benchState, benchEnemy, perception, awarenessCtx));
		var brainMs = MeasureMs(100, () => new MiniRPG.Core.AI.Utility.UtilityBrain().Evaluate(
			benchState, benchEnemy, perception, new AIBehaviorContext(benchState), SimDetail.Full, new Random(42)));
		var healthSyncMs = MeasureMs(100, () => HealthSystem.Sync(benchEnemy, benchState.Turn,
			DefaultEnvironmentExposureProvider.Instance.Capture(benchState, benchEnemy)));
		var captureMs = MeasureMs(200, () => DefaultEnvironmentExposureProvider.Instance.Capture(benchState, benchEnemy));
		benchState.SnapshotCache = null;

		// 2. 主测试：模拟玩家结束回合
		var state = CreatePerfState(enemyCount);
		TimelineTurnManager.Reset(state);
		Assert.True(TimelineTurnManager.IsPlayerTurn(state), "Reset 后应是玩家回合");
		var turnBefore = state.Turn;
		TurnModule.ResetWorldSystemTickCount();

		// 玩家向右移动一格，消耗行动
		var playerAction = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.Move(1, 0));
		Assert.True(playerAction.ActionConsumed, "玩家移动应被消耗");

		// 3. 计时 AI 行动循环直到再次轮到玩家
		var stepTimes = new List<double>(enemyCount * 2);
		TimelineTurnManager.EnableStepProfiling();
		var sw = Stopwatch.StartNew();
		TimelineStepResult result;
		var stepSw = new Stopwatch();
		do
		{
			stepSw.Restart();
			result = TimelineTurnManager.AdvanceAuto(state, watchModeEnabled: false, fastTurnModeEnabled: false);
			stepSw.Stop();
			stepTimes.Add(stepSw.Elapsed.TotalMilliseconds);
		} while (result.HasPendingAutoStep && !result.PlayerTurnReady && stepTimes.Count < enemyCount * 4);
		sw.Stop();
		var stageStats = TimelineTurnManager.DisableStepProfilingAndRead();
		var turnDelta = state.Turn - turnBefore;
		var worldTicks = TurnModule.WorldSystemTickCount;

		var totalMs    = sw.Elapsed.TotalMilliseconds;
		var steps      = stepTimes.Count;
		var avgMs      = totalMs / steps;
		stepTimes.Sort();
		var p50        = stepTimes[steps / 2];
		var p95        = stepTimes[(int)(steps * 0.95)];
		var maxMs      = stepTimes[^1];

		// 估算各子系统占比
		// 世界系统现在仅在 AdvanceCharges（每轮约 1 次）时触发，而非每步
		var estSync  = syncMs  * 2 * steps;
		var estWorld = worldMs * worldTicks;
		var estAI    = totalMs - estSync - estWorld;

		// 4. 打印报告
		output.WriteLine($"");
		output.WriteLine($"╔══ Timeline Perf: {enemyCount} enemies ══");
		output.WriteLine($"║  steps      : {steps}");
		output.WriteLine($"║  turnDelta  : {turnDelta}  (worldTicks: {worldTicks})");
		output.WriteLine($"║  total      : {totalMs,8:F1} ms");
		output.WriteLine($"║  avg/step   : {avgMs,8:F2} ms");
		output.WriteLine($"║  p50/step   : {p50,8:F2} ms");
		output.WriteLine($"║  p95/step   : {p95,8:F2} ms");
		output.WriteLine($"║  max/step   : {maxMs,8:F2} ms");
		output.WriteLine($"╠── 子系统微基准 (per call)");
		output.WriteLine($"║  SyncActors : {syncMs,8:F3} ms/call  × 2 × {steps} = {estSync,7:F1} ms");
		output.WriteLine($"║  AdvanceWorld:{worldMs,8:F3} ms/call  × {worldTicks} = {estWorld,7:F1} ms");
		output.WriteLine($"║  AI vision  : {visionMs,8:F3} ms/call");
		output.WriteLine($"║  AI full    : {aiFullMs,8:F3} ms/call  (vision+brain+exec)");
		output.WriteLine($"║  Awareness  : {awarenessMs,8:F3} ms/call");
		output.WriteLine($"║  Brain      : {brainMs,8:F3} ms/call");
		output.WriteLine($"║  HealthSync : {healthSyncMs,8:F3} ms/call");
		output.WriteLine($"║  EnvCapture : {captureMs,8:F3} ms/call");
		output.WriteLine($"╠── 估算占比");
		output.WriteLine($"║  SyncActors : {estSync / totalMs * 100,5:F1} %  (~{estSync:F1} ms)");
		output.WriteLine($"║  AdvanceWorld:{estWorld / totalMs * 100,5:F1} %  (~{estWorld:F1} ms)");
		output.WriteLine($"║  AI+other   : {Math.Max(0, estAI) / totalMs * 100,5:F1} %  (~{Math.Max(0, estAI):F1} ms)");
		if (steps > 1)
		{
			var firstMs = stepTimes.Max();
			var restMs = totalMs - firstMs;
			var restAvg = restMs / (steps - 1);
			output.WriteLine($"╠── 首步 vs 后续 (batch cache)");
			output.WriteLine($"║  first step : {firstMs,8:F2} ms");
			output.WriteLine($"║  rest avg   : {restAvg,8:F3} ms  × {steps - 1} = {restMs:F1} ms");
		}

		// 单步阶段剖析（EnsureCurrent / HealthDeath / AI / Finalize）
		if (stageStats.Steps > 0)
		{
			var ensureMs = TimelineTurnManager.TicksToMs(stageStats.EnsureCurrentTicks);
			var deathMs = TimelineTurnManager.TicksToMs(stageStats.HealthDeathTicks);
			var aiMs = TimelineTurnManager.TicksToMs(stageStats.AiDispatchTicks);
			var finMs = TimelineTurnManager.TicksToMs(stageStats.FinalizeTicks);
			var finCdMs = TimelineTurnManager.TicksToMs(stageStats.FinalizeCooldownTicks);
			var finCtMs = TimelineTurnManager.TicksToMs(stageStats.FinalizeConsumeTurnTicks);
			var finPbMs = TimelineTurnManager.TicksToMs(stageStats.FinalizeProbeTicks);
			var sumMs = ensureMs + deathMs + aiMs + finMs;
			var n = stageStats.Steps;
			output.WriteLine($"╠── 阶段剖析 (AdvanceAutoSingleStep, {n} steps)");
			output.WriteLine($"║  EnsureCurrent: {ensureMs,8:F2} ms  ({ensureMs / n,6:F3} ms/step, {ensureMs / sumMs * 100,5:F1}%)");
			output.WriteLine($"║  HealthDeath  : {deathMs,8:F2} ms  ({deathMs / n,6:F3} ms/step, {deathMs / sumMs * 100,5:F1}%)");
			output.WriteLine($"║  AI Dispatch  : {aiMs,8:F2} ms  ({aiMs / n,6:F3} ms/step, {aiMs / sumMs * 100,5:F1}%)");
			output.WriteLine($"║  Finalize     : {finMs,8:F2} ms  ({finMs / n,6:F3} ms/step, {finMs / sumMs * 100,5:F1}%)");
			output.WriteLine($"║    .cooldown+grav : {finCdMs,8:F2} ms  ({finCdMs / n,6:F3} ms/step)");
			output.WriteLine($"║    .consumeTurn   : {finCtMs,8:F2} ms  ({finCtMs / n,6:F3} ms/step)");
			output.WriteLine($"║    .probe         : {finPbMs,8:F2} ms  ({finPbMs / n,6:F3} ms/step)");
			output.WriteLine($"║  Sum          : {sumMs,8:F2} ms");
		}

		PrintVisionMetrics("Vision Batch Metrics");

		// Fast-turn 模式测量
		var fastState = CreatePerfState(enemyCount);
		TimelineTurnManager.Reset(fastState);
		TimelineTurnManager.SubmitPlayerAction(fastState, TimelinePlayerAction.Move(1, 0));
		var fastSw = Stopwatch.StartNew();
		TimelineStepResult fastResult;
		var fastSteps = 0;
		do
		{
			fastResult = TimelineTurnManager.AdvanceAuto(fastState, watchModeEnabled: false, fastTurnModeEnabled: true);
			fastSteps++;
		} while (fastResult.HasPendingAutoStep && !fastResult.PlayerTurnReady && fastSteps < enemyCount * 4);
		fastSw.Stop();
		output.WriteLine($"╠── Fast-turn 模式");
		output.WriteLine($"║  total      : {fastSw.Elapsed.TotalMilliseconds,8:F1} ms  ({fastSteps} calls)");
		output.WriteLine($"╚══");
	}

	private void RunPerfLog3D(int enemyCount, int layerCount)
	{
		TestSupport.EnsureGameplayDataLoaded();

		output.WriteLine($"");
		output.WriteLine($"╔══ 3D Multi-Layer Perf: {enemyCount} enemies across {layerCount} layers ══");

		var state = CreateMultiLayerPerfState(enemyCount, layerCount);
		TimelineTurnManager.Reset(state);

		var benchEnemy = state.Actors.Values.First(a => a.Faction == Factions.Hostile);
		state.SnapshotCache = new TimelineSnapshotCache();

		var visionMs = MeasureMs(50, () => PerceptionBuilder.Build(state, benchEnemy, SimDetail.Full));
		var visionMetrics = AIVisionBatch.LastMetrics;
		output.WriteLine($"╠── 单次 Vision (per-actor, Full detail)");
		output.WriteLine($"║  vision     : {visionMs,8:F3} ms/call");
		PrintVisionMetrics("Single-actor Vision");

		var batchRequests = new List<AIVisionRequest>();
		foreach (var actor in state.Actors.Values)
		{
			if (actor.BrainId == null) continue;
			if (CombatModule.IsDead(actor)) continue;
			batchRequests.Add(new AIVisionRequest(actor, SimDetail.Full));
		}

		var batchMs = MeasureMs(10, () => AIVisionBatch.Build(state, batchRequests));
		output.WriteLine($"╠── 批量 Vision ({batchRequests.Count} observers)");
		output.WriteLine($"║  batch total: {batchMs,8:F3} ms");
		output.WriteLine($"║  per observer:{batchMs / batchRequests.Count,8:F4} ms");
		PrintVisionMetrics("Batch Vision");

		state.SnapshotCache = null;

		Assert.True(TimelineTurnManager.IsPlayerTurn(state), "Reset 后应是玩家回合");
		var playerAction = TimelineTurnManager.SubmitPlayerAction(state, TimelinePlayerAction.Move(1, 0));
		Assert.True(playerAction.ActionConsumed, "玩家移动应被消耗");

		var stepTimes = new List<double>(enemyCount * 2);
		TimelineTurnManager.EnableStepProfiling();
		var sw = Stopwatch.StartNew();
		TimelineStepResult result;
		var stepSw = new Stopwatch();
		do
		{
			stepSw.Restart();
			result = TimelineTurnManager.AdvanceAuto(state, watchModeEnabled: false, fastTurnModeEnabled: false);
			stepSw.Stop();
			stepTimes.Add(stepSw.Elapsed.TotalMilliseconds);
		} while (result.HasPendingAutoStep && !result.PlayerTurnReady && stepTimes.Count < enemyCount * 4);
		sw.Stop();
		var stageStats = TimelineTurnManager.DisableStepProfilingAndRead();

		var totalMs = sw.Elapsed.TotalMilliseconds;
		var steps = stepTimes.Count;
		stepTimes.Sort();
		output.WriteLine($"╠── 完整回合循环");
		output.WriteLine($"║  steps      : {steps}");
		output.WriteLine($"║  total      : {totalMs,8:F1} ms");
		output.WriteLine($"║  avg/step   : {totalMs / steps,8:F2} ms");
		output.WriteLine($"║  p50/step   : {stepTimes[steps / 2],8:F2} ms");
		output.WriteLine($"║  p95/step   : {stepTimes[(int)(steps * 0.95)],8:F2} ms");
		output.WriteLine($"║  max/step   : {stepTimes[^1],8:F2} ms");

		if (stageStats.Steps > 0)
		{
			var aiMs = TimelineTurnManager.TicksToMs(stageStats.AiDispatchTicks);
			var n = stageStats.Steps;
			output.WriteLine($"╠── AI Dispatch 阶段");
			output.WriteLine($"║  total      : {aiMs,8:F2} ms  ({aiMs / n,6:F3} ms/step)");
		}

		PrintVisionMetrics("Final Vision Batch");
		output.WriteLine($"╚══");
	}

	private void RunVisionIsolated()
	{
		TestSupport.EnsureGameplayDataLoaded();

		output.WriteLine($"");
		output.WriteLine($"╔══ Vision 3D Isolated Benchmark ══");

		var configs = new[]
		{
			(Label: "100 same-layer", Count: 100, Layers: 1),
			(Label: "100 multi-layer (5)", Count: 100, Layers: 5),
			(Label: "200 same-layer", Count: 200, Layers: 1),
			(Label: "200 multi-layer (5)", Count: 200, Layers: 5),
			(Label: "500 same-layer", Count: 500, Layers: 1),
			(Label: "500 multi-layer (5)", Count: 500, Layers: 5),
		};

		foreach (var (label, count, layers) in configs)
		{
			var state = layers == 1
				? CreatePerfState(count)
				: CreateMultiLayerPerfState(count, layers);

			var requests = new List<AIVisionRequest>();
			foreach (var actor in state.Actors.Values)
			{
				if (actor.BrainId == null) continue;
				if (CombatModule.IsDead(actor)) continue;
				requests.Add(new AIVisionRequest(actor, SimDetail.Full));
			}

			for (var warmup = 0; warmup < 3; warmup++)
				AIVisionBatch.Build(state, requests);

			const int iterations = 20;
			var times = new double[iterations];
			for (var i = 0; i < iterations; i++)
			{
				state.SnapshotCache = new TimelineSnapshotCache();
				var iterSw = Stopwatch.StartNew();
				AIVisionBatch.Build(state, requests);
				iterSw.Stop();
				times[i] = iterSw.Elapsed.TotalMilliseconds;
			}

			Array.Sort(times);
			var m = AIVisionBatch.LastMetrics;
			output.WriteLine($"╠── {label}");
			output.WriteLine($"║  observers  : {m.ObserverCount}");
			output.WriteLine($"║  candidates : {m.CandidateCount}  (max/obs: {m.MaxCandidatesPerObserver})");
			output.WriteLine($"║  shortlist  : {m.ShortlistCount}  (max/obs: {m.MaxShortlistPerObserver})");
			output.WriteLine($"║  LOS checks : {m.LosChecks}  (cache hits: {m.LosCacheHits})");
			output.WriteLine($"║  visible    : {m.VisibleActorCount}");
			output.WriteLine($"║  median     : {times[iterations / 2],8:F3} ms");
			output.WriteLine($"║  p95        : {times[(int)(iterations * 0.95)],8:F3} ms");
			output.WriteLine($"║  min        : {times[0],8:F3} ms");
			output.WriteLine($"║  max        : {times[^1],8:F3} ms");
		}

		output.WriteLine($"╚══");
	}

	private void PrintVisionMetrics(string label)
	{
		var m = AIVisionBatch.LastMetrics;
		output.WriteLine($"╠── {label}");
		output.WriteLine($"║  observers  : {m.ObserverCount}");
		output.WriteLine($"║  candidates : {m.CandidateCount}  (max/obs: {m.MaxCandidatesPerObserver})");
		output.WriteLine($"║  shortlist  : {m.ShortlistCount}  (max/obs: {m.MaxShortlistPerObserver})");
		output.WriteLine($"║  LOS checks : {m.LosChecks}  (cache hits: {m.LosCacheHits})");
		output.WriteLine($"║  visible    : {m.VisibleActorCount}");
		output.WriteLine($"║  elapsed    : {m.ElapsedMs,8:F3} ms");
		output.WriteLine($"║    candidate: {m.CandidateScanMs,8:F3} ms");
		output.WriteLine($"║    rank     : {m.RankMs,8:F3} ms");
		output.WriteLine($"║    LOS      : {m.LosMs,8:F3} ms");
		output.WriteLine($"║    local ctx: {m.LocalContextMs,8:F3} ms");
	}

	// ──────────────────────────────────────────────
	//  辅助方法
	// ──────────────────────────────────────────────

	private static double MeasureMs(int n, Action action)
	{
		var sw = Stopwatch.StartNew();
		for (var i = 0; i < n; i++) action();
		sw.Stop();
		return sw.Elapsed.TotalMilliseconds / n;
	}

	private static GameState CreatePerfState(int enemyCount)
	{
		var state = new GameState
		{
			WorldSeed = 20260411,
			PlayerId  = "player",
			PlayerX   = 0,
			PlayerY   = 0,
			PlayerZ   = 0,
			GeneratorId = "flat_floor",
			ViewModeId  = "single_layer",
			World = new WorldMap(20260411, new FlatFloorGenerator()),
		};
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type      = WeatherType.Clear,
			Intensity = WeatherIntensity.Normal,
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X       = 0;
		player.Y       = 0;
		player.Z       = 0;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;
		ActorModule.Add(state, player);

		for (var i = 0; i < enemyCount; i++)
		{
			var enemy = PresetDB.SpawnActor("player", $"e{i}");
			enemy.X          = 5 + (i % 10);
			enemy.Y          = i / 10;
			enemy.Z          = 0;
			enemy.Faction    = Factions.Hostile;
			enemy.BrainId    = "simple";
			enemy.DisplayName = $"Enemy{i}";
			ActorModule.Add(state, enemy);
		}

		return state;
	}

	private static GameState CreateMultiLayerPerfState(int enemyCount, int layerCount)
	{
		var state = new GameState
		{
			WorldSeed = 20260411,
			PlayerId  = "player",
			PlayerX   = 0,
			PlayerY   = 0,
			PlayerZ   = 0,
			GeneratorId = "multi_layer",
			ViewModeId  = "single_layer",
			World = new WorldMap(20260411, new MultiLayerGenerator(layerCount)),
		};
		state.Weather.DebugOverride = new WeatherDebugOverride
		{
			Type      = WeatherType.Clear,
			Intensity = WeatherIntensity.Normal,
		};

		var player = PresetDB.SpawnActor("player", "player");
		player.X       = 0;
		player.Y       = 0;
		player.Z       = 0;
		player.FacingX = 1;
		player.FacingY = 0;
		player.BrainId = null;
		ActorModule.Add(state, player);

		var halfLayers = layerCount / 2;
		var perLayer = enemyCount / layerCount;
		var remainder = enemyCount % layerCount;
		var idx = 0;
		for (var layer = 0; layer < layerCount; layer++)
		{
			var z = layer - halfLayers;
			var countThisLayer = perLayer + (layer < remainder ? 1 : 0);
			for (var i = 0; i < countThisLayer; i++)
			{
				var enemy = PresetDB.SpawnActor("player", $"e{idx}");
				enemy.X          = 3 + (i % 10);
				enemy.Y          = i / 10;
				enemy.Z          = z;
				enemy.Faction    = Factions.Hostile;
				enemy.BrainId    = "simple";
				enemy.DisplayName = $"Enemy{idx}";
				ActorModule.Add(state, enemy);
				idx++;
			}
		}

		return state;
	}

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id   => "flat_floor";
		public string Name => "Flat Floor";
		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			var terrainId = chunk.Coord.Cz == 0
				? TerrainRegistry.GetId(Terrains.Floor)
				: TerrainRegistry.GetId(Terrains.WallStone);
			chunk.Fill(terrainId);
		}
		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}

	private sealed class MultiLayerGenerator(int layerCount) : IMapGenerator
	{
		public string Id   => "multi_layer";
		public string Name => "Multi Layer";
		public void GenerateChunk(ChunkData chunk, int worldSeed)
		{
			var halfLayers = layerCount / 2;
			var z = chunk.Coord.Cz;
			if (z >= -halfLayers && z <= halfLayers)
				chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
			else
				chunk.Fill(TerrainRegistry.GetId(Terrains.WallStone));
		}
		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
