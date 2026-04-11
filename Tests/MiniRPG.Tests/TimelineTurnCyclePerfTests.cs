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

	// ──────────────────────────────────────────────
	//  辅助方法
	// ──────────────────────────────────────────────

	/// <summary>执行 <paramref name="n"/> 次 <paramref name="action"/>，返回单次平均毫秒。</summary>
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

		// 敌人分布在玩家周围的网格（x 5..14, y 0..N/10）
		// 使用 "player" 模板确保生物有 Moving/Consciousness/Metabolism 参与 timeline
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

	private sealed class FlatFloorGenerator : IMapGenerator
	{
		public string Id   => "flat_floor";
		public string Name => "Flat Floor";
		public void GenerateChunk(ChunkData chunk, int worldSeed) =>
			chunk.Fill(TerrainRegistry.GetId(Terrains.Floor));
		public void PopulateChunk(ChunkData chunk, int worldSeed) { }
	}
}
